using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Rhino;
using Rhino.Runtime.InProcess;

namespace Nuclei.DefinitionValidationHost;

/// <summary>
/// The first post-bootstrap stage. This class references RhinoCommon but has no
/// Grasshopper/GH_IO types. GrasshopperStage is found by string only after the
/// installed RhinoCore is alive, so the JIT cannot reverse that load order.
/// </summary>
internal static class RhinoHost
{
    private static string _stage = "process-start";
    private static long _stageDeadlineTicks = long.MaxValue;
    private static int _completed;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    public static int Run(string[] args)
    {
        HostOptions? options = null;
        string? isolatedProfile = null;
        RhinoCore? core = null;
        try
        {
            options = HostOptions.Parse(args);
            options.Validate();
            InstallResolvers(options);
            isolatedProfile = CreateIsolatedGrasshopperProfile();
            StartWatchdog(options.StageTimeoutSeconds);

            Progress("rhino-core-starting", options.StageTimeoutSeconds);
            var rhinoArguments = options.SafeMode
                ? new[] { "/safemode", "/nosplash", "/notemplate" }
                : new[] { "/nosplash", "/notemplate" };
            core = new RhinoCore(
                rhinoArguments,
                WindowStyle.NoWindow);
            Progress("rhino-core-started", options.StageTimeoutSeconds);
            Console.WriteLine("Rhino version: " + RhinoApp.Version);
            if (options.SafeMode)
            {
                if (!RhinoApp.IsSafeModeEnabled)
                {
                    throw new InvalidOperationException("Rhino did not enter requested safe mode.");
                }
                foreach (var id in new[]
                {
                    new Guid("2668d7ed-f507-4a68-8295-8172147a0e39"),
                    new Guid("9d864247-4774-464c-bd81-1a11953b2f8f"),
                    new Guid("f0b5b632-cc3c-43e7-bc88-da29c47b98bc")
                })
                {
                    if (Rhino.PlugIns.PlugIn.GetPlugInInfo(id)?.IsLoaded is true)
                    {
                        throw new InvalidOperationException("Safe mode loaded forbidden Rhino-MCP plug-in " + id + ".");
                    }
                }
                Console.WriteLine("Safe mode active; all three Rhino-MCP plug-ins are unloaded.");
            }

            string stageName = options.RunValidator
                ? "Nuclei.DefinitionValidationHost.PythonValidationStage"
                : "Nuclei.DefinitionValidationHost.GrasshopperStage";
            Type stageType = Assembly.GetExecutingAssembly().GetType(
                stageName,
                throwOnError: true)!;
            MethodInfo execute = stageType.GetMethod(
                "Execute",
                BindingFlags.Static | BindingFlags.Public)
                ?? throw new MissingMethodException(stageType.FullName, "Execute");
            execute.Invoke(null, new object[] { options, isolatedProfile, core });

            Progress("complete", options.StageTimeoutSeconds);
            Volatile.Write(ref _completed, 1);
            return 0;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            Volatile.Write(ref _completed, 1);
            WriteFailure(error.InnerException);
            return 1;
        }
        catch (Exception error)
        {
            Volatile.Write(ref _completed, 1);
            WriteFailure(error);
            return 1;
        }
        finally
        {
            try
            {
                core?.Dispose();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("RhinoCore disposal failed: " + error.Message);
            }

            if (isolatedProfile is not null)
            {
                TryDeleteOwnedTemporaryProfile(isolatedProfile);
            }
        }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static void Progress(string stage, int timeoutSeconds)
    {
        _stage = stage;
        Interlocked.Exchange(ref _stageDeadlineTicks, DateTime.UtcNow.AddSeconds(timeoutSeconds).Ticks);
        Console.WriteLine(DateTime.UtcNow.ToString("O") + " " + stage);
        Console.Out.Flush();
    }

    private static void InstallResolvers(HostOptions options)
    {
        var roots = options.AssemblyRoots();
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, roots) + Path.PathSeparator + currentPath);
        if (!SetDllDirectory(options.RhinoSystem))
        {
            throw new InvalidOperationException(
                "SetDllDirectory failed for the Rhino System directory (Win32 error "
                + Marshal.GetLastWin32Error() + ").");
        }

