using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Runtime.InProcess;
using Vortice.Direct3D11;
using Vortice.DXGI;

public static class CaptureProbe
{
    public static int Run(string[] args, string assemblyPath)
    {
        string output = Path.GetFullPath(Program.Option(args, "--output", ".codex-temp/trail-capture-current"));
        Directory.CreateDirectory(output);
        Assembly renderer = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        if (args.Contains("--compile-shaders"))
        {
            string source = (string)renderer.GetType("Nuclei4.ParticleTrailD3DRenderer", true)
                .GetField("ShaderSource", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            foreach (var shader in new[] { ("VSMain", "vs_4_0"), ("PSMain", "ps_4_0") })
            {
                using var blob = Vortice.D3DCompiler.Compiler.Compile(source, shader.Item1, "TrailCaptureProbe", null, null,
                    shader.Item2, Vortice.D3DCompiler.ShaderFlags.OptimizationLevel3, Vortice.D3DCompiler.EffectFlags.None);
                Console.WriteLine(shader.Item1 + " compiled, bytes=" + blob.AsBytes().Length);
            }
            return 0;
        }
        if (!args.Contains("--case") || args.Contains("--gpu-only") == args.Contains("--projection-only"))
            throw new ArgumentException("Capture requires --case and exactly one of --projection-only or --gpu-only; use one capture per process.");
        bool safe = !args.Contains("--full-rhino");
        string[] startup = safe ? new[] { "/safemode", "/nosplash", "/notemplate" }
            : new[] { "/nosplash", "/notemplate" };
        Console.WriteLine("Starting isolated Rhino runtime; safe=" + safe);
        using var core = new RhinoCore(startup, WindowStyle.Hidden);
        Console.WriteLine("Rhino=" + RhinoApp.Version);
        using RhinoDoc document = RhinoDoc.Create(null);
        RhinoView view = document.Views.Add("Trail capture probe", DefinedViewportProjection.Perspective,
            new Rectangle(0, 0, 960, 640), false);
        if (view == null) throw new InvalidOperationException("Isolated Rhino did not create a view.");
        view.ActiveViewport.DisplayMode = DisplayModeDescription.FindByName("Wireframe");
        view.ActiveViewport.ConstructionGridVisible = false;
        view.ActiveViewport.ConstructionAxesVisible = false;
        using var conduit = new TrailConduit(renderer);
        conduit.Enabled = true;
        var results = new List<object>();
        bool passed = true;
        foreach (bool perspective in new[] { false, true })
        {
            string caseFilter = Program.Option(args, "--case", "");
            if (caseFilter.Length != 0 && !caseFilter.StartsWith(perspective ? "perspective-" : "parallel-")) continue;
            RhinoViewport vp = view.ActiveViewport;
            vp.SetProjection(DefinedViewportProjection.Perspective, "Probe", false);
            vp.SetCameraLocations(new Point3d(50, 50, 50), new Point3d(175, -210, 180));
            if (!perspective) vp.ChangeToParallelProjection(true);
            vp.ZoomBoundingBox(TrailConduit.Bounds);
            vp.Magnify(0.8, false);
            view.Redraw();
            core.DoEvents();
            var sizes = new List<Size> { new(960, 640), new(1920, 1280), new(1200, 1200), new(1800, 600), new(600, 1800) };
            if (args.Contains("--large")) sizes.Add(new Size(5000, 3000));
            foreach (Size size in sizes)
            {
                string name = (perspective ? "perspective" : "parallel") + "-" + size.Width + "x" + size.Height;
                string selectedCase = Program.Option(args, "--case", "");
                if (selectedCase.Length != 0 && selectedCase != name) continue;
                Console.WriteLine("Capturing " + name);
                conduit.DrawNative = true;
                conduit.Events.Clear();
                conduit.SawTiledDraw = false;
                conduit.TargetSize = size;
                using Bitmap native = args.Contains("--gpu-only")
                    ? new Bitmap(Path.Combine(output, name + "-native.png")) : Capture(view, size);
                Console.WriteLine(args.Contains("--gpu-only") ? "Loaded saved native reference" : "Native capture returned");
                if (!args.Contains("--gpu-only")) native.Save(Path.Combine(output, name + "-native.png"), ImageFormat.Png);
                if (args.Contains("--projection-only"))
                {
                    using Bitmap oldProjection = conduit.Project(false);
                    using Bitmap newProjection = conduit.Project(true);
                    oldProjection.Save(Path.Combine(output, name + "-old-projection.png"), ImageFormat.Png);
                    newProjection.Save(Path.Combine(output, name + "-new-projection.png"), ImageFormat.Png);
                    Alignment oldMetric = Compare(native, oldProjection), newMetric = Compare(native, newProjection);
                    bool projectionSupported = conduit.TilingAvailable && !conduit.SawTiledDraw;
                    bool projectionSuccess = projectionSupported && newMetric.NativeNearGpu >= 0.97 && newMetric.GpuNearNative >= 0.97;
                    passed &= projectionSuccess;
                    var projectionResult = new { name, mode = "native-reference-and-projection", projectionSupported,
                        projectionSuccess, oldMetric, newMetric, events = conduit.Events.ToArray() };
                    results.Add(projectionResult);
                    Console.WriteLine(JsonSerializer.Serialize(projectionResult));
                    File.WriteAllText(Path.Combine(output, "projection-results.json"), JsonSerializer.Serialize(
                        new { rhino = RhinoApp.Version.ToString(), assemblyPath, passed, results }, new JsonSerializerOptions { WriteIndented = true }));
                    if (selectedCase.Length != 0) ExitAfterReport(projectionSupported ? (projectionSuccess ? 0 : 1) : 2);
                    continue;
                }
                conduit.DrawNative = false;
                int draws = conduit.SuccessfulDraws;
                conduit.Events.Clear();
                Console.WriteLine("GPU capture starting");
                using Bitmap gpu = Capture(view, size);
                gpu.Save(Path.Combine(output, name + "-gpu.png"), ImageFormat.Png);
                bool drawSucceeded = conduit.SuccessfulDraws > draws;
                Alignment metric = Compare(native, gpu);
                bool success = drawSucceeded && metric.NativePixels > 50 && metric.GpuPixels > 50
                    && metric.NativeNearGpu >= 0.97 && metric.GpuNearNative >= 0.97;
                passed &= success;
                var result = new { name, mode = "gpu-against-saved-native-reference", nativeReference = Path.Combine(output, name + "-native.png"),
                    success, drawSucceeded, metric, events = conduit.Events.ToArray(), conduit.LastError };
                results.Add(result);
                Console.WriteLine(JsonSerializer.Serialize(result));
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(
                    new { rhino = RhinoApp.Version.ToString(), assemblyPath, passed, results }, new JsonSerializerOptions { WriteIndented = true }));
                if (selectedCase.Length != 0) ExitAfterReport(drawSucceeded ? (success ? 0 : 1) : 2);
                if (!drawSucceeded)
                {
                    Console.Error.WriteLine("D3D trail draw unavailable; this is not a passing regression result.");
                    return 2;
                }
            }
        }
        throw new ArgumentException("Unknown --case value; no capture was performed.");
    }

