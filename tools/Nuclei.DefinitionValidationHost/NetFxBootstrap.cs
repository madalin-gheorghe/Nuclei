using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Nuclei.DefinitionValidationHost.NetFx
{
    /// <summary>BCL-only bootstrap; Rhino types are isolated in NetFxHost.</summary>
    internal static class NetFxBootstrap
    {
        private const string RhinoSystem = @"C:\Program Files\Rhino 9 WIP\System";
        private const string Grasshopper = @"C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper";
        private static readonly string Pufferfish = ApplicationDataPath("Grasshopper", "Libraries");
        private static readonly string Dendro = ApplicationDataPath(
            "McNeel", "Rhinoceros", "packages", "9.0", "DendroGH", "0.9.1-alpha");
        private static readonly string Ghgl = ApplicationDataPath(
            "McNeel", "Rhinoceros", "packages", "9.0", "ghgl", "9.0.0");
        private static readonly string MeshEdit = ApplicationDataPath(
            "McNeel", "Rhinoceros", "packages", "9.0", "MeshEdit-Components", "2.0.0.0");
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string rhinoSystem = Option(args, "--rhino-system") ?? RhinoSystem;
                string grasshopperDll = Option(args, "--grasshopper");
                string grasshopper = string.IsNullOrWhiteSpace(grasshopperDll)
                    ? Grasshopper
                    : Path.GetDirectoryName(Path.GetFullPath(grasshopperDll));
                string suppliedGha = Option(args, "--v4-gha");
                string suppliedGhaDirectory = string.IsNullOrWhiteSpace(suppliedGha)
                    ? Path.Combine(
                        FindRepositoryRoot(),
                        "Nuclei-v4",
                        "Nuclei4",
                        "bin",
                        "Release",
                        "net48")
                    : Path.GetDirectoryName(Path.GetFullPath(suppliedGha));
                string[] roots = { rhinoSystem, grasshopper, Pufferfish, Dendro, Ghgl, MeshEdit, suppliedGhaDirectory };
                Environment.SetEnvironmentVariable(
                    "PATH",
                    string.Join(Path.PathSeparator.ToString(), roots) + Path.PathSeparator
                    + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty));
                if (!SetDllDirectory(rhinoSystem))
                {
                    throw new InvalidOperationException("SetDllDirectory failed: " + Marshal.GetLastWin32Error());
                }
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs)
                {
                    var name = new AssemblyName(eventArgs.Name).Name;
                    foreach (string root in roots)
                    {
                        foreach (string extension in new[] { ".dll", ".gha" })
                        {
                            string candidate = Path.Combine(root, name + extension);
                            if (File.Exists(candidate))
                            {
                                return Assembly.LoadFrom(candidate);
                            }
                        }
                    }
                    return null;
                };

                string rhinoCommon = Path.Combine(rhinoSystem, "RhinoCommon.dll");
                Assembly installed = Assembly.LoadFrom(rhinoCommon);
                Console.WriteLine("Bootstrapped net48 RhinoCommon: " + installed.Location);
                Console.WriteLine("Bootstrapped net48 RhinoCommon version: " + installed.GetName().Version);
                Type host = Assembly.GetExecutingAssembly().GetType(
                    "Nuclei.DefinitionValidationHost.NetFx.NetFxHost",
                    true);
                return (int)host.GetMethod("Run", BindingFlags.Static | BindingFlags.Public)
                    .Invoke(null, new object[] { args });
            }
            catch (TargetInvocationException error)
            {
                Console.Error.WriteLine(error.InnerException ?? error);
                return 1;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static string Option(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) return null;
            return args[index + 1];
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

        internal static string FindRepositoryRoot()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                DirectoryInfo directory = new DirectoryInfo(Path.GetFullPath(start));
                while (directory != null)
                {
                    string project = Path.Combine(
                        directory.FullName,
                        "tools",
                        "Nuclei.DefinitionValidationHost",
                        "Nuclei.DefinitionValidationHost.NetFx.csproj");
                    if (File.Exists(project) && Directory.Exists(Path.Combine(directory.FullName, "Nuclei-v4")))
                        return directory.FullName;

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException(
                "The Nuclei repository root could not be found from the net48 validation host or current directory. "
                + "Supply the repository-local path options explicitly.");
        }
    }
}
