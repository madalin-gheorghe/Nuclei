using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

internal static class Program
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<Assembly> Assemblies = new();

    static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string directory = Path.GetFullPath(Option(args, "--nuclei-output")
                ?? "Nuclei-v4/Nuclei4/bin/Release/net7.0-windows");
            Load(directory);
            Console.WriteLine("Assembly: " + directory);
            switch (Option(args, "--mode") ?? "correctness")
            {
                case "correctness": Correctness(args); break;
                case "transitions": Transitions(args); break;
                case "performance": Performance(args); break;
                default: throw new ArgumentException("--mode must be correctness, transitions, or performance.");
            }
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    static void Correctness(string[] args)
    {
        string? record = Option(args, "--record-dir");
        string? reference = Option(args, "--reference-dir");
        if ((record == null) == (reference == null))
            throw new ArgumentException("Specify exactly one of --record-dir and --reference-dir.");
        string destination = Path.GetFullPath(record ?? reference!);
        if (record != null) Directory.CreateDirectory(destination);
        int cases = 0;
        foreach (var shape in new[] { (23, 19, 17), (23, 19, 1), (23, 1, 17), (1, 19, 17) })
        foreach (bool wrap in new[] { false, true })
        foreach (string mask in new[] { "sparse", "full", "empty" })
        foreach (int variant in new[] { 0, 1, 2 })
        {
            // Default tiled/fused, direct/separate reference, and maximum-halo tiled.
            int range = variant == 2 ? 16 : 5;
            string label = $"{shape.Item1}x{shape.Item2}x{shape.Item3}_{mask}_wrap{wrap}_v{variant}";
            uint[]? flags = Mask(shape.Item1, shape.Item2, shape.Item3, mask);
            object input = Input(shape.Item1, shape.Item2, shape.Item3, flags, detailed: true);
            object settings = Settings(wrap, range);
            object dimension = Dimension(shape.Item1, shape.Item2, shape.Item3);
            object engine = Engine(input, settings);
            try
            {
                if (variant == 1)
                {
                    Set(engine, "forceDirectDiffusionForValidation", true);
                    Set(engine, "disableScalarDecayFusionForValidation", true);
                }
                using var output = new MemoryStream();
                using var writer = new BinaryWriter(output);
                for (int iteration = 1; iteration <= 4; iteration++)
                {
                    // Last iteration exercises an existing-field boundary transition.
                    if (iteration == 4) Set(settings, "WrapBoundaries", !wrap);
                    Step(engine, settings, dimension, iteration);
                    Invoke(engine, "ReadBackDensity");
                    Invoke(engine, "ReadBackParticles");
                    Write(writer, Get<float[]>(engine, "densityReadback"));
                    Write(writer, Get<float[]>(engine, "particlePositionReadback"));
                    Write(writer, Get<float[]>(engine, "particleDirectionReadback"));
                    Write(writer, Get<float[]>(engine, "particleYAxisReadback"));
                    writer.Write(Get<int>(engine, "particleCount"));
                    int[] owners = (int[])Invoke(engine, "ReadBackValidationParticleOwners")!;
                    writer.Write(owners.Length);
                    foreach (int owner in owners) writer.Write(owner);
                }
                writer.Flush();
                byte[] actual = output.ToArray();
                string path = Path.Combine(destination, label + ".bin");
                if (record != null) File.WriteAllBytes(path, actual);
                else
                {
                    byte[] expected = File.ReadAllBytes(path);
                    if (!actual.AsSpan().SequenceEqual(expected))
                    {
                        int common = Math.Min(actual.Length, expected.Length), mismatch = 0;
                        while (mismatch < common && actual[mismatch] == expected[mismatch]) mismatch++;
                        throw new InvalidOperationException($"{label}: snapshot differs at byte {mismatch}; expected {expected.Length} bytes, actual {actual.Length} bytes.");
                    }
                }
                Console.WriteLine($"PASS {label}: {(record != null ? "recorded" : "bit-identical")} 4 steps density, particle state, ownership");
                cases++;
            }
            finally { ((IDisposable)engine).Dispose(); }
        }
        Console.WriteLine($"PASS {cases} hardware-GPU cases, {cases * 4} steps.");
    }

    static void Performance(string[] args)
    {
        int grid = int.Parse(Option(args, "--grid") ?? "350");
        int warmup = int.Parse(Option(args, "--warmup") ?? "4");
        int steps = int.Parse(Option(args, "--steps") ?? "4");
        int repeats = int.Parse(Option(args, "--repeats") ?? "3");
        double decay = double.Parse(Option(args, "--decay") ?? "0.03");
        if (grid < 16 || grid > 400 || warmup < 1 || steps < 1 || repeats < 1)
            throw new ArgumentException("Use grid16..400 and positive warmup/steps/repeats.");
        if (!double.IsFinite(decay) || decay < 0) throw new ArgumentException("Use finite nonnegative decay.");
        string selection = Option(args, "--selection") ?? "all";
        var results = new List<object>();
        foreach (string mask in selection == "all" ? new[] { "full", "10", "25", "function" } : new[] { selection })
        {
            uint[]? flags = Mask(grid, grid, grid, mask);
            int count = checked(grid * grid * grid);
            long active = flags == null ? count : flags.Sum(word => (long)System.Numerics.BitOperations.PopCount(word));
            Console.WriteLine($"BENCH grid={grid} mask={mask} active={active}/{count} ({100.0 * active / count:F3}%) diffuse=0.15 range=5 decay={decay}");
            object input = Input(grid, grid, grid, flags, detailed: false);
            object settings = Settings(false, 5);
            Set(settings, "Decay", decay);
            object dimension = Dimension(grid, grid, grid);
            object engine = Engine(input, settings);
            try
            {
                var samples = new List<double>();
                var diffusionSamples = new List<double>();
                int iteration = 0;
                for (int i = 0; i < warmup; i++) Step(engine, settings, dimension, ++iteration);
                Invoke(engine, "ReadBackParticles"); // One tiny particle staging buffer fences all earlier dispatches.
                for (int repeat = 0; repeat < repeats; repeat++)
                {
                    Invoke(engine, "BeginGpuPassTimestampBatch", steps);
                    for (int i = 0; i < steps; i++) Step(engine, settings, dimension, ++iteration);
                    Invoke(engine, "EndGpuTimestampBatch");
                    Invoke(engine, "ReadBackParticles");
                    object timestamps = Invoke(engine, "ResolveGpuPassTimestampBatch")!;
                    double total = Get<double>(timestamps, "TotalMilliseconds") / steps;
                    double diffusion = 0;
                    var passes = new Dictionary<string, double>();
                    foreach (object sample in Get<IEnumerable>(timestamps, "Samples"))
                    {
                        string name = Get<string>(sample, "PassName");
                        double elapsed = Get<double>(sample, "Milliseconds") / steps;
                        passes[name] = passes.GetValueOrDefault(name) + elapsed;
                        if (name.StartsWith("Diffusion", StringComparison.Ordinal)) diffusion += elapsed;
                    }
                    samples.Add(total); diffusionSamples.Add(diffusion);
                    Console.WriteLine($"  repeat={repeat + 1} totalMs={total:F4} diffusionMs={diffusion:F4} passes={JsonSerializer.Serialize(passes)}");
                }
                double median = Median(samples), diffusionMedian = Median(diffusionSamples);
                Console.WriteLine($"RESULT grid={grid} mask={mask} active={active} gpuMs={median:F4} diffusionMs={diffusionMedian:F4}");
                results.Add(new { Grid = grid, Mask = mask, Active = active, Count = count, Decay = decay, GpuMs = median, DiffusionMs = diffusionMedian, Samples = samples, DiffusionSamples = diffusionSamples });
            }
            finally { ((IDisposable)engine).Dispose(); }
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
        string? json = Option(args, "--json");
        if (json != null) File.WriteAllText(Path.GetFullPath(json), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void Transitions(string[] args)
    {
        string? record = Option(args, "--record-dir"), reference = Option(args, "--reference-dir");
        if ((record == null) == (reference == null)) throw new ArgumentException("Specify --record-dir or --reference-dir.");
        string destination = Path.GetFullPath(record ?? reference!);
        if (record != null) Directory.CreateDirectory(destination);
        foreach (var shape in new[] { (19, 21, 23), (19, 21, 1) })
        foreach (bool wrap in new[] { false, true })
        foreach (bool ants in new[] { false, true })
        {
            int count = shape.Item1 * shape.Item2 * shape.Item3;
            object input = Input(shape.Item1, shape.Item2, shape.Item3, null, detailed: true);
            if (ants)
            {
                Set(input, "HasAntParticles", true);
                Set(input, "AntFoodPheromone", Enumerable.Range(0, count).Select(i => 0.2f + i % 9 * 0.03f).ToArray());
                Set(input, "AntBasePheromone", Enumerable.Range(0, count).Select(i => 0.1f + i % 11 * 0.02f).ToArray());
            }
            object settings = Settings(wrap, 5);
            Set(settings, "AntDiffuseRange", 5);
            object dimension = Dimension(shape.Item1, shape.Item2, shape.Item3);
            object engine = Engine(input, settings);
            string label = $"live_{shape.Item1}x{shape.Item2}x{shape.Item3}_wrap{wrap}_ants{ants}";
            try
            {
                using var output = new MemoryStream();
                using var writer = new BinaryWriter(output);
                for (int iteration = 1; iteration <= 8; iteration++)
                {
                    if (iteration == 3 || iteration == 5)
                    {
                        uint[]? flags = null;
                        if (iteration == 3)
                        {
                            flags = new uint[(count + 31) / 32];
                            for (int i = 0; i < count; i++)
                                if (i % 3 == 0) flags[i >> 5] |= 1u << (i & 31);
                        }
                        object update = Input(shape.Item1, shape.Item2, shape.Item3, flags, detailed: true);
                        if (!(bool)Invoke(engine, "UpdateVoxelBehaviorFields", update)!)
                            throw new InvalidOperationException(label + ": live mask update rejected.");
                    }
                    if (iteration == 7)
                    {
                        Set(settings, "Diffuse", 0d);
                        Set(settings, "AntFoodDiffuse", 0d); Set(settings, "AntBaseDiffuse", 0d);
                    }
                    Step(engine, settings, dimension, iteration);
                    Invoke(engine, "ReadBackDensity"); Invoke(engine, "ReadBackParticles");
                    Write(writer, Get<float[]>(engine, "densityReadback"));
                    Write(writer, Get<float[]>(engine, "particlePositionReadback"));
                    Write(writer, Get<float[]>(engine, "particleDirectionReadback"));
                    if (ants)
                    {
                        Invoke(engine, "ReadBackAntFields");
                        Write(writer, Get<float[]>(engine, "antFoodReadback"));
                        Write(writer, Get<float[]>(engine, "antBaseReadback"));
                    }
                }
                writer.Flush();
                string path = Path.Combine(destination, label + ".bin");
                byte[] actual = output.ToArray();
                if (record != null) File.WriteAllBytes(path, actual);
                else if (!actual.AsSpan().SequenceEqual(File.ReadAllBytes(path)))
                    throw new InvalidOperationException(label + ": live-mask/ant/decay snapshot differs.");
                Console.WriteLine($"PASS {label}: {(record != null ? "recorded" : "bit-identical")} full→alternating→full→decay-only, 8steps");
            }
            finally { ((IDisposable)engine).Dispose(); }
        }
        Console.WriteLine("PASS 8 live-mask/ant/decay-only hardware-GPU cases, 64steps.");
    }

    static uint[]? Mask(int xSize, int ySize, int zSize, string mode)
    {
        int count = checked(xSize * ySize * zSize);
        if (mode == "full") return null;
        if (mode == "function")
        {
            object grid = RequiredType("Nuclei4.VoxelGridData").GetMethod("CreateFullDomain", Static)!
                .Invoke(null, new object[] { xSize, ySize, zSize, 1d })!;
            object selector = Create("Nuclei4.GpuPeriodicSurface");
            try
            {
                string formula = (string)RequiredType("Nuclei4.GpuPeriodicSurface").GetMethod("Translate", Static)!
                    .Invoke(null, new object[] { "Math.Sin(x*x) + Math.Sin(y*y) + Math.Sin(z*z)" })!;
                int[] selected = (int[])Invoke(selector, "Select", grid, formula, 3.15f, 0f, 0f, 4f)!;
                var words = new uint[selected.Length];
                Buffer.BlockCopy(selected, 0, words, 0, selected.Length * 4);
                return words;
            }
            finally { ((IDisposable)selector).Dispose(); }
        }
        uint[] flags = new uint[(count + 31) / 32];
        if (mode == "empty") return flags;
        if (mode is not ("sparse" or "10" or "25")) throw new ArgumentException("Unknown mask " + mode);
        for (int x = 0, index = 0; x < xSize; x++)
        for (int y = 0; y < ySize; y++)
        for (int z = 0; z < zSize; z++, index++)
        {
            bool selected = mode == "sparse" ? ((x / 3 + y / 4 + z / 5) % 4 == 0)
                : x < Math.Max(1, xSize * int.Parse(mode) / 100);
            if (selected) flags[index >> 5] |= 1u << (index & 31);
        }
        return flags;
    }

    static object Input(int xSize, int ySize, int zSize, uint[]? flags, bool detailed)
    {
        int count = checked(xSize * ySize * zSize);
        object input = Create("Nuclei4.GpuSolverInput");
        bool Active(int i) => flags == null || (flags[i >> 5] & (1u << (i & 31))) != 0;
        float[] density = new float[count];
        for (int i = 0; i < count; i++) density[i] = detailed ? 0.04f + ((i * 37 + i / 11) % 211) / 500f : 0.8f;
        uint[]? traversable = flags == null ? null : (uint[])flags.Clone();
        if (detailed)
        {
            float[] limits = Enumerable.Repeat(-1f, count * 2).ToArray();
            // Active, movement-blocked cells must still participate in diffusion.
            if (traversable == null)
            {
                traversable = Enumerable.Repeat(uint.MaxValue, (count + 31) / 32).ToArray();
                if ((count & 31) != 0) traversable[^1] = (1u << (count & 31)) - 1;
            }
            for (int i = 0; i < count; i++)
            {
                if (i % 37 == 0) limits[i] = 0.21f;
                if (i % 29 == 0)
                {
                    limits[count + i] = 0.13f;
                    traversable[i >> 5] &= ~(1u << (i & 31));
                }
            }
            Set(input, "VoxelDensityLimits", limits);
            Set(input, "MinimumDensityOffset", 0); Set(input, "MaximumDensityOffset", count);
        }
        int[] seeds = detailed ? Enumerable.Range(0, count)
            .Where(i => Active(i) && i % 29 != 0 && i % 17 == 1).Take(12).ToArray()
            : Enumerable.Range(0, count).Where(Active).Take(1).ToArray();
        int slots = Math.Max(1, seeds.Length + 1);
        float[] positions = new float[slots * 3], directions = new float[slots * 3], axes = new float[slots * 3];
        int[] parents = Enumerable.Repeat(-1, slots).ToArray(), groups = Enumerable.Repeat(-1, slots).ToArray();
        for (int i = 0; i < seeds.Length; i++)
        {
            int index = seeds[i];
            positions[i * 3] = index / (ySize * zSize) + 0.25f;
            positions[i * 3 + 1] = index / zSize % ySize + 0.25f;
            positions[i * 3 + 2] = index % zSize + 0.25f;
            int direction = zSize > 1 ? 2 : 1;
            directions[i * 3 + direction] = 1;
            axes[i * 3 + (direction == 2 ? 1 : 2)] = 1;
            parents[i] = index; groups[i] = 0;
        }
        Set(input, "ResX", xSize); Set(input, "ResY", ySize); Set(input, "ResZ", zSize);
        Set(input, "VoxelSize", 1f); Set(input, "ParticleCount", slots); Set(input, "GroupCount", 1);
        Set(input, "HasSlimeParticles", true); Set(input, "HasAntParticles", false);
        Set(input, "ParticlePositionsXyz", positions); Set(input, "ParticleDirectionsXyz", directions);
        Set(input, "ParticleYAxesXyz", axes); Set(input, "ParticleHomesXyz", positions.Clone());
        Set(input, "ParticleParentIndices", parents); Set(input, "ParticleGroupIndices", groups);
        Set(input, "ParticleAntStates", new uint[slots]); Set(input, "ParticleAntLaunchBoundaryStates", new uint[slots]);
        Set(input, "ParticleAges", new int[slots]);
        Set(input, "GroupData0", new[] { detailed ? 0.65f : 0f, 1f, 0.3f, 0.2f });
        Set(input, "GroupData1", new[] { 0.025f, 0f, 0f, 0f });
        Set(input, "GroupColorData", new float[4]);
        Set(input, "ActiveVoxelFlags", flags); Set(input, "VoxelFlags", traversable);
        Set(input, "VoxelDensity", density);
        return input;
    }

    static object Settings(bool wrap, int range)
    {
        object settings = Create("Nuclei4.SolverGpuSettings");
        Set(settings, "WrapBoundaries", wrap); Set(settings, "Diffuse", 0.15d);
        Set(settings, "DiffuseRange", range); Set(settings, "Decay", 0.013d);
        Set(settings, "MaximumPopulation", 100); Set(settings, "MinimumPopulation", 0);
        return settings;
    }
    static object Engine(object input, object settings)
    {
        object sink = RuntimeHelpers.GetUninitializedObject(RequiredType("Nuclei4.Gh1GpuSolverOutputSink"));
        object engine = RequiredType("Nuclei4.GpuFullSlimeSolverEngine").GetConstructors(Instance)
            .Single(ctor => ctor.GetParameters().Length == 8).Invoke(new[] { input, sink, settings, false, false, false, 0, 1 });
        object capabilities = engine.GetType().GetProperty("Capabilities")!.GetValue(engine)!;
        if (Get<bool>(capabilities, "SoftwareFallback"))
        {
            ((IDisposable)engine).Dispose();
            throw new InvalidOperationException("Hardware GPU required; WARP fallback selected.");
        }
        return engine;
    }
    static object Dimension(int x, int y, int z) => RequiredType("Nuclei4.SolverGpuDimensionMode")
        .GetMethod("FromResolution", Static)!.Invoke(null, new object[] { x, y, z })!;
    static void Step(object engine, object settings, object dimension, int iteration)
        => Invoke(engine, "Step", settings, dimension, iteration, false, false, false);
    static void Write(BinaryWriter writer, float[]? values)
    {
        writer.Write(values?.Length ?? -1);
        if (values != null) foreach (float value in values) writer.Write(value);
    }
    static double Median(List<double> values) => values.OrderBy(v => v).ElementAt(values.Count / 2);
    static object Create(string name) => Activator.CreateInstance(RequiredType(name), nonPublic: true)!;
    static Type RequiredType(string name) => Assemblies.Select(a => a.GetType(name)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);
    static void Set(object target, string name, object? value) => target.GetType().GetField(name, Instance)!.SetValue(target, value);
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance)!.GetValue(target)!;
    static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethods(Instance)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static string? Option(string[] args, string option)
    {
        int i = Array.IndexOf(args, option);
        if (i < 0) return null;
        if (i + 1 == args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException(option + " requires a value.");
        return args[i + 1];
    }
    static void Load(string directory)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string local = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(local)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(local);
            string package = name.Name is "RhinoCommon" ? "rhinocommon"
                : name.Name is "Grasshopper" or "GH_IO" ? "grasshopper" : name.Name!.ToLowerInvariant();
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", package);
            if (!Directory.Exists(root)) return null;
            string? path = Directory.EnumerateFiles(root, name.Name + ".dll", SearchOption.AllDirectories)
                .Where(p => p.Contains(Path.DirectorySeparatorChar + "lib" + Path.DirectorySeparatorChar))
                .OrderByDescending(p => p.Contains("net7", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            return path == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        };
        foreach (string name in new[] { "Nuclei4.gha", "Nuclei4.Core.dll", "Nuclei4.Gpu.Abstractions.dll", "Nuclei4.Gpu.D3D11.dll", "Nuclei4.Display.Abstractions.dll", "Nuclei4.Display.D3D11.dll" })
            Assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, name)));
    }
}
