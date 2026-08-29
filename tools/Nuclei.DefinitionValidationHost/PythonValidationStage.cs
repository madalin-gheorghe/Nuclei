using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using Rhino;
using Rhino.Runtime;
using Rhino.Runtime.InProcess;

namespace Nuclei.DefinitionValidationHost;

/// <summary>
/// Executes the repository's authoritative Rhino Python validator inside the
/// already-started RhinoCore. The Python script owns Grasshopper/GHA loading and
/// writes the durable report; this wrapper only supplies its isolated environment,
/// mirrors progress to stdout/watchdog, and rejects an incomplete/stale report.
/// </summary>
internal static class PythonValidationStage
{
    public static void Execute(HostOptions options, string isolatedProfile, RhinoCore core)
    {
        string reportPath = Path.Combine(options.Definitions, "_rhino9_validation.json");
        string progressPath = Path.Combine(options.Definitions, "_rhino9_validation.progress.json");
        File.Delete(reportPath);
        File.Delete(progressPath);

        ConfigureEnvironment(options, isolatedProfile);
        RhinoHost.Progress("validator-gha-preload", options.StageTimeoutSeconds);
        Type loaderType = Assembly.GetExecutingAssembly().GetType(
            "Nuclei.DefinitionValidationHost.GrasshopperStage",
            throwOnError: true)!;
        MethodInfo preload = loaderType.GetMethod(
            "PreloadForValidator",
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingMethodException(loaderType.FullName, "PreloadForValidator");
        preload.Invoke(null, new object[] { options, isolatedProfile, core });

        using var monitorCancellation = new CancellationTokenSource();
        var monitor = new Thread(() => MonitorProgress(progressPath, options.StageTimeoutSeconds, monitorCancellation.Token))
        {
            IsBackground = true,
            Name = "Nuclei Python validator progress monitor"
        };
        monitor.Start();

        RhinoDoc? ownedDocument = null;
        try
        {
            RhinoHost.Progress("python-validator-creating", options.StageTimeoutSeconds);
            var python = PythonScript.Create()
                ?? throw new InvalidOperationException("Rhino.Runtime.PythonScript.Create returned null.");
            python.Output = value =>
            {
                Console.Write(value);
                Console.Out.Flush();
            };

            RhinoDoc? contextDocument = RhinoDoc.ActiveDoc;
            if (contextDocument is null)
            {
                ownedDocument = RhinoDoc.CreateHeadless(null);
                contextDocument = ownedDocument;
            }
            if (contextDocument is not null)
            {
                python.SetupScriptContext(contextDocument);
            }

            RhinoHost.Progress("python-validator-executing", options.StageTimeoutSeconds);
            if (!python.ExecuteFile(options.ValidatorScript))
            {
                throw new InvalidOperationException("Rhino Python reported that the validator file did not execute.");
            }
            core.DoEvents();
        }
        finally
        {
            monitorCancellation.Cancel();
            monitor.Join(TimeSpan.FromSeconds(2));
            ownedDocument?.Dispose();
        }

        RhinoHost.Progress("python-report-verifying", options.StageTimeoutSeconds);
        VerifyReport(options, reportPath, progressPath);
        Console.WriteLine(File.ReadAllText(reportPath));
    }

    private static void ConfigureEnvironment(HostOptions options, string isolatedProfile)
    {
        Set("NUCLEI_VALIDATION_DEFINITIONS", options.Definitions);
        Set("NUCLEI_VALIDATION_V4_GHA", options.V4Gha);
        Set("NUCLEI_VALIDATION_MAP", options.Map);
        Set("NUCLEI_VALIDATION_NORMALIZE", "0");
        Set("NUCLEI_VALIDATION_SOLVE_DENDRO", "1");
        Set("NUCLEI_VALIDATION_ORIGINAL_APPDATA", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Set("NUCLEI_VALIDATION_GRASSHOPPER_DLL", Path.Combine(options.GrasshopperDirectory, "Grasshopper.dll"));
        Set("NUCLEI_VALIDATION_ISOLATED_GH_APPDATA", isolatedProfile);
        Set("NUCLEI_VALIDATION_AUTOLOAD", "0");
        Set("NUCLEI_VALIDATION_USE_NORMAL_PROFILE", "0");
        Set("NUCLEI_VALIDATION_GRASSHOPPER_PRELOADED", options.SafeMode ? "1" : "0");
        Set("NUCLEI_VALIDATION_START_AT", null);
        Set("NUCLEI_VALIDATION_ONLY_FILE", options.OnlyFile);
        Set("NUCLEI_VALIDATION_SKIP_EXTRAS", options.SkipExtras);
        Set("NUCLEI_VALIDATION_EXPECTED_V4_SHA256", options.ExpectedV4Sha256 ?? Sha256(options.V4Gha));
    }

    private static void Set(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);

    private static void MonitorProgress(string path, int timeoutSeconds, CancellationToken token)
    {
        string? lastStatus = null;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var json = JsonDocument.Parse(ReadSharedText(path));
                    if (json.RootElement.TryGetProperty("status", out var property))
                    {
                        string? status = property.GetString();
                        if (!string.IsNullOrWhiteSpace(status)
                            && !string.Equals(status, lastStatus, StringComparison.Ordinal))
                        {
                            lastStatus = status;
                            RhinoHost.Progress("python:" + status, timeoutSeconds);
                        }
                    }
                }
            }
            catch (IOException)
            {
                // The script atomically replaces this file; a scanner can still
                // hold it briefly. The next poll is authoritative.
            }
            catch (JsonException)
            {
                // Same retry policy for a transient read during replacement.
            }
            token.WaitHandle.WaitOne(250);
        }
    }

    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void VerifyReport(HostOptions options, string reportPath, string progressPath)
    {
        if (!File.Exists(reportPath))
        {
            throw new InvalidOperationException("Python validator exited without writing " + reportPath + ".");
        }
        if (File.Exists(progressPath))
        {
            throw new InvalidOperationException("Python validator left an incomplete progress file: " + progressPath + ".");
        }

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        JsonElement root = report.RootElement;
        if (!RequiredBoolean(root, "success"))
        {
            string error = root.TryGetProperty("error", out var value) ? value.GetString() ?? "unknown error" : "unknown error";
            throw new InvalidOperationException("Python validator reported failure: " + error);
        }

        int expectedFileCount = options.OnlyFile is null ? ManifestFileCount(options.Definitions) : 1;
        int fileCount = root.GetProperty("fileCount").GetInt32();
        if (fileCount != expectedFileCount)
        {
            throw new InvalidOperationException(
                "Python validator reported " + fileCount + " files; expected " + expectedFileCount + ".");
        }
        string reportHash = root.GetProperty("v4GhaSha256").GetString() ?? string.Empty;
        string actualHash = Sha256(options.V4Gha);
        if (!string.Equals(reportHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Python report does not identify the requested V4 GHA hash.");
        }
        foreach (string property in new[]
        {
            "v4GhaSha256Before",
            "v4GhaSha256After",
            "expectedV4GhaSha256"
        })
        {
            string value = root.GetProperty(property).GetString() ?? string.Empty;
            if (!string.Equals(value, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(property + " does not identify the exact final V4 GHA hash.");
            }
        }
        if (!RequiredBoolean(root, "v4GhaUnchangedDuringValidation")
            || RequiredBoolean(root, "normalized"))
        {
            throw new InvalidOperationException("Final validation must keep the V4 GHA and saved definitions unchanged.");
        }

        VerifyDefinitionHashes(options, root);

        foreach (string property in new[]
        {
            "wireParityAfterApprovedSchemaAdapters",
            "allFilesOpened",
            "noMissingObjects",
            "noV3Residue",
            "structurePreserved"
        })
        {
            if (!RequiredBoolean(root, property))
            {
                throw new InvalidOperationException("Python report did not prove " + property + ".");
            }
        }

        JsonElement runtimeChecks = root.GetProperty("targetedRuntimeChecks");
        if (runtimeChecks.GetArrayLength() != 1)
        {
            throw new InvalidOperationException("Python report must contain exactly one targeted runtime check.");
        }
        JsonElement runtime = runtimeChecks[0];
        if (!RequiredBoolean(runtime, "solved")
            || RequiredBoolean(runtime, "savedDocumentModified")
            || !RequiredBoolean(runtime, "noPathRuntimeErrors"))
        {
            throw new InvalidOperationException("Targeted runtime success/path/disk flags are invalid.");
        }

        string before = runtime.GetProperty("savedDocumentSha256Before").GetString() ?? string.Empty;
        string after = runtime.GetProperty("savedDocumentSha256After").GetString() ?? string.Empty;
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Targeted runtime check changed the saved definition hash.");
        }
        if (runtime.GetProperty("method").GetInt32() != 0)
        {
            throw new InvalidOperationException("Targeted runtime Dendro method is not Continuous (0).");
        }

        string[] expectedStages =
        {
            "gpu-reset",
            "solver-step-1",
            "solver-step-2",
            "solver-step-3",
            "solver-step-4",
            "solver-step-5",
            "dendro-update-rising-edge"
        };
        string[] actualStages = runtime.GetProperty("stages")
            .EnumerateArray()
            .Select(value => value.GetProperty("stage").GetString() ?? string.Empty)
            .ToArray();
        if (!actualStages.SequenceEqual(expectedStages, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Targeted runtime stages are incomplete: " + string.Join(", ", actualStages) + ".");
        }

        RequireTypeFragment(runtime, "dendroOutputTypes", "DendroGH.VolumeGOO", "DendroGH.DendroVolume");
        RequireTypeFragment(runtime, "smoothVolumeOutputTypes", "DendroGH.VolumeGOO", "DendroGH.DendroVolume");
        RequireTypeFragment(runtime, "volumeToMeshOutputTypes", "Rhino.Geometry.Mesh");
    }

    private static bool RequiredBoolean(JsonElement value, string property) =>
        value.GetProperty(property).GetBoolean();

    private static void VerifyDefinitionHashes(HostOptions options, JsonElement reportRoot)
    {
        string manifestPath = Path.Combine(options.Definitions, "_conversion_manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var expected = manifest.RootElement.GetProperty("files")
            .EnumerateArray()
            .Where(value => options.OnlyFile is null
                || string.Equals(
                    value.GetProperty("file").GetString(),
                    options.OnlyFile,
                    StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                value => value.GetProperty("file").GetString() ?? string.Empty,
                value => value.GetProperty("targetSha256").GetString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
        JsonElement files = reportRoot.GetProperty("files");
        if (files.GetArrayLength() != expected.Count)
        {
            throw new InvalidOperationException("Definition hash report does not cover the complete selected manifest set.");
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement file in files.EnumerateArray())
        {
            string name = file.GetProperty("file").GetString() ?? string.Empty;
            if (!seen.Add(name) || !expected.TryGetValue(name, out string? expectedHash))
            {
                throw new InvalidOperationException("Definition hash report contains an unexpected or duplicate file: " + name + ".");
            }
            if (RequiredBoolean(file, "savedDocumentModified"))
            {
                throw new InvalidOperationException("Rhino validation modified the saved definition: " + name + ".");
            }
            foreach (string property in new[]
            {
                "sha256",
                "expectedSha256",
                "savedDocumentSha256Before",
                "savedDocumentSha256After"
            })
            {
                string value = file.GetProperty(property).GetString() ?? string.Empty;
                if (!string.Equals(value, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(name + " has a mismatched " + property + ".");
                }
            }
        }
        if (seen.Count != expected.Count)
        {
            throw new InvalidOperationException("Definition hash report omitted one or more selected manifest files.");
        }
    }

    private static void RequireTypeFragment(JsonElement runtime, string property, params string[] fragments)
    {
        string[] values = runtime.GetProperty(property)
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        if (!values.Any(value => fragments.All(fragment => value.Contains(fragment, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException(
                property + " does not contain " + string.Join(" + ", fragments) + ": " + string.Join(", ", values));
        }
    }

    private static int ManifestFileCount(string definitions)
    {
        string path = Path.Combine(definitions, "_conversion_manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(path));
        return manifest.RootElement.GetProperty("fileCount").GetInt32();
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
