using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

internal static partial class ChecksStage
{
    static void CaptureWalkthrough(string output)
    {
        bool ant = Environment.GetCommandLineArgs().Contains("--ant");
        string source = ant ? @"C:\Nuclei\Nuclei Definitions\v4\13_Ants Intro.gh" : @"C:\Nuclei\Nuclei Definitions\v4\01_Slime Intro.gh";
        string originalHash = Hash(source);
        var io = new GH_DocumentIO();
        Check(io.Open(source), "Open original definition for capture");
        using var doc = io.Document;
        string graph = Graph(doc);
        foreach (var timer in doc.Objects.OfType<GH_Timer>()) timer.Locked = true;
        var reset = (GH_BooleanToggle)Find(doc, "SolverGPU").Params.Input[0].Sources.Single();
        reset.Value = true;
        GH_Document.EnableSolutions = true; doc.Enabled = true;
        reset.ExpireSolution(false); doc.NewSolution(false); NoErrors(doc);
        GH_Document.EnableSolutions = false;
        foreach (var group in doc.Objects.OfType<GH_Group>()) group.NickName = "";
        foreach (var o in doc.Objects) { o.IconDisplayMode = GH_IconDisplayMode.icon; o.Attributes.ExpireLayout(); }
        foreach (var o in doc.Objects) o.Attributes.PerformLayout();
        var specs = ant ? new[] {
            ("VoxelConstructor", "construct-voxels.png"),
            ("ParticleGroup_Constructor_Ant", "construct-ants.png"),
            ("Voxel_Values", "food-map.png"),
            ("EnivronmentSettings_Ant", "ant-settings.png"),
            ("SolverGPU", "solver-preview.png")
        } : new[] {
            ("VoxelConstructor", "construct-voxels-clean.png"),
            ("ParticleGroup_Constructor_Slime", "construct-slime-clean.png"),
            ("Particle_Settings_Trail", "particle-trail-settings-clean.png"),
            ("EnivronmentSettings", "voxel-settings-slime-clean.png"),
            ("SolverGPU", "solver-preview-clean.png")
        };
        var manifest = new List<object>();
        foreach (var (type, filename) in specs)
        {
            var target = doc.Objects.OfType<GH_Component>().First(c => c.GetType().Name == type);
            var visible = new HashSet<IGH_DocumentObject> { target };
            foreach (var p in target.Params.Input.SelectMany(p => p.Sources))
                if (p.Attributes.GetTopLevel.DocObject is not GH_Component) visible.Add(p.Attributes.GetTopLevel.DocObject);
            if (type == "SolverGPU") {
                visible.Add(Find(doc, ant ? "Preview_Voxel" : "Preview_Particle_Trails_GPU"));
                foreach (var timer in doc.Objects.OfType<GH_Timer>()) visible.Add(timer);
            }
            var groups = doc.Objects.OfType<GH_Group>().Where(g => g.ObjectIDs.Contains(target.InstanceGuid) && !g.Attributes.Bounds.IsEmpty).ToArray();
            foreach (var group in groups) {
                visible.Add(group);
                foreach (var id in group.ObjectIDs) {
                    var o = doc.Objects.FirstOrDefault(o => o.InstanceGuid == id);
                    if (o != null) visible.Add(o);
                }
            }
            RectangleF bounds = RectangleF.Empty;
            foreach (var o in visible) if (!o.Attributes.Bounds.IsEmpty)
                bounds = bounds.IsEmpty ? o.Attributes.Bounds : RectangleF.Union(bounds, o.Attributes.Bounds);
            bounds.Inflate(35, 25);
            RenderWiredCrop(doc, visible, bounds, Path.Combine(output, filename));
            var connected = target.Params.Output.Select(p => new {
                port = p.NickName,
                recipients = doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input)
                    .Where(input => input.Sources.Contains(p)).Select(input => input.Attributes.GetTopLevel.DocObject.Name + "." + input.NickName).ToArray()
            }).ToArray();
            manifest.Add(new { file = filename, component = target.Name, outputs = connected });
            Console.WriteLine("Captured actual wires: " + filename);
        }
        Check(Graph(doc) == graph, "Original connections preserved");
        Check(Hash(source) == originalHash, "Original definition unchanged");
        Json(Path.Combine(output, "capture-manifest.json"), new { source, originalHash, captures = manifest, checks });
        Json(Path.Combine(output, "saved-inputs.json"), doc.Objects.OfType<GH_Component>().Select(c => new {
            type = c.GetType().Name, name = c.Name,
            inputs = c.Params.Input.Select(p => new { name = p.Name, nickname = p.NickName,
                sources = p.Sources.Select(s => new { name = s.Attributes.GetTopLevel.DocObject.Name, nickname = s.NickName }).ToArray(),
                values = p.VolatileData.AllData(true).Take(10).Select(v => v?.ToString()).ToArray(), count = p.VolatileDataCount }).ToArray()
        }).ToArray());
    }

    static void RenderWiredCrop(GH_Document doc, HashSet<IGH_DocumentObject> visible, RectangleF bounds, string path)
    {
        float zoom = 1840f / bounds.Width;
        var size = new Size((int)Math.Ceiling(bounds.Width * zoom), (int)Math.Ceiling(bounds.Height * zoom));
        using var canvas = new GH_Canvas { Size = size, Document = doc };
        canvas.CreateControl(); canvas.Widgets.Clear(); canvas.Viewport.Size = size; canvas.Viewport.Zoom = zoom;
        canvas.Viewport.Target = new Point((int)(-bounds.Left * zoom), (int)(-bounds.Top * zoom)); canvas.Viewport.ComputeProjection();
        using var bitmap = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bitmap)) {
            var field = typeof(GH_Canvas).GetField("_graphics", All); field.SetValue(canvas, g);
            g.Clear(GH_Skin.canvas_back); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(-bounds.Left * zoom, -bounds.Top * zoom); g.ScaleTransform(zoom, zoom);
            using (var pen = new Pen(Color.FromArgb(25, 0, 0, 0), 1f / zoom)) {
                for (float x = (float)Math.Floor(bounds.Left / 50) * 50; x < bounds.Right; x += 50) g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
                for (float y = (float)Math.Floor(bounds.Top / 50) * 50; y < bounds.Bottom; y += 50) g.DrawLine(pen, bounds.Left, y, bounds.Right, y);
            }
            foreach (var group in visible.OfType<GH_Group>()) group.Attributes.RenderToCanvas(canvas, GH_CanvasChannel.Groups);
            // Input attributes own wire rendering. Include off-crop recipients so connected outputs remain visible.
            foreach (var input in doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input)) {
                var sources = input.Sources.ToArray(); if (sources.Length == 0) continue;
                var method = input.Attributes.GetType().GetMethods(All).First(m => m.Name == "RenderIncomingWires" && m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType == typeof(IEnumerable<Pen>));
                var pens = sources.Select(_ => new Pen(GH_Skin.wire_default, 3f)).ToArray();
                try { method.Invoke(method.IsStatic ? null : input.Attributes, new object[] { canvas.Painter, sources, pens }); }
                finally { foreach (var pen in pens) pen.Dispose(); }
            }
            foreach (var timer in visible.OfType<GH_Timer>()) timer.Attributes.RenderToCanvas(canvas, GH_CanvasChannel.Wires);
            foreach (var o in visible.Where(o => o is not GH_Group)) o.Attributes.RenderToCanvas(canvas, GH_CanvasChannel.Objects);
            field.SetValue(canvas, null);
        }
        bitmap.Save(path, ImageFormat.Png); canvas.Document = null;
    }
}
