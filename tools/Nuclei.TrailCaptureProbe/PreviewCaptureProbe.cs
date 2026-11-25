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
using Point = System.Drawing.Point;

// One isolated capture per process; no user document or Grasshopper definition is loaded.
public static class PreviewCaptureProbe
{
    public static int Run(string[] args, string assemblyPath)
    {
        string kind = Program.Option(args, "--preview", "particles");
        bool foodOverlay = args.Contains("--food-overlay-fixture");
        bool highResolution = args.Contains("--high-resolution");
        int valueIndex = int.Parse(Program.Option(args, "--value-index", "11"));
        if (!new[] { "particles", "particle-cpu", "density-plane", "density-volume" }.Contains(kind))
            throw new ArgumentException("Unknown preview kind: " + kind);
        if (foodOverlay && (!kind.StartsWith("density-") || valueIndex < 7 || valueIndex > 11))
            throw new ArgumentException("Food overlay fixtures require a density preview and --value-index 7 through 11.");
        if (args.Contains("--compare-images"))
        {
            string saved = Path.Combine(Path.GetFullPath(Program.Option(args, "--output", ".codex-temp/preview-capture")),
                kind + "-" + Program.Option(args, "--case", ""));
            using var reference = new Bitmap(saved + "-native.png");
            string gpuPrefix = saved + (args.Contains("--fancy") ? "-fancy" : "");
            using var gpu = new Bitmap(gpuPrefix + "-gpu.png");
            MaskMetrics comparison = Compare(reference, gpu, kind.StartsWith("particle"), saved);
            File.WriteAllText(gpuPrefix + "-comparison.json", JsonSerializer.Serialize(comparison, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(comparison));
            return comparison.Passed ? 0 : 1;
        }
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        if (args.Contains("--compile-shaders"))
        {
            string rendererName = kind.StartsWith("particle") ? "ParticlePreviewD3DRenderer" : "GpuDensityFieldD3DRenderer";
            string source = (string)assembly.GetType("Nuclei4." + rendererName, true)
                .GetField("ShaderSource", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            var shaders = kind.StartsWith("particle")
                ? new[] { ("VSMain", "vs_4_0"), ("VSGpu", "vs_4_0"), ("PSMain", "ps_4_0") }
                : new[] { ("VSMain", "vs_4_0"), ("PSMain", "ps_5_0") };
            foreach (var shader in shaders)
            {
                using var blob = Vortice.D3DCompiler.Compiler.Compile(source, shader.Item1, "PreviewCaptureProbe", null, null,
                    shader.Item2, Vortice.D3DCompiler.ShaderFlags.OptimizationLevel3, Vortice.D3DCompiler.EffectFlags.None);
                Console.WriteLine(rendererName + " " + shader.Item1 + " compiled, bytes=" + blob.AsBytes().Length);
            }
            return 0;
        }
        bool native = args.Contains("--native-reference");
        if (!args.Contains("--case") || !foodOverlay && native == args.Contains("--gpu-only"))
            throw new ArgumentException("Use --case and exactly one of --native-reference or --gpu-only.");
        if (foodOverlay && native) throw new ArgumentException("Food overlay fixtures directly validate the actual GPU result.");
        string name = Program.Option(args, "--case", "");
        string[] parts = name.Split('-');
        string[] dimensions = parts.Last().Split('x');
        if (parts.Length != 2 || !new[] { "parallel", "perspective" }.Contains(parts[0]) || dimensions.Length != 2)
            throw new ArgumentException("Case must be parallel-WIDTHxHEIGHT or perspective-WIDTHxHEIGHT.");
        Size size = new(int.Parse(dimensions[0]), int.Parse(dimensions[1]));
        if (size.Width < 100 || size.Width > 8192 || size.Height < 100 || size.Height > 8192)
            throw new ArgumentException("Capture dimensions must be between 100 and 8192.");
        string output = Path.GetFullPath(Program.Option(args, "--output", ".codex-temp/preview-capture"));
        Directory.CreateDirectory(output);
        string prefix = Path.Combine(output, kind + "-" + name);
        if (foodOverlay) prefix += "-food-overlay-" + valueIndex + (highResolution ? "-high" : "-standard");
        Console.WriteLine("Starting isolated Rhino for " + kind + " " + name);
        using var core = new RhinoCore(new[] { "/nosplash", "/notemplate" }, WindowStyle.Hidden);
        using RhinoDoc document = RhinoDoc.Create(null);
        RhinoView view = document.Views.Add("Preview capture regression", DefinedViewportProjection.Perspective,
            new Rectangle(0, 0, 960, 640), false) ?? throw new InvalidOperationException("Could not create isolated view.");
        RhinoViewport vp = view.ActiveViewport;
        vp.DisplayMode = DisplayModeDescription.FindByName("Wireframe");
        vp.ConstructionGridVisible = false;
        vp.ConstructionAxesVisible = false;
        using var conduit = new PreviewFixtureConduit(assembly, kind) { Native = native, Fancy = args.Contains("--fancy"),
            FoodOverlayFixture = foodOverlay, HighResolution = highResolution, ValueIndex = valueIndex, Enabled = true };
        vp.SetProjection(DefinedViewportProjection.Perspective, "Probe", false);
        vp.SetCameraLocations(new Point3d(50, 50, 50), new Point3d(175, -210, 180));
        if (foodOverlay)
        {
            vp.SetCameraLocations(new Point3d(50, 50, 50), new Point3d(50, 50, 220));
            vp.CameraUp = Vector3d.YAxis;
        }
        if (parts[0] == "parallel") vp.ChangeToParallelProjection(true);
        vp.ZoomBoundingBox(PreviewFixtureConduit.Bounds);
        vp.Magnify(0.8, false);
        view.Redraw();
        core.DoEvents();
        conduit.Events.Clear();
        int before = conduit.SuccessfulDraws;
        var capture = new ViewCapture
        {
            Width = size.Width, Height = size.Height, DrawAxes = false, DrawGrid = false,
            DrawGridAxes = false, ScaleScreenItems = false, TransparentBackground = false
        };
        Console.WriteLine("Capturing " + (native ? "native reference" : "actual D3D preview"));
        using Bitmap bitmap = capture.CaptureToBitmap(view) ?? throw new InvalidOperationException("Capture returned null.");
        string resultPrefix = prefix + (!native && conduit.Fancy ? "-fancy" : "");
        bitmap.Save(resultPrefix + (native ? "-native.png" : "-gpu.png"), ImageFormat.Png);
        bool drawSucceeded = native || conduit.SuccessfulDraws > before;
        object metric = null;
        bool passed = true;
        if (foodOverlay)
        {
            using JsonDocument events = JsonDocument.Parse(JsonSerializer.Serialize(conduit.Events));
            float[] projection = events.RootElement.EnumerateArray().Last().GetProperty("projection")
                .EnumerateArray().Select(v => v.GetSingle()).ToArray();
            FoodOverlayMetrics comparison = InspectFoodOverlay(bitmap, projection);
            metric = comparison;
            passed = drawSucceeded && comparison.Passed && conduit.OverlayDraws > 0
                && (kind != "density-volume" || highResolution || conduit.OverlayUsedNewRenderer);
        }
        else if (!native)
        {
            using var reference = new Bitmap(prefix + "-native.png");
            MaskMetrics comparison = Compare(reference, bitmap, kind.StartsWith("particle"), prefix);
            metric = comparison;
            passed = drawSucceeded && comparison.Passed;
        }
        var report = new { kind, name, assemblyPath, fancy = conduit.Fancy, foodOverlay, highResolution, valueIndex,
            conduit.OverlayDraws, conduit.OverlayUsedNewRenderer, rhino = RhinoApp.Version.ToString(), mode = native ? "native-reference" : "actual-gpu",
            passed, drawSucceeded, metric, conduit.LastError, events = conduit.Events.ToArray() };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(resultPrefix + (native ? "-native.json" : "-results.json"), json);
        Console.WriteLine(JsonSerializer.Serialize(new { kind, name, passed, drawSucceeded, conduit.LastError,
            summary = metric is MaskMetrics m ? new { m.NativePixels, m.GpuPixels, m.NativeNearGpu, m.GpuNearNative,
                nativeComponents = m.NativeComponents.Length, gpuComponents = m.GpuComponents.Length,
                m.MaximumCenterError, m.PixelSizePreserved, m.IsolatedParticlesChecked } : null }));
        Program.ExitImmediately(drawSucceeded ? (passed ? 0 : 1) : 2);
        return 3;
    }

    public sealed record FoodOverlayMetrics(bool Passed, int FoodPixels, int RedEmptyPixels, int Samples, int[] FoodCenter, int[] EmptyCenter);

    static FoodOverlayMetrics InspectFoodOverlay(Bitmap image, float[] matrix)
    {
        Point Project(double x, double y)
        {
            const double z = 50;
            double w = matrix[3] * x + matrix[7] * y + matrix[11] * z + matrix[15];
            return new Point((int)Math.Round(((matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12]) / w + 1) * image.Width / 2),
                (int)Math.Round((1 - (matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13]) / w) * image.Height / 2));
        }
        int Count(Point center, Func<Color, bool> predicate)
        {
            int count = 0;
            for (int y = center.Y - 3; y <= center.Y + 3; y++)
                for (int x = center.X - 3; x <= center.X + 3; x++)
                    if (x >= 0 && x < image.Width && y >= 0 && y < image.Height && predicate(image.GetPixel(x, y))) count++;
            return count;
        }
        Point food = Project(36, 50), empty = Project(76, 50);
        int foodPixels = Count(food, c => Math.Min(c.R, Math.Min(c.G, c.B)) > 65 && Math.Abs(c.R - c.G) < 65 && Math.Abs(c.R - c.B) < 65);
        int redPixels = Count(empty, c => c.R > 150 && c.G < 25 && c.B < 25);
        return new(foodPixels >= 45 && redPixels >= 45, foodPixels, redPixels, 49,
            new[] { food.X, food.Y }, new[] { empty.X, empty.Y });
    }