    static void ExitAfterReport(int status)
    {
        // RhinoCore's hidden display host can hang on a second capture or disposal.
        // Each requested case owns this entire process and an unsaved synthetic document.
        // Flush durable evidence, then let process teardown release native/GPU resources.
        Program.ExitImmediately(status);
    }

    static Bitmap Capture(RhinoView view, Size size)
    {
        var capture = new ViewCapture
        {
            Width = size.Width, Height = size.Height, DrawAxes = false, DrawGrid = false,
            DrawGridAxes = false, ScaleScreenItems = false, TransparentBackground = false
        };
        using Bitmap captured = capture.CaptureToBitmap(view) ?? throw new InvalidOperationException("Rhino capture returned null.");
        return new Bitmap(captured);
    }

    public sealed record Alignment(int NativePixels, int GpuPixels, double NativeNearGpu, double GpuNearNative);

    // Compare chromatic line masks in both directions, allowing normal line rasterizer/AA differences.
    static Alignment Compare(Bitmap native, Bitmap gpu)
    {
        bool[] a = Mask(native), b = Mask(gpu);
        int ac = a.Count(value => value), bc = b.Count(value => value);
        double Coverage(bool[] source, bool[] target, int count)
        {
            int near = 0, width = native.Width, height = native.Height;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (!source[y * width + x]) continue;
                    bool found = false;
                    for (int dy = -3; dy <= 3 && !found; dy++)
                        for (int dx = -3; dx <= 3 && !found; dx++)
                        {
                            int tx = x + dx, ty = y + dy;
                            if (tx >= 0 && tx < width && ty >= 0 && ty < height && target[ty * width + tx]) found = true;
                        }
                    if (found) near++;
                }
            return count == 0 ? 0 : (double)near / count;
        }
        return new Alignment(ac, bc, Coverage(a, b, ac), Coverage(b, a, bc));
    }

    static unsafe bool[] Mask(Bitmap bitmap)
    {
        bool[] mask = new bool[bitmap.Width * bitmap.Height];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte* p = row + x * 4;
                    mask[y * bitmap.Width + x] = p[2] > p[1] + 25 && p[0] > p[1] + 25;
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
        return mask;
    }
}

