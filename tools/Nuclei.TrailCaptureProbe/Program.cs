using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetDllDirectory(string path);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool TerminateProcess(IntPtr process, uint exitCode);

    internal static void ExitImmediately(int status)
    {
        Console.Out.Flush();
        Console.Error.Flush();
        // Terminate only this synthetic-document host; Rhino finalizers can block Environment.Exit.
        TerminateProcess(new IntPtr(-1), (uint)status);
        Environment.Exit(status);
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string assembly = Path.GetFullPath(Option(args, "--renderer",
                "Nuclei-v4/Nuclei4.Display.D3D11/bin/Release/net7.0-windows/Nuclei4.Display.D3D11.dll"));
            string system = @"C:\Program Files\Rhino 9 WIP\System";
            string[] roots = { Path.Combine(system, "netcore"), Path.GetDirectoryName(assembly), system };
            SetDllDirectory(system);
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, roots)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                foreach (string root in roots)
                {
                    string path = Path.Combine(root, name.Name + ".dll");
                    if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                }
                return null;
            };
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(roots[0], "RhinoCommon.dll"));
            using var watchdog = new System.Threading.Timer(_ =>
            {
                Console.Error.WriteLine("Probe timed out after 180 seconds.");
                ExitImmediately(3);
            }, null, TimeSpan.FromSeconds(180), Timeout.InfiniteTimeSpan);
            return (int)typeof(Program).Assembly.GetType("CaptureProbe", true).GetMethod("Run")
                .Invoke(null, new object[] { args, assembly });
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.Error.WriteLine(error);
            return 3;
        }
    }

    internal static string Option(string[] args, string name, string fallback)
    {
        int index = Array.IndexOf(args, name);
        return index < 0 ? fallback : args[index + 1];
    }
}