        AssemblyLoadContext.Default.Resolving += (_, name) => ResolveAssembly(name, roots);
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
            ResolveAssembly(new AssemblyName(eventArgs.Name), roots);
    }

    private static Assembly? ResolveAssembly(AssemblyName name, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(name.Name))
        {
            return null;
        }
        foreach (string root in roots)
        {
            foreach (string extension in new[] { ".dll", ".gha" })
            {
                string candidate = Path.Combine(root, name.Name + extension);
                if (File.Exists(candidate))
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
                }
            }
        }
        return null;
    }

    private static string CreateIsolatedGrasshopperProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "NucleiDefinitionValidationHost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Libraries"));
        return Path.GetFullPath(root);
    }

    private static void StartWatchdog(int timeoutSeconds)
    {
        Progress("watchdog-started", timeoutSeconds);
        var watchdog = new Thread(() =>
        {
            while (Volatile.Read(ref _completed) == 0)
            {
                Thread.Sleep(250);
                if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _stageDeadlineTicks))
                {
                    continue;
                }
                string message = "Validation host stage timed out: " + _stage;
                Console.Error.WriteLine(message);
                Console.Error.Flush();
                Environment.FailFast(message);
            }
        })
        {
            IsBackground = true,
            Name = "Nuclei definition validation watchdog"
        };
        watchdog.Start();
    }

    private static void WriteFailure(Exception error)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            success = false,
            stage = _stage,
            error = error.ToString()
        }, JsonOptions));
    }

    private static void TryDeleteOwnedTemporaryProfile(string profile)
    {
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(profile);
        string expectedPrefix = temporaryRoot + Path.DirectorySeparatorChar + "NucleiDefinitionValidationHost-";
        if (!resolved.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Refusing to remove unexpected profile path: " + resolved);
            return;
        }
        try
        {
            Directory.Delete(resolved, true);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Temporary profile cleanup failed: " + error.Message);
        }
    }
}