    public sealed record Component(int Pixels, double X, double Y, int Width, int Height);
    public sealed record MaskMetrics(bool Passed, int NativePixels, int GpuPixels, double NativeNearGpu, double GpuNearNative,
        Component[] NativeComponents, Component[] GpuComponents, double MaximumCenterError, bool PixelSizePreserved, int IsolatedParticlesChecked);

    static MaskMetrics Compare(Bitmap native, Bitmap gpu, bool particles, string referencePrefix)
    {
        bool[] a = Mask(native, particles ? 25 : 5), b = Mask(gpu, particles ? 25 : 5);
        int width = native.Width, height = native.Height;
        Component[] ac = Components(a, width, height), bc = Components(b, width, height);
        int ap = a.Count(v => v), bp = b.Count(v => v);
        double Coverage(bool[] source, bool[] target, int count)
        {
            int near = 0;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                if (!source[y * width + x]) continue;
                bool found = false;
                for (int dy = -3; dy <= 3 && !found; dy++) for (int dx = -3; dx <= 3 && !found; dx++)
                {
                    int tx = x + dx, ty = y + dy;
                    if (tx >= 0 && tx < width && ty >= 0 && ty < height && target[ty * width + tx]) found = true;
                }
                if (found) near++;
            }
            return count > 0 ? (double)near / count : 0;
        }
        double ab = Coverage(a, b, ap), ba = Coverage(b, a, bp);
        Component[] isolatedReference = Array.Empty<Component>();
        if (particles)
        {
            // Perspective can overlap a pair of the 27 points. Use the recorded projection only
            // to identify isolated samples; placement is still measured against native raster centroids.
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(referencePrefix + "-native.json"));
            float[] projection = metadata.RootElement.GetProperty("events").EnumerateArray().Last()
                .GetProperty("projection").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            var centers = new List<PointF>();
            foreach (int z in new[] { 19, 48, 81 }) foreach (int y in new[] { 22, 51, 77 }) foreach (int x in new[] { 18, 49, 82 })
            {
                double w = projection[3] * x + projection[7] * y + projection[11] * z + projection[15];
                centers.Add(new PointF((float)(((projection[0] * x + projection[4] * y + projection[8] * z + projection[12]) / w + 1) * width * 0.5),
                    (float)((1 - (projection[1] * x + projection[5] * y + projection[9] * z + projection[13]) / w) * height * 0.5)));
            }
            isolatedReference = centers.Where((p, i) => centers.Where((_, j) => j != i)
                .All(q => Math.Sqrt(Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2)) > 16))
                .Select(p => ac.OrderBy(q => Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2)).FirstOrDefault())
                .Where(p => p != null).Distinct().ToArray();
        }
        Component NearestGpu(Component p) => bc.OrderBy(q => Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2)).FirstOrDefault();
        double center = particles && isolatedReference.Length > 0 && bc.Length > 0
            ? isolatedReference.Max(p => { Component q = NearestGpu(p); return Math.Sqrt(Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2)); }) : 0;
        // The renderer specifies a 12-pixel diameter; AA may exclude at most one pixel per side.
        Component[] checkedGpu = isolatedReference.Select(NearestGpu).Where(p => p != null).Distinct().ToArray();
        bool sizes = !particles || checkedGpu.Length >= 20 && checkedGpu.All(c => c.Width >= 10 && c.Width <= 12 && c.Height >= 10 && c.Height <= 12
            && Math.Abs(c.Width - c.Height) <= 1);
        bool passed = ap > 50 && bp > 50 && ab >= 0.97 && ba >= 0.97
            && (!particles || isolatedReference.Length >= 20 && center <= 1.5 && sizes);
        return new(passed, ap, bp, ab, ba, ac, bc, center, sizes, checkedGpu.Length);
    }

    static Component[] Components(bool[] mask, int width, int height)
    {
        bool[] seen = new bool[mask.Length];
        var result = new List<Component>();
        var queue = new Queue<int>();
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i] || seen[i]) continue;
            seen[i] = true; queue.Enqueue(i);
            int count = 0, minX = width, minY = height, maxX = 0, maxY = 0;
            long sumX = 0, sumY = 0;
            while (queue.Count > 0)
            {
                int index = queue.Dequeue(), x = index % width, y = index / width;
                count++; sumX += x; sumY += y;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int tx = x + dx, ty = y + dy;
                    if (tx < 0 || tx >= width || ty < 0 || ty >= height) continue;
                    int next = ty * width + tx;
                    if (mask[next] && !seen[next]) { seen[next] = true; queue.Enqueue(next); }
                }
            }
            if (count >= 5) result.Add(new(count, (double)sumX / count, (double)sumY / count, maxX - minX + 1, maxY - minY + 1));
        }
        return result.ToArray();
    }

    static unsafe bool[] Mask(Bitmap bitmap, int threshold)
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
                    mask[y * bitmap.Width + x] = p[2] > p[1] + threshold && p[0] > p[1] + threshold;
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
        return mask;
    }
}

