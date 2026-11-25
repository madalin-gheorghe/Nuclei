using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Runtime.InProcess;
using Vortice.Direct3D11;
using Vortice.DXGI;

// An isolated, unsaved Rhino document. This does not register a component or change a renderer.
public static class MixedResolutionRenderProbe
{
    public static int Run(string[] args, string assemblyPath)
    {
        if (Program.Option(args, "--preview", "") != "density-volume")
            throw new ArgumentException("Mixed-resolution checks require --preview density-volume.");
        if (args.Contains("--volume-fixture") && !new[] { "filaments", "ant-pheromones" }.Contains(Program.Option(args, "--volume-fixture", "")))
            throw new ArgumentException("The available built-in volume fixtures are filaments and ant-pheromones.");
        string name = Program.Option(args, "--case", "parallel-native");
        string[] parts = name.Split('-');
        if (parts.Length != 2 || !new[] { "parallel", "perspective" }.Contains(parts[0]))
            throw new ArgumentException("Use --case parallel-native, perspective-native, or projection-WIDTHxHEIGHT.");
        string rendererMode = Program.Option(args, "--renderer-mode", "baseline");
        if (!new[] { "baseline", "new", "toggle" }.Contains(rendererMode))
            throw new ArgumentException("--renderer-mode must be baseline, new, or toggle.");
        bool expectIdentical = args.Contains("--expect-identical");
        bool benchmarkAbba = args.Contains("--benchmark-abba");
        int abbaCycles = benchmarkAbba ? ReadBounded(args, "--abba-cycles", 2, 1, 4) : 0;
        if (benchmarkAbba && (args.Contains("--renderer-mode") || args.Contains("--interactive-timing")))
            throw new ArgumentException("--benchmark-abba selects both renderers in one capture; do not combine it with --renderer-mode or --interactive-timing.");
        string label = Program.Option(args, "--label", benchmarkAbba ? "abba" : rendererMode);
        if (label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The experiment label must be a filename component.");
        string output = Path.GetFullPath(Program.Option(args, "--output", ".codex-temp/mixed-resolution-render"));
        Directory.CreateDirectory(output);
        string prefix = Path.Combine(output, "volume-" + name + "-" + label);
        foreach (string suffix in new[] { ".png", ".json" })
            if (File.Exists(prefix + suffix)) throw new IOException("Refusing to replace experiment evidence: " + prefix + suffix);

        VolumeFixture fixture = VolumeFixture.Load(args, out PreviewStyle style);
        int warmup = ReadBounded(args, "--warmup-draws", benchmarkAbba ? 8 : 4, benchmarkAbba ? 8 : 0, 64);
        int count = ReadBounded(args, "--timing-draws", benchmarkAbba ? 24 : 12, benchmarkAbba ? 1 : 0, 100);
        int samples = style.VolumeSampleCount;
        int valueIndex = style.ValueIndex;
        bool publishesGradient = style.VolumeRendererVersion == 2 && HasDynamicGradient(valueIndex);
        bool refresh = !args.Contains("--static-version");
        bool captureBatch = !args.Contains("--interactive-timing");
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        Console.WriteLine("Starting isolated Rhino volume fixture: " + fixture.Metadata.TestName);
        using var core = new RhinoCore(new[] { "/nosplash", "/notemplate" }, WindowStyle.Hidden);
        using RhinoDoc document = RhinoDoc.Create(null);
        RhinoView view = document.Views.Add("Isolated mixed-resolution volume test", DefinedViewportProjection.Perspective,
            new Rectangle(0, 0, 960, 640), false) ?? throw new InvalidOperationException("Could not create isolated viewport.");
        RhinoViewport viewport = view.ActiveViewport;
        viewport.DisplayMode = DisplayModeDescription.FindByName("Wireframe");
        viewport.ConstructionGridVisible = false;
        viewport.ConstructionAxesVisible = false;
        using var conduit = new VolumeConduit(assembly, fixture, samples, valueIndex, refresh, rendererMode,
            captureBatch, warmup, count, abbaCycles, style) { Enabled = true };
        BoundingBox bounds = fixture.Bounds;
        Point3d target = bounds.Center;
        double extent = Math.Max(bounds.Diagonal.X, Math.Max(bounds.Diagonal.Y, bounds.Diagonal.Z));
        viewport.SetProjection(DefinedViewportProjection.Perspective, "Volume A/B", false);
        viewport.SetCameraLocations(target, target + new Vector3d(1.25 * extent, -2.6 * extent, 1.3 * extent));
        if (parts[0] == "parallel") viewport.ChangeToParallelProjection(true);
        viewport.ZoomBoundingBox(bounds);
        viewport.Magnify(0.8, false);

        if (!captureBatch)
        {
            for (int i = 0; i < warmup; i++) { view.Redraw(); core.DoEvents(); }
            conduit.Phase = "interactive-measured";
            for (int i = 0; i < count; i++) { view.Redraw(); core.DoEvents(); }
        }
        conduit.Phase = "capture";
        Size size = viewport.Size;
        if (parts[1] != "native")
        {
            string[] dimensions = parts[1].Split('x');
            if (dimensions.Length != 2) throw new ArgumentException("Capture size must be native or WIDTHxHEIGHT.");
            size = new Size(int.Parse(dimensions[0]), int.Parse(dimensions[1]));
            if (size.Width < 100 || size.Height < 100 || size.Width > 8192 || size.Height > 8192)
                throw new ArgumentException("Capture dimensions must be 100 through 8192.");
        }
        var capture = new ViewCapture { Width = size.Width, Height = size.Height, DrawAxes = false,
            DrawGrid = false, DrawGridAxes = false, ScaleScreenItems = false, TransparentBackground = false };
        int before = conduit.SuccessfulDraws;
        using Bitmap image = capture.CaptureToBitmap(view) ?? throw new InvalidOperationException("Capture returned null.");
        bool captureDrawSucceeded = conduit.SuccessfulDraws > before;
        image.Save(prefix + ".png", ImageFormat.Png);
        bool neutralFoodFixture = args.Contains("--food-shapes");
        int visiblePixels = CountVisiblePixels(image, neutralFoodFixture);
        GpuSample[] gpuSamples = conduit.ResolveTimings();
        string measuredPhase = captureBatch ? "controlled-capture-measured" : "interactive-measured";
        GpuSample[] measured = gpuSamples.Where(s => s.Phase == measuredPhase && s.Valid).ToArray();
        GpuSample[] allMeasured = gpuSamples.Where(s => s.Phase == measuredPhase).ToArray();
        int expectedMeasured = benchmarkAbba ? checked(abbaCycles * 4 * count) : count;
        object[] batchSummaries = benchmarkAbba ? Enumerable.Range(0, abbaCycles * 4).Select(batch =>
        {
            GpuSample[] batchSamples = allMeasured.Where(s => s.Batch == batch).ToArray();
            double[] values = batchSamples.Where(s => s.Valid).Select(s => s.Milliseconds).ToArray();
            return (object)new { batch, cycle = batch / 4, mode = batch % 4 == 0 || batch % 4 == 3 ? "original" : "new",
                expected = count, totalSamples = batchSamples.Length, validSamples = values.Length,
                medianGpuMilliseconds = Median(values), p95GpuMilliseconds = Percentile95(values) };
        }).ToArray() : Array.Empty<object>();
        object[] modeSummaries = new[] { false, true }.Select(isNew =>
        {
            GpuSample[] modeSamples = allMeasured.Where(s => s.UseNewRenderer == isNew).ToArray();
            double[] values = modeSamples.Where(s => s.Valid).Select(s => s.Milliseconds).ToArray();
            return (object)new { mode = isNew ? "new" : "original", totalSamples = modeSamples.Length, validSamples = values.Length,
                medianGpuMilliseconds = Median(values), p95GpuMilliseconds = Percentile95(values) };
        }).ToArray();
        object[] cycleComparisons = benchmarkAbba ? Enumerable.Range(0, abbaCycles).Select(cycle =>
        {
            double? original = Median(measured.Where(s => s.Batch / 4 == cycle && !s.UseNewRenderer).Select(s => s.Milliseconds));
            double? newTime = Median(measured.Where(s => s.Batch / 4 == cycle && s.UseNewRenderer).Select(s => s.Milliseconds));
            return (object)new { cycle, originalMedianGpuMilliseconds = original, newMedianGpuMilliseconds = newTime,
                speedup = newTime > 0 ? original / newTime : null,
                timeReductionPercent = original > 0 ? 100.0 * (1.0 - newTime / original) : null };
        }).ToArray() : Array.Empty<object>();
        string reference = Program.Option(args, "--reference", "");
        ImageDifference comparison = null;
        if (reference.Length > 0)
        {
            reference = Path.GetFullPath(reference);
            using var previous = new Bitmap(reference);
            comparison = Compare(previous, image);
        }
        bool visualPassed = captureDrawSucceeded && visiblePixels > 50 && conduit.Errors.Count == 0
            && (comparison == null || !expectIdentical || comparison.Identical);
        bool timingRequested = count > 0;
        bool timingPassed = !timingRequested || (benchmarkAbba
            ? allMeasured.Length == expectedMeasured && measured.Length == expectedMeasured
                && Enumerable.Range(0, abbaCycles * 4).All(batch => measured.Count(s => s.Batch == batch) == count)
            : measured.Length >= count);
        bool passed = visualPassed && timingPassed;
        Assembly loadedAbstractions = AppDomain.CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "Nuclei4.Display.Abstractions");
        var report = new
        {
            fixture = fixture.Metadata, fixture.Source, densitySha256 = Convert.ToHexString(SHA256.HashData(fixture.Density)),
            gradientSha256 = Convert.ToHexString(SHA256.HashData(fixture.Gradient)),
            assemblyPath, rendererMode, expectIdentical, rendererSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))),
            shaderSourceSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                (string)assembly.GetType("Nuclei4.GpuDensityFieldD3DRenderer", true)
                    .GetField("ShaderSource", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue()))),
            baselinePixelShaderSha256 = ResourceHash(assembly, "Nuclei4.GpuShaders.DensityPreviewPS.cso"),
            loadedAbstractionsPath = loadedAbstractions.Location,
            loadedAbstractionsSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loadedAbstractions.Location))),
            rhino = RhinoApp.Version.ToString(), adapter = conduit.Adapter, name, label, passed, visualPassed,
            timingRequested, timingPassed, captureBatch, benchmarkAbba, abbaCycles, measuredPhase, captureDrawSucceeded, visiblePixels, neutralFoodFixture,
            captureSize = new { size.Width, size.Height }, samples, valueIndex, opacity = style.VolumeOpacity, contrast = style.VolumeContrast,
            style, volumeRendererVersion = style.VolumeRendererVersion, publishesGradient,
            fancy = false, refreshVolumeVersionEachDraw = refresh, requestedWarmupDraws = warmup, requestedMeasuredDraws = count,
            expectedMeasuredDraws = expectedMeasured, batchSummaries, modeSummaries, cycleComparisons,
            benchmarkOrder = benchmarkAbba ? string.Concat(Enumerable.Repeat("ABBA", abbaCycles)) : rendererMode,
            benchmarkModeKey = "A=original full-resolution renderer; B=new half-XY renderer",
            timingScope = captureBatch
                ? "Controlled repeated draws of one frozen field within one actual Rhino capture callback, at the recorded capture raster size. Field provenance and hashes are recorded separately; exported fields are not synthetic substitutes. Hardware timestamps surround each complete TryDraw, including occupancy updates for new versions and new-renderer offscreen draw plus reconstruction. Background copies and batch-completion waits are outside timed intervals. Excludes solver, atlas/gradient generation, presentation and capture/readback. This GPU-timeline interval may include submission gaps; it is not live simulation frame time. Per-batch warmups and final image draws are excluded from the medians."
                : "Synthetic diagnostic only. Hardware timestamps surround actual TryDraw on Rhino's draw context, including occupancy for new versions. Excludes solver, atlas/gradient preparation and presentation; not end-to-end FPS. Hidden Rhino may issue no interactive callbacks, which fails requested timing rather than inventing samples.",
            validMeasuredDraws = measured.Length, medianGpuMilliseconds = benchmarkAbba ? null : Median(measured.Select(s => s.Milliseconds)),
            minimumGpuMilliseconds = measured.Length > 0 ? measured.Min(s => s.Milliseconds) : (double?)null,
            maximumGpuMilliseconds = measured.Length > 0 ? measured.Max(s => s.Milliseconds) : (double?)null,
            reference, comparison, gpuSamples, events = conduit.Events.ToArray(), errors = conduit.Errors.ToArray()
        };
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, visualPassed, timingRequested, timingPassed,
            captureDrawSucceeded, validMeasuredDraws = measured.Length,
            medianGpuMilliseconds = benchmarkAbba ? null : Median(measured.Select(s => s.Milliseconds)),
            benchmarkAbba, batchSummaries, modeSummaries, cycleComparisons, comparison, image = prefix + ".png", report = prefix + ".json" }));
        Program.ExitImmediately(passed ? 0 : 1);
        return 3;
    }

    static bool HasDynamicGradient(int valueIndex) => valueIndex >= 7 && valueIndex <= 13;

    static int ReadBounded(string[] args, string name, int fallback, int min, int max)
    {
        int value = int.Parse(Program.Option(args, name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return value >= min && value <= max ? value : throw new ArgumentException(name + " is outside its allowed range.");
    }

    static string ResourceHash(Assembly assembly, string name)
    {
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded shader: " + name);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    static double? Median(IEnumerable<double> values)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? null : (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
    }

    static double? Percentile95(IEnumerable<double> values)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? null : sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
    }

    public sealed class PreviewStyle
    {
        public int ValueIndex { get; set; } = -1;
        public float VolumeOpacity { get; set; } = 0.8f;
        public float VolumeContrast { get; set; } = 1.5f;
        public float PreviewScale { get; set; } = 1.5f;
        public float MinimumThreshold { get; set; } = 0;
        public float MaximumThreshold { get; set; } = float.MaxValue;
        public int VolumeSampleCount { get; set; } = 128;
        public int VolumeRendererVersion { get; set; }
        public bool UseCustomColor { get; set; }
        public float ColorR { get; set; }
        public float ColorG { get; set; }
        public float ColorB { get; set; }
        public float ColorA { get; set; } = 1f;

        public static PreviewStyle Load(string[] args, int defaultValueIndex)
        {
            PreviewStyle style = args.Contains("--style-json")
                ? JsonSerializer.Deserialize<PreviewStyle>(File.ReadAllText(Path.GetFullPath(Program.Option(args, "--style-json", ""))),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new ArgumentException("Style JSON was null.")
                : new PreviewStyle();
            if (style.ValueIndex < 0) style.ValueIndex = defaultValueIndex;
            style.ValueIndex = ReadBounded(args, "--value-index", style.ValueIndex, 0, 13);
            // The component normalizes the legacy slime-v2 choice before publishing a frame.
            if (style.ValueIndex == 12) style.ValueIndex = 7;
            style.VolumeSampleCount = ReadBounded(args, "--samples", style.VolumeSampleCount, 0, 256);
            if (style.VolumeSampleCount > 0 && style.VolumeSampleCount < 8)
                throw new ArgumentException("A positive ray sample count must be at least 8.");
            style.VolumeRendererVersion = ReadBounded(args, "--volume-renderer-version",
                style.VolumeRendererVersion > 0 ? style.VolumeRendererVersion : HasDynamicGradient(style.ValueIndex) ? 2 : 1, 1, 2);
            float Number(string name, float fallback) => args.Contains(name)
                ? float.Parse(Program.Option(args, name, ""), System.Globalization.CultureInfo.InvariantCulture) : fallback;
            style.VolumeOpacity = Number("--opacity", style.VolumeOpacity);
            style.VolumeContrast = Number("--contrast", style.VolumeContrast);
            style.PreviewScale = Number("--preview-scale", style.PreviewScale);
            style.MinimumThreshold = Number("--minimum-threshold", style.MinimumThreshold);
            style.MaximumThreshold = Number("--maximum-threshold", style.MaximumThreshold);
            if (args.Contains("--color"))
            {
                float[] rgba = Program.Option(args, "--color", "").Split(',')
                    .Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (rgba.Length != 3 && rgba.Length != 4) throw new ArgumentException("--color requires R,G,B or R,G,B,A in 0..1.");
                style.UseCustomColor = true;
                style.ColorR = rgba[0]; style.ColorG = rgba[1]; style.ColorB = rgba[2];
                if (rgba.Length == 4) style.ColorA = rgba[3];
            }
            if (args.Contains("--use-custom-color")) style.UseCustomColor = bool.Parse(Program.Option(args, "--use-custom-color", ""));
            float[] numbers = { style.VolumeOpacity, style.VolumeContrast, style.PreviewScale, style.MinimumThreshold,
                style.MaximumThreshold, style.ColorR, style.ColorG, style.ColorB, style.ColorA };
            if (numbers.Any(v => !float.IsFinite(v)) || style.VolumeOpacity < 0 || style.VolumeContrast < 0 || style.PreviewScale < 0
                || style.MinimumThreshold > style.MaximumThreshold
                || new[] { style.ColorR, style.ColorG, style.ColorB, style.ColorA }.Any(v => v < 0 || v > 1))
                throw new ArgumentException("Preview style contains invalid numeric settings.");
            return style;
        }
    }

    public sealed record ImageDifference(bool Identical, int DifferentPixels, int MaximumChannelDifference, double MeanAbsoluteChannelDifference);
    static unsafe int CountVisiblePixels(Bitmap image, bool neutralFoodFixture)
    {
        BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int count = 0;
        try
        {
            for (int y = 0; y < image.Height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < image.Width; x++)
                {
                    byte* p = row + x * 4;
                    int maximum = Math.Max(p[0], Math.Max(p[1], p[2]));
                    int minimum = Math.Min(p[0], Math.Min(p[1], p[2]));
                    if (neutralFoodFixture ? maximum > 12 : maximum - minimum > 12) count++;
                }
            }
        }
        finally { image.UnlockBits(data); }
        return count;
    }

    static unsafe ImageDifference Compare(Bitmap first, Bitmap second)
    {
        if (first.Size != second.Size) throw new ArgumentException("Reference and candidate capture dimensions differ.");
        Rectangle area = new(0, 0, first.Width, first.Height);
        BitmapData a = first.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData b = second.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int different = 0, maximum = 0;
        long total = 0;
        try
        {
            for (int y = 0; y < first.Height; y++)
            {
                byte* ar = (byte*)a.Scan0 + y * a.Stride, br = (byte*)b.Scan0 + y * b.Stride;
                for (int x = 0; x < first.Width; x++)
                {
                    bool unequal = false;
                    for (int c = 0; c < 4; c++)
                    {
                        int delta = Math.Abs(ar[x * 4 + c] - br[x * 4 + c]);
                        total += delta; maximum = Math.Max(maximum, delta); unequal |= delta != 0;
                    }
                    if (unequal) different++;
                }
            }
        }
        finally { first.UnlockBits(a); second.UnlockBits(b); }
        return new(different == 0, different, maximum, total / (4.0 * first.Width * first.Height));
    }

    public sealed class VolumeMetadata
    {
        public string TestName { get; set; } = "filaments";
        public string Pattern { get; set; } = "filaments";
        public string Format { get; set; } = "R32G32B32A32_Float";
        public int Channels { get; set; } = 4;
        public int ResX { get; set; }
        public int ResY { get; set; }
        public int ResZ { get; set; }
        public int SourceResX { get; set; }
        public int SourceResY { get; set; }
        public int SourceResZ { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int AtlasColumns { get; set; }
        public int AtlasRows { get; set; }
        public float VoxelSize { get; set; } = 1;
    }

    sealed record VolumeFixture(VolumeMetadata Metadata, byte[] Density, byte[] Gradient, string Source)
    {
        public BoundingBox Bounds => new(new Point3d(0, 0, 0), new Point3d(
            (Metadata.SourceResX > 0 ? Metadata.SourceResX : Metadata.ResX) * Metadata.VoxelSize,
            (Metadata.SourceResY > 0 ? Metadata.SourceResY : Metadata.ResY) * Metadata.VoxelSize,
            (Metadata.SourceResZ > 0 ? Metadata.SourceResZ : Metadata.ResZ) * Metadata.VoxelSize));

        public static VolumeFixture Load(string[] args, out PreviewStyle style)
        {
            if (!args.Contains("--volume-metadata"))
            {
                bool ants = Program.Option(args, "--volume-fixture", "filaments") == "ant-pheromones";
                style = PreviewStyle.Load(args, ants ? 10 : 7);
                return ants ? AntPheromones(style.ValueIndex, args.Contains("--food-shapes"), args.Contains("--food-overlap")) : Filaments();
            }
            string metadataPath = Path.GetFullPath(Program.Option(args, "--volume-metadata", ""));
            VolumeMetadata metadata = JsonSerializer.Deserialize<VolumeMetadata>(File.ReadAllText(metadataPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (metadata == null || metadata.ResX <= 1 || metadata.ResY <= 1 || metadata.ResZ <= 1
                || metadata.Width < metadata.ResX * metadata.AtlasColumns || metadata.Height < metadata.ResY * metadata.AtlasRows
                || metadata.AtlasColumns <= 0 || metadata.AtlasRows <= 0 || (long)metadata.AtlasColumns * metadata.AtlasRows < metadata.ResZ
                || !float.IsFinite(metadata.VoxelSize) || metadata.VoxelSize <= 0 || (metadata.Channels != 1 && metadata.Channels != 4))
                throw new ArgumentException("Invalid volume metadata.");
            string expectedFormat = metadata.Channels == 4 ? "R32G32B32A32_Float" : "R32_Float";
            if (!metadata.Format.Equals(expectedFormat, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Density format and Channels disagree.");
            string directory = Path.GetDirectoryName(metadataPath);
            string densityPath = Path.GetFullPath(Program.Option(args, "--volume-atlas", Path.Combine(directory, "density.raw")));
            string gradientPath = Path.GetFullPath(Program.Option(args, "--volume-gradient", Path.Combine(directory, "gradient.raw")));
            byte[] density = File.ReadAllBytes(densityPath), gradient = File.ReadAllBytes(gradientPath);
            if (density.LongLength != checked((long)metadata.Width * metadata.Height * metadata.Channels * 4)
                || gradient.LongLength != checked((long)metadata.Width * metadata.Height * 8))
                throw new ArgumentException("Raw data must contain tightly packed full atlas rows, without headers.");
            style = PreviewStyle.Load(args, metadata.Channels == 4 ? 7 : 0);
            return new(metadata, density, gradient, "GPU export: " + metadataPath + " | " + densityPath + " | " + gradientPath);
        }

        static VolumeFixture AntPheromones(int valueIndex, bool foodShapes = false, bool foodOverlap = false)
        {
            if ((valueIndex < 8 || valueIndex > 11) && valueIndex != 13)
                throw new ArgumentException("The ant-pheromones fixture requires --value-index 8, 9, 10, or 11.");
            const int n = 96, columns = 12, rows = 8;
            var metadata = new VolumeMetadata { TestName = "synthetic-96-ant-pheromones", Pattern = "ant-pheromones",
                ResX = n, ResY = n, ResZ = n, Width = n * columns, Height = n * rows,
                AtlasColumns = columns, AtlasRows = rows };
            var density = new float[metadata.Width * metadata.Height * 4];
            var scalar = new float[n * n * n];
            int Index(int x, int y, int z) => (x * n + y) * n + z;
            int Pixel(int x, int y, int z) => ((z / columns * n + y) * metadata.Width + z % columns * n + x) * 4;
            for (int x = 0; x < n; x++) for (int y = 0; y < n; y++) for (int z = 0; z < n; z++)
            {
                double dx = x / (double)(n - 1), dy = y / (double)(n - 1), dz = z / (double)(n - 1);
                double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * dx) * Math.Sin(Math.PI * dy) * Math.Sin(Math.PI * dz)), 0.25);
                double food = 0, home = 0;
                // Food trails run along X; base trails run along Y. Their distinct paths
                // include crossings so channel selection, color mixing and lighting are visible.
                for (int tube = 0; tube < 3; tube++)
                {
                    double foodY = 0.25 + tube * 0.23 + 0.08 * Math.Sin(dx * 7 + tube);
                    double foodZ = 0.32 + tube * 0.17 + 0.10 * Math.Sin(dx * 6 + tube * 1.7);
                    double baseX = 0.27 + tube * 0.23 + 0.07 * Math.Sin(dy * 6 + tube);
                    double baseZ = 0.36 + tube * 0.14 + 0.11 * Math.Cos(dy * 7 + tube * 1.7);
                    double sigma = (1.5 + tube * 0.6) / n;
                    food += Math.Exp(-((dy - foodY) * (dy - foodY) + (dz - foodZ) * (dz - foodZ)) / (2 * sigma * sigma)) * (0.65 + tube * 0.12);
                    home += Math.Exp(-((dx - baseX) * (dx - baseX) + (dz - baseZ) * (dz - baseZ)) / (2 * sigma * sigma)) * (0.65 + tube * 0.12);
                }
                int pixel = Pixel(x, y, z);
                density[pixel + 1] = foodShapes ? 0 : (float)(food * envelope);
                density[pixel + 2] = foodShapes ? 0 : (float)(home * envelope);
                if (foodShapes)
                {
                    // Thin slab, L-shaped patch, and isolated small block: food
                    // geometry that must not become shaded spherical blobs.
                    bool slab = x >= 18 && x < 42 && y >= 20 && y < 40 && z >= 28 && z < 31;
                    bool elbow = x >= 52 && x < 76 && y >= 50 && y < 74 && z >= 46 && z < 52
                        && (x < 58 || y < 56);
                    bool block = x >= 27 && x < 33 && y >= 62 && y < 68 && z >= 61 && z < 67;
                    density[pixel + 3] = slab ? 1f : elbow ? 3f : block ? .25f : 0f;
                    if (foodOverlap && density[pixel + 3] > 0)
                    {
                        density[pixel + 1] = 1f;
                        density[pixel + 2] = .8f;
                    }
                }
                // Match the selected visible scalar in the production gradient pass.
                scalar[Index(x, y, z)] = valueIndex == 13 ? density[pixel + 3]
                    : valueIndex == 8 ? Math.Max(density[pixel + 3], density[pixel + 1])
                    : valueIndex == 9 ? Math.Max(density[pixel + 3], density[pixel + 2])
                    : Math.Max(density[pixel + 3], Math.Max(valueIndex == 11 ? density[pixel] : 0,
                        Math.Max(density[pixel + 1], density[pixel + 2])));
            }
            var gradient = new Half[metadata.Width * metadata.Height * 4];
            float Sample(int x, int y, int z) => scalar[Index(Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1), Math.Clamp(z, 0, n - 1))];
            for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int pixel = Pixel(x, y, z);
                float xn = 0.5f * (Sample(x + 1, y, z) - Sample(x - 1, y, z));
                float yn = 0.5f * (Sample(x, y + 1, z) - Sample(x, y - 1, z));
                float zn = 0.5f * (Sample(x, y, z + 1) - Sample(x, y, z - 1));
                float xf = 0.25f * (Sample(x + 2, y, z) - Sample(x - 2, y, z));
                float yf = 0.25f * (Sample(x, y + 2, z) - Sample(x, y - 2, z));
                float zf = 0.25f * (Sample(x, y, z + 2) - Sample(x, y, z - 2));
                float gx = xn + 0.68f * (xf - xn), gy = yn + 0.68f * (yf - yn), gz = zn + 0.68f * (zf - zn);
                float magnitude = MathF.Sqrt(gx * gx + gy * gy + gz * gz);
                gradient[pixel] = (Half)(magnitude > 0.000001f ? gx / magnitude : 0);
                gradient[pixel + 1] = (Half)(magnitude > 0.000001f ? gy / magnitude : 0);
                gradient[pixel + 2] = (Half)(magnitude > 0.000001f ? gz / magnitude : 0);
                gradient[pixel + 3] = (Half)magnitude;
            }
            return new(metadata, MemoryMarshal.AsBytes(density.AsSpan()).ToArray(),
                MemoryMarshal.AsBytes(gradient.AsSpan()).ToArray(),
                "Deterministic synthetic ant food/base trails; CPU diagnostic gradient for value index " + valueIndex
                    + "; production palette/material. Not proof of GPU gradient parity.");
        }

        static VolumeFixture Filaments()
        {
            const int n = 96, columns = 12, rows = 8;
            var metadata = new VolumeMetadata { TestName = "synthetic-96-filaments", ResX = n, ResY = n, ResZ = n,
                Width = n * columns, Height = n * rows, AtlasColumns = columns, AtlasRows = rows };
            var scalar = new float[n * n * n];
            int Index(int x, int y, int z) => (x * n + y) * n + z;
            for (int x = 0; x < n; x++) for (int y = 0; y < n; y++) for (int z = 0; z < n; z++)
            {
                double dx = x / (double)(n - 1), dy = y / (double)(n - 1), dz = z / (double)(n - 1);
                double envelope = Math.Sin(Math.PI * dx) * Math.Sin(Math.PI * dy) * Math.Sin(Math.PI * dz);
                double value = 0;
                // Curved, crossing tubes at several widths, plus dim broad density for lighting.
                for (int tube = 0; tube < 6; tube++)
                {
                    double cy = 0.17 + tube * 0.125 + 0.09 * Math.Sin(dx * 8 + tube * 1.7);
                    double cz = 0.5 + 0.28 * Math.Cos(dx * 7 + tube * 1.2);
                    double sigma = (tube % 3 + 0.7) / n;
                    double distance = (dy - cy) * (dy - cy) + (dz - cz) * (dz - cz);
                    value += Math.Exp(-distance / (2 * sigma * sigma)) * (0.3 + tube * 0.1);
                }
                scalar[Index(x, y, z)] = (float)(value * Math.Pow(Math.Max(envelope, 0), 0.25) + 0.005 * envelope);
            }
            var density = new float[metadata.Width * metadata.Height * 4];
            var gradient = new Half[metadata.Width * metadata.Height * 4];
            float Sample(int x, int y, int z) => Math.Max(0, scalar[Index(Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1), Math.Clamp(z, 0, n - 1))]);
            for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int pixel = ((z / columns * n + y) * metadata.Width + z % columns * n + x) * 4;
                density[pixel] = scalar[Index(x, y, z)];
                float xn = 0.5f * (Sample(x + 1, y, z) - Sample(x - 1, y, z));
                float yn = 0.5f * (Sample(x, y + 1, z) - Sample(x, y - 1, z));
                float zn = 0.5f * (Sample(x, y, z + 1) - Sample(x, y, z - 1));
                float xf = 0.25f * (Sample(x + 2, y, z) - Sample(x - 2, y, z));
                float yf = 0.25f * (Sample(x, y + 2, z) - Sample(x, y - 2, z));
                float zf = 0.25f * (Sample(x, y, z + 2) - Sample(x, y, z - 2));
                float gx = xn + 0.68f * (xf - xn), gy = yn + 0.68f * (yf - yn), gz = zn + 0.68f * (zf - zn);
                float magnitude = MathF.Sqrt(gx * gx + gy * gy + gz * gz);
                gradient[pixel] = (Half)(magnitude > 0.000001f ? gx / magnitude : 0);
                gradient[pixel + 1] = (Half)(magnitude > 0.000001f ? gy / magnitude : 0);
                gradient[pixel + 2] = (Half)(magnitude > 0.000001f ? gz / magnitude : 0);
                gradient[pixel + 3] = (Half)magnitude;
            }
            // This CPU-generated fallback is a visual fixture, not proof of GPU gradient parity.
            return new(metadata, MemoryMarshal.AsBytes(density.AsSpan()).ToArray(),
                MemoryMarshal.AsBytes(gradient.AsSpan()).ToArray(), "Deterministic synthetic thin filaments; CPU diagnostic gradient, production material.");
        }
    }

    public sealed record GpuSample(string Phase, int Draw, bool InCapture, bool UseNewRenderer, bool Valid, double Milliseconds,
        string Error, int Batch = -1, int BatchDraw = -1)
    {
        public string RendererMode => UseNewRenderer ? "new" : "original";
    }
    sealed class VolumeConduit : DisplayConduit, IDisposable
    {
        readonly VolumeFixture fixture;
        readonly MethodInfo draw, getD3D, unregister;
        readonly Type frameType, rendererType;
        readonly Guid id = Guid.NewGuid();
        readonly int samples, valueIndex;
        readonly bool refresh;
        readonly string rendererMode;
        readonly bool captureBatch;
        readonly int warmupCount, measuredCount;
        readonly int abbaCycles;
        readonly PreviewStyle style;
        int activeBatch = -1, activeBatchDraw = -1;
        bool? rendererOverride;
        bool abbaCaptureExecuted;
        readonly FieldInfo newRendererField;
        readonly List<PendingTiming> timings = new();
        ID3D11Texture2D density, gradient;
        ID3D11Buffer sliceChallengeBuffer;
        ID3D11Device fixtureDevice;
        ID3D11DeviceContext1 context;
        object frame;
        long version;
        public string Phase = "warmup";
        public string Adapter;
        public int SuccessfulDraws;
        public readonly List<string> Errors = new();
        public readonly List<object> Events = new();

        public VolumeConduit(Assembly assembly, VolumeFixture fixture, int samples, int valueIndex, bool refresh, string rendererMode,
            bool captureBatch, int warmupCount, int measuredCount, int abbaCycles, PreviewStyle style)
        {
            this.fixture = fixture; this.samples = samples; this.valueIndex = valueIndex; this.refresh = refresh;
            this.rendererMode = rendererMode;
            this.captureBatch = captureBatch; this.warmupCount = warmupCount; this.measuredCount = measuredCount;
            this.abbaCycles = abbaCycles; this.style = style;
            Type renderer = assembly.GetType("Nuclei4.GpuDensityFieldD3DRenderer", true);
            rendererType = renderer;
            draw = renderer.GetMethod("TryDraw"); unregister = renderer.GetMethod("Unregister");
            getD3D = assembly.GetType("Nuclei4.RhinoWipD3DPreviewProbe", true).GetMethod("TryGetRhinoD3D");
            frameType = Assembly.Load("Nuclei4.Display.Abstractions").GetType("Nuclei4.GpuDensityFieldPreviewFrame", true);
            newRendererField = frameType.GetField("UseNewRenderer");
            if ((rendererMode != "baseline" || abbaCycles > 0) && newRendererField == null)
                throw new InvalidOperationException("Requested new renderer, but this assembly has no UseNewRenderer field.");
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e) => e.IncludeBoundingBox(fixture.Bounds);
        protected override void PostDrawObjects(DrawEventArgs e)
        {
            try
            {
                object[] pointers = { e.Display, e.Viewport, IntPtr.Zero, IntPtr.Zero };
                if (!(bool)getD3D.Invoke(null, pointers)) throw new InvalidOperationException("Rhino D3D context unavailable.");
                if (frame == null) CreateFrame((IntPtr)pointers[2], (IntPtr)pointers[3]);
                if (context.NativePointer != (IntPtr)pointers[3])
                    throw new InvalidOperationException("Rhino changed D3D contexts during this fixture; refusing cross-context timing.");
                if (captureBatch && e.Display.IsInViewCapture) DrawCaptureBatch(e);
                else DrawOnce(e);
            }
            catch (Exception error) { Errors.Add(error.ToString()); }
        }

        void DrawCaptureBatch(DrawEventArgs e)
        {
            if (abbaCycles > 0)
            {
                if (abbaCaptureExecuted)
                    throw new InvalidOperationException("ABBA requires one untiled capture callback; refusing to merge multiple callbacks into one benchmark.");
                abbaCaptureExecuted = true;
                MethodInfo tilingMethod = e.Display.GetType().GetMethod("IsInTiledDraw");
                object[] tiling = { Size.Empty, Rectangle.Empty };
                if (tilingMethod == null || (bool)tilingMethod.Invoke(e.Display, tiling))
                    throw new InvalidOperationException("ABBA requires a verified untiled capture.");
            }
            var renderTargets = new ID3D11RenderTargetView[1];
            context.OMGetRenderTargets(1, renderTargets, out ID3D11DepthStencilView depth);
            try
            {
                if (renderTargets[0] == null) throw new InvalidOperationException("Capture has no color target.");
                using ID3D11Resource resource = renderTargets[0].Resource;
                using ID3D11Texture2D output = resource.QueryInterface<ID3D11Texture2D>();
                ID3D11Device device = fixtureDevice;
                Texture2DDescription description = output.Description;
                // Rhino may supply an MSAA target; D3D11 requires render-target
                // binding on an MSAA copy texture even when we only copy into it.
                description.BindFlags = BindFlags.RenderTarget;
                description.Usage = ResourceUsage.Default;
                description.CPUAccessFlags = CpuAccessFlags.None;
                description.MiscFlags = ResourceOptionFlags.None;
                using ID3D11Texture2D background = device.CreateTexture2D(description);
                context.CopyResource(background, output);
                if (abbaCycles > 0)
                {
                    for (int batch = 0; batch < abbaCycles * 4; batch++)
                    {
                        activeBatch = batch;
                        rendererOverride = batch % 4 == 1 || batch % 4 == 2;
                        Console.WriteLine("ABBA batch " + (batch + 1) + "/" + (abbaCycles * 4)
                            + ": " + (rendererOverride.Value ? "new" : "original"));
                        for (int iteration = 0; iteration < warmupCount + measuredCount; iteration++)
                        {
                            activeBatchDraw = iteration;
                            context.CopyResource(output, background);
                            Phase = iteration < warmupCount ? "controlled-capture-warmup" : "controlled-capture-measured";
                            DrawOnce(e);
                        }
                        WaitForBatchCompletion();
                    }
                    activeBatch = -1;
                    activeBatchDraw = -1;
                    rendererOverride = false;
                    Phase = "capture-final-image";
                    context.CopyResource(output, background);
                    DrawOnce(e);
                }
                else
                {
                    for (int iteration = 0; iteration < warmupCount + measuredCount + 1; iteration++)
                    {
                        // Preserve the original framebuffer rather than accumulating translucent
                        // copies of the test volume. This GPU copy precedes the start timestamp.
                        context.CopyResource(output, background);
                        Phase = iteration < warmupCount ? "controlled-capture-warmup"
                            : iteration < warmupCount + measuredCount ? "controlled-capture-measured" : "capture-final-image";
                        DrawOnce(e);
                    }
                }
            }
            finally { renderTargets[0]?.Dispose(); depth?.Dispose(); }
        }

        void WaitForBatchCompletion()
        {
            using ID3D11Query completed = fixtureDevice.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            context.End(completed);
            context.Flush();
            var wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 30000)
            {
                if (context.GetData(completed, out int finished) && finished != 0) return;
                Thread.Yield();
            }
            throw new TimeoutException("ABBA batch did not complete on the GPU within 30 seconds.");
        }

        void DrawOnce(DrawEventArgs e)
        {
                using var sliceChallenge = new ConstantBufferSliceChallenge(context, sliceChallengeBuffer);
                if (refresh) Set("Version", ++version);
                ID3D11Device device = fixtureDevice;
                bool useNewRenderer = rendererOverride ?? (rendererMode == "new" || rendererMode == "toggle" && (timings.Count % 2 == 1));
                newRendererField?.SetValue(frame, useNewRenderer);
                long newSequenceBefore = useNewRenderer ? NewRendererUseSequence() : 0;
                string stateBefore = DrawState(context);
                var pending = new PendingTiming(device, Phase, timings.Count, e.Display.IsInViewCapture, useNewRenderer,
                    activeBatch, activeBatchDraw);
                timings.Add(pending);
                context.Begin(pending.Disjoint); context.End(pending.Start);
                try
                {
                    if ((bool)draw.Invoke(null, new[] { (object)id, e, frame })) SuccessfulDraws++;
                    else throw new InvalidOperationException("Current volume renderer TryDraw returned false.");
                }
                finally { context.End(pending.End); context.End(pending.Disjoint); }
                long newSequenceAfter = useNewRenderer ? NewRendererUseSequence() : 0;
                bool newRendererActive = useNewRenderer && newSequenceAfter > newSequenceBefore;
                if (useNewRenderer && !newRendererActive)
                    Errors.Add("New renderer requested, but no successful reduced-target use occurred; fallback is not a pass.");
                string stateAfter = DrawState(context);
                if (stateBefore != stateAfter)
                    Errors.Add("D3D state changed across TryDraw: before=" + stateBefore + " after=" + stateAfter);
                MethodInfo tilingMethod = e.Display.GetType().GetMethod("IsInTiledDraw");
                object[] tiling = { Size.Empty, Rectangle.Empty };
                bool? tiled = tilingMethod == null ? null : (bool)tilingMethod.Invoke(e.Display, tiling);
                Events.Add(new { phase = Phase, batch = activeBatch, batchDraw = activeBatchDraw, useNewRenderer,
                    newRendererActive, stateRestored = stateBefore == stateAfter, viewportWidth = e.Viewport.Size.Width, viewportHeight = e.Viewport.Size.Height,
                    e.Display.IsInViewCapture, e.Display.IsPrinting,
                    raster = context.RSGetViewports<Vortice.Mathematics.Viewport>().Select(v => new { v.Width, v.Height, v.X, v.Y }).ToArray(),
                    projection = e.Display.GetOpenGLWorldToClip(true), tiled, fullSize = tiling[0], tile = tiling[1] });
        }

        long NewRendererUseSequence()
        {
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            object singleton = rendererType.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (singleton == null) return 0;
            var views = rendererType.GetField("sharedTextureViews", privateInstance)?.GetValue(singleton) as System.Collections.IDictionary;
            object view = views?[id];
            if (view == null) return 0;
            object helper = view.GetType().GetField("newRenderer", privateInstance)?.GetValue(view);
            return helper == null ? 0 : (long)(helper.GetType().GetField("useSequence", privateInstance)?.GetValue(helper) ?? 0L);
        }

        sealed class ConstantBufferSliceChallenge : IDisposable
        {
            readonly ID3D11DeviceContext1 context;
            readonly ID3D11Buffer[] original = new ID3D11Buffer[1];
            readonly int[] first = new int[1], count = new int[1];
            public ConstantBufferSliceChallenge(ID3D11DeviceContext1 context, ID3D11Buffer challenge)
            {
                this.context = context;
                context.PSGetConstantBuffers1(1, 1, original, first, count);
                context.PSSetConstantBuffers1(1, new[] { challenge }, new[] { 16 }, new[] { 16 });
            }
            public void Dispose()
            {
                context.PSSetConstantBuffers1(1, original, first, count);
                original[0]?.Dispose();
            }
        }


        // Read back touched bindings before and after the draw. COM getters add references;
        // release those immediately and keep only stable native identities for comparison.
        static string DrawState(ID3D11DeviceContext1 context)
        {
            var targets = new ID3D11RenderTargetView[8];
            context.OMGetRenderTargets(targets.Length, targets, out ID3D11DepthStencilView depth);
            var buffers = new ID3D11Buffer[2];
            context.PSGetConstantBuffers(0, buffers);
            var rangedBuffer = new ID3D11Buffer[1];
            var firstConstant = new int[1];
            var constantCount = new int[1];
            context.PSGetConstantBuffers1(1, 1, rangedBuffer, firstConstant, constantCount);
            var resources = new ID3D11ShaderResourceView[8];
            context.PSGetShaderResources(0, resources);
            var samplers = new ID3D11SamplerState[2];
            context.PSGetSamplers(0, samplers);
            using var blend = context.OMGetBlendState();
            using var rasterizer = context.RSGetState();
            using var pixelShader = context.PSGetShader();
            using var vertexShader = context.VSGetShader();
            try
            {
                return JsonSerializer.Serialize(new
                {
                    targets = targets.Select(v => v?.NativePointer.ToInt64() ?? 0).ToArray(),
                    depth = depth?.NativePointer.ToInt64() ?? 0,
                    buffers = buffers.Select(v => v?.NativePointer.ToInt64() ?? 0).ToArray(),
                    b1FirstConstant = firstConstant[0], b1ConstantCount = constantCount[0],
                    resources = resources.Select(v => v?.NativePointer.ToInt64() ?? 0).ToArray(),
                    samplers = samplers.Select(v => v?.NativePointer.ToInt64() ?? 0).ToArray(),
                    blend = blend?.NativePointer.ToInt64() ?? 0,
                    rasterizer = rasterizer?.NativePointer.ToInt64() ?? 0,
                    pixelShader = pixelShader?.NativePointer.ToInt64() ?? 0,
                    vertexShader = vertexShader?.NativePointer.ToInt64() ?? 0,
                    viewports = context.RSGetViewports<Vortice.Mathematics.Viewport>()
                        .Select(v => new { v.X, v.Y, v.Width, v.Height, v.MinDepth, v.MaxDepth }).ToArray()
                });
            }
            finally
            {
                foreach (var target in targets) target?.Dispose();
                depth?.Dispose();
                foreach (var buffer in buffers) buffer?.Dispose();
                rangedBuffer[0]?.Dispose();
                foreach (var resource in resources) resource?.Dispose();
                foreach (var sampler in samplers) sampler?.Dispose();
            }
        }

        void Set(string name, object value) => frameType.GetField(name).SetValue(frame, value);
        unsafe void CreateFrame(IntPtr devicePointer, IntPtr contextPointer)
        {
            Marshal.AddRef(devicePointer); Marshal.AddRef(contextPointer);
            fixtureDevice = new ID3D11Device(devicePointer);
            ID3D11Device device = fixtureDevice;
            context = new ID3D11DeviceContext1(contextPointer);
            sliceChallengeBuffer = device.CreateBuffer(1024, BindFlags.ConstantBuffer,
                ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
            using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            using (var adapter = dxgiDevice.GetAdapter()) Adapter = adapter.Description.Description;
            VolumeMetadata m = fixture.Metadata;
            ID3D11Texture2D Texture(byte[] bytes, Format format, int bytesPerPixel)
            {
                var desc = new Texture2DDescription(format, m.Width, m.Height, 1, 1, BindFlags.ShaderResource,
                    ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.Shared);
                fixed (byte* pointer = bytes)
                    return device.CreateTexture2D(desc, new[] { new SubresourceData((IntPtr)pointer, m.Width * bytesPerPixel, 0) });
            }
            density = Texture(fixture.Density, m.Channels == 4 ? Format.R32G32B32A32_Float : Format.R32_Float, m.Channels * 4);
            gradient = Texture(fixture.Gradient, Format.R16G16B16A16_Float, 8);
            using var densityResource = density.QueryInterface<IDXGIResource>();
            using var gradientResource = gradient.QueryInterface<IDXGIResource>();
            frame = Activator.CreateInstance(frameType);
            Set("SharedHandle", densityResource.SharedHandle);
            Set("GradientSharedHandle", HasDynamicGradient(valueIndex) && style.VolumeRendererVersion == 2 ? gradientResource.SharedHandle : IntPtr.Zero);
            Set("Width", m.Width); Set("Height", m.Height); Set("ResX", m.ResX); Set("ResY", m.ResY); Set("ResZ", m.ResZ);
            Set("SourceResX", m.SourceResX > 0 ? m.SourceResX : m.ResX);
            Set("SourceResY", m.SourceResY > 0 ? m.SourceResY : m.ResY);
            Set("SourceResZ", m.SourceResZ > 0 ? m.SourceResZ : m.ResZ);
            Set("VoxelSize", m.VoxelSize); Set("AxisMode", 3); Set("AtlasColumns", m.AtlasColumns); Set("AtlasRows", m.AtlasRows);
            Set("ValueIndex", valueIndex); Set("UseCustomColor", style.UseCustomColor);
            Set("ColorR", style.ColorR); Set("ColorG", style.ColorG); Set("ColorB", style.ColorB); Set("ColorA", style.ColorA);
            Set("VolumeOpacity", style.VolumeOpacity); Set("VolumeContrast", style.VolumeContrast); Set("PreviewScale", style.PreviewScale);
            Set("MinimumThreshold", style.MinimumThreshold); Set("MaximumThreshold", style.MaximumThreshold);
            Set("VolumeSampleCount", samples); Set("VolumeRendererVersion", style.VolumeRendererVersion); Set("FancyRender", false);
            context.Flush();
        }

        public GpuSample[] ResolveTimings()
        {
            if (context == null) return Array.Empty<GpuSample>();
            context.Flush();
            var result = new List<GpuSample>();
            foreach (PendingTiming pending in timings)
            {
                var wait = Stopwatch.StartNew();
                ulong start = 0, end = 0;
                QueryDataTimestampDisjoint disjoint = default;
                bool ready = false;
                while (wait.ElapsedMilliseconds < 10000)
                {
                    ready = context.GetData(pending.Disjoint, out disjoint) && context.GetData(pending.Start, out start)
                        && context.GetData(pending.End, out end);
                    if (ready) break;
                    Thread.Yield();
                }
                bool valid = ready && !disjoint.Disjoint && disjoint.Frequency > 0 && end >= start;
                result.Add(new(pending.Phase, pending.Draw, pending.InCapture, pending.UseNewRenderer, valid,
                    valid ? (end - start) * 1000.0 / disjoint.Frequency : 0,
                    valid ? null : ready ? "Disjoint or invalid hardware timestamp interval" : "Timestamp completion timeout",
                    pending.Batch, pending.BatchDraw));
                if (!ready) break;
            }
            return result.ToArray();
        }

        public void Dispose()
        {
            Enabled = false; unregister?.Invoke(null, new object[] { id });
            foreach (PendingTiming pending in timings) pending.Dispose();
            density?.Dispose(); gradient?.Dispose(); sliceChallengeBuffer?.Dispose(); context?.Dispose(); fixtureDevice?.Dispose();
        }

        sealed class PendingTiming : IDisposable
        {
            public readonly ID3D11Query Start, End, Disjoint;
            public readonly string Phase;
            public readonly int Draw;
            public readonly bool InCapture;
            public readonly bool UseNewRenderer;
            public readonly int Batch, BatchDraw;
            public PendingTiming(ID3D11Device device, string phase, int draw, bool capture, bool useNewRenderer,
                int batch = -1, int batchDraw = -1)
            {
                Phase = phase; Draw = draw; InCapture = capture; UseNewRenderer = useNewRenderer;
                Batch = batch; BatchDraw = batchDraw;
                Start = device.CreateQuery(new QueryDescription(QueryType.Timestamp, QueryFlags.None));
                End = device.CreateQuery(new QueryDescription(QueryType.Timestamp, QueryFlags.None));
                Disjoint = device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint, QueryFlags.None));
            }
            public void Dispose() { Start.Dispose(); End.Dispose(); Disjoint.Dispose(); }
        }
    }
}

