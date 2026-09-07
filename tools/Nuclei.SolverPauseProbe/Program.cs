using System.Reflection;
using System.Runtime.InteropServices;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetDllDirectory(string path);

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string[] roots =
            {
#if NETFRAMEWORK
                @"C:\Program Files\Rhino 9 WIP\System",
#else
                @"C:\Program Files\Rhino 9 WIP\System\netcore",
#endif
                @"C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper",
                @"C:\Program Files\Rhino 9 WIP\System"
            };
            SetDllDirectory(roots[2]);
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator.ToString(), roots)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            Assembly Resolve(AssemblyName name)
            {
                foreach (string root in roots)
                {
                    string path = Path.Combine(root, name.Name + ".dll");
                    if (File.Exists(path)) return LoadAssembly(path);
                }
                return null;
            }
#if NETFRAMEWORK
            AppDomain.CurrentDomain.AssemblyResolve += (_, item) => Resolve(new AssemblyName(item.Name));
#else
            AssemblyLoadContext.Default.Resolving += (_, name) => Resolve(name);
#endif
            Assembly rhino = LoadAssembly(Path.Combine(roots[0], "RhinoCommon.dll"));
            using var watchdog = new System.Threading.Timer(_ =>
            {
                Console.Error.WriteLine("Probe timed out during Rhino/Grasshopper initialization or execution.");
                Environment.Exit(2);
            }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
            Type coreType = rhino.GetType("Rhino.Runtime.InProcess.RhinoCore", true);
            Type windowStyle = rhino.GetType("Rhino.Runtime.InProcess.WindowStyle", true);
            Console.WriteLine("Starting isolated Rhino runtime with no window, in safe mode.");
            using var core = (IDisposable)Activator.CreateInstance(coreType,
                new object[] { new[] { "/safemode", "/nosplash", "/notemplate" }, Enum.Parse(windowStyle, "NoWindow") });
            return (int)typeof(Program).Assembly.GetType("PauseProbe", true)
                .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { args });
        }
        catch (Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null)
                exception = exception.InnerException;
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    internal static Assembly LoadAssembly(string path)
    {
#if NETFRAMEWORK
        return Assembly.LoadFrom(path);
#else
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
#endif
    }
}
