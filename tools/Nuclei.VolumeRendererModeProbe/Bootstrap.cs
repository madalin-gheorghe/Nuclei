using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

// Deliberately BCL-only. Rhino and Grasshopper types live in separate stages so
// the JIT cannot load package assemblies before the installed Rhino resolver.
internal static class Program
{
    internal const string RhinoSystem = @"C:\Program Files\Rhino 9 WIP\System";
    internal const string GrasshopperRoot = @"C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetDllDirectory(string path);

    [DllImport("kernel32.dll")]
    static extern bool TerminateProcess(IntPtr process, uint code);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 3 || !args.Contains("--run"))
        {
            Console.WriteLine("Usage: Nuclei.VolumeRendererModeProbe <built Nuclei4.gha> <new scratch output directory> --run");
            Console.WriteLine("Without --run no Rhino host is started. Only synthetic test documents are created.");
            return 2;
        }

        int status = 1;
        try
        {
            string gha = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            if (!File.Exists(gha)) throw new FileNotFoundException("Built GHA not found.", gha);
            string allowed = Path.GetFullPath(@"C:\Nuclei\.codex-temp") + Path.DirectorySeparatorChar;
            if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
                throw new IOException("Choose a new directory below C:/Nuclei/.codex-temp; existing output is never overwritten.");

            var roots = new[] { Path.Combine(RhinoSystem, "netcore"), GrasshopperRoot, Path.GetDirectoryName(gha), RhinoSystem };
            SetDllDirectory(RhinoSystem);
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, roots)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                foreach (string root in roots)
                {
                    foreach (string extension in new[] { ".dll", ".gha" })
                    {
                        string candidate = Path.Combine(root, name.Name + extension);
                        if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
                    }
                }
                return null;
            };
            Assembly installed = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(RhinoSystem, "netcore", "RhinoCommon.dll"));
            Console.WriteLine("Bootstrapped RhinoCommon: " + installed.Location);
            new Thread(() =>
            {
                Thread.Sleep(180000);
                Console.Error.WriteLine("Renderer mode probe watchdog expired after 180 seconds.");
                Console.Error.Flush();
                TerminateProcess(new IntPtr(-1), 3);
            }) { IsBackground = true }.Start();
            typeof(Program).Assembly.GetType("RhinoStage", true).GetMethod("Run").Invoke(null, new object[] { gha, output });
            status = 0;
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.Error.WriteLine(error);
        }
        Console.Out.Flush();
        Console.Error.Flush();
        // Own isolated process only: Rhino native finalizers may block normal shutdown.
        TerminateProcess(new IntPtr(-1), (uint)status);
        return status;
    }
}
