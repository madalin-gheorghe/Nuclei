using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

internal static class PauseProbe
{
    public static int Run(string[] args)
    {
        string profile = Path.Combine(Path.GetTempPath(), "NucleiSolverPauseProbe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(profile, "Libraries"));
        typeof(Grasshopper.Folders).GetField("m_appdataFolder", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, profile + Path.DirectorySeparatorChar);
        Console.WriteLine("Grasshopper " + typeof(GH_Timer).Assembly.GetName().Version);
        string repository = FindRepository();
        foreach (string version in new[] { "3", "4" })
        {
#if NETFRAMEWORK
            const string framework = "net48";
#else
            const string framework = "net7.0-windows";
#endif
            string path = args.Length == 2 ? Path.GetFullPath(args[version == "3" ? 0 : 1])
                : Path.Combine(repository, "Nuclei-v" + version, "Nuclei" + version, "bin", "Release", framework, "Nuclei" + version + ".gha");
            Assembly assembly = Program.LoadAssembly(path);
            if (args.Contains("--dendro"))
            {
                DendroUpdateProbe.Run(assembly, version);
                continue;
            }
            MethodInfo pause = assembly.GetType("Nuclei" + version + ".SolverIterationLimit", true)
                .GetMethod("PauseDedicatedTimers", BindingFlags.Static | BindingFlags.NonPublic);
            TestPause(pause);
            TestDeletedTargets(pause);
            Console.WriteLine("PASS V" + version + ": paused callback, downstream, exclusions, retained settings/state, re-enable, reset.");
        }
        Console.WriteLine(args.Contains("--dendro")
            ? "PASS: Dendro Update graph regressions for both built components; no user document opened."
            : "PASS: real Grasshopper timer callback and both built solver helpers; no user document opened.");
        return 0;
    }

    static void TestPause(MethodInfo pause)
    {
        using var document = new GH_Document();
        Grasshopper.Instances.DocumentServer.AddDocument(document);
        GH_Document.EnableSolutions = true;
        document.Enabled = true;
        var solver = new CountingComponent();
        var downstream = new CountingComponent();
        var other = new CountingComponent();
        document.AddObject(solver, false);
        document.AddObject(downstream, false);
        document.AddObject(other, false);
        downstream.Params.Input[0].AddSource(solver.Params.Output[0]);

        var dedicated = AddTimer(document, 1234, solver);
        var manual = AddTimer(document, -1, solver);
        var shared = AddTimer(document, 2345, solver, other);
        var unrelated = AddTimer(document, 3456, other);
        var locked = AddTimer(document, 4567, solver);
        locked.Locked = true;
        var empty = AddTimer(document, 5678);

        // This is the real delegate GH_Timer submits to ScheduleSolution. Capturing
        // it before pause exercises a callback that is already pending at the cap.
        MethodInfo callbackMethod = typeof(GH_Timer).GetMethod("ScheduleCallBack", BindingFlags.Instance | BindingFlags.NonPublic);
        var pendingCallback = (Action<GH_Document>)callbackMethod.CreateDelegate(typeof(Action<GH_Document>), dedicated);
        Console.WriteLine("Fixture: locked=" + dedicated.Locked + " block=" + GH_Timer.GlobalTimerBlock
            + " enabled=" + document.Enabled + " owned=" + ReferenceEquals(dedicated.OnPingDocument(), document));
        Ready(solver, downstream);
        MakeDue(dedicated);
        pendingCallback(document);
        Require(solver.Expirations > 0, "Baseline automatic callback must expire its solver.");
        Require(downstream.Expirations > 0, "Baseline callback must expire the connected downstream component.");

        Ready(solver, downstream);
        object state = solver.State;
        int interval = dedicated.Interval;
        Guid[] targets = dedicated.Targets.ToArray();
        MakeDue(dedicated);
        pause.Invoke(null, new object[] { solver });
        Require(dedicated.Locked, "Dedicated automatic timer was not paused.");
        Require(locked.Locked, "An already locked timer was unlocked.");
        Require(!manual.Locked && manual.Manual, "Manual trigger changed.");
        Require(!shared.Locked, "Shared timer was paused.");
        Require(!unrelated.Locked && !empty.Locked, "Unrelated timer changed.");
        Require(dedicated.Interval == interval && dedicated.Targets.SequenceEqual(targets), "Timer settings changed.");

        // Ignore any notification from changing the timer itself: only callback
        // behavior below is relevant to stopping repeated downstream work.
        Ready(solver, downstream);
        for (int i = 0; i < 20; i++) pendingCallback(document);
        Require(solver.Expirations == 0 && downstream.Expirations == 0, "A paused callback expired the solver or downstream component.");
        Require(ReferenceEquals(solver.State, state), "Pausing discarded solver-owned state.");
        Require(solver.Phase == GH_SolutionPhase.Computed && downstream.Phase == GH_SolutionPhase.Computed,
            "Paused callback invalidated computed output.");
        pause.Invoke(null, new object[] { solver });
        Require(dedicated.Interval == interval && dedicated.Targets.SequenceEqual(targets), "Repeated pause changed settings.");

        dedicated.Locked = false;
        Ready(solver, downstream);
        MakeDue(dedicated);
        pendingCallback(document);
        Require(solver.Expirations > 0 && downstream.Expirations > 0, "Explicit timer re-enable could not resume downstream expiration.");

        pause.Invoke(null, new object[] { solver });
        Ready(solver, downstream);
        solver.State = new object();
        solver.ExpireSolution(false);
        Require(solver.Expirations == 1 && downstream.Expirations > 0, "Paused timer prevented explicit solver reset/expiration.");
        Require(!ReferenceEquals(solver.State, state), "Explicit state reset was not retained.");
    }

    static void TestDeletedTargets(MethodInfo pause)
    {
        using var document = new GH_Document();
        Grasshopper.Instances.DocumentServer.AddDocument(document);
        document.Enabled = true;
        var solver = new CountingComponent();
        var downstream = new CountingComponent();
        var other = new CountingComponent();
        document.AddObject(solver, false);
        document.AddObject(downstream, false);
        document.AddObject(other, false);
        downstream.Params.Input[0].AddSource(solver.Params.Output[0]);

        // City Map retained these two deleted target IDs alongside its solver.
        // The raw count is three, but Grasshopper expires only the live target.
        var timer = AddTimer(document, 1, solver);
        timer.AddTarget(new Guid("232c0878-9ed0-4c05-9af9-96153654944f"));
        timer.AddTarget(new Guid("aa17753d-76ef-4267-829c-c3df6beb77b1"));
        var shared = AddTimer(document, 1, solver, other);
        shared.AddTarget(Guid.NewGuid());
        var deletedOnly = AddTimer(document, 1);
        deletedOnly.AddTarget(Guid.NewGuid());
        Guid[] targets = timer.Targets.ToArray();
        Require(timer.TargetCount == 3, "Deleted-target fixture must keep three saved IDs.");

        var callback = (Action<GH_Document>)typeof(GH_Timer)
            .GetMethod("ScheduleCallBack", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<GH_Document>), timer);
        Ready(solver, downstream);
        MakeDue(timer);
        callback(document);
        Require(solver.Expirations > 0 && downstream.Expirations > 0,
            "A due trigger with deleted targets must still expire its live target.");
        Ready(solver, downstream);
        pause.Invoke(null, new object[] { solver });
        Require(timer.Locked, "Deleted target IDs prevented the solver trigger from pausing.");
        Require(!shared.Locked, "A genuinely shared trigger with deleted IDs was paused.");
        Require(!deletedOnly.Locked, "A trigger with no live solver target was changed.");
        Require(timer.Targets.SequenceEqual(targets), "Pausing edited saved trigger references.");
        MakeDue(timer);
        for (int i = 0; i < 20; i++) callback(document);
        Require(solver.Expirations == 0 && downstream.Expirations == 0,
            "A paused trigger with deleted targets continued downstream work.");
        Console.WriteLine("PASS: City Map deleted-target regression.");
    }

