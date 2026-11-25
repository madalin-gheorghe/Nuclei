using Rhino.Runtime.InProcess;

internal static class RhinoStage
{
    public static void Run(string gha, string output)
    {
        bool capture = Environment.GetCommandLineArgs().Contains("--capture");
        Console.WriteLine(capture ? "Starting isolated hidden Rhino with viewport rendering." : "Starting isolated Rhino in safe mode, no window.");
        var core = new RhinoCore(capture ? new[] { "/nosplash", "/notemplate" } : new[] { "/safemode", "/nosplash", "/notemplate" },
            capture ? WindowStyle.Hidden : WindowStyle.NoWindow);
        Console.WriteLine("Rhino ready: " + Rhino.RhinoApp.Version);
        typeof(Program).Assembly.GetType("ChecksStage", true).GetMethod("Run").Invoke(null, new object[] { gha, output });
        GC.KeepAlive(core);
    }
}
