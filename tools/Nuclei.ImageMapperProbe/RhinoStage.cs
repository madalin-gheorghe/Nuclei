using Rhino.Runtime.InProcess;

internal static class RhinoStage
{
    public static void Run(string gha, string output)
    {
        bool render = Environment.GetCommandLineArgs().Contains("--image-fidelity");
        Console.WriteLine(render ? "Starting isolated hidden Rhino with viewport rendering." : "Starting isolated Rhino in safe mode.");
        var core = new RhinoCore(render ? new[] { "/nosplash", "/notemplate" }
            : new[] { "/safemode", "/nosplash", "/notemplate" }, render ? WindowStyle.Hidden : WindowStyle.NoWindow);
        Console.WriteLine("Rhino ready: " + Rhino.RhinoApp.Version);
        typeof(Program).Assembly.GetType("ChecksStage", true).GetMethod("Run").Invoke(null, new object[] { gha, output });
        GC.KeepAlive(core);
    }
}
