using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class ChecksStage
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static Assembly main;
    static Type gridType, fieldType, componentType;
    static int checks;

    public static void Run(string gha, string output)
    {
        Directory.CreateDirectory(output);
        string profile = Path.Combine(output, "profile");
        Directory.CreateDirectory(Path.Combine(profile, "Libraries"));
        typeof(Grasshopper.Folders).GetField("m_appdataFolder", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, profile + Path.DirectorySeparatorChar);
        GH_Document.EnableSolutions = false;
        var server = Grasshopper.Instances.ComponentServer;
        server.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name == "LoadGHA" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(GH_ExternalFile))
            .Invoke(server, new object[] { new GH_ExternalFile(gha), false });
        main = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Nuclei4");
        gridType = main.GetType("Nuclei4.VoxelGridData", true);
        fieldType = main.GetType("Nuclei4.VoxelField", true);
        componentType = main.GetType("Nuclei4.Voxel_ImageMapper", true);
        string[] arguments = Environment.GetCommandLineArgs();
        int imageArgument = Array.IndexOf(arguments, "--image-fidelity");
        if (imageArgument >= 0)
        {
            ImageFidelityChecks.Run(main, arguments[imageArgument + 1], output);
            return;
        }
        GpuChecks();
        ComponentChecks(output);
        File.WriteAllText(Path.Combine(output, "passed.txt"), $"PASS: {checks} assertions. GPU sampling, remapping, sparse fields, cache, warnings, embedded archives and canvas rendering.");
        Console.WriteLine($"PASS: {checks} assertions.");
    }

    static void GpuChecks()
    {
        var backend = Assembly.Load("Nuclei4.Gpu.D3D11").GetType("Nuclei4.GpuImageMapper", true);
        using var gpu = (IDisposable)Activator.CreateInstance(backend, true);
        float[] Map(int[] pixels, int w, int h, int x, int y, int z, float a = 0, float b = 1, bool clamp = false) =>
            (float[])backend.GetMethod("Map").Invoke(gpu, new object[] { pixels, w, h, x, y, z, a, b, clamp });
        int[] colors = { Color.Black.ToArgb(), Color.White.ToArgb(), Color.Red.ToArgb(), Color.Lime.ToArgb() };
        // Top row black/white; bottom row red/green. Voxel order is vertical-fast.
        foreach (var dims in new[] { new[] { 2, 2, 1 }, new[] { 2, 1, 2 }, new[] { 1, 2, 2 } })
            Near(Map(colors, 2, 2, dims[0], dims[1], dims[2]), new[] { .2126, 0, .7152, 1.0 }, "plane orientation/color");
        int[] bw = { Color.Black.ToArgb(), Color.White.ToArgb() };
        Near(Map(bw, 2, 1, 2, 1, 1, .2f, .4f), new[] { .2, .4 }, "forward remap");
        Near(Map(bw, 2, 1, 2, 1, 1, .4f, .2f), new[] { .4, .2 }, "reversed remap");
        Near(Map(bw, 2, 1, 4, 1, 1), new[] { 0, .25, .75, 1.0 }, "bilinear sampling");
        Near(Map(bw, 2, 1, 1, 1, 1), new[] { .5 }, "one voxel samples center");
        Near(Map(bw, 2, 1, 3, 2, 1, .3f, .3f), Enumerable.Repeat(.3, 6).ToArray(), "constant target");
        Near(Map(bw, 2, 1, 2, 1, 1, -2, 3, true), new[] { 0.0, 1.0 }, "density limits");
        Near(Map(bw, 2, 1, 2, 1, 1, -1, 3, true), new[] { -1.0, 1.0 }, "density sentinel");
        Near(Map(new[] { Color.Transparent.ToArgb() }, 1, 1, 2, 2, 1), Enumerable.Repeat(1.0, 4).ToArray(), "transparent white canvas");
        try { Map(bw, 2, 1, 2, 2, 2); throw new Exception("3D accepted"); }
        catch (TargetInvocationException e) { Check(e.InnerException.Message == "Only 2D Voxel Field Allowed", "GPU rejects 3D"); }
        var random = new Random(731);
        int[] pattern = Enumerable.Range(0, 17 * 11).Select(_ => Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)).ToArgb()).ToArray();
        foreach (var dims in new[] { new[] { 37, 29, 1 }, new[] { 37, 1, 29 }, new[] { 1, 37, 29 } })
        {
            float[] actual = Map(pattern, 17, 11, dims[0], dims[1], dims[2], .4f, .2f);
            for (int u = 0; u < 37; u++)
            for (int v = 0; v < 29; v++)
            {
                double px = Math.Clamp((u + .5) / 37 * 17 - .5, 0, 16);
                double py = Math.Clamp((1 - (v + .5) / 29) * 11 - .5, 0, 10);
                int ax = (int)px, ay = (int)py, bx = Math.Min(16, ax + 1), by = Math.Min(10, ay + 1);
                double Gray(int x, int y) { var c = Color.FromArgb(pattern[y * 17 + x]); return (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255; }
                double Lerp(double a, double b, double t) => a + (b - a) * t;
                double gray = Lerp(Lerp(Gray(ax, ay), Gray(bx, ay), px - ax), Lerp(Gray(ax, by), Gray(bx, by), px - ax), py - ay);
                Check(Math.Abs(actual[u * 29 + v] - (.4 - .2 * gray)) < 1e-6, "CPU reference sampling");
            }
        }
        Map(pattern, 17, 11, 1024, 1024, 1);
        var timings = new List<double>();
        for (int i = 0; i < 7; i++) { var watch = Stopwatch.StartNew(); Map(pattern, 17, 11, 1024, 1024, 1); timings.Add(watch.Elapsed.TotalMilliseconds); }
        Console.WriteLine($"1,048,576 voxels, warm GPU map plus readback median: {timings.OrderBy(x => x).ElementAt(3):F2} ms. Software fallback: {backend.GetProperty("SoftwareFallback").GetValue(gpu)}");
    }

    static void ComponentChecks(string output)
    {
        using var doc = new GH_Document();
        var c = (GH_Component)Activator.CreateInstance(componentType);
        c.CreateAttributes(); c.Attributes.Pivot = new PointF(480, 200); doc.AddObject(c, false);
        Check(c.Params.Input.Select(p => p.NickName).SequenceEqual(new[] { "voxels", "type", "targetStart", "targetEnd" }), "input schema");
        SetImage(c, ImageBytes(Color.Black, Color.White));
        object data = Grid(2, 1, 1);
        data = gridType.GetMethod("WithScalarValues").Invoke(data, new object[] { 6, new double[] { 7 } });
        SetField(c, data); Number(c, 2, .4); Number(c, 3, .2);
        GH_Document.EnableSolutions = true; doc.Enabled = true;
        Solve(doc, c);
        Check(doc.Objects.OfType<GH_ValueList>().Count() == 1, "automatic Type list");
        var list = doc.Objects.OfType<GH_ValueList>().Single();
        list.SelectItem(2); list.ExpireSolution(false); Solve(doc, c);
        Near(MapValues(c, "Speed"), new[] { .4, .2 }, "component remap output");
        Check(ReferenceEquals(gridType.GetField("Food").GetValue(data), gridType.GetField("Food").GetValue(OutputData(c))), "unrelated map retained");
        Check(gridType.GetField("Speed").GetValue(data).GetType().GetField("Values").GetValue(gridType.GetField("Speed").GetValue(data)) == null, "input unmodified");
        var cached = PrivateValue(c, "mappedValues");
        var oldData = OutputData(c);
        var timing = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) Solve(doc, c);
        Check(ReferenceEquals(cached, PrivateValue(c, "mappedValues")), "unchanged solve skips GPU");
        Check(ReferenceEquals(oldData, OutputData(c)), "unchanged solve shares mapped data");
        Check(doc.Objects.OfType<GH_ValueList>().Count() == 1, "no duplicated Type lists");
        Console.WriteLine($"Cached GH solution mean: {timing.Elapsed.TotalMilliseconds / 10:F3} ms");
        Number(c, 2, .2); Number(c, 3, .4); Solve(doc, c);
        Near(MapValues(c, "Speed"), new[] { .2, .4 }, "range change invalidates cache");
        SetImage(c, ImageBytes(Color.White, Color.Black)); Solve(doc, c);
        Near(MapValues(c, "Speed"), new[] { .4, .2 }, "new image invalidates cache");
        SetImage(c, ImageBytes(Color.Black, Color.White));
        SetField(c, Grid(4, 3, 1)); Solve(doc, c);
        Check(c.Message.Contains("different aspect ratios") && c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count == 0
            && c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "aspect notice only, no warning/error colour");
        Check(MapValues(c, "Speed").Length == 12, "stretched mapping still produces voxels");
        Render(doc, c, Path.Combine(output, "canvas-stretched.png"));
        SetField(c, Grid(2, 2, 2)); Solve(doc, c);
        Check(c.Message == "Only 2D Voxel Field Allowed" && c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0
            && c.Params.Output[0].VolatileData.AllData(true).All(g => g == null || !g.IsValid),
            $"3D rejects stale output: message={c.Message}; count={c.Params.Output[0].VolatileDataCount}; enabled={doc.Enabled}/{GH_Document.EnableSolutions}");
        Render(doc, c, Path.Combine(output, "canvas-3d.png"));
        SetField(c, Grid(2, 1, 1)); Solve(doc, c);
        Check(!c.Message.Contains("aspect") && c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count == 0
            && c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "warnings and errors clear on valid input");
        Number(c, 2, double.NaN); Solve(doc, c);
        Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && c.Params.Output[0].VolatileData.AllData(true).All(g => g == null || !g.IsValid), "NaN rejected");
        Number(c, 2, .2);
        object sparse = gridType.GetMethod("WithActiveMask").Invoke(Grid(4, 2, 1), new object[] { new[] { true, false, true, false, false, false, false, true } });
        SetField(c, sparse); Solve(doc, c);
        var sparseValues = MapValues(c, "Speed");
        Near(new[] { sparseValues[0], sparseValues[2], sparseValues[7] }, new[] { .2, .25, .4 }, "sparse uses whole domain");
        Check(sparseValues[1] == -1 && sparseValues[3] == -1, "inactive values preserved");
        Check((int)gridType.GetField("ActiveCount").GetValue(OutputData(c)) == 3, "sparse selection retained");
        ResizeChecks(doc, c, output);
        // Real archive roundtrip, containing an embedded source image and wired Type selector.
        ((Param_GenericObject)c.Params.Input[0]).PersistentData.Clear();
        c.Params.Input[0].ExpireSolution(false);
        string path = Path.Combine(output, "embedded-map.gh");
        Check(new GH_DocumentIO(doc).SaveQuiet(path), "save embedded document");
        var io = new GH_DocumentIO(); Check(io.Open(path), "reopen embedded document");
        using var restoredDoc = io.Document;
        restoredDoc.Enabled = true;
        var restored = (GH_Component)restoredDoc.Objects.Single(o => o.InstanceGuid == c.InstanceGuid);
        Check(((byte[])PrivateValue(restored, "embeddedImage")).SequenceEqual((byte[])PrivateValue(c, "embeddedImage")), "embedded bytes restored without file path");
        Check(PrivateValue(restored, "thumbnail") != null, "thumbnail restored");
        restored.Attributes.PerformLayout();
        Check(((GH_ComponentAttributes)restored.Attributes).ContentBox.Size == new SizeF(440, 320), "resized dimensions survive document reopening");
        SetField(restored, Grid(2, 1, 1)); Solve(restoredDoc, restored);
        Check(restored.Params.Output[0].VolatileData.AllData(true).Any(g => g != null && g.IsValid),
            $"reopened output: enabled={restoredDoc.Enabled}/{GH_Document.EnableSolutions}; message={restored.Message}; errors={string.Join(" | ", restored.RuntimeMessages(GH_RuntimeMessageLevel.Error))}");
        Near(MapValues(restored, "Speed"), new[] { .2, .4 }, "reopened image maps correctly");
        Render(restoredDoc, restored, Path.Combine(output, "canvas.png"));
        var restoredTypeSource = restored.Params.Input[1].Sources.Single();
        restoredDoc.RemoveObject(restored, false);
        Check(PrivateValue(restored, "gpu") == null && PrivateValue(restored, "thumbnail") == null, "removal disposes GPU and bitmap");
        restoredDoc.AddObject(restored, false); restored.Params.Input[1].AddSource(restoredTypeSource);
        SetField(restored, Grid(2, 1, 1)); Solve(restoredDoc, restored);
        Near(MapValues(restored, "Speed"), new[] { .2, .4 }, "re-add restores image");
    }

    static void ResizeChecks(GH_Document doc, GH_Component c, string output)
    {
        bool enabled = doc.Enabled;
        using var canvas = new GH_Canvas { Size = new Size(1200, 800), Document = doc };
        var attributes = (GH_ComponentAttributes)c.Attributes;
        attributes.PerformLayout();
        var original = attributes.ContentBox;
        var originalPivot = attributes.Pivot;
        var mapped = PrivateValue(c, "mappedValues");
        var originalOutput = OutputData(c);
        int solutions = 0;
        GH_Document.SolutionStartEventHandler onSolution = (s, e) => solutions++;
        doc.SolutionStart += onSolution;
        void Drag(float dx, float dy, float zoom)
        {
            canvas.Viewport.Zoom = zoom;
            attributes.PerformLayout();
            float inset = zoom < 1 ? 40 : 25;
            var handle = new PointF(attributes.Bounds.Right - inset, attributes.Bounds.Bottom - inset);
            GH_CanvasMouseEvent Mouse(PointF location, System.Windows.Forms.MouseButtons button) =>
                new GH_CanvasMouseEvent(new Point((int)(location.X * zoom), (int)(location.Y * zoom)), location, button, 1, 0);
            Check(attributes.RespondToMouseMove(canvas, Mouse(handle, System.Windows.Forms.MouseButtons.None)) == GH_ObjectResponse.Handled, "resize hover");
            Check(canvas.Cursor == System.Windows.Forms.Cursors.SizeNWSE, "resize cursor");
            Check(attributes.RespondToMouseDown(canvas, Mouse(handle, System.Windows.Forms.MouseButtons.Left)) == GH_ObjectResponse.Capture, "grip captures drag");
            var half = new PointF(handle.X + dx / 2, handle.Y + dy / 2);
            var end = new PointF(handle.X + dx, handle.Y + dy);
            attributes.RespondToMouseMove(canvas, Mouse(half, System.Windows.Forms.MouseButtons.Left));
            attributes.RespondToMouseMove(canvas, Mouse(end, System.Windows.Forms.MouseButtons.Left));
            Check(attributes.RespondToMouseUp(canvas, Mouse(end, System.Windows.Forms.MouseButtons.Left)) == GH_ObjectResponse.Release, "grip releases drag");
            Check(attributes.ContentBox.Location == original.Location && attributes.Pivot == originalPivot, "resize keeps upper-left and pivot fixed");
        }
        Drag(220, 120, .5f);
        Check(attributes.ContentBox.Size == new SizeF(440, 320), "grow at half zoom");
        Check(ReferenceEquals(mapped, PrivateValue(c, "mappedValues")) && ReferenceEquals(originalOutput, OutputData(c)) && solutions == 0, "resizing only redraws");
        Check(doc.Undo(), "resize undo available"); attributes.PerformLayout();
        Check(attributes.ContentBox == original, "one undo restores full drag");
        Check(doc.Redo(), "resize redo available"); attributes.PerformLayout();
        Check(attributes.ContentBox.Size == new SizeF(440, 320), "redo restores size");
        Check(ReferenceEquals(mapped, PrivateValue(c, "mappedValues")) && solutions == 0, "layout undo preserves map cache");
        Drag(-500, -500, 1.75f);
        Check(attributes.ContentBox.Size == new SizeF(180, 160), "minimum size at high zoom");
        Check(doc.Undo(), "shrink undo"); attributes.PerformLayout();
        Check(attributes.ContentBox.Size == new SizeF(440, 320), "restore large preview");
        Check(c.Params.Output[0].Attributes.Bounds.Left >= attributes.ContentBox.Right, "output grip follows resized box");
        doc.SolutionStart -= onSolution;
        canvas.Document = null; doc.Enabled = enabled;
        Render(doc, c, Path.Combine(output, "canvas-resized.png"));

        // Old archives have no dimensions and must keep their original size.
        var old = new GH_IO.Serialization.GH_LooseChunk("Attributes");
        attributes.Write(old);
        old.RemoveItem("ImageMapperWidth"); old.RemoveItem("ImageMapperHeight");
        var fresh = (GH_Component)Activator.CreateInstance(componentType); fresh.CreateAttributes();
        fresh.Attributes.Read(old); fresh.Attributes.PerformLayout();
        Check(((GH_ComponentAttributes)fresh.Attributes).ContentBox.Size == new SizeF(220, 200), "legacy archive default size");
    }

    static object Grid(int x, int y, int z) => gridType.GetMethod("CreateFullDomain").Invoke(null, new object[] { x, y, z, 1.0 });
    static void SetField(GH_Component c, object data)
    {
        object field = Activator.CreateInstance(fieldType, Private, null, new object[] { data, null, null }, null);
        var p = (Param_GenericObject)c.Params.Input[0]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_ObjectWrapper(field)); p.ExpireSolution(false);
    }
    static byte[] ImageBytes(params Color[] colors)
    {
        using var bmp = new Bitmap(colors.Length, 1);
        for (int i = 0; i < colors.Length; i++) bmp.SetPixel(i, 0, colors[i]);
        using var stream = new MemoryStream(); bmp.Save(stream, ImageFormat.Png); return stream.ToArray();
    }
    static void SetImage(GH_Component c, byte[] bytes)
    {
        componentType.GetMethod("ReleaseImage", Private).Invoke(c, null);
        componentType.GetField("embeddedImage", Private).SetValue(c, bytes);
        componentType.GetField("imageName", Private).SetValue(c, "black-to-white.png");
        componentType.GetMethod("RestoreImage", Private).Invoke(c, null);
    }
    static object PrivateValue(GH_Component c, string name) => componentType.GetField(name, Private).GetValue(c);
    static object OutputData(GH_Component c)
    {
        var goo = c.Params.Output[0].VolatileData.AllData(true).Single();
        object field = goo is GH_ObjectWrapper wrapper ? wrapper.Value : goo.ScriptVariable();
        return fieldType.GetProperty("Data", Private).GetValue(field);
    }
    static float[] MapValues(GH_Component c, string name)
    {
        object map = gridType.GetField(name).GetValue(OutputData(c));
        return (float[])map.GetType().GetField("Values").GetValue(map);
    }
    static void Number(GH_Component c, int i, double value)
    {
        var p = (Param_Number)c.Params.Input[i]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(value)); p.ExpireSolution(false);
    }
    static void Solve(GH_Document doc, GH_Component c) { c.ExpireSolution(false); doc.NewSolution(false); }
    static void Render(GH_Document doc, GH_Component c, string path)
    {
        bool enabled = doc.Enabled;
        c.Attributes.ExpireLayout(); c.Attributes.PerformLayout();
        var box = ((GH_ComponentAttributes)c.Attributes).ContentBox;
        var size = new Size(Math.Max(960, (int)c.Attributes.Bounds.Width + 450), Math.Max(440, (int)box.Height + 200));
        using var canvas = new GH_Canvas { Size = size, Document = doc };
        canvas.CreateControl(); canvas.Viewport.Zoom = 1; canvas.Viewport.Focus(new PointF(box.Left + box.Width / 2 - 120, box.Top + box.Height / 2));
        using var bitmap = new Bitmap(size.Width, size.Height); canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        // A fresh canvas fades its labels in; capture after the animation settles.
        Thread.Sleep(400);
        canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        bitmap.Save(path, ImageFormat.Png); canvas.Document = null;
        doc.Enabled = enabled;
        Check(c.Attributes.Bounds.Width >= 220 && c.Attributes.Bounds.Height >= 200, "image canvas bounds");
    }
    static void Near(float[] actual, double[] expected, string label)
    {
        Check(actual.Length == expected.Length, label + " length");
        for (int i = 0; i < actual.Length; i++) Check(Math.Abs(actual[i] - expected[i]) < 1e-6, $"{label}[{i}]: {actual[i]} != {expected[i]}");
    }
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; }
}