sealed class TrailConduit : DisplayConduit, IDisposable
{
    public static readonly BoundingBox Bounds = new(new Point3d(0, 0, 0), new Point3d(100, 100, 100));
    readonly Line[] lines;
    readonly MethodInfo draw, getD3D, unregister;
    readonly Type gpuType, displayType;
    readonly Guid previewId = Guid.NewGuid();
    ID3D11Texture2D texture;
    object frame;
    public bool DrawNative;
    public int SuccessfulDraws;
    public string LastError;
    public readonly List<object> Events = new();
    public Size TargetSize;
    public bool SawTiledDraw;
    public bool TilingAvailable = true;
    float[] projection;
    Transform screen;
    Size viewportSize;

    object Tiling(DisplayPipeline pipeline)
    {
        MethodInfo method = pipeline.GetType().GetMethod("IsInTiledDraw");
        if (method == null) { TilingAvailable = false; return new { supported = false }; }
        object[] args = { Size.Empty, Rectangle.Empty };
        bool tiled = (bool)method.Invoke(pipeline, args);
        SawTiledDraw |= tiled;
        return new { supported = true, tiled, fullSize = args[0], tile = args[1] };
    }

    public TrailConduit(Assembly assembly)
    {
        Type renderer = assembly.GetType("Nuclei4.ParticleTrailD3DRenderer", true);
        draw = renderer.GetMethod("TryDraw");
        unregister = renderer.GetMethod("Unregister");
        getD3D = assembly.GetType("Nuclei4.RhinoWipD3DPreviewProbe", true).GetMethod("TryGetRhinoD3D");
        Assembly abstractions = Assembly.Load("Nuclei4.Display.Abstractions");
        gpuType = abstractions.GetType("Nuclei4.GpuParticleTrailPreviewFrame", true);
        displayType = abstractions.GetType("Nuclei4.ParticleTrailPreviewDisplayFrame", true);
        var geometry = new List<Line>();
        // Short, asymmetrically distributed segments test position, scale, and perspective at several depths.
        foreach (int z in new[] { 20, 50, 80 })
            foreach (int x in new[] { 20, 50, 80 })
            {
                geometry.Add(new Line(new Point3d(x - 7, 23, z), new Point3d(x + 7, 37, z + 6)));
                geometry.Add(new Line(new Point3d(x - 7, 60, z), new Point3d(x + 7, 60, z)));
                geometry.Add(new Line(new Point3d(x, 70, z - 6), new Point3d(x, 83, z + 6)));
            }
        lines = geometry.ToArray();
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e) => e.IncludeBoundingBox(Bounds);
    protected override void PostDrawObjects(DrawEventArgs e)
    {
        if (DrawNative)
        {
            projection = e.Display.GetOpenGLWorldToClip(true);
            screen = e.Viewport.GetTransform(Rhino.DocObjects.CoordinateSystem.World, Rhino.DocObjects.CoordinateSystem.Screen);
            viewportSize = e.Viewport.Size;
            Events.Add(new { viewportSize, pipelineProjection = projection,
                tiling = Tiling(e.Display) });
            e.Display.DrawLines(lines, Color.Magenta, 1);
            return;
        }
        try
        {
            if (frame == null) Console.WriteLine("GPU first callback: resolving D3D pointers");
            object[] pointers = { e.Display, e.Viewport, IntPtr.Zero, IntPtr.Zero };
            if (!(bool)getD3D.Invoke(null, pointers)) { LastError = "Rhino did not provide D3D11 pointers."; return; }
            if (frame == null) { Console.WriteLine("GPU first callback: creating frame"); CreateFrame((IntPtr)pointers[2]); Console.WriteLine("GPU first callback: frame ready"); }
            bool success = (bool)draw.Invoke(null, new[] { (object)previewId, e, frame });
            if (success) SuccessfulDraws++;
            Events.Add(new { success, viewportWidth = e.Viewport.Size.Width, viewportHeight = e.Viewport.Size.Height,
                pipelineProjection = e.Display.GetOpenGLWorldToClip(true),
                tiling = Tiling(e.Display) });
        }
        catch (Exception error)
        {
            LastError = error.ToString();
        }
    }

