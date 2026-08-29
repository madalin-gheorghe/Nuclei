using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Nuclei.DefinitionValidationHost;

/// <summary>
/// BCL-only entry point. Do not put RhinoCommon, Grasshopper, or GH_IO types in
/// this class: the CLR must register the installed Rhino 9 WIP resolution roots
/// before it JIT-compiles any method that mentions those types.
/// </summary>
internal static class Program
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string rhinoSystem = FullPathOption(args, "--rhino-system", DefaultRhinoSystem);
            string grasshopper = FullPathOption(args, "--grasshopper", DefaultGrasshopper);
            string v4 = RepositoryFullPathOption(
                args,
                "--v4-gha",
                "Nuclei-v4",
                "Nuclei4",
                "bin",
                "Release",
                "net7.0-windows",
                "Nuclei4.gha");
            string pufferfish = FullPathOption(args, "--pufferfish-gha", DefaultPufferfish);
            string dendro = FullPathOption(args, "--dendro-gha", DefaultDendro);
            string ghgl = FullPathOption(args, "--ghgl-gha", DefaultGhgl);
            string meshEdit = FullPathOption(args, "--mesh-edit-gha", DefaultMeshEdit);

            var roots = new[]
            {
                Path.Combine(rhinoSystem, "netcore"),
                grasshopper,
                Path.GetDirectoryName(pufferfish)!,
                Path.GetDirectoryName(dendro)!,
                Path.GetDirectoryName(ghgl)!,
                Path.GetDirectoryName(meshEdit)!,
                Path.GetDirectoryName(v4)!,
                rhinoSystem
            }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable(
                "PATH",
                string.Join(Path.PathSeparator, roots) + Path.PathSeparator + currentPath);
            if (!SetDllDirectory(rhinoSystem))
            {
                throw new InvalidOperationException(
                    "SetDllDirectory failed for the Rhino System directory (Win32 error "
                    + Marshal.GetLastWin32Error() + ").");
            }

            Assembly? Resolve(AssemblyLoadContext _, AssemblyName name) => ResolveAssembly(name, roots);
            Assembly? ResolveLegacy(object? _, ResolveEventArgs eventArgs) =>
                ResolveAssembly(new AssemblyName(eventArgs.Name), roots);
            AssemblyLoadContext.Default.Resolving += Resolve;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLegacy;

            string rhinoCommon = Path.Combine(rhinoSystem, "netcore", "RhinoCommon.dll");
            if (!File.Exists(rhinoCommon))
            {
                throw new FileNotFoundException("Installed Rhino 9 WIP netcore RhinoCommon was not found.", rhinoCommon);
            }

            // This explicit load is deliberately before the string-based lookup
            // of RhinoHost. It pins the installed WIP RhinoCommon in the default
            // context before any V4 or NuGet RhinoCommon could be considered.
            Assembly installedRhinoCommon = AssemblyLoadContext.Default.LoadFromAssemblyPath(rhinoCommon);
            Console.WriteLine("Bootstrapped RhinoCommon: " + installedRhinoCommon.Location);
            Console.WriteLine("Bootstrapped RhinoCommon version: " + installedRhinoCommon.GetName().Version);

            Type host = Assembly.GetExecutingAssembly().GetType(
                "Nuclei.DefinitionValidationHost.RhinoHost",
                throwOnError: true)!;
            MethodInfo run = host.GetMethod(
                "Run",
                BindingFlags.Static | BindingFlags.Public)
                ?? throw new MissingMethodException(host.FullName, "Run");
            return (int)(run.Invoke(null, new object[] { args }) ?? 1);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            Console.Error.WriteLine(error.InnerException);
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string FullPathOption(string[] args, string name, string fallback)
    {
        int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return Path.GetFullPath(fallback);
        }
        if (index == args.Length - 1)
        {
            throw new ArgumentException("Missing value after " + name + ".");
        }
        return Path.GetFullPath(args[index + 1]);
    }

    private static string RepositoryFullPathOption(string[] args, string name, params string[] segments)
    {
        int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            if (index == args.Length - 1)
                throw new ArgumentException("Missing value after " + name + ".");
            return Path.GetFullPath(args[index + 1]);
        }

        return Path.GetFullPath(Path.Combine(new[] { FindRepositoryRoot() }.Concat(segments).ToArray()));
    }

    internal static string FindRepositoryRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                string project = Path.Combine(
                    directory.FullName,
                    "tools",
                    "Nuclei.DefinitionValidationHost",
                    "Nuclei.DefinitionValidationHost.csproj");
                if (File.Exists(project) && Directory.Exists(Path.Combine(directory.FullName, "Nuclei-v4")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "The Nuclei repository root could not be found from the validation host or current directory. "
            + "Supply the repository-local path options explicitly.");
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

    private static Assembly? ResolveAssembly(AssemblyName name, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(name.Name))
        {
            return null;
        }

        foreach (string root in roots)
        {
            string candidate = Path.Combine(root, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }

            candidate = Path.Combine(root, name.Name + ".gha");
            if (File.Exists(candidate))
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
