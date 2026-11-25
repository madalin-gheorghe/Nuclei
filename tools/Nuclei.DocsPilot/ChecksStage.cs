using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

// Runs only in its own RhinoCore process and isolated Grasshopper profile.
// Original examples are read-only; all generated artifacts go to a new scratch directory.
internal static partial class ChecksStage
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly library;
    static readonly List<string> checks = new();
    static object Member(object value, string name) => value.GetType().GetField(name, All)?.GetValue(value)
        ?? value.GetType().GetProperty(name, All)?.GetValue(value);
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks.Add(name); }
    static void Json(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    static GH_Component Create(string type) => (GH_Component)Activator.CreateInstance(library.GetType("Nuclei4." + type, true));
    static object Output(GH_Component c) => ((GH_ObjectWrapper)c.Params.Output[0].VolatileData.AllData(true).First()).Value;
    static GH_Component Find(GH_Document doc, string type) => doc.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == type);
    static string Graph(GH_Document doc) => string.Join("\n", doc.Objects.OrderBy(o => o.InstanceGuid).Select(o =>
        o.InstanceGuid + ":" + o.ComponentGuid + ":" + (o is GH_Component c
            ? string.Join(";", c.Params.Input.SelectMany((p, i) => p.Sources.Select(s => i + "<" + s.InstanceGuid))) : "")));
    static void PutNumber(GH_Component c, int index, double value)
    {
        var p = (Param_Number)c.Params.Input[index];
        p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(value)); p.ExpireSolution(false);
    }
    static void PutInteger(GH_Component c, int index, int value)
    {
        var p = (Param_Integer)c.Params.Input[index];
        p.PersistentData.Clear(); p.PersistentData.Append(new GH_Integer(value)); p.ExpireSolution(false);
    }
    static void SetSlider(GH_Component c, int index, decimal value)
    {
        var slider = (GH_NumberSlider)c.Params.Input[index].Sources.Single();
        slider.Slider.Minimum = Math.Min(slider.Slider.Minimum, value);
        slider.Slider.Maximum = Math.Max(slider.Slider.Maximum, value);
        slider.SetSliderValue(value); slider.ExpireSolution(false);
    }
    static void NoErrors(GH_Document doc)
    {
        foreach (var c in doc.Objects.OfType<GH_Component>())
            if (c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0)
                throw new Exception(c.Name + ": " + string.Join(" | ", c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
    }
    static object Port(IGH_Param p, int index)
    {
        var persistent = p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure;
        return new { index, name = p.Name, nickname = p.NickName, ghType = p.TypeName,
            access = p.Access.ToString(), optional = p.Optional, mapping = p.DataMapping.ToString(),
            defaults = persistent?.AllData(true).Select(v => v?.ToString()).ToArray() ?? Array.Empty<string>() };
    }
    static object Contract(GH_Component c) => new {
        name = c.Name, componentGuid = c.ComponentGuid, dotnetType = c.GetType().FullName,
        inputs = c.Params.Input.Select(Port).ToArray(), outputs = c.Params.Output.Select(Port).ToArray()
    };
    public static void Run(string gha, string output)
    {
        Directory.CreateDirectory(output);
        string profile = Path.Combine(output, "profile");
        Directory.CreateDirectory(Path.Combine(profile, "Libraries"));
        typeof(Grasshopper.Folders).GetField("m_appdataFolder", All).SetValue(null, profile + Path.DirectorySeparatorChar);
        GH_Document.EnableSolutions = false;
        var server = Grasshopper.Instances.ComponentServer;
        server.GetType().GetMethods(All).Single(m => m.Name == "LoadGHA" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(GH_ExternalFile))
            .Invoke(server, new object[] { new GH_ExternalFile(gha), false });
        library = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Nuclei4");
        Check(Path.GetFullPath(library.Location) == Path.GetFullPath(gha), "Exact requested GHA loaded");
        if (Environment.GetCommandLineArgs().Contains("--capture-walkthrough")) { CaptureWalkthrough(output); return; }
        if (Environment.GetCommandLineArgs().Contains("--catalog-only")) {
            string assemblyHash = Hash(gha);
            var components = library.GetTypes().Where(t => !t.IsAbstract && typeof(GH_Component).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (GH_Component)Activator.CreateInstance(t)).Where(c => c.Exposure != GH_Exposure.hidden).OrderBy(c => c.Name).ToArray();
            Json(Path.Combine(output, "component-contracts.json"), new {
                schemaVersion = 1, scope = "Public V4 component metadata; descriptive reference, not an executable API",
                pluginVersion = library.GetName().Version.ToString(), ghaSha256 = assemblyHash,
                rhinoVersion = Rhino.RhinoApp.Version.ToString(), components = components.Select(c => new {
                    name = c.Name, description = c.Description, category = c.Category, subcategory = c.SubCategory,
                    componentGuid = c.ComponentGuid, dotnetType = c.GetType().FullName,
                    inputs = c.Params.Input.Select((p, i) => new { metadata = Port(p, i), description = p.Description }).ToArray(),
                    outputs = c.Params.Output.Select((p, i) => new { metadata = Port(p, i), description = p.Description }).ToArray()
                }).ToArray()
            });
            Check(Hash(gha) == assemblyHash, "Assembly unchanged during metadata capture");
            Console.WriteLine("CATALOG COMPLETE: " + components.Length + " public components"); return;
        }
        string[] selected = { "VoxelConstructor", "Voxel_Values", "ParticleGroup_Constructor_Slime", "SolverGPU", "Particle_Extractor_TrailPoints" };
        Json(Path.Combine(output, "component-contracts.json"), new {
            schemaVersion = 1, scope = "Five-component documentation pilot; descriptive metadata, not an executable API",
            pluginVersion = library.GetName().Version.ToString(), ghaSha256 = Hash(gha),
            rhinoVersion = Rhino.RhinoApp.Version.ToString(), components = selected.Select(t => Contract(Create(t))).ToArray()
        });
        MappingChecks();
        string source = @"C:\Nuclei\Nuclei Definitions\v4\01_Slime Intro.gh";
        bool originalIntro = Environment.GetCommandLineArgs().Contains("--original-intro");
        int dimension = originalIntro ? 1000 : 100;
        int requested = originalIntro ? 50000 : 1000;
        string originalHash = Hash(source);
        var io = new GH_DocumentIO();
        Check(io.Open(source), "Open original Slime Intro");
        using var doc = io.Document;
        string originalGraph = Graph(doc);
        var field = Find(doc, "VoxelConstructor");
        var slime = Find(doc, "ParticleGroup_Constructor_Slime");
        var solver = Find(doc, "SolverGPU");
        var reset = (GH_BooleanToggle)solver.Params.Input[0].Sources.Single();
        var timers = doc.Objects.OfType<GH_Timer>().ToArray();
        Check(timers.Length == 1 && timers[0].Targets.Contains(solver.InstanceGuid), "Original Trigger targets solver");
        foreach (var timer in timers) timer.Locked = true;
        if (!originalIntro) {
            SetSlider(field, 1, 100); SetSlider(field, 2, 100); SetSlider(field, 3, 1);
            PutNumber(field, 0, 1); SetSlider(slime, 2, 1000);
        }
        reset.Value = true;
        doc.Enabled = true;
        string starter = Path.Combine(output, originalIntro ? "slime-intro-test-copy.gh" : "slime-starter.gh");
        Check(Graph(doc) == originalGraph, "Original objects and wire endpoints preserved");
        Check(io.SaveQuiet(starter), "Save test copy");
        CaptureCanvas(doc, Path.Combine(output, "slime-starter-definition.png"));
        // An unsaved inspection sink requests CPU data from the GPU solver.
        // It is not added to the starter file or its definition image.
        var trails = Create("Particle_Extractor_TrailPoints"); trails.CreateAttributes();
        doc.AddObject(trails, false); trails.Params.Input[0].AddSource(solver.Params.Output[0]);
        GH_Document.EnableSolutions = true; doc.Enabled = true;
        reset.ExpireSolution(false); doc.NewSolution(false); NoErrors(doc);
        Check((int)Member(solver, "iteration") == 0, "Reset yields iteration zero");
        object voxelPayload = Output(field);
        Check((int)Member(voxelPayload, "Count") == dimension * dimension, "Expected voxel count: " + dimension * dimension);
        Check((double)Member(voxelPayload, "VoxelSize") == 1, "Stored Voxel Size is 1");
        Check((int)Member(voxelPayload, "ResZ") == 1, "Test copy has one Z layer");
        var before = Positions(solver);
        Console.WriteLine("Initialized: " + slime.Message + "; " + solver.Message + "; CPU count=" + before.Length);
        var generated = (IList)Member(slime, "outputParticles");
        Check(generated.Count == requested, "Constructor generates requested " + requested + " particles");
        int eligible = generated.Cast<object>().Select(p => ((Plane)Member(p, "pPlane")).Origin)
            .Where(p => p.X >= 1 && p.X < dimension - 1 && p.Y >= 1 && p.Y < dimension - 1).Count();
        Check(before.Length == eligible && eligible > 0, "Solver retains particles outside reflective boundary cells");
        reset.Value = false; reset.ExpireSolution(false);
        var clock = Stopwatch.StartNew();
        for (int i = 1; i <= 200; i++)
        {
            solver.ExpireSolution(false); doc.NewSolution(false); NoErrors(doc);
            if (i % 50 == 0) Console.WriteLine("Slime starter: " + solver.Message);
        }
        clock.Stop();
        Check((int)Member(solver, "iteration") == 200, "Exactly 200 non-reset steps");
        var after = Positions(solver);
        Check(after.Length == before.Length, "Retained population stays constant without growth or death");
        int moved = before.Zip(after).Count(pair => pair.First.DistanceTo(pair.Second) > 1e-7);
        Check(moved > 0, "Particle positions change");
        Check(after.All(p => p.IsValid && p.X >= 0 && p.X < dimension && p.Y >= 0 && p.Y < dimension && p.Z >= 0 && p.Z < 1), "Particles remain within domain");
        var particles = (IList)Member(solver, "particles");
        int nonemptyTrails = particles.Cast<object>().Count(p => ((IList)Member(p, "trails")).Count > 1);
        Check(nonemptyTrails > 0, "Trail history is populated");
        CaptureTrailGeometry(particles, Path.Combine(output, "slime-starter-result.png"), dimension);
        int pausedIteration = (int)Member(solver, "iteration");
        Thread.Sleep(200);
        Check((int)Member(solver, "iteration") == pausedIteration && timers.All(t => t.Locked), "Locked Trigger does not advance while idle");
        // Inspect the temporary consumer without adding it to the downloadable starter.
        trails.ExpireSolution(false); doc.NewSolution(false); NoErrors(doc);
        var tree = trails.Params.Output[0].VolatileData;
        Check(tree.PathCount > 0 && tree.Paths.All(p => p.Indices.Length == 2 && p.Indices[0] == 0), "Trail extractor emits group/particle paths");
        Check(tree.AllData(true).All(v => v is GH_Point), "Trail extractor emits Rhino points");
        var trailPaths = tree.Paths.Take(4).Select(p => p.ToString()).ToArray();
        reset.Value = true; reset.ExpireSolution(false); doc.NewSolution(false); NoErrors(doc);
        Check((int)Member(solver, "iteration") == 0, "Reset after run returns to zero");
        GH_Document.EnableSolutions = false;
        var reopen = new GH_DocumentIO(); Check(reopen.Open(starter), "Reopen saved test copy");
        using var reopened = reopen.Document;
        Check(Graph(reopened) == originalGraph, "Reopened test copy keeps original graph");
        Check(reopened.Objects.OfType<GH_Timer>().All(t => t.Locked), "Test copy reopens with Trigger paused");
        Check(((GH_BooleanToggle)Find(reopened, "SolverGPU").Params.Input[0].Sources.Single()).Value, "Test copy reopens with Reset true");
        Check(Hash(source) == originalHash, "Original .gh unchanged");
        var savedInputs = reopened.Objects.OfType<GH_Component>().Select(c => new { c.Name,
            inputs = c.Params.Input.Select((p, i) => new { index = i, p.Name, p.NickName,
                sources = p.Sources.Select(s => s.Attributes.GetTopLevel.DocObject.InstanceGuid).ToArray(),
                persistent = (p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure)?.AllData(true).Select(v => v.ToString()).ToArray(),
                sliders = p.Sources.OfType<GH_NumberSlider>().Select(s => s.CurrentValue).ToArray() }).ToArray() }).ToArray();
        Json(Path.Combine(output, "validation.json"), new {
            passed = true, checks, ghaSha256 = Hash(gha), pluginVersion = library.GetName().Version.ToString(),
            rhinoVersion = Rhino.RhinoApp.Version.ToString(), source, originalSha256 = originalHash,
            starterSha256 = Hash(starter), originalObjectCount = reopened.ObjectCount,
            componentCount = reopened.Objects.OfType<GH_Component>().Count(), iterations = 200,
            originalIntro, requestedParticles = requested, retainedParticles = before.Length,
            elapsedMilliseconds = clock.ElapsedMilliseconds, movedParticles = moved, nonemptyTrails, trailPaths,
            resultImage = "Orthographic plot of actual extracted trail coordinates; not a Rhino viewport screenshot", savedInputs,
            inspection = "A temporary unsaved trail extractor requested CPU readback during runtime checks; starter topology and image retain the original component set.",
            warnings = doc.Objects.OfType<GH_Component>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Select(w => c.Name + ": " + w)).ToArray()
        });
        Console.WriteLine("DOCS PILOT PASSED: " + checks.Count + " checks");
    }
    static Point3d[] Positions(GH_Component solver)
    {
        object particles = Member(solver, "particles");
        particles.GetType().GetMethod("EnsureCpuStateCurrent", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(particles, null);
        return ((IEnumerable)particles).Cast<object>().Select(p => ((Plane)Member(p, "pPlane")).Origin).ToArray();
    }
    static void MappingChecks()
    {
        using var doc = new GH_Document();
        var field = Create("VoxelConstructor"); field.CreateAttributes(); doc.AddObject(field, false);
        PutInteger(field, 1, 2); PutInteger(field, 2, 2); PutInteger(field, 3, 1);
        var map = Create("Voxel_Values"); map.CreateAttributes(); doc.AddObject(map, false);
        map.Params.Input[0].AddSource(field.Params.Output[0]); PutInteger(map, 1, 2);
        // A real wire suppresses automatic dropdown insertion and pins the test enum.
        var type = new Param_Integer(); type.CreateAttributes(); type.PersistentData.Append(new GH_Integer(2));
        doc.AddObject(type, false); map.Params.Input[1].AddSource(type);
        var input = (Param_Number)map.Params.Input[2];
        void Values(params double[][] branches) {
            input.PersistentData.Clear();
            for (int b = 0; b < branches.Length; b++) foreach (double v in branches[b]) input.PersistentData.Append(new GH_Number(v), new GH_Path(b));
            input.ExpireSolution(false); map.ExpireSolution(false); doc.NewSolution(false); NoErrors(doc);
        }
        double[] Read() {
            object data = Member(Output(map), "Data"); object scalar = Member(data, "Speed");
            if (scalar == null) throw new Exception("Speed map missing");
            var get = scalar.GetType().GetMethod("Get", All);
            return Enumerable.Range(0, 4).Select(i => Convert.ToDouble(get.Invoke(scalar, new object[] { i }))).ToArray();
        }
        GH_Document.EnableSolutions = true; doc.Enabled = true;
        Values(new[] { 10.0, 20, 30, 40 }); Check(Read().SequenceEqual(new[] { 10.0, 20, 30, 40 }), "Flat multiplier list follows voxel order");
        Values(new[] { 10.0, 20 }, new[] { 30.0, 40 }); Check(Read().SequenceEqual(new[] { 10.0, 30, 20, 40 }), "Image-row tree remaps to voxel order");
        Values(new[] { 2.0 }); Check(Read().All(v => v == 2), "Single multiplier broadcasts");
        Values(new[] { 2.0, 3, 4 });
        Check(map.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("Multiplier count (3)")), "Mismatched count reports exact warning");
        Check(Read().All(v => v == -1), "Mismatched count preserves incoming map");
        GH_Document.EnableSolutions = false;
    }
    static void CaptureCanvas(GH_Document doc, string path)
    {
        var groups = doc.Objects.OfType<GH_Group>().ToArray(); var names = groups.Select(g => g.NickName).ToArray();
        foreach (var group in groups) group.NickName = "";
        foreach (var o in doc.Objects) { o.IconDisplayMode = GH_IconDisplayMode.icon; o.Attributes.ExpireLayout(); }
        foreach (var o in doc.Objects) o.Attributes.PerformLayout();
        RectangleF b = RectangleF.Empty;
        foreach (var o in doc.Objects) b = b.IsEmpty ? o.Attributes.Bounds : RectangleF.Union(b, o.Attributes.Bounds);
        b.Inflate(25, 25); float zoom = Math.Min(1.8f, 2400f / b.Width);
        var size = new Size((int)Math.Ceiling(b.Width * zoom), (int)Math.Ceiling(b.Height * zoom));
        using var canvas = new GH_Canvas { Size = size, Document = doc };
        canvas.CreateControl(); canvas.Widgets.Clear(); canvas.Viewport.Size = size; canvas.Viewport.Zoom = zoom;
        canvas.Viewport.Target = new System.Drawing.Point((int)(-b.Left * zoom), (int)(-b.Top * zoom)); canvas.Viewport.ComputeProjection();
        using var bitmap = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bitmap)) {
            var graphics = typeof(GH_Canvas).GetField("_graphics", All); graphics.SetValue(canvas, g);
            g.Clear(GH_Skin.canvas_back); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(-b.Left * zoom, -b.Top * zoom); g.ScaleTransform(zoom, zoom);
            foreach (var channel in new[] { GH_CanvasChannel.Groups, GH_CanvasChannel.Wires, GH_CanvasChannel.Objects })
                foreach (var o in doc.Objects) o.Attributes.RenderToCanvas(canvas, channel);
            graphics.SetValue(canvas, null);
        }
        bitmap.Save(path, ImageFormat.Png); canvas.Document = null;
        for (int i = 0; i < groups.Length; i++) groups[i].NickName = names[i];
    }
    static void CaptureTrailGeometry(IList particles, string path, int dimension)
    {
        using var image = new Bitmap(1200, 1200); using var g = Graphics.FromImage(image);
        g.Clear(Color.Black); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(170, 220, 255, 0), 1.25f);
        foreach (var p in particles) {
            var trail = (IList)Member(p, "trails");
            if (trail.Count < 2) continue;
            var points = trail.Cast<Point3d>().Select(v => new PointF(30 + (float)v.X * 1140f / dimension, 1170 - (float)v.Y * 1140f / dimension)).ToArray();
            g.DrawLines(pen, points);
        }
        image.Save(path, ImageFormat.Png);
    }
}
