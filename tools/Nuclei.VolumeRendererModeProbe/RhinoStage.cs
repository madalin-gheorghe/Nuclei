using Rhino.Runtime.InProcess;

// No Grasshopper/GH_IO types in this stage. The document stage is reflected
// only after the installed native RhinoCore has initialized.
internal static class RhinoStage
{
    public static void Run(string gha, string output)
    {
        Console.WriteLine("Starting isolated RhinoCore, safe mode, no window.");
        var core = new RhinoCore(new[] { "/safemode", "/nosplash", "/notemplate" }, WindowStyle.NoWindow);
        Console.WriteLine("Rhino ready: " + Rhino.RhinoApp.Version);
        typeof(Program).Assembly.GetType("ChecksStage", true).GetMethod("Run").Invoke(null, new object[] { gha, output });
        GC.KeepAlive(core);
    }
}
