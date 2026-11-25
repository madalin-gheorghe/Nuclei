using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Compares actual D3D11 state, not a CPU reimplementation of the shader.
internal static partial class Program
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<Assembly> Assemblies = new();
    static readonly List<object> Results = new();
    static readonly Variant[] Variants = {
        new("direct-separate", true, true, false), new("direct-fused", true, false, false),
        new("tiled-separate", false, true, false), new("tiled-fused", false, false, false),
        new("paired-direct-separate", true, true, true), new("paired-direct-fused", true, false, true),
        new("paired-tiled-separate", false, true, true), new("paired-tiled-fused", false, false, true),
        new("paired-tiled-fused-particle-deposits", false, false, true, true),
        new("paired-tiled-fused-auto-deposits", false, false, true, false, true)
    };
    sealed record Variant(string Name, bool Direct, bool Separate, bool Paired, bool ParticleDrivenDeposits = false, bool AdaptiveDeposits = false);
    sealed record Fixture(string Name, int X, int Y, int Z, int Range, bool Wrap,
        double FoodDiffuse, double BaseDiffuse, double FoodDecay = .031, double BaseDecay = .073,
        bool Masks = true, bool Limits = true, bool Particles = true, bool Mixed = false, bool Live = false, int ParticleLimit = 0);

    static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(Option(args, "--nuclei-output") ?? "Nuclei-v4/Nuclei4/bin/Release/net7.0-windows");
            Load(directory);
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "Nuclei4.Gpu.D3D11.dll"))));
            Console.WriteLine($"ASSEMBLY {directory}; backend sha256={hash}");
            if (args.Contains("--actual-food")) { RunActualFoodTests(args); return 0; }
            if (args.Contains("--departure")) { RunDepartureTests(args); return 0; }
            if (args.Contains("--trail")) { RunTrailTests(args); return 0; }
            if (args.Contains("--nest")) { RunNestTests(args); return 0; }
            if (args.Contains("--food-source-emission")) { RunFoodSourceEmission(); return 0; }
            if (args.Contains("--food-following")) { RunFoodFollowing(args); return 0; }
            var fixtures = Fixtures().Where(f => Option(args, "--case") is not string selected || f.Name == selected).ToArray();
            Assert(fixtures.Length > 0, "No matching fixture.");
            foreach (Fixture fixture in fixtures) Run(fixture);
            string? json = Option(args, "--json");
            if (json != null) File.WriteAllText(Path.GetFullPath(json), JsonSerializer.Serialize(new { BackendSha256 = hash, Fixtures = Results }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {fixtures.Length} fixtures × {Variants.Length} variants × 8/14 steps; bit-exact legacy/paired state, static ant-only scalar, mixed-species scalar updates and live independent field settings.");
            return 0;
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static IEnumerable<Fixture> Fixtures()
    {
        // 12 cases cover all requested radii, including tiled limit16/fallback17,
        // repeated wrapping when radius exceeds an axis, and partial16×16 tiles.
        foreach (int range in new[] { 0, 1, 2, 5, 16, 17 })
        foreach (bool wrap in new[] { false, true })
            yield return new($"3d-r{range}-{(wrap ? "wrap" : "closed")}", 35, 19, 9, range, wrap, .17, .31);
        foreach (var shape in new[] { (35, 19, 1, "xy"), (35, 1, 19, "xz"), (1, 35, 19, "yz") })
        foreach (bool wrap in new[] { false, true })
            yield return new($"plane-{shape.Item4}-{(wrap ? "wrap" : "closed")}", shape.Item1, shape.Item2, shape.Item3, 2, wrap, .1, .23);
        yield return new("food-only", 19, 9, 7, 5, false, .2, 0, 0, .045);
        yield return new("base-only", 19, 9, 7, 5, true, 0, .2, .045, 0);
        yield return new("decay-only-boundaries", 19, 9, 7, 2, false, 0, 0, .031, .073, Particles: false);
        yield return new("decay-only-wrapped", 19, 9, 7, 2, true, 0, 0, .031, .073, Particles: false);
        yield return new("dense-defaults", 19, 9, 7, 2, false, .1, .1, Masks: false, Limits: false);
        yield return new("unit-diffusion-zero-decay", 19, 9, 7, 1, true, 1, 1, 0, 0);
        foreach (var shape in new[] { (35, 19, 9, "3d"), (35, 19, 1, "xy"), (35, 1, 19, "xz"), (1, 35, 19, "yz") })
        {
            yield return new($"mixed-{shape.Item4}", shape.Item1, shape.Item2, shape.Item3, 2, true, .17, .31, Mixed: true);
            yield return new($"live-{shape.Item4}", shape.Item1, shape.Item2, shape.Item3, 2, false, .17, 0, Mixed: shape.Item4 is "3d" or "yz", Live: true);
        }
        yield return new("live-sparse-auto", 67, 35, 19, 2, false, .17, 0, Live: true, ParticleLimit: 10);
    }

    static void Run(Fixture fixture)
    {
        object input = CreateInput(fixture), settings = CreateSettings(fixture);
        object dimension = RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution", Static)!
            .Invoke(null, new object[] { fixture.X, fixture.Y, fixture.Z })!;
        var engines = new List<object>();
        try
        {
            for (int variantIndex = 0; variantIndex < Variants.Length; variantIndex++)
            {
                Variant variant = Variants[variantIndex];
                object sink = RuntimeHelpers.GetUninitializedObject(RequiredType("Nuclei4.Gh1GpuSolverOutputSink"));
                object engine = RequiredType("Nuclei4.GpuFullSlimeSolverEngine").GetConstructors(Instance)
                    .Single(c => c.GetParameters().Length == 8).Invoke(new[] { input, sink, settings, false, false, false, 0, 1 });
                engines.Add(engine);
                object capabilities = engine.GetType().GetProperty("Capabilities")!.GetValue(engine)!;
                Assert(!Get<bool>(capabilities, "SoftwareFallback"), "Hardware D3D11 required; WARP selected.");
                Set(engine, "forceDirectDiffusionForValidation", variant.Direct);
                Set(engine, "disableAntDecayFusionForValidation", variant.Separate);
                Set(engine, "disablePairedAntDiffusionForValidation", !variant.Paired);
                Set(engine, "forceParticleDrivenDepositForValidation", variant.ParticleDrivenDeposits);
                SetOptional(engine, "disableAdaptiveAntDepositsForValidation", !variant.AdaptiveDeposits);
                Set(engine, "disableScalarDecayFusionForValidation", true);
                // Mixed reference restores the previous always-update condition;
                // other variants must match it with normal species gating.
                Set(engine, "forceScalarDensityUpdatesForValidation", fixture.Mixed && variantIndex == 0);
                Assert((int)Invoke(engine, "SynchronizeActiveParticleCount")! == Get<int>(input, "ParticleCount"), "Fixture reset dropped particles.");
            }
            Dictionary<string, byte[]> initial = Snapshot(engines[0]);
            for (int variant = 1; variant < engines.Count; variant++)
                Compare(initial, Snapshot(engines[variant]), fixture.Name, Variants[variant].Name, -1);
            string finalHash = "";
            int comparedBuffers = 0;
            int steps = fixture.Live ? 14 : 8;
            bool sawIndependentBuffers = false;
            var settingsHistory = new List<object>();
            var automaticDepositPasses = new List<string>();
            for (int iteration = 0; iteration < steps; iteration++)
            {
                Fixture current = SettingsAtStep(fixture, iteration);
                ApplySettings(settings, current);
                settingsHistory.Add(new { Iteration = iteration, current.Range, current.Wrap, current.FoodDiffuse, current.BaseDiffuse, current.FoodDecay, current.BaseDecay });
                Dictionary<string, byte[]>? expected = null;
                for (int variant = 0; variant < engines.Count; variant++)
                {
                    bool profileAutomaticDispatch = fixture.Name == "live-sparse-auto" && Variants[variant].AdaptiveDeposits && iteration == 2;
                    if (profileAutomaticDispatch) Invoke(engines[variant], "BeginGpuPassTimestampBatch", 1);
                    object result = Invoke(engines[variant], "Step", settings, dimension, iteration, false, false, false)!;
                    if (profileAutomaticDispatch) Invoke(engines[variant], "EndGpuTimestampBatch");
                    ValidatePassCount(result, current, Variants[variant]);
                    Dictionary<string, byte[]> actual = Snapshot(engines[variant]);
                    if (profileAutomaticDispatch)
                    {
                        object batch = Invoke(engines[variant], "ResolveGpuPassTimestampBatch")!;
                        string[] names = ((System.Collections.IEnumerable)Get<object>(batch, "Samples")).Cast<object>()
                            .Select(sample => Get<string>(sample, "PassName")).Where(name => name.StartsWith("ApplyDeposits.", StringComparison.Ordinal)).ToArray();
                        Assert(names.Contains("ApplyDeposits.ParticleSparseAnt"), $"{fixture.Name}: automatic particle-driven shader dispatch was not observed ({string.Join(", ", names)}).");
                        automaticDepositPasses.AddRange(names);
                    }
                    sawIndependentBuffers |= Get<bool>(engines[variant], "antFoodInA") != Get<bool>(engines[variant], "antBaseInA");
                    if (!fixture.Mixed)
                        Assert(initial["densityReadback"].AsSpan().SequenceEqual(actual["densityReadback"]), $"{fixture.Name}/{Variants[variant].Name}: ant-only scalar changed at iteration {iteration}.");
                    if (variant == 0)
                    {
                        expected = actual;
                        if (iteration == 0 && current.FoodDiffuse == 0 && current.BaseDiffuse == 0)
                            ValidateDecayBoundaries(input, current, actual);
                    }
                    else Compare(expected!, actual, fixture.Name, Variants[variant].Name, iteration);
                    comparedBuffers += actual.Count;
                }
                if (iteration == steps - 1)
                {
                    if (fixture.Mixed)
                        Assert(!initial["densityReadback"].AsSpan().SequenceEqual(expected!["densityReadback"]), $"{fixture.Name}: mixed scalar never advanced.");
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    foreach (byte[] bytes in expected!.Values) hash.AppendData(bytes);
                    finalHash = Convert.ToHexString(hash.GetHashAndReset());
                }
            }
            if (fixture.Live) Assert(sawIndependentBuffers, $"{fixture.Name}: live fixture did not exercise independent food/base ping-pong buffers.");
            Results.Add(new { Fixture = fixture, ParticleCount = Get<int>(input, "ParticleCount"), Steps = steps, Variants = Variants.Select(v => v.Name), ComparedBuffers = comparedBuffers, IndependentBuffersObserved = sawIndependentBuffers, AutomaticDepositPasses = automaticDepositPasses, SettingsHistory = settingsHistory, FinalStateSha256 = finalHash });
            Console.WriteLine($"PASS {fixture.Name}: {fixture.X}×{fixture.Y}×{fixture.Z}, particles={Get<int>(input, "ParticleCount")}, full-state sha256={finalHash}");
        }
        finally { foreach (object engine in engines) ((IDisposable)engine).Dispose(); }
    }

    static void ValidatePassCount(object result, Fixture f, Variant variant)
    {
        int axes = f.X > 1 && f.Y > 1 && f.Z > 1 ? 3 : 2;
        int FieldPasses(double diffuse) => diffuse > 0 ? axes + (variant.Separate ? 1 : 0) : 1;
        int antPasses = FieldPasses(f.FoodDiffuse) + FieldPasses(f.BaseDiffuse);
        if (variant.Paired && f.FoodDiffuse > 0 && f.BaseDiffuse > 0) antPasses = axes + (variant.Separate ? 1 : 0);
        if (variant.Paired && f.FoodDiffuse <= 0 && f.BaseDiffuse <= 0) antPasses = 1;
        // A single enabled ant field keeps the existing independent dispatch;
        // mixed fixtures also run scalar diffusion and its separate decay.
        int expected = 1 + (f.Mixed ? axes + 1 : 0) + antPasses;
        Assert(Get<int>(result, "Passes") == expected, $"{f.Name}/{variant.Name}: expected {expected} field passes, got {Get<int>(result, "Passes")}.");
    }

    static Dictionary<string, byte[]> Snapshot(object engine)
    {
        Invoke(engine, "ReadBackDensity");
        Invoke(engine, "ReadBackAntFields");
        Invoke(engine, "ReadBackParticles");
        var state = new Dictionary<string, byte[]>();
        foreach (string name in new[] { "densityReadback", "antFoodReadback", "antBaseReadback", "antFoodRemainingReadback", "particlePositionReadback", "particleDirectionReadback", "particleYAxisReadback", "particleHomeReadback", "particleHomeAxesReadback", "particleAuxReadback", "populationStateReadback" })
        {
            object? data = engine.GetType().GetField(name, Instance)!.GetValue(engine);
            state.Add(name, data is Array array ? Bytes(array) : Array.Empty<byte>());
        }
        foreach (string name in new[] { "ParticleCounts", "ParticleOwners", "Deposits" })
            state.Add(name, Bytes((Array)Invoke(engine, "ReadBackValidation" + name)!));
        return state;
    }

    static void Compare(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual, string fixture, string variant, int iteration)
    {
        foreach (var entry in expected)
        {
            byte[] found = actual[entry.Key];
            if (entry.Value.AsSpan().SequenceEqual(found)) continue;
            int offset = 0;
            while (offset < entry.Value.Length && offset < found.Length && entry.Value[offset] == found[offset]) offset++;
            int index = offset / 4;
            string bits = index * 4 + 4 <= entry.Value.Length && index * 4 + 4 <= found.Length
                ? $"; expected bits0x{BitConverter.ToUInt32(entry.Value, index * 4):X8} ({BitConverter.ToSingle(entry.Value, index * 4):R}), actual bits0x{BitConverter.ToUInt32(found, index * 4):X8} ({BitConverter.ToSingle(found, index * 4):R})"
                : $"; lengths {entry.Value.Length}/{found.Length}";
            throw new InvalidOperationException($"{fixture}/{variant}, iteration {iteration}, {entry.Key}[{index}] differs{bits}.");
        }
    }

    static void ValidateDecayBoundaries(object input, Fixture f, Dictionary<string, byte[]> state)
    {
        float[] food = Get<float[]>(input, "AntFoodPheromone"), home = Get<float[]>(input, "AntBasePheromone");
        uint[]? active = Get<uint[]?>(input, "ActiveVoxelFlags");
        int checkedBoundary = 0;
        for (int x = 0; x < f.X; x++) for (int y = 0; y < f.Y; y++) for (int z = 0; z < f.Z; z++)
        {
            int i = (x * f.Y + y) * f.Z + z;
            bool isActive = active == null || (active[i >> 5] & 1u << (i & 31)) != 0;
            bool boundary = (f.X > 1 && (x == 0 || x == f.X - 1)) || (f.Y > 1 && (y == 0 || y == f.Y - 1)) || (f.Z > 1 && (z == 0 || z == f.Z - 1));
            float expectedFood = !isActive || boundary && !f.Wrap ? 0 : Math.Max(food[i] + Get<float[]>(input, "InitialAntFood")[i] - (float)f.FoodDecay, 0);
            float expectedHome = !isActive ? 0 : Math.Max(home[i] - (float)f.BaseDecay, 0);
            float actualFood = BitConverter.ToSingle(state["antFoodReadback"], i * 4);
            float actualHome = BitConverter.ToSingle(state["antBaseReadback"], i * 4);
            Assert(BitConverter.SingleToInt32Bits(expectedFood) == BitConverter.SingleToInt32Bits(actualFood), $"{f.Name}: food decay/boundary mismatch at {i}.");
            Assert(BitConverter.SingleToInt32Bits(expectedHome) == BitConverter.SingleToInt32Bits(actualHome), $"{f.Name}: base decay/boundary mismatch at {i}.");
            if (boundary && isActive && expectedHome > 0) checkedBoundary++;
        }
        Assert(checkedBoundary > 0, "Boundary fixture did not exercise positive base pheromone.");
    }

    static object CreateSettings(Fixture f)
    {
        object settings = Create("Nuclei4.SolverGpuSettings");
        ApplySettings(settings, f);
        return settings;
    }

    static void ApplySettings(object settings, Fixture f)
    {
        Set(settings, "Diffuse", f.Mixed ? .11d : 0d); Set(settings, "Decay", .013d);
        Set(settings, "WrapBoundaries", f.Wrap); Set(settings, "AntDiffuseRange", f.Range);
        Set(settings, "AntFoodDiffuse", f.FoodDiffuse); Set(settings, "AntBaseDiffuse", f.BaseDiffuse);
        Set(settings, "AntFoodDecay", f.FoodDecay); Set(settings, "AntBaseDecay", f.BaseDecay);
        Set(settings, "AntSlime", .25d);
        Set(settings, "SlimeAntFood", f.Mixed ? .2d : 0d);
        Set(settings, "SlimeAntBase", f.Mixed ? .1d : 0d);
    }

    static Fixture SettingsAtStep(Fixture f, int iteration)
    {
        if (!f.Live) return f;
        int[] ranges = { 2, 2, 0, 1, 16, 17, 5, 2, 1, 0, 16, 17, 5, 2 };
        (double Food, double Base)[] rates = {
            (.17, 0), (0, .31), (.17, .31), (0, 0), (.17, .31), (0, .31), (.17, 0),
            (.17, .31), (0, .31), (0, 0), (.17, .31), (.17, 0), (.17, .31), (0, .31)
        };
        return f with { Range = ranges[iteration], FoodDiffuse = rates[iteration].Food, BaseDiffuse = rates[iteration].Base,
            FoodDecay = iteration % 3 == 0 ? 0 : .031, BaseDecay = iteration % 4 == 0 ? 0 : .073,
            Wrap = iteration >= 5 && iteration < 11 };
    }

    static object CreateInput(Fixture f)
    {
        object input = Create("Nuclei4.GpuSolverInput");
        int count = f.X * f.Y * f.Z;
        Set(input, "ResX", f.X); Set(input, "ResY", f.Y); Set(input, "ResZ", f.Z); Set(input, "VoxelSize", 1f);
        Set(input, "HasAntParticles", true); Set(input, "HasSlimeParticles", f.Mixed); Set(input, "GroupCount", f.Mixed ? 2 : 1);
        float[] density = new float[count], food = new float[count], home = new float[count], remaining = new float[count], limits = new float[count * 2];
        uint[] active = new uint[(count + 31) / 32], valid = new uint[active.Length];
        var positions = new List<float>(); var directions = new List<float>(); var yAxes = new List<float>(); var homes = new List<float>(); var parents = new List<int>();
        for (int x = 0; x < f.X; x++) for (int y = 0; y < f.Y; y++) for (int z = 0; z < f.Z; z++)
        {
            int i = (x * f.Y + y) * f.Z + z;
            uint h = Hash((uint)i + 71);
            density[i] = .015f + (h & 255) * .003f;
            food[i] = i % 11 == 0 ? 0 : .017f + ((h >> 8) & 1023) * .0021f;
            home[i] = i % 13 == 0 ? 0 : .019f + ((h >> 18) & 1023) * .0019f;
            remaining[i] = i % 29 == 0 ? 3.25f : 0;
            bool selected = !f.Masks || i % 17 != 0;
            bool obstacle = f.Masks && i % 23 == 0;
            if (selected) active[i >> 5] |= 1u << (i & 31);
            if (selected && !obstacle) valid[i >> 5] |= 1u << (i & 31);
            // Normal, zero-minimum, restrictive-maximum, and min>max cases.
            limits[i] = i % 31 == 0 ? 1.2f : i % 7 == 0 ? .22f : i % 3 == 0 ? 0 : -1;
            limits[count + i] = obstacle ? 0 : i % 31 == 0 ? .125f : i % 5 == 0 ? .44f : -1;
            bool ParticleCoordinate(int c, int size) => size == 1 || c >= 3 && c <= size - 4 && (c - 3) % 7 == 0;
            if (f.Particles && (f.ParticleLimit <= 0 || parents.Count < f.ParticleLimit) && selected && !obstacle && ParticleCoordinate(x, f.X) && ParticleCoordinate(y, f.Y) && ParticleCoordinate(z, f.Z))
            {
                parents.Add(i);
                positions.AddRange(new[] { x + .5f, y + .5f, z + .5f });
                bool yz = f.X == 1, xz = f.Y == 1;
                directions.AddRange(yz ? new[] { 0f, 1f, 0f } : new[] { 1f, 0f, 0f });
                yAxes.AddRange(yz || xz ? new[] { 0f, 0f, 1f } : new[] { 0f, 1f, 0f });
                homes.AddRange(new[] { x + .5f, y + .5f, z + .5f });
                remaining[i] = parents.Count % 2 == 0 ? 3.25f : 0;
            }
        }
        int particles = parents.Count;
        Assert(!f.Particles || particles > 0, "No particles in fixture " + f.Name);
        Assert(!f.Mixed || particles >= 2, "Mixed fixture needs both species.");
        Set(input, "ParticleCount", particles); Set(input, "ParticlePositionsXyz", positions.ToArray());
        Set(input, "ParticleDirectionsXyz", directions.ToArray()); Set(input, "ParticleYAxesXyz", yAxes.ToArray()); Set(input, "ParticleHomesXyz", homes.ToArray());
        Set(input, "ParticleParentIndices", parents.ToArray()); Set(input, "ParticleGroupIndices", Enumerable.Range(0, particles).Select(i => f.Mixed ? i % 2 : 0).ToArray());
        Set(input, "ParticleAges", Enumerable.Range(0, particles).Select(i => i + 400).ToArray());
        Set(input, "ParticleAntStates", Enumerable.Range(0, particles).Select(i => f.Mixed ? i % 2 == 0 ? (uint)(i / 2 % 2) : 0u : (uint)(i % 2)).ToArray());
        Set(input, "ParticleAntLaunchBoundaryStates", Enumerable.Repeat(1u, particles).ToArray());
        Set(input, "GroupData0", f.Mixed ? new[] { .13f, 1.75f, .63f, 0f, .11f, 1.5f, .6f, 0f } : new[] { .13f, 1.75f, .63f, 0f });
        Set(input, "GroupData1", f.Mixed ? new[] { .41f, 1f, .15f, 17f, .39f, 0f, .07f, 0f } : new[] { .41f, 1f, .15f, 17f });
        Set(input, "GroupColorData", f.Mixed ? new[] { 1f, .5f, .1f, 1f, .2f, .8f, .3f, 1f } : new[] { 1f, .5f, .1f, 1f });
        Set(input, "VoxelDensity", density); Set(input, "AntFoodPheromone", food); Set(input, "AntBasePheromone", home); Set(input, "InitialAntFood", remaining);
        if (f.Masks) { Set(input, "ActiveVoxelFlags", active); Set(input, "VoxelFlags", valid); }
        if (f.Limits)
        {
            Set(input, "VoxelDensityLimits", limits); Set(input, "MinimumDensityOffset", 0); Set(input, "MaximumDensityOffset", count);
        }
        return input;
    }

    static byte[] Bytes(Array array) { byte[] bytes = new byte[Buffer.ByteLength(array)]; Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length); return bytes; }
    static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; return x ^ (x >> 16); }
    static object Create(string name) => Activator.CreateInstance(RequiredType(name), nonPublic: true)!;
    static Type RequiredType(string name) => Assemblies.Select(a => a.GetType(name)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);
    static void Set(object target, string name, object? value) => (target.GetType().GetField(name, Instance) ?? throw new MissingFieldException(target.GetType().FullName, name)).SetValue(target, value);
    static void SetOptional(object target, string name, object? value) => target.GetType().GetField(name, Instance)?.SetValue(target, value);
    static T Get<T>(object target, string name) => (T)(target.GetType().GetField(name, Instance) ?? throw new MissingFieldException(target.GetType().FullName, name)).GetValue(target)!;
    static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethods(Instance).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string? Option(string[] args, string name) { int i = Array.IndexOf(args, name); return i < 0 ? null : i + 1 < args.Length ? args[i + 1] : throw new ArgumentException(name + " requires a value."); }
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