    static GH_Timer AddTimer(GH_Document document, int interval, params IGH_DocumentObject[] targets)
    {
        var timer = new GH_Timer { Interval = interval };
        document.AddObject(timer, false);
        foreach (var target in targets) timer.AddTarget(target.InstanceGuid);
        return timer;
    }

    static void MakeDue(GH_Timer timer)
    {
        // Avoid sleeps/message pumping while exercising a genuinely due callback.
        typeof(GH_Timer).GetField("m_schedule", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(timer, DateTime.UtcNow.AddMinutes(-1));
    }

    static void Ready(params CountingComponent[] components)
    {
        foreach (var component in components)
        {
            component.Phase = GH_SolutionPhase.Computed;
            foreach (var parameter in component.Params.Input.Concat(component.Params.Output))
                parameter.Phase = GH_SolutionPhase.Computed;
            component.Expirations = 0;
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Nuclei-v4")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot locate the Nuclei repository.");
    }

    sealed class CountingComponent : GH_Component
    {
        public int Expirations;
        public object State = new object();
        public CountingComponent() : base("Pause probe", "Pause probe", "Timer pause regression component", "Test", "Test") { }
        public override Guid ComponentGuid => new Guid("A6D271ED-4882-41D7-B842-4EA641862A7B");
        protected override void RegisterInputParams(GH_InputParamManager manager)
        {
            manager.AddGenericParameter("Input", "I", "Input", GH_ParamAccess.item);
            manager[0].Optional = true;
        }
        protected override void RegisterOutputParams(GH_OutputParamManager manager)
            => manager.AddGenericParameter("Output", "O", "Output", GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess access) => access.SetData(0, State);
        public override void ExpireSolution(bool recompute)
        {
            Expirations++;
            base.ExpireSolution(recompute);
        }
    }
}