sealed class PreviewFixtureConduit : DisplayConduit, IDisposable
{
    public static readonly BoundingBox Bounds = new(new Point3d(0, 0, 0), new Point3d(100, 100, 100));
    readonly string kind;
    readonly bool particle;
    readonly MethodInfo draw, drawFoodOverlay, getD3D, unregister;
    readonly Type renderer;
    readonly Assembly abstractions;
    readonly Guid id = Guid.NewGuid();
    readonly Point3d[] points;
    ID3D11Texture2D texture;
    object frame;
    public bool Native;
    public bool Fancy;
    public bool FoodOverlayFixture;
    public bool HighResolution;
    public int ValueIndex = 11;
    public int OverlayDraws;
    public bool OverlayUsedNewRenderer;
    public int SuccessfulDraws;
    public string LastError;
    public readonly List<object> Events = new();

    public PreviewFixtureConduit(Assembly assembly, string kind)
    {
        this.kind = kind;
        particle = kind.StartsWith("particle");
        renderer = assembly.GetType("Nuclei4." + (particle ? "ParticlePreviewD3DRenderer" : "GpuDensityFieldD3DRenderer"), true);
        draw = renderer.GetMethod("TryDraw"); unregister = renderer.GetMethod("Unregister");
        drawFoodOverlay = renderer.GetMethod("TryDrawFoodOverlay");
        getD3D = assembly.GetType("Nuclei4.RhinoWipD3DPreviewProbe", true).GetMethod("TryGetRhinoD3D");
        abstractions = Assembly.Load("Nuclei4.Display.Abstractions");
        var geometry = new List<Point3d>();
        foreach (int z in new[] { 19, 48, 81 }) foreach (int y in new[] { 22, 51, 77 }) foreach (int x in new[] { 18, 49, 82 })
            geometry.Add(new Point3d(x, y, z));
        points = geometry.ToArray();
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e) => e.IncludeBoundingBox(Bounds);

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        try
        {
            if (Native)
            {
                if (particle) e.Display.DrawPoints(points, PointStyle.RoundSimple, 6, Color.Magenta);
                else if (kind == "density-plane") e.Display.DrawPolygon(new[] { new Point3d(0, 0, 0), new Point3d(100, 0, 0),
                    new Point3d(100, 100, 0), new Point3d(0, 100, 0) }, Color.Magenta, true);
                else
                {
                    using Mesh box = Mesh.CreateFromBox(Bounds, 1, 1, 1);
                    e.Display.DrawMeshShaded(box, new DisplayMaterial(Color.Magenta));
                }
            }
            else
            {
                object[] pointers = { e.Display, e.Viewport, IntPtr.Zero, IntPtr.Zero };
                if (!(bool)getD3D.Invoke(null, pointers)) { LastError = "D3D pointers unavailable"; return; }
                if (frame == null) CreateFrame((IntPtr)pointers[2]);
                if ((bool)draw.Invoke(null, new[] { (object)id, e, frame })) SuccessfulDraws++;
                else LastError = "Renderer TryDraw returned false";
                if (FoodOverlayFixture)
                {
                    // Hide the entire original preview after its signal pass.
                    // The final food pass must reveal only positive food pixels.
                    e.Display.PushDepthTesting(false);
                    e.Display.PushDepthWriting(false);
                    try
                    {
                        e.Display.DrawPolygon(new[] { new Point3d(0, 0, 50), new Point3d(100, 0, 50),
                            new Point3d(100, 100, 50), new Point3d(0, 100, 50) }, Color.Red, true);
                    }
                    finally { e.Display.PopDepthWriting(); e.Display.PopDepthTesting(); }
                    if (drawFoodOverlay == null) throw new MissingMethodException("TryDrawFoodOverlay is unavailable.");
                    if ((bool)drawFoodOverlay.Invoke(null, new[] { (object)id, e, frame })) OverlayDraws++;
                    else LastError = "Renderer TryDrawFoodOverlay returned false";
                    object instance = renderer.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                    var cache = (System.Collections.IDictionary)renderer.GetField("foodOverlayTextureViews", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
                    object cached = cache[id];
                    OverlayUsedNewRenderer = cached?.GetType().GetField("newRenderer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cached) != null;
                }
            }
            MethodInfo tilingMethod = e.Display.GetType().GetMethod("IsInTiledDraw");
            object[] tiling = { Size.Empty, Rectangle.Empty };
            bool? tiled = tilingMethod == null ? null : (bool)tilingMethod.Invoke(e.Display, tiling);
            object raster = null;
            object[] rasterPointers = { e.Display, e.Viewport, IntPtr.Zero, IntPtr.Zero };
            if ((bool)getD3D.Invoke(null, rasterPointers))
            {
                Marshal.AddRef((IntPtr)rasterPointers[3]);
                using var context = new ID3D11DeviceContext1((IntPtr)rasterPointers[3]);
                raster = context.RSGetViewports<Vortice.Mathematics.Viewport>()
                    .Select(v => new { v.Width, v.Height, v.X, v.Y }).ToArray();
            }
            Events.Add(new { viewportWidth = e.Viewport.Size.Width, viewportHeight = e.Viewport.Size.Height,
                e.Display.IsInViewCapture, e.Display.IsPrinting, raster,
                projection = e.Display.GetOpenGLWorldToClip(true), tiled, fullSize = tiling[0], tile = tiling[1] });
        }
        catch (Exception error) { LastError = error.ToString(); }
    }

    static void Set(object target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);
    object Frame(string name) => Activator.CreateInstance(abstractions.GetType("Nuclei4." + name, true));

    unsafe void CreateFrame(IntPtr devicePointer)
    {
        if (kind == "particle-cpu")
        {
            frame = Frame("ParticlePreviewDisplayFrame");
            var cloud = new PointCloud();
            foreach (Point3d point in points) cloud.Add(point, Color.Magenta);
            Set(frame, "SlimePointCloud", cloud); Set(frame, "PointSize", 12.0); Set(frame, "HasPoint", true); Set(frame, "ClippingBox", Bounds);
            return;
        }
        Marshal.AddRef(devicePointer);
        using var device = new ID3D11Device(devicePointer);
        int width = particle ? points.Length : (kind == "density-volume" ? 128 : 32);
        int height = particle ? 2 : (kind == "density-volume" ? 256 : 32);
        int channels = particle || FoodOverlayFixture ? 4 : 1;
        float[] pixels = new float[width * height * channels];
        if (particle)
        {
            for (int i = 0; i < points.Length; i++)
            {
                int pos = i * 4, color = (width + i) * 4;
                pixels[pos] = (float)points[i].X; pixels[pos + 1] = (float)points[i].Y; pixels[pos + 2] = (float)points[i].Z;
                pixels[color] = 1; pixels[color + 2] = 1; pixels[color + 3] = 1;
            }
        }
        else if (FoodOverlayFixture)
        {
            for (int z = 0; z < (kind == "density-volume" ? 32 : 1); z++)
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    int index = ((z / 4 * 32 + y) * width + z % 4 * 32 + x) * 4;
                    pixels[index] = 6; pixels[index + 1] = 6; pixels[index + 2] = 6;
                    pixels[index + 3] = x >= 8 && x < 16 && y >= 10 && y < 22 ? 0.65f : 0;
                }
        }
        else Array.Fill(pixels, 1f);
        var description = new Texture2DDescription(particle || FoodOverlayFixture ? Format.R32G32B32A32_Float : Format.R32_Float, width, height, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.Shared);
        fixed (float* pointer = pixels)
            texture = device.CreateTexture2D(description, new[] { new SubresourceData((IntPtr)pointer, width * channels * 4, 0) });
        using var resource = texture.QueryInterface<IDXGIResource>();
        if (particle)
        {
            object gpu = Frame("GpuParticlePreviewFrame");
            Set(gpu, "SharedHandle", resource.SharedHandle); Set(gpu, "TextureWidth", width); Set(gpu, "TextureHeight", height);
            Set(gpu, "ParticleCount", points.Length); Set(gpu, "ResX", 100); Set(gpu, "ResY", 100); Set(gpu, "ResZ", 100); Set(gpu, "VoxelSize", 1f);
            frame = Frame("ParticlePreviewDisplayFrame");
            Set(frame, "GpuFrame", gpu); Set(frame, "PointSize", 12.0); Set(frame, "HasPoint", true); Set(frame, "ClippingBox", Bounds);
        }
        else
        {
            frame = Frame("GpuDensityFieldPreviewFrame");
            Set(frame, "SharedHandle", resource.SharedHandle); Set(frame, "Width", width); Set(frame, "Height", height);
            Set(frame, "ResX", 32); Set(frame, "ResY", 32); Set(frame, "ResZ", kind == "density-volume" ? 32 : 1);
            Set(frame, "VoxelSize", 100f / 32); Set(frame, "AxisMode", kind == "density-volume" ? 3 : 0);
            Set(frame, "AtlasColumns", kind == "density-volume" ? 4 : 1); Set(frame, "AtlasRows", kind == "density-volume" ? 8 : 1);
            Set(frame, "ValueIndex", 0); Set(frame, "UseCustomColor", true); Set(frame, "ColorR", 1f); Set(frame, "ColorG", 0f);
            Set(frame, "ColorB", 1f); Set(frame, "ColorA", 1f); Set(frame, "VolumeOpacity", 12f); Set(frame, "VolumeSampleCount", 128);
            Set(frame, "VolumeRendererVersion", 2); Set(frame, "FancyRender", Fancy);
            if (FoodOverlayFixture)
            {
                Set(frame, "ValueIndex", ValueIndex); Set(frame, "UseCustomColor", false);
                Set(frame, "MinimumThreshold", 2f); Set(frame, "MaximumThreshold", 8f);
                Set(frame, "VolumeOpacity", 0.8f); Set(frame, "VolumeRendererVersion", 1);
                Set(frame, "UseNewRenderer", !HighResolution && kind == "density-volume");
                Set(frame, "VolumeSampleCount", HighResolution ? 256 : 128);
            }
        }
    }

    public void Dispose()
    {
        Enabled = false;
        unregister?.Invoke(null, new object[] { id });
        texture?.Dispose();
    }
}