    public Bitmap Project(bool pipeline)
    {
        var bitmap = new Bitmap(TargetSize.Width, TargetSize.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        using var pen = new Pen(Color.Magenta, 1);
        PointF Map(Point3d p)
        {
            if (!pipeline)
            {
                p.Transform(screen);
                return new PointF((float)(p.X / viewportSize.Width * TargetSize.Width), (float)(p.Y / viewportSize.Height * TargetSize.Height));
            }
            double x = projection[0] * p.X + projection[4] * p.Y + projection[8] * p.Z + projection[12];
            double y = projection[1] * p.X + projection[5] * p.Y + projection[9] * p.Z + projection[13];
            double w = projection[3] * p.X + projection[7] * p.Y + projection[11] * p.Z + projection[15];
            return new PointF((float)((x / w + 1) * 0.5 * TargetSize.Width), (float)((1 - y / w) * 0.5 * TargetSize.Height));
        }
        foreach (Line line in lines) graphics.DrawLine(pen, Map(line.From), Map(line.To));
        return bitmap;
    }

    unsafe void CreateFrame(IntPtr devicePointer)
    {
        Marshal.AddRef(devicePointer);
        using var device = new ID3D11Device(devicePointer);
        int width = lines.Length;
        float[] pixels = new float[width * 2 * 4];
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < width; i++)
            {
                Point3d point = row == 0 ? lines[i].From : lines[i].To;
                int offset = (row * width + i) * 4;
                pixels[offset] = (float)point.X; pixels[offset + 1] = (float)point.Y; pixels[offset + 2] = (float)point.Z;
            }
        var description = new Texture2DDescription(Format.R32G32B32A32_Float, width, 2, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.Shared);
        fixed (float* pointer = pixels)
            texture = device.CreateTexture2D(description, new[] { new SubresourceData((IntPtr)pointer, width * 16, 0) });
        using var resource = texture.QueryInterface<IDXGIResource>();
        object gpu = Activator.CreateInstance(gpuType);
        void Set(object target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);
        Set(gpu, "SharedHandle", resource.SharedHandle);
        Set(gpu, "TextureWidth", width); Set(gpu, "TextureHeight", 2); Set(gpu, "ParticleCount", width);
        Set(gpu, "TrailSize", 2); Set(gpu, "ValidTrailCount", 2); Set(gpu, "HeadIndex", 0);
        Set(gpu, "ResX", 100); Set(gpu, "ResY", 100); Set(gpu, "ResZ", 100); Set(gpu, "VoxelSize", 1f);
        frame = Activator.CreateInstance(displayType);
        Set(frame, "GpuFrame", gpu); Set(frame, "FreshColor", Color.Magenta); Set(frame, "OldColor", Color.Magenta);
        Set(frame, "FreshColors", new[] { Color.Magenta }); Set(frame, "OldColors", new[] { Color.Magenta });
        Set(frame, "Alpha", 1.0); Set(frame, "FadePower", 1.0); Set(frame, "DepthFocus", 0.0); Set(frame, "ClippingBox", Bounds);
    }

    public void Dispose()
    {
        Enabled = false;
        unregister.Invoke(null, new object[] { previewId });
        texture?.Dispose();
    }
}
