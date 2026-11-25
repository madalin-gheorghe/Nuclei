using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Vortice.Direct3D11;
using Vortice.DXGI;

// Exercises the actual solver dispatch and GPU textures without opening Rhino.
internal static class Program
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<Assembly> Assemblies = new();
    static readonly List<object> Results = new();
    static bool DensityOnly;

    static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(Option(args, "--nuclei-output")
                ?? "Nuclei-v4/Nuclei4/bin/Release/net7.0-windows");
            Load(directory);
            DensityOnly = args.Contains("--density-only");
            Console.WriteLine("Assembly: " + directory);
            foreach (var shape in new[] { (9, 8, 7), (9, 8, 1), (9, 1, 7), (1, 8, 7) })
                Run(shape.Item1, shape.Item2, shape.Item3, "mixed");
            Run(9, 8, 7, "empty-ants-slime-decoy");
            Run(9, 8, 7, "ant-only");
            Run(9, 8, 7, "slime-only");
            // Cross tile boundaries in X/Z and retain partial edge tiles/padding.
            Run(35, 19, 21, "mixed");
            Run(257, 5, 17, "mixed");
            // Exceed the texture width limit with a small voxel count to exercise
            // adaptive atlas sampling without allocating a huge cubic field.
            Run(17001, 2, 2, "mixed");
            string? json = Option(args, "--json");
            if (json != null)
                File.WriteAllText(Path.GetFullPath(json), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {Results.Count} GPU preview frames; {(DensityOnly ? "density atlas and slime gradient compatibility" : "selected ant/slime/food gradients")}, cached frames, field/scale switching, and all planar orientations.");
            return 0;
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static void Run(int nx, int ny, int nz, string scenario)
    {
        bool ants = scenario != "slime-only";
        object input = Create("Nuclei4.GpuSolverInput");
        int count = nx * ny * nz;
        float[] slime = new float[count], food = new float[count], home = new float[count], remaining = new float[count];
        for (int x = 0; x < nx; x++)
        for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
        {
            int i = (x * ny + y) * nz + z;
            slime[i] = scenario == "ant-only" ? 0 : 3 * x;
            if (ants && scenario != "empty-ants-slime-decoy")
            {
                food[i] = 4 * y;
                home[i] = 5 * z;
                // This field must also determine normals where the displayed food wins.
                if (x < 3 && y < 3 && z < 3) remaining[i] = 40 - 3 * x - 4 * y - 5 * z;
            }
        }
        Set(input, "ResX", nx); Set(input, "ResY", ny); Set(input, "ResZ", nz);
        Set(input, "VoxelSize", 1f);
        Set(input, "HasAntParticles", ants); Set(input, "HasSlimeParticles", scenario != "ant-only");
        Set(input, "VoxelDensity", slime); Set(input, "AntFoodPheromone", food); Set(input, "AntBasePheromone", home);
        Set(input, "InitialAntFood", remaining);
        object settings = Create("Nuclei4.SolverGpuSettings");
        object dimension = RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution", Static)!
            .Invoke(null, new object[] { nx, ny, nz })!;
        object sink = RuntimeHelpers.GetUninitializedObject(RequiredType("Nuclei4.Gh1GpuSolverOutputSink"));
        object engine = RequiredType("Nuclei4.GpuFullSlimeSolverEngine").GetConstructors(Instance)
            .Single(c => c.GetParameters().Length == 8).Invoke(new[] { input, sink, settings, false, false, false, 0, 1 });
        try
        {
            object capabilities = engine.GetType().GetProperty("Capabilities")!.GetValue(engine)!;
            Assert(!Get<bool>(capabilities, "SoftwareFallback"), "Hardware D3D11 is required; WARP was selected.");
            bool volume = nx > 1 && ny > 1 && nz > 1;
            // Revisiting fields and scales catches stale atlas/cache/constant-buffer state.
            foreach (int scale in new[] { 1, 10, 1 })
            {
            var retained = new Dictionary<int, (object Frame, byte[] Gradient)>();
            foreach (int requested in ants ? new[] { 8, 9, 10, 11, 13, 7, 12, 8 } : new[] { 7, 11, 13, 12 })
            {
                int field = requested == 12 ? 7 : requested;
                object frame = Invoke(engine, "CreateVoxelFieldPreviewFrame", requested, dimension, 0f, float.MaxValue, scale)
                    ?? throw new InvalidOperationException($"Missing {scenario} preview {requested}.");
                Assert(Get<int>(frame, "ValueIndex") == field, "Unexpected source field.");
                var density = Read(engine, frame, false);
                ValidateDensity(frame, density, slime, food, home, remaining, nx, ny, nz);
                byte[]? gradient = null;
                if (volume && (!DensityOnly || field == 7))
                {
                    Assert(Get<IntPtr>(frame, "GradientSharedHandle") != IntPtr.Zero,
                        $"{scenario} {nx}x{ny}x{nz}, field {requested}: missing GPU gradient texture.");
                    gradient = Read(engine, frame, true);
                    ValidateGradient(frame, density, gradient, field);
                    if (scenario == "empty-ants-slime-decoy" && field is 8 or 9 or 10)
                        Assert(gradient.All(b => b == 0), "Empty ant field picked up unrelated slime normals.");
                    foreach (var previous in retained)
                    {
                        IntPtr previousHandle = Get<IntPtr>(previous.Value.Frame, "GradientSharedHandle");
                        Assert((Get<IntPtr>(frame, "GradientSharedHandle") == previousHandle) == (previous.Key == field),
                            "Different source fields require distinct gradient textures; slime 7/12 must share one.");
                        Assert(Read(engine, previous.Value.Frame, true).AsSpan().SequenceEqual(previous.Value.Gradient),
                            $"Selecting field {requested} overwrote the retained field {previous.Key} gradient.");
                    }
                    retained[field] = (frame, gradient);
                }
                else if (!volume) Assert(Get<IntPtr>(frame, "GradientSharedHandle") == IntPtr.Zero, "Planar previews must not publish a volume gradient.");

                // Repeated requests must keep a stable version and identical GPU output.
                object cached = Invoke(engine, "CreateVoxelFieldPreviewFrame", requested, dimension, 0f, float.MaxValue, scale)!;
                Assert(Get<long>(cached, "Version") == Get<long>(frame, "Version"), "Unchanged preview invalidated its cache.");
                Assert(Read(engine, cached, false).AsSpan().SequenceEqual(density), "Cached density changed.");
                if (gradient != null)
                    Assert(Read(engine, cached, true).AsSpan().SequenceEqual(gradient), "Cached gradient changed.");
                Results.Add(new { Scenario = scenario, Shape = $"{nx}x{ny}x{nz}", Field = requested, Scale = scale,
                    Width = Get<int>(frame, "Width"), Height = Get<int>(frame, "Height"),
                    DensitySha256 = Convert.ToHexString(SHA256.HashData(density)),
                    GradientSha256 = gradient == null ? null : Convert.ToHexString(SHA256.HashData(gradient)) });
            }
            Invoke(engine, "SetSharedDensityPreviewEnabled", false, dimension, scale);
            Assert(Invoke(engine, "CreateDensityFieldPreviewFrame") == null, "Disabled shared preview still published a frame.");
            if (!DensityOnly)
            {
                Assert(Get<IntPtr>(engine, "densityGradientPreviewSharedHandle") == IntPtr.Zero,
                    "Disabling preview retained an active gradient handle.");
                Assert(((System.Collections.IDictionary)Get<object>(engine, "densityGradientPreviews")).Count == 0,
                    "Disabling preview did not dispose all field gradients.");
            }
            Invoke(engine, "SetSharedDensityPreviewEnabled", true, dimension, scale);
            Assert(Invoke(engine, "CreateDensityFieldPreviewFrame") != null, "Reenabled shared preview failed to recover.");
            }
            if (!ants)
                foreach (int field in new[] { 8, 9, 10 })
                    Assert(Invoke(engine, "CreateVoxelFieldPreviewFrame", field, dimension, 0f, float.MaxValue, 1) == null,
                        "A simulation without ants published a pheromone field.");
            if (volume && scenario == "mixed" && !DensityOnly)
            {
                var before = new Dictionary<int, byte[]>();
                foreach (int field in new[] { 7, 8, 9, 10, 11, 13 })
                    before[field] = Read(engine, Invoke(engine, "CreateVoxelFieldPreviewFrame", field, dimension, 0f, float.MaxValue, 1)!, true);
                Set(settings, "Diffuse", 0d); Set(settings, "AntFoodDiffuse", 0d); Set(settings, "AntBaseDiffuse", 0d);
                Set(settings, "Decay", 0.25d); Set(settings, "AntFoodDecay", 0.25d); Set(settings, "AntBaseDecay", 0.25d);
                // Deplete the packed food channel without resetting the engine.
                // Both preview modes must refresh from this live GPU state.
                Invoke(engine, "UploadFoodChannel", new float[nx * ny * nz], Get<int>(engine, "foodRemainingOffset"));
                Invoke(engine, "Step", settings, dimension, 2, false, false, false);
                foreach (int field in before.Keys)
                {
                    object updated = Invoke(engine, "CreateVoxelFieldPreviewFrame", field, dimension, 0f, float.MaxValue, 1)!;
                    byte[] density = Read(engine, updated, false), gradient = Read(engine, updated, true);
                    ValidateGradient(updated, density, gradient, field);
                    if (field == 13 || field == 11)
                        for (int offset = 12; offset < density.Length; offset += 16)
                            Assert(BitConverter.ToSingle(density, offset) == 0, "Depleted food remained visible in field " + field);
                    Assert(!gradient.AsSpan().SequenceEqual(before[field]), $"Field {field} reused a stale gradient after a solver step.");
                }
            }
            Console.WriteLine($"PASS {scenario} {nx}x{ny}x{nz}");
        }
        finally { ((IDisposable)engine).Dispose(); }
    }

    static void ValidateDensity(object frame, byte[] raw, float[] slime, float[] food, float[] home, float[] remaining, int nx, int ny, int nz)
    {
        int width = Get<int>(frame, "Width"), height = Get<int>(frame, "Height"), axis = Get<int>(frame, "AxisMode");
        int px = Get<int>(frame, "ResX"), py = Get<int>(frame, "ResY"), pz = Get<int>(frame, "ResZ");
        int columns = Get<int>(frame, "AtlasColumns");
        float[] values = new float[raw.Length / 4]; Buffer.BlockCopy(raw, 0, values, 0, raw.Length);
        for (int v = 0; v < height; v++)
        for (int u = 0; u < width; u++)
        {
            bool padding = axis == 3 && v / py * columns + u / px >= pz;
            float x, y, z;
            if (axis == 3)
            {
                x = SourceIndex(u % px, px, nx); y = SourceIndex(v % py, py, ny);
                z = SourceIndex(v / py * columns + u / px, pz, nz);
            }
            else
            {
                float a = u * (float)((axis == 2 ? ny : nx) - 1) / Math.Max(width - 1, 1);
                float b = v * (float)((axis == 0 ? ny : nz) - 1) / Math.Max(height - 1, 1);
                x = axis == 2 ? 0 : a; y = axis == 2 ? a : axis == 0 ? b : 0; z = axis == 0 ? 0 : b;
            }
            float[] channels = { Sample(slime, x, y, z), Sample(food, x, y, z), Sample(home, x, y, z), Sample(remaining, x, y, z) };
            for (int c = 0; c < 4; c++)
                Assert(Math.Abs(values[(v * width + u) * 4 + c] - (padding ? 0 : channels[c])) < 0.0001f,
                    $"Density channel {c} mismatch at atlas ({u},{v}); ant resource binding or atlas mapping is incorrect.");
        }
        float Sample(float[] source, float x, float y, float z)
        {
            int x0 = Math.Clamp((int)x, 0, nx - 1), y0 = Math.Clamp((int)y, 0, ny - 1), z0 = Math.Clamp((int)z, 0, nz - 1);
            int x1 = Math.Min(x0 + 1, nx - 1), y1 = Math.Min(y0 + 1, ny - 1), z1 = Math.Min(z0 + 1, nz - 1);
            float dx = x - x0, dy = y - y0, dz = z - z0;
            float At(int xx, int yy, int zz) => source[(xx * ny + yy) * nz + zz];
            return Lerp(Lerp(Lerp(At(x0,y0,z0), At(x1,y0,z0), dx), Lerp(At(x0,y1,z0), At(x1,y1,z0), dx), dy),
                Lerp(Lerp(At(x0,y0,z1), At(x1,y0,z1), dx), Lerp(At(x0,y1,z1), At(x1,y1,z1), dx), dy), dz);
        }
    }

    static void ValidateGradient(object frame, byte[] density, byte[] gradient, int field)
    {
        int width = Get<int>(frame, "Width"), height = Get<int>(frame, "Height");
        int nx = Get<int>(frame, "ResX"), ny = Get<int>(frame, "ResY"), nz = Get<int>(frame, "ResZ");
        int columns = Get<int>(frame, "AtlasColumns");
        float[] values = new float[density.Length / 4]; Buffer.BlockCopy(density, 0, values, 0, density.Length);
        for (int v = 0; v < height; v++)
        for (int u = 0; u < width; u++)
        {
            int x = u % nx, y = v % ny, z = v / ny * columns + u / nx;
            float[] expected = new float[4];
            if (z < nz)
            {
                float dx = Derivative(1, 0, 0), dy = Derivative(0, 1, 0), dz = Derivative(0, 0, 1);
                float length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                expected = length > 0.000001f ? new[] { dx / length, dy / length, dz / length, length } : new[] { 0f, 0f, 0f, length };
            }
            for (int c = 0; c < 4; c++)
            {
                float actual = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(gradient, (v * width + u) * 8 + c * 2));
                float tolerance = Math.Max(0.001f, Math.Abs(expected[c]) * 0.001f);
                Assert(float.IsFinite(actual) && Math.Abs(actual - expected[c]) <= tolerance,
                    $"Field {field}, atlas ({u},{v}), gradient channel {c}: expected {expected[c]}, got {actual}.");
            }
            float Derivative(int dx, int dy, int dz) =>
                0.16f * (At(x + dx, y + dy, z + dz) - At(x - dx, y - dy, z - dz))
                + 0.17f * (At(x + 2 * dx, y + 2 * dy, z + 2 * dz) - At(x - 2 * dx, y - 2 * dy, z - 2 * dz));
        }
        float At(int x, int y, int z)
        {
            x = Math.Clamp(x, 0, nx - 1); y = Math.Clamp(y, 0, ny - 1); z = Math.Clamp(z, 0, nz - 1);
            int p = ((z / columns * ny + y) * width + z % columns * nx + x) * 4;
            float selected = field switch { 7 => values[p], 8 => values[p + 1], 9 => values[p + 2],
                10 => Math.Max(values[p + 1], values[p + 2]),
                11 => Math.Max(values[p], Math.Max(values[p + 1], values[p + 2])), 13 => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(field)) };
            // Existing slime normals intentionally stay based on the slime channel.
            return field == 7 ? Math.Max(0, selected) : Math.Max(0, Math.Max(values[p + 3], selected));
        }
    }

    static byte[] Read(object engine, object frame, bool gradient)
    {
        var device = Get<ID3D11Device>(engine, "device"); var context = Get<ID3D11DeviceContext>(engine, "context");
        using var source = device.OpenSharedResource<ID3D11Texture2D>(Get<IntPtr>(frame, gradient ? "GradientSharedHandle" : "SharedHandle"));
        var d = source.Description;
        Assert(d.Format == (gradient ? Format.R16G16B16A16_Float : Format.R32G32B32A32_Float), "Unexpected atlas format: " + d.Format);
        using var staging = device.CreateTexture2D(new Texture2DDescription(d.Format, d.Width, d.Height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
        context.CopyResource(staging, source);
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowBytes = d.Width * (gradient ? 8 : 16); byte[] bytes = new byte[rowBytes * d.Height];
            for (int row = 0; row < d.Height; row++) Marshal.Copy(mapped.DataPointer + row * mapped.RowPitch, bytes, row * rowBytes, rowBytes);
            return bytes;
        }
        finally { context.Unmap(staging, 0); }
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static int SourceIndex(int i, int preview, int source) => preview <= 1 ? 0 : (int)MathF.Floor(i * (float)(source - 1) / (preview - 1) + 0.5f);
    static object Create(string name) => Activator.CreateInstance(RequiredType(name), nonPublic: true)!;
    static Type RequiredType(string name) => Assemblies.Select(a => a.GetType(name)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);
    static void Set(object target, string name, object? value) => target.GetType().GetField(name, Instance)!.SetValue(target, value);
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance)!.GetValue(target)!;
    static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethods(Instance)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name); if (i < 0) return null;
        if (i + 1 == args.Length) throw new ArgumentException(name + " requires a value.");
        return args[i + 1];
    }
    static void Load(string directory)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string local = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(local)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(local);
            string package = name.Name == "RhinoCommon" ? "rhinocommon" : name.Name is "Grasshopper" or "GH_IO" ? "grasshopper" : name.Name!.ToLowerInvariant();
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", package);
            if (!Directory.Exists(root)) return null;
            string? path = Directory.EnumerateFiles(root, name.Name + ".dll", SearchOption.AllDirectories)
                .Where(p => p.Contains(Path.DirectorySeparatorChar + "lib" + Path.DirectorySeparatorChar))
                .OrderByDescending(p => p.Contains("net7", StringComparison.OrdinalIgnoreCase)).ThenByDescending(p => p).FirstOrDefault();
            return path == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        };
        foreach (string name in new[] { "Nuclei4.gha", "Nuclei4.Core.dll", "Nuclei4.Gpu.Abstractions.dll", "Nuclei4.Gpu.D3D11.dll", "Nuclei4.Display.Abstractions.dll" })
            Assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, name)));
    }
}
