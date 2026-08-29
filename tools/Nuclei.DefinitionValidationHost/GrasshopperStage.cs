using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Grasshopper.Kernel;
using Rhino;
using Rhino.PlugIns;
using Rhino.Runtime.InProcess;

namespace Nuclei.DefinitionValidationHost;

/// <summary>
/// This type is deliberately loaded by reflection only after RhinoCore exists.
/// </summary>
internal static class GrasshopperStage
{
    private static readonly Guid GrasshopperId = new("b45a29b1-4343-4035-989e-044e8580d9cf");

    public static void Execute(HostOptions options, string isolatedProfile, RhinoCore core)
    {
        PreloadForValidator(options, isolatedProfile, core);

        var server = Grasshopper.Instances.ComponentServer
            ?? throw new InvalidOperationException("Grasshopper component server is unavailable after initialization.");
        RhinoHost.Progress("definition-opening", options.StageTimeoutSeconds);
        var io = new GH_DocumentIO();
        if (!io.Open(options.Definition))
        {
            throw new InvalidOperationException("Grasshopper could not open the target definition.");
        }
        var document = io.Document
            ?? throw new InvalidOperationException("Grasshopper opened the archive without producing a document.");
        try
        {
            document.Enabled = false;
            var objects = document.Objects.ToArray();
            var placeholders = objects
                .Where(value => IsPlaceholder(value.GetType()))
                .Select(value => new
                {
                    type = value.GetType().FullName,
                    instanceGuid = value.InstanceGuid
                })
                .ToArray();
            if (placeholders.Length != 0)
            {
                throw new InvalidOperationException(
                    "The definition contains unresolved Grasshopper objects: "
                    + string.Join(", ", placeholders.Select(value => value.type + " " + value.instanceGuid)));
            }

            var nucleiObjects = objects
                .Where(value => string.Equals(value.GetType().Namespace, "Nuclei4", StringComparison.Ordinal))
                .ToArray();
            if (nucleiObjects.Length == 0)
            {
                throw new InvalidOperationException("The target definition resolved no Nuclei4 objects.");
            }
            VerifyNucleiAssemblyOrigin(nucleiObjects, options.V4Gha);

            int wireCount = objects.Sum(CountInputSources);
            var result = new
            {
                success = true,
                rhinoVersion = RhinoApp.Version.ToString(),
                grasshopperVersion = typeof(Grasshopper.Instances).Assembly.GetName().Version?.ToString(),
                definition = options.Definition,
                definitionSha256 = Sha256(options.Definition),
                objectCount = objects.Length,
                wireCount,
                unresolvedObjectCount = placeholders.Length,
                nucleiV4ObjectCount = nucleiObjects.Length,
                nucleiV4Gha = options.V4Gha,
                nucleiV4GhaSha256 = Sha256(options.V4Gha),
                isolatedGrasshopperProfile = isolatedProfile
            };
            Console.WriteLine(JsonSerializer.Serialize(result, RhinoHost.JsonOptions));
        }
        finally
        {
            document.Dispose();
        }
    }

    public static void PreloadForValidator(HostOptions options, string isolatedProfile, RhinoCore core)
    {
        ConfigureGrasshopperProfile(isolatedProfile);
        RhinoHost.Progress("grasshopper-rhp-loading", options.StageTimeoutSeconds);
        bool grasshopperLoaded;
        if (options.SafeMode)
        {
            // Rhino's safe mode intentionally rejects every RHP load, including
            // force-load and explicit-path calls. The Grasshopper assemblies are
            // installed system assemblies and their headless component/document
            // services do not require the UI RHP wrapper. Access the isolated
            // component server directly while all third-party Rhino RHPs remain
            // disabled.
            grasshopperLoaded = Grasshopper.Instances.ComponentServer is not null;
            Console.WriteLine("Safe-mode headless Grasshopper component server: " + grasshopperLoaded);
        }
        else
        {
            grasshopperLoaded = PlugIn.LoadPlugIn(GrasshopperId, false, false);
        }
        if (!grasshopperLoaded)
        {
            throw new InvalidOperationException("Rhino 9 did not load the Grasshopper Rhino plug-in.");
        }
        core.DoEvents();
        RhinoHost.Progress("grasshopper-rhp-loaded", options.StageTimeoutSeconds);

        var server = Grasshopper.Instances.ComponentServer
            ?? throw new InvalidOperationException("Grasshopper component server is unavailable after initialization.");
        var skipped = new HashSet<string>(
            (options.SkipExtras ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        var candidates = new[]
        {
            (options.PufferfishGha, "pufferfish"),
            (options.DendroGha, "dendro"),
            (options.GhglGha, "ghgl"),
            (options.MeshEditGha, "mesh-edit")
        };
        foreach (var candidate in candidates)
        {
            if (!skipped.Contains(Path.GetFileName(candidate.Item1)))
            {
                int loadTimeout = string.Equals(candidate.Item2, "dendro", StringComparison.Ordinal)
                    ? Math.Min(options.StageTimeoutSeconds, 60)
                    : options.StageTimeoutSeconds;
                LoadGha(server, candidate.Item1, candidate.Item2, loadTimeout);
            }
        }
        LoadGha(server, options.V4Gha, "nuclei-v4", options.StageTimeoutSeconds);
        core.DoEvents();
    }

    private static void ConfigureGrasshopperProfile(string isolatedProfile)
    {
        var field = typeof(Grasshopper.Folders).GetField(
            "m_appdataFolder",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new MissingFieldException(typeof(Grasshopper.Folders).FullName, "m_appdataFolder");
        }
        field.SetValue(null, isolatedProfile + Path.DirectorySeparatorChar);
    }

    private static LoadResult LoadGha(
        GH_ComponentServer server,
        string path,
        string label,
        int timeoutSeconds)
    {
        RhinoHost.Progress("gha-loading:" + label, timeoutSeconds);
        var method = server.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, "LoadGHA", StringComparison.Ordinal))
                {
                    return false;
                }
                var parameters = candidate.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(GH_ExternalFile)
                    && parameters[1].ParameterType == typeof(bool);
            })
            ?? throw new MissingMethodException(server.GetType().FullName, "LoadGHA(GH_ExternalFile, bool)");

        bool returnValue = method.Invoke(server, new object[] { new GH_ExternalFile(path), false }) is true;
        RhinoHost.Progress("gha-loaded:" + label, timeoutSeconds);
        return new LoadResult(label, path, Sha256(path), returnValue);
    }

    private static bool IsPlaceholder(Type type)
    {
        string name = type.FullName ?? type.Name;
        return name.Contains("Placeholder", StringComparison.OrdinalIgnoreCase)
            || name.Contains("UnknownObject", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ProxyObject", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountInputSources(IGH_DocumentObject value)
    {
        if (value is IGH_Component component)
        {
            return component.Params.Input.Sum(parameter => parameter.SourceCount);
        }
        if (value is IGH_Param parameter)
        {
            return parameter.SourceCount;
        }
        return 0;
    }

    private static void VerifyNucleiAssemblyOrigin(IEnumerable<IGH_DocumentObject> objects, string expectedGha)
    {
        string expectedPath = Path.GetFullPath(expectedGha);
        string expectedHash = Sha256(expectedPath);
        foreach (var value in objects)
        {
            string actualPath = Path.GetFullPath(value.GetType().Assembly.Location);
            if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Sha256(actualPath), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Nuclei4 object " + value.InstanceGuid + " resolved from " + actualPath
                    + " instead of the requested GHA " + expectedPath + ".");
            }
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record LoadResult(string Label, string Path, string Sha256, bool LoaderReturnValue);
}
