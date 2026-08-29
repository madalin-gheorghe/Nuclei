using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.PlugIns;
using Rhino.Runtime;
using Rhino.Runtime.InProcess;

namespace Nuclei.DefinitionValidationHost.NetFx
{
    internal static class NetFxHost
    {
        private static string Definitions = string.Empty;
        private static string V4Gha = string.Empty;
        private static string Map = string.Empty;
        private static string Validator = string.Empty;
        private static string GrasshopperDll = @"C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper\Grasshopper.dll";
        private const string RhinoPythonHost = @"C:\Program Files\Rhino 9 WIP\Plug-ins\IronPython\RhinoPythonHost.dll";
        private static string _stage = "process-start";
        private static long _deadline = long.MaxValue;
        private static int _completed;

        public static int Run(string[] args)
        {
            Definitions = RepositoryOption(args, "--definitions", "Nuclei Definitions", "v4_updated");
            V4Gha = RepositoryOption(
                args,
                "--v4-gha",
                "Nuclei-v4",
                "Nuclei4",
                "bin",
                "Release",
                "net48",
                "Nuclei4.gha");
            Map = RepositoryOption(
                args,
                "--map",
                "tools",
                "Nuclei.DefinitionConverter",
                "v3.3-to-v4.json");
            Validator = RepositoryOption(
                args,
                "--validator-script",
                "tools",
                "Nuclei.DefinitionValidator",
                "ValidateInRhino.py");
            string onlyFile = Option(args, "--only-file");
            string skipExtras = Option(args, "--skip-extras");
            int timeout = ParseTimeout(Option(args, "--stage-timeout-seconds"));
            string profile = CreateProfile();
            string report = Path.Combine(Definitions, "_rhino9_validation.json");
            string progress = Path.Combine(Definitions, "_rhino9_validation.progress.json");
            RhinoCore core = null;
            RhinoDoc document = null;
            try
            {
                string suppliedGrasshopper = Option(args, "--grasshopper");
                if (!string.IsNullOrWhiteSpace(suppliedGrasshopper)) GrasshopperDll = Path.GetFullPath(suppliedGrasshopper);
                bool standaloneDiagnostic = Option(args, "--dump-component-schema") != null
                    || Option(args, "--structural-roundtrip") != null
                    || Option(args, "--legacy-trail-roundtrip") != null;
                ValidateInputs(!standaloneDiagnostic);
                string expectedV4Sha256 = NormalizeExpectedSha256(Option(args, "--expected-v4-sha256"));
                if (expectedV4Sha256 == null)
                    expectedV4Sha256 = Sha256(V4Gha);
                if (!string.Equals(Sha256(V4Gha), expectedV4Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The requested net48 GHA does not match --expected-v4-sha256.");
                if (!standaloneDiagnostic)
                {
                    File.Delete(report);
                    File.Delete(progress);
                }
                ConfigureEnvironment(profile, onlyFile, skipExtras, expectedV4Sha256);
                StartWatchdog(timeout);

                Stage("net48-rhino-core-starting", timeout);
                core = new RhinoCore(new[] { "/safemode", "/nosplash", "/notemplate" }, WindowStyle.NoWindow);
                Stage("net48-rhino-core-started", timeout);
                Console.WriteLine("Rhino version: " + RhinoApp.Version);
                AssertSafeModeAndNoMcp();
                PreloadGrasshopper(profile, skipExtras, core, timeout);

                string legacyTrailRoundTrip = Option(args, "--legacy-trail-roundtrip");
                if (!string.IsNullOrWhiteSpace(legacyTrailRoundTrip))
                {
                    string legacyTrailOutput = Option(args, "--legacy-trail-roundtrip-output");
                    if (string.IsNullOrWhiteSpace(legacyTrailOutput))
                        throw new ArgumentException("--legacy-trail-roundtrip-output is required.");
                    ValidateLegacyTrailRoundTrip(
                        Path.GetFullPath(legacyTrailRoundTrip),
                        Path.GetFullPath(legacyTrailOutput),
                        Guid.Parse(Option(args, "--trail-component-guid")
                            ?? "cd0bb03c-2b66-4dbb-864e-02015f0255e7"));
                    Stage("complete", timeout);
                    Interlocked.Exchange(ref _completed, 1);
                    return 0;
                }

                string structuralRoundTrip = Option(args, "--structural-roundtrip");
                if (!string.IsNullOrWhiteSpace(structuralRoundTrip))
                {
                    string structuralRoundTripOutput = Option(args, "--structural-roundtrip-output");
                    if (string.IsNullOrWhiteSpace(structuralRoundTripOutput))
                        throw new ArgumentException("--structural-roundtrip-output is required.");
                    ValidateStructuralRoundTrip(
                        Path.GetFullPath(structuralRoundTrip),
                        Path.GetFullPath(structuralRoundTripOutput));
                    Stage("complete", timeout);
                    Interlocked.Exchange(ref _completed, 1);
                    return 0;
                }

                string dumpComponentSchema = Option(args, "--dump-component-schema");
                if (!string.IsNullOrWhiteSpace(dumpComponentSchema))
                {
                    DumpComponentSchema(
                        Guid.Parse(dumpComponentSchema),
                        Option(args, "--dump-component-schema-output"));
                    Stage("complete", timeout);
                    Interlocked.Exchange(ref _completed, 1);
                    return 0;
                }

                var python = PythonScript.Create();
                if (python == null)
                {
                    Stage("net48-ironpython-host-loading", timeout);
                    var assembly = Assembly.LoadFrom(RhinoPythonHost);
                    var type = assembly.GetType("RhinoPython.PythonScriptScope", true);
                    python = Activator.CreateInstance(type) as PythonScript;
                    if (python == null)
                    {
                        throw new InvalidOperationException(
                            "The installed RhinoPythonHost did not create a PythonScriptScope.");
                    }
                    var setSearchPaths = type.GetMethod(
                        "SetSearchPaths",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(IEnumerable<string>) },
                        null);
                    if (setSearchPaths == null)
                    {
                        throw new MissingMethodException(type.FullName, "SetSearchPaths(IEnumerable<string>)");
                    }
                    setSearchPaths.Invoke(
                        python,
                        new object[] { new[] { Path.Combine(Path.GetDirectoryName(RhinoPythonHost), "Lib") } });
                    AssertSafeModeAndNoMcp();
                    Console.WriteLine("Created net48 Python scope directly from " + assembly.Location + ".");
                    Stage("net48-ironpython-host-loaded", timeout);
                }
                python.Output = delegate(string value)
                {
                    Console.Write(value);
                    Console.Out.Flush();
                };
                document = RhinoDoc.ActiveDoc ?? RhinoDoc.CreateHeadless(null);
                if (document != null)
                {
                    python.SetupScriptContext(document);
                }

                var monitorStop = new ManualResetEvent(false);
                var monitor = new Thread(delegate() { MonitorProgress(progress, timeout, monitorStop); });
                monitor.IsBackground = true;
                monitor.Name = "Nuclei net48 validator progress";
                monitor.Start();
                try
                {
                    Stage("net48-python-validator-executing", timeout);
                    if (!python.ExecuteFile(Validator))
                    {
                        throw new InvalidOperationException("Python validator did not execute.");
                    }
                    core.DoEvents();
                }
                finally
                {
                    monitorStop.Set();
                    monitor.Join(2000);
                    monitorStop.Dispose();
                }

                Stage("net48-report-verifying", timeout);
                VerifyReport(report, progress, onlyFile, expectedV4Sha256);
                Console.WriteLine(File.ReadAllText(report));
                Stage("complete", timeout);
                Interlocked.Exchange(ref _completed, 1);
                return 0;
            }
            catch (Exception error)
            {
                Interlocked.Exchange(ref _completed, 1);
                Console.Error.WriteLine("FAILED at " + _stage + ": " + error);
                return 1;
            }
            finally
            {
                if (document != null && document != RhinoDoc.ActiveDoc)
                {
                    document.Dispose();
                }
                if (core != null)
                {
                    core.Dispose();
                }
                DeleteProfile(profile);
            }
        }

        private static void ConfigureEnvironment(string profile, string onlyFile, string skipExtras, string expectedV4Sha256)
        {
            Set("NUCLEI_VALIDATION_DEFINITIONS", Definitions);
            Set("NUCLEI_VALIDATION_V4_GHA", V4Gha);
            Set("NUCLEI_VALIDATION_MAP", Map);
            Set("NUCLEI_VALIDATION_NORMALIZE", "0");
            Set("NUCLEI_VALIDATION_SOLVE_DENDRO", "1");
            Set("NUCLEI_VALIDATION_ORIGINAL_APPDATA", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            Set("NUCLEI_VALIDATION_GRASSHOPPER_DLL", GrasshopperDll);
            Set("NUCLEI_VALIDATION_ISOLATED_GH_APPDATA", profile);
            Set("NUCLEI_VALIDATION_AUTOLOAD", "0");
            Set("NUCLEI_VALIDATION_USE_NORMAL_PROFILE", "0");
            Set("NUCLEI_VALIDATION_GRASSHOPPER_PRELOADED", "1");
            Set("NUCLEI_VALIDATION_START_AT", null);
            Set("NUCLEI_VALIDATION_ONLY_FILE", onlyFile);
            Set("NUCLEI_VALIDATION_SKIP_EXTRAS", skipExtras);
            Set("NUCLEI_VALIDATION_EXPECTED_V4_SHA256", expectedV4Sha256);
        }

        private static void AssertSafeModeAndNoMcp()
        {
            if (!RhinoApp.IsSafeModeEnabled)
                throw new InvalidOperationException("Rhino did not enter requested net48 safe mode.");
            foreach (var id in new[]
            {
                new Guid("2668d7ed-f507-4a68-8295-8172147a0e39"),
                new Guid("9d864247-4774-464c-bd81-1a11953b2f8f"),
                new Guid("f0b5b632-cc3c-43e7-bc88-da29c47b98bc")
            })
            {
                var info = PlugIn.GetPlugInInfo(id);
                if (info != null && info.IsLoaded)
                    throw new InvalidOperationException("Safe mode loaded forbidden Rhino-MCP plug-in " + id + ".");
            }
            Console.WriteLine("Net48 safe mode active; all three Rhino-MCP plug-ins are unloaded.");
        }

        private static void PreloadGrasshopper(string profile, string skipExtras, RhinoCore core, int timeout)
        {
            Stage("net48-grasshopper-direct-initializing", timeout);
            var appData = typeof(Grasshopper.Folders).GetField(
                "m_appdataFolder",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (appData == null)
                throw new MissingFieldException(typeof(Grasshopper.Folders).FullName, "m_appdataFolder");
            appData.SetValue(null, profile + Path.DirectorySeparatorChar);
            GH_DocumentIO.DisableOverwriteProtection = true;
            var server = Grasshopper.Instances.ComponentServer;
            if (server == null)
                throw new InvalidOperationException("Safe-mode net48 Grasshopper ComponentServer is unavailable.");

            var skipped = new HashSet<string>(
                (skipExtras ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
            var extras = new[]
            {
                Tuple.Create(ApplicationDataPath("Grasshopper", "Libraries", "Pufferfish3-0.gha"), "pufferfish"),
                Tuple.Create(ApplicationDataPath("McNeel", "Rhinoceros", "packages", "9.0", "DendroGH", "0.9.1-alpha", "DendroGH.gha"), "dendro"),
                Tuple.Create(ApplicationDataPath("McNeel", "Rhinoceros", "packages", "9.0", "ghgl", "9.0.0", "ghgl.gha"), "ghgl"),
                Tuple.Create(ApplicationDataPath("McNeel", "Rhinoceros", "packages", "9.0", "MeshEdit-Components", "2.0.0.0", "Meshedit2000.gha"), "mesh-edit")
            };
            foreach (var extra in extras)
            {
                if (skipped.Contains(Path.GetFileName(extra.Item1))) continue;
                int loadTimeout = extra.Item2 == "dendro" ? Math.Min(timeout, 60) : timeout;
                LoadGha(server, extra.Item1, extra.Item2, loadTimeout);
            }
            LoadGha(server, V4Gha, "nuclei-v4-net48", timeout);
            core.DoEvents();
            Stage("net48-grasshopper-direct-ready", timeout);
        }

        private static void LoadGha(GH_ComponentServer server, string path, string label, int timeout)
        {
            Stage("net48-gha-loading:" + label, timeout);
            var method = server.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(candidate =>
                {
                    if (candidate.Name != "LoadGHA") return false;
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 2
                        && parameters[0].ParameterType == typeof(GH_ExternalFile)
                        && parameters[1].ParameterType == typeof(bool);
                });
            if (method == null)
                throw new MissingMethodException(server.GetType().FullName, "LoadGHA(GH_ExternalFile, bool)");
            object loadResult = method.Invoke(server, new object[] { new GH_ExternalFile(path), false });
            Console.WriteLine("LoadGHA result for " + label + ": " + (loadResult ?? "<null>"));
            Stage("net48-gha-loaded:" + label, timeout);
        }

        private static void DumpComponentSchema(Guid componentGuid, string outputPath)
        {
            Stage("net48-component-schema-emitting", 60);
            var server = Grasshopper.Instances.ComponentServer
                ?? throw new InvalidOperationException("Grasshopper ComponentServer is unavailable.");
            IGH_DocumentObject component = server.EmitObject(componentGuid);
            if (component == null)
                throw new InvalidOperationException("ComponentServer could not emit " + componentGuid + ".");
            bool previousSolutions = GH_Document.EnableSolutions;
            var document = new GH_Document();
            try
            {
                GH_Document.EnableSolutions = false;
                document.Enabled = false;
                component.CreateAttributes();
                if (!document.AddObject(component, false))
                    throw new InvalidOperationException("Grasshopper could not add emitted component to a document.");
                component.Attributes.ExpireLayout();
                component.Attributes.PerformLayout();

                var archive = new GH_Archive();
                if (!archive.AppendObject(component, "Object"))
                    throw new InvalidOperationException("GH_IO could not serialize emitted component " + componentGuid + ".");
                string xml = archive.Serialize_Xml();
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    string fullPath = Path.GetFullPath(outputPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    File.WriteAllText(fullPath, xml);
                    Console.WriteLine("Serialized fresh in-document component schema to " + fullPath + ".");
                }
                else
                {
                    Console.WriteLine(xml);
                }
            }
            finally
            {
                document.Dispose();
                GH_Document.EnableSolutions = previousSolutions;
            }
            Stage("net48-component-schema-emitted", 60);
        }

        private static void ValidateStructuralRoundTrip(string sourcePath, string roundTripPath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Structural validation definition was not found.", sourcePath);
            if (string.IsNullOrWhiteSpace(roundTripPath))
                throw new ArgumentException("--structural-roundtrip-output is required.");
            if (File.Exists(roundTripPath))
                throw new IOException("Structural round-trip output already exists: " + roundTripPath);
            Directory.CreateDirectory(Path.GetDirectoryName(roundTripPath));

            bool previousSolutions = GH_Document.EnableSolutions;
            GH_Document first = null;
            GH_Document second = null;
            try
            {
                GH_Document.EnableSolutions = false;
                Stage("net48-standard-document-open:first", 180);
                var firstIo = new GH_DocumentIO();
                if (!firstIo.Open(sourcePath) || firstIo.Document == null)
                    throw new InvalidOperationException("Standard GH_DocumentIO.Open rejected " + sourcePath + ".");
                first = firstIo.Document;
                first.Enabled = false;
                JObject firstResult = ValidateSolverDocument(first, "first-open");

                Stage("net48-standard-document-save", 180);
                if (!firstIo.SaveQuiet(roundTripPath))
                    throw new InvalidOperationException("GH_DocumentIO.SaveQuiet rejected " + roundTripPath + ".");

                Stage("net48-standard-document-open:roundtrip", 180);
                var secondIo = new GH_DocumentIO();
                if (!secondIo.Open(roundTripPath) || secondIo.Document == null)
                    throw new InvalidOperationException("Standard GH_DocumentIO.Open rejected the round-trip copy.");
                second = secondIo.Document;
                second.Enabled = false;
                JObject secondResult = ValidateSolverDocument(second, "roundtrip-open");

                Console.WriteLine(new JObject
                {
                    ["success"] = true,
                    ["source"] = sourcePath,
                    ["sourceSha256"] = Sha256(sourcePath),
                    ["roundTrip"] = roundTripPath,
                    ["roundTripSha256"] = Sha256(roundTripPath),
                    ["first"] = firstResult,
                    ["second"] = secondResult
                }.ToString());
            }
            finally
            {
                if (second != null) second.Dispose();
                if (first != null) first.Dispose();
                GH_Document.EnableSolutions = previousSolutions;
            }
        }

        private static void ValidateLegacyTrailRoundTrip(string sourcePath, string roundTripPath, Guid componentGuid)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Legacy Trail Settings definition was not found.", sourcePath);
            if (File.Exists(roundTripPath))
                throw new IOException("Legacy Trail Settings round-trip output already exists: " + roundTripPath);
            Directory.CreateDirectory(Path.GetDirectoryName(roundTripPath));

            IGH_DocumentObject emitted = Grasshopper.Instances.ComponentServer.EmitObject(componentGuid);
            Console.WriteLine("Focused Trail Settings emit result: " + (emitted == null ? "<null>" : emitted.GetType().FullName));

            bool previousSolutions = GH_Document.EnableSolutions;
            GH_Document first = null;
            GH_Document second = null;
            try
            {
                GH_Document.EnableSolutions = false;
                var firstIo = new GH_DocumentIO();
                if (!firstIo.Open(sourcePath) || firstIo.Document == null)
                    throw new InvalidOperationException("Grasshopper rejected the legacy Trail Settings archive.");
                first = firstIo.Document;
                first.Enabled = false;
                AssertSingleInputTrailSettings(first, "legacy-open", componentGuid);

                if (!firstIo.SaveQuiet(roundTripPath))
                    throw new InvalidOperationException("Grasshopper could not save the migrated Trail Settings archive.");

                var secondIo = new GH_DocumentIO();
                if (!secondIo.Open(roundTripPath) || secondIo.Document == null)
                    throw new InvalidOperationException("Grasshopper could not reopen the migrated Trail Settings archive.");
                second = secondIo.Document;
                second.Enabled = false;
                AssertSingleInputTrailSettings(second, "migrated-reopen", componentGuid);

                var archive = new GH_Archive();
                if (!archive.ReadFromFile(roundTripPath))
                    throw new InvalidOperationException("GH_IO could not inspect the migrated Trail Settings archive.");
                string xml = archive.Serialize_Xml();
                if (xml.IndexOf("Frequency Of Particle Trail Sampling", StringComparison.Ordinal) >= 0
                    || xml.IndexOf(">Trail Frequency<", StringComparison.Ordinal) >= 0)
                    throw new InvalidOperationException("The migrated archive still serializes the retired Trail Frequency parameter.");

                Console.WriteLine("Legacy two-input Trail Settings opened, saved, and reopened as the one-input schema.");
            }
            finally
            {
                if (second != null) second.Dispose();
                if (first != null) first.Dispose();
                GH_Document.EnableSolutions = previousSolutions;
            }
        }

        private static void AssertSingleInputTrailSettings(GH_Document document, string stage, Guid componentGuid)
        {
            GH_Component component = document.Objects.OfType<GH_Component>()
                .Single(value => value.ComponentGuid == componentGuid);
            if (component.Params.Input.Count != 1
                || component.Params.Output.Count != 1
                || !string.Equals(component.Params.Input[0].Name, "Trail Size", StringComparison.Ordinal)
                || !string.Equals(component.Params.Output[0].Name, "Trail Settings", StringComparison.Ordinal))
                throw new InvalidOperationException(stage + " did not materialize the one-input Trail Settings schema: inputs="
                    + component.Params.Input.Count + " [" + string.Join(", ", component.Params.Input.Select(value => value.Name))
                    + "], outputs=" + component.Params.Output.Count + " [" + string.Join(", ", component.Params.Output.Select(value => value.Name)) + "].");
        }

        private static JObject ValidateSolverDocument(GH_Document document, string stage)
        {
            IGH_DocumentObject[] objects = document.Objects.ToArray();
            IGH_DocumentObject[] placeholders = objects.Where(value =>
            {
                string name = value.GetType().FullName ?? value.GetType().Name;
                return name.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("UnknownObject", StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToArray();
            if (placeholders.Length != 0)
                throw new InvalidOperationException(stage + " contains " + placeholders.Length + " unresolved objects: "
                    + string.Join("; ", placeholders.Select(value => value.InstanceGuid.ToString("D") + " | " + value.Name + " | " + value.GetType().FullName)));

            var solverGuid = new Guid("e794ab27-6d27-4107-929f-b88e16209976");
            GH_Component solver = objects.OfType<GH_Component>().Single(value => value.ComponentGuid == solverGuid);
            if (solver.Params.Input.Count != 4 || solver.Params.Output.Count != 3)
                throw new InvalidOperationException(stage + " SolverGPU parameter count is not 4 inputs / 3 outputs.");
            IGH_Param status = solver.Params.Output[2];
            if (!string.Equals(status.Name, "GPU Status", StringComparison.Ordinal)
                || !string.Equals(status.NickName, "status", StringComparison.Ordinal)
                || !string.Equals(status.GetType().FullName, "Grasshopper.Kernel.Parameters.Param_String", StringComparison.Ordinal)
                || status.SourceCount != 0)
                throw new InvalidOperationException(stage + " SolverGPU output 2 schema/type is invalid.");

            solver.Attributes.PerformLayout();
            var inputGeometry = new JArray(solver.Params.Input.Select(parameter => Geometry(parameter.Attributes)));
            var outputGeometry = new JArray(solver.Params.Output.Select(parameter => Geometry(parameter.Attributes)));
            AssertOrderedNonOverlapping(stage + " inputs", solver.Params.Input.Select(parameter => parameter.Attributes.Bounds).ToArray());
            AssertOrderedNonOverlapping(stage + " outputs", solver.Params.Output.Select(parameter => parameter.Attributes.Bounds).ToArray());

            int wireCount = objects.Sum(value =>
            {
                var component = value as GH_Component;
                if (component != null) return component.Params.Input.Sum(input => input.SourceCount);
                var parameter = value as IGH_Param;
                return parameter == null ? 0 : parameter.SourceCount;
            });
            return new JObject
            {
                ["objectCount"] = objects.Length,
                ["wireCount"] = wireCount,
                ["solverInputCount"] = solver.Params.Input.Count,
                ["solverOutputCount"] = solver.Params.Output.Count,
                ["statusType"] = status.GetType().FullName,
                ["statusInstanceGuid"] = status.InstanceGuid.ToString("D"),
                ["unresolvedObjectCount"] = placeholders.Length,
                ["componentGeometry"] = Geometry(solver.Attributes),
                ["inputGeometry"] = inputGeometry,
                ["outputGeometry"] = outputGeometry
            };
        }

        private static JObject Geometry(IGH_Attributes attributes)
        {
            return new JObject
            {
                ["x"] = attributes.Bounds.X,
                ["y"] = attributes.Bounds.Y,
                ["width"] = attributes.Bounds.Width,
                ["height"] = attributes.Bounds.Height,
                ["pivotX"] = attributes.Pivot.X,
                ["pivotY"] = attributes.Pivot.Y
            };
        }

        private static void AssertOrderedNonOverlapping(string label, System.Drawing.RectangleF[] bounds)
        {
            for (int index = 1; index < bounds.Length; index++)
            {
                if (bounds[index].Top + 0.001f < bounds[index - 1].Bottom)
                    throw new InvalidOperationException(label + " overlap after standard document materialization.");
            }
        }

        private static void Set(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, string.IsNullOrWhiteSpace(value) ? null : value);
        }

        private static void MonitorProgress(string path, int timeout, WaitHandle stop)
        {
            string previous = null;
            while (!stop.WaitOne(250))
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    string status = (string)JObject.Parse(ReadSharedText(path))["status"];
                    if (string.IsNullOrWhiteSpace(status) || status == previous) continue;
                    previous = status;
                    int stageTimeout = status.IndexOf("DendroGH.gha", StringComparison.OrdinalIgnoreCase) >= 0
                        ? Math.Min(timeout, 60)
                        : timeout;
                    Stage("net48-python:" + status, stageTimeout);
                }
                catch (IOException) { }
                catch (Newtonsoft.Json.JsonException) { }
            }
        }

        private static string ReadSharedText(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
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

        private static void VerifyReport(string reportPath, string progressPath, string onlyFile, string expectedV4Sha256)
        {
            if (!File.Exists(reportPath)) throw new InvalidOperationException("No validation report was written.");
            if (File.Exists(progressPath)) throw new InvalidOperationException("Incomplete progress file remains.");
            var root = JObject.Parse(File.ReadAllText(reportPath));
            if (!(bool)root["success"])
            {
                throw new InvalidOperationException("Validator failed: " + (string)root["error"]);
            }
            int expectedFiles = string.IsNullOrWhiteSpace(onlyFile)
                ? (int)JObject.Parse(File.ReadAllText(Path.Combine(Definitions, "_conversion_manifest.json")))["fileCount"]
                : 1;
            if ((int)root["fileCount"] != expectedFiles) throw new InvalidOperationException("Wrong validated file count.");
            string exactGhaHash = Sha256(V4Gha);
            if (!string.Equals(exactGhaHash, expectedV4Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The final V4 GHA hash changed after validation began.");
            if (!string.Equals((string)root["v4GhaSha256"], exactGhaHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Report identifies the wrong V4 GHA.");
            foreach (string property in new[] { "v4GhaSha256Before", "v4GhaSha256After", "expectedV4GhaSha256" })
                if (!string.Equals((string)root[property], exactGhaHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(property + " does not identify the exact final V4 GHA hash.");
            if (!(bool)root["v4GhaUnchangedDuringValidation"] || (bool)root["normalized"])
                throw new InvalidOperationException("Final validation must keep the V4 GHA and saved definitions unchanged.");
            VerifyDefinitionHashes(root, onlyFile);
            foreach (string flag in new[] { "wireParityAfterApprovedSchemaAdapters", "allFilesOpened", "noMissingObjects", "noV3Residue", "structurePreserved" })
                if (!(bool)root[flag]) throw new InvalidOperationException("Report flag is false: " + flag);

            var checks = (JArray)root["targetedRuntimeChecks"];
            if (checks.Count != 1) throw new InvalidOperationException("Expected exactly one runtime check.");
            var runtime = (JObject)checks[0];
            if (!(bool)runtime["solved"] || (bool)runtime["savedDocumentModified"] || !(bool)runtime["noPathRuntimeErrors"])
                throw new InvalidOperationException("Runtime success/disk/error flags are invalid.");
            if ((int)runtime["method"] != 0) throw new InvalidOperationException("Dendro method is not 0.");
            if (!string.Equals((string)runtime["savedDocumentSha256Before"], (string)runtime["savedDocumentSha256After"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Saved definition hash changed.");
            string[] wanted = { "gpu-reset", "solver-step-1", "solver-step-2", "solver-step-3", "solver-step-4", "solver-step-5", "dendro-update-rising-edge" };
            string[] actual = ((JArray)runtime["stages"]).Select(value => (string)value["stage"]).ToArray();
            if (!wanted.SequenceEqual(actual)) throw new InvalidOperationException("Runtime stage sequence is incomplete.");
            RequireTypes(runtime, "dendroOutputTypes", "DendroGH.VolumeGOO", "DendroGH.DendroVolume");
            RequireTypes(runtime, "smoothVolumeOutputTypes", "DendroGH.VolumeGOO", "DendroGH.DendroVolume");
            RequireTypes(runtime, "volumeToMeshOutputTypes", "Rhino.Geometry.Mesh");
        }

        private static void VerifyDefinitionHashes(JObject report, string onlyFile)
        {
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(Definitions, "_conversion_manifest.json")));
            var expected = ((JArray)manifest["files"])
                .OfType<JObject>()
                .Where(value => string.IsNullOrWhiteSpace(onlyFile)
                    || string.Equals((string)value["file"], onlyFile, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    value => (string)value["file"],
                    value => (string)value["targetSha256"],
                    StringComparer.OrdinalIgnoreCase);
            var files = (JArray)report["files"];
            if (files.Count != expected.Count)
                throw new InvalidOperationException("Definition hash report does not cover the complete selected manifest set.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files.OfType<JObject>())
            {
                string name = (string)file["file"];
                string expectedHash;
                if (!seen.Add(name) || !expected.TryGetValue(name, out expectedHash))
                    throw new InvalidOperationException("Definition hash report contains an unexpected or duplicate file: " + name + ".");
                if ((bool)file["savedDocumentModified"])
                    throw new InvalidOperationException("Rhino validation modified the saved definition: " + name + ".");
                foreach (string property in new[] { "sha256", "expectedSha256", "savedDocumentSha256Before", "savedDocumentSha256After" })
                    if (!string.Equals((string)file[property], expectedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(name + " has a mismatched " + property + ".");
            }
            if (seen.Count != expected.Count)
                throw new InvalidOperationException("Definition hash report omitted one or more selected manifest files.");
        }

        private static void RequireTypes(JObject runtime, string property, params string[] fragments)
        {
            var values = ((JArray)runtime[property]).Select(value => (string)value).ToArray();
            if (!values.Any(value => fragments.All(fragment => value.Contains(fragment))))
                throw new InvalidOperationException(property + " lacks " + string.Join(" + ", fragments));
        }

        private static string Sha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string Option(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= args.Length) throw new ArgumentException("Missing value after " + name);
                return args[i + 1];
            }
            return null;
        }

        private static string RepositoryOption(string[] args, string name, params string[] segments)
        {
            string supplied = Option(args, name);
            if (!string.IsNullOrWhiteSpace(supplied))
                return Path.GetFullPath(supplied);

            string[] parts = new[] { NetFxBootstrap.FindRepositoryRoot() }
                .Concat(segments)
                .ToArray();
            return Path.GetFullPath(Path.Combine(parts));
        }

        private static string NormalizeExpectedSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim().ToUpperInvariant();
            if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
                throw new ArgumentException("--expected-v4-sha256 must contain exactly 64 hexadecimal characters.");
            return normalized;
        }

        private static int ParseTimeout(string value)
        {
            int result;
            if (value == null) return 900;
            if (!int.TryParse(value, out result) || result < 30 || result > 1800)
                throw new ArgumentOutOfRangeException("--stage-timeout-seconds");
            return result;
        }

        private static void ValidateInputs(bool requireValidatorInputs)
        {
            var paths = new List<string> { V4Gha, GrasshopperDll };
            if (requireValidatorInputs)
            {
                paths.AddRange(new[] { Definitions, Map, Validator, RhinoPythonHost, Path.Combine(Path.GetDirectoryName(RhinoPythonHost), "Lib"), Path.Combine(Definitions, "_conversion_manifest.json") });
            }
            foreach (string path in paths)
                if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Required path missing", path);
        }

        private static string CreateProfile()
        {
            string path = Path.Combine(Path.GetTempPath(), "NucleiDefinitionValidationNetFx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(path, "Libraries"));
            return Path.GetFullPath(path);
        }

        private static void DeleteProfile(string path)
        {
            string expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar + "NucleiDefinitionValidationNetFx-";
            if (path.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
                Directory.Delete(path, true);
        }

        private static void Stage(string value, int timeout)
        {
            _stage = value;
            Interlocked.Exchange(ref _deadline, DateTime.UtcNow.AddSeconds(timeout).Ticks);
            Console.WriteLine(DateTime.UtcNow.ToString("O") + " " + value);
            Console.Out.Flush();
        }

        private static void StartWatchdog(int timeout)
        {
            Stage("net48-watchdog-started", timeout);
            var thread = new Thread(delegate()
            {
                while (Thread.VolatileRead(ref _completed) == 0)
                {
                    Thread.Sleep(250);
                    if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _deadline)) continue;
                    Environment.FailFast("Net48 validation stage timed out: " + _stage);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
