using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string assembly = Path.GetFullPath(Option(args, "--assembly") ?? "Nuclei-v3/Nuclei3/bin/Release/net7.0-windows/Nuclei3.gha");
            string[] roots = { Path.GetDirectoryName(assembly), @"C:\Program Files\Rhino 9 WIP\System\netcore", @"C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper", @"C:\Program Files\Rhino 9 WIP\System" };
            SetDllDirectory(roots[3]);
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, roots) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                if (name.Name == "Nuclei3") return AssemblyLoadContext.Default.LoadFromAssemblyPath(assembly);
                foreach (string root in roots)
                {
                    string file = Path.Combine(root, name.Name + ".dll");
                    if (File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
                }
                return null;
            };
            Console.WriteLine("ASSEMBLY " + assembly);
            IDisposable core = null;
            try
            {
                if (!args.Contains("--benchmark"))
                {
                    Assembly rhino = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(roots[1], "RhinoCommon.dll"));
                    core = (IDisposable)Activator.CreateInstance(rhino.GetType("Rhino.Runtime.InProcess.RhinoCore", true),
                        new object[] { new[] { "/safemode", "/nosplash", "/notemplate" }, Enum.Parse(rhino.GetType("Rhino.Runtime.InProcess.WindowStyle", true), "NoWindow") });
                }
                return (int)typeof(Program).Assembly.GetType("Probe").GetMethod("Run").Invoke(null, new object[] { args });
            }
            finally { core?.Dispose(); }
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.Error.WriteLine(error);
            return 1;
        }
    }
    internal static string Option(string[] args, string name) { int i = Array.IndexOf(args, name); return i < 0 ? null : args[i + 1]; }
}
