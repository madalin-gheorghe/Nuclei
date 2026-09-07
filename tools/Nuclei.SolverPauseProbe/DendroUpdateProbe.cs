using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class DendroUpdateProbe
{
    internal static void Run(Assembly assembly, string version)
    {
        using var document = new GH_Document();
        Grasshopper.Instances.DocumentServer.AddDocument(document);
        GH_Document.EnableSolutions = true;
        document.Enabled = true;
        var source = new Source();
        var sink = new Sink();
        var update = new GH_BooleanToggle { Value = false };
        string typeName = version == "4" ? "Nuclei4.GpuVolumeToMesh" : "Nuclei3.NucleiToDendro";
        var converter = (GH_Component)Activator.CreateInstance(assembly.GetType(typeName, true));
        document.AddObject(source, false);
        document.AddObject(update, false);
        document.AddObject(converter, false);
        document.AddObject(sink, false);
        converter.Params.Input[0].AddSource(source.Params.Output[0]);
        converter.Params.Input[4].AddSource(update);
        sink.Params.Input[0].AddSource(converter.Params.Output[0]);
        object cache = new GH_ObjectWrapper("retained volume");
        FieldInfo cacheField = converter.GetType().GetField(version == "4" ? "cachedOutput" : "cachedVolume",
            BindingFlags.Instance | BindingFlags.NonPublic);
        cacheField.SetValue(converter, cache);
        document.NewSolution(false);
        Require(sink.Solves > 0, "Initial cached output was not published.");
        int baseline = sink.Solves;
        int reads = source.Data.Reads;
        Require(reads == 0, "Initial Update=false resolved the voxel payload.");
        for (int i = 0; i < 5; i++)
        {
            source.ExpireSolution(false);
            Require(converter.Phase == GH_SolutionPhase.Computed,
                "Update=false invalidated the converter and its cached output tree.");
            document.NewSolution(false);
        }
        Require(sink.Solves == baseline, "Update=false recomputed downstream on every solver tick.");
        Require(source.Data.Reads == reads, "Update=false resolved the voxel payload.");
        Require(ReferenceEquals(cacheField.GetValue(converter), cache), "Disabled updates lost the cached volume.");
        Require(converter.Message == (version == "4" ? "Update Off" : "Convert Off"), "Disabled status is stale.");

        update.Value = true;
        update.ExpireSolution(false);
        document.NewSolution(false);
        Require(sink.Solves > baseline, "Enabling Update did not wake downstream in the same solution.");
        Require(source.Data.Reads > reads, "Enabling Update did not read the voxel payload.");
        int enabled = sink.Solves;
        source.ExpireSolution(false);
        document.NewSolution(false);
        Require(sink.Solves > enabled, "Held-true Update stopped continuous propagation.");

        update.Value = false;
        update.ExpireSolution(false);
        document.NewSolution(false);
        baseline = sink.Solves;
        reads = source.Data.Reads;
        source.ExpireSolution(false);
        document.NewSolution(false);
        Require(sink.Solves == baseline && source.Data.Reads == reads, "Disabling Update did not stop repeated work.");

        // A downstream input change must still be able to retrieve the retained
        // result even though solver-triggered expiration is suppressed.
        sink.ExpireSolution(false);
        document.NewSolution(false);
        Require(sink.Solves == baseline + 1 && sink.Last == "retained volume", "Explicit downstream refresh lost cached output.");

        converter.Params.Input[4].RemoveAllSources();
        document.NewSolution(false);
        baseline = sink.Solves;
        source.ExpireSolution(false);
        Require(converter.Phase == GH_SolutionPhase.Computed, "Default unconnected Update=false did not hold conversion.");
        document.NewSolution(false);
        Require(sink.Solves == baseline, "Default unconnected Update=false recomputed downstream.");
        Console.WriteLine("PASS V" + version + ": actual Dendro component graph holds downstream while off and resumes while on.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class Payload : GH_Goo<object>
    {
        internal int Reads;
        public override bool IsValid => true;
        public override string TypeName => "Probe voxel payload";
        public override string TypeDescription => TypeName;
        public override IGH_Goo Duplicate() => this;
        public override string ToString() => TypeName;
        public override object ScriptVariable() { Reads++; return new object(); }
    }

    sealed class Source : GH_Component
    {
        internal readonly Payload Data = new Payload();
        public Source() : base("Source", "Source", "Probe solver output", "Test", "Test") { }
        public override Guid ComponentGuid => new Guid("79F97A49-FD07-4E5B-8A9E-466CF7E3E020");
        protected override void RegisterInputParams(GH_InputParamManager manager) { }
        protected override void RegisterOutputParams(GH_OutputParamManager manager) => manager.AddGenericParameter("Voxels", "V", "Voxels", GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess access) => access.SetData(0, Data);
    }

    sealed class Sink : GH_Component
    {
        internal int Solves;
        internal string Last;
        public Sink() : base("Sink", "Sink", "Probe downstream processing", "Test", "Test") { }
        public override Guid ComponentGuid => new Guid("190D5D77-CE86-4A0B-A9E6-4590106FE7FC");
        protected override void RegisterInputParams(GH_InputParamManager manager) => manager.AddGenericParameter("Volume", "V", "Volume", GH_ParamAccess.item);
        protected override void RegisterOutputParams(GH_OutputParamManager manager) { }
        protected override void SolveInstance(IGH_DataAccess access)
        {
            Solves++;
            object value = null;
            access.GetData(0, ref value);
            Last = value is GH_ObjectWrapper wrapper ? Convert.ToString(wrapper.Value) : Convert.ToString(value);
        }
    }
}