internal sealed record HostOptions(
    string RhinoSystem,
    string GrasshopperDirectory,
    string V4Gha,
    string? ExpectedV4Sha256,
    string PufferfishGha,
    string DendroGha,
    string GhglGha,
    string MeshEditGha,
    string Definition,
    int StageTimeoutSeconds,
    bool RunValidator,
    string ValidatorScript,
    string Definitions,
    string Map,
    string? OnlyFile,
    string? SkipExtras,
    bool SafeMode)
{
    private const string DefaultRhinoSystem = @"C:\Program Files\Rhino 9 WIP\System";
    private const string DefaultGrasshopper = @"C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper";
    private static readonly string DefaultPufferfish = ApplicationDataPath(
        "Grasshopper", "Libraries", "Pufferfish3-0.gha");
    private static readonly string DefaultDendro = ApplicationDataPath(
        "McNeel", "Rhinoceros", "packages", "9.0", "DendroGH", "0.9.1-alpha", "DendroGH.gha");
    private static readonly string DefaultGhgl = ApplicationDataPath(
        "McNeel", "Rhinoceros", "packages", "9.0", "ghgl", "9.0.0", "ghgl.gha");
    private static readonly string DefaultMeshEdit = ApplicationDataPath(
        "McNeel", "Rhinoceros", "packages", "9.0", "MeshEdit-Components", "2.0.0.0", "Meshedit2000.gha");
    internal string[] AssemblyRoots() => new[]
    {
        Path.Combine(RhinoSystem, "netcore"),
        GrasshopperDirectory,
        Path.GetDirectoryName(PufferfishGha)!,
        Path.GetDirectoryName(DendroGha)!,
        Path.GetDirectoryName(GhglGha)!,
        Path.GetDirectoryName(MeshEditGha)!,
        Path.GetDirectoryName(V4Gha)!,
        RhinoSystem
    }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static HostOptions Parse(string[] args)
    {
        string? repositoryRoot = null;

        string Option(string name, string fallback)
        {
            int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return fallback;
            }
            if (index == args.Length - 1)
            {
                throw new ArgumentException("Missing value after " + name + ".");
            }
            return args[index + 1];
        }

        string RepositoryOption(string name, params string[] segments)
        {
            int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                if (index == args.Length - 1)
                    throw new ArgumentException("Missing value after " + name + ".");
                return args[index + 1];
            }

            repositoryRoot ??= Program.FindRepositoryRoot();
            return Path.Combine(new[] { repositoryRoot }.Concat(segments).ToArray());
        }

        string timeoutText = Option("--stage-timeout-seconds", "180");
        if (!int.TryParse(timeoutText, out int timeout) || timeout < 15 || timeout > 1800)
        {
            throw new ArgumentOutOfRangeException(
                "--stage-timeout-seconds",
                "Stage timeout must be an integer from 15 through 1800.");
        }
        return new HostOptions(
            Path.GetFullPath(Option("--rhino-system", DefaultRhinoSystem)),
            Path.GetFullPath(Option("--grasshopper", DefaultGrasshopper)),
            Path.GetFullPath(RepositoryOption(
                "--v4-gha",
                "Nuclei-v4",
                "Nuclei4",
                "bin",
                "Release",
                "net7.0-windows",
                "Nuclei4.gha")),
            NullIfWhiteSpace(Option("--expected-v4-sha256", string.Empty))?.Trim().ToUpperInvariant(),
            Path.GetFullPath(Option("--pufferfish-gha", DefaultPufferfish)),
            Path.GetFullPath(Option("--dendro-gha", DefaultDendro)),
            Path.GetFullPath(Option("--ghgl-gha", DefaultGhgl)),
            Path.GetFullPath(Option("--mesh-edit-gha", DefaultMeshEdit)),
            Path.GetFullPath(RepositoryOption(
                "--definition",
                "Nuclei Definitions",
                "v4_updated",
                "15_3D Intro_v3.gh")),
            timeout,
            args.Any(value => string.Equals(value, "--validator", StringComparison.OrdinalIgnoreCase)),
            Path.GetFullPath(RepositoryOption(
                "--validator-script",
                "tools",
                "Nuclei.DefinitionValidator",
                "ValidateInRhino.py")),
            Path.GetFullPath(RepositoryOption("--definitions", "Nuclei Definitions", "v4_updated")),
            Path.GetFullPath(RepositoryOption(
                "--map",
                "tools",
                "Nuclei.DefinitionConverter",
                "v3.3-to-v4.json")),
            NullIfWhiteSpace(Option("--only-file", string.Empty)),
            NullIfWhiteSpace(Option("--skip-extras", string.Empty)),
            args.Any(value => string.Equals(value, "--safe-mode", StringComparison.OrdinalIgnoreCase)));
    }

    private static string ApplicationDataPath(params string[] segments)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
        {
            throw new InvalidOperationException("The roaming application-data directory could not be resolved.");
        }

        return segments.Aggregate(root, Path.Combine);
    }

    internal void Validate()
    {
        RequireDirectory(RhinoSystem, "Rhino System directory");
        RequireFile(Path.Combine(RhinoSystem, "netcore", "RhinoCommon.dll"), "RhinoCommon");
        RequireDirectory(GrasshopperDirectory, "Grasshopper directory");
        RequireFile(Path.Combine(GrasshopperDirectory, "Grasshopper.dll"), "Grasshopper assembly");
        RequireFile(Path.Combine(GrasshopperDirectory, "GH_IO.dll"), "GH_IO assembly");
        RequireFile(V4Gha, "Nuclei4 GHA");
        if (ExpectedV4Sha256 is not null)
        {
            if (ExpectedV4Sha256.Length != 64 || ExpectedV4Sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new ArgumentException("--expected-v4-sha256 must contain exactly 64 hexadecimal characters.");
            }
            using var stream = File.OpenRead(V4Gha);
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, ExpectedV4Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The requested V4 GHA does not match --expected-v4-sha256.");
            }
        }
        RequireFile(PufferfishGha, "Pufferfish GHA");
        RequireFile(DendroGha, "Dendro GHA");
        RequireFile(Definition, "Grasshopper definition");
        if (RunValidator)
        {
            RequireFile(GhglGha, "ghgl GHA");
            RequireFile(MeshEditGha, "MeshEdit GHA");
            RequireFile(ValidatorScript, "Rhino Python validator");
            RequireDirectory(Definitions, "Converted definitions directory");
            RequireFile(Path.Combine(Definitions, "_conversion_manifest.json"), "conversion manifest");
            RequireFile(Map, "component GUID map");
        }
    }

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(label + " was not found.", path);
        }
    }

    private static void RequireDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(label + " was not found: " + path);
        }
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
