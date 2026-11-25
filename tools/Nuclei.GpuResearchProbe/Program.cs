using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Standalone D3D11 diagnostic: no production build or Rhino document is required.
internal static class Program
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<Assembly> Assemblies = new();
    static readonly List<object> Results = new();
    static bool IntroProfile;
    static bool ParticlePreview;
    static bool DynamicScan, CpuFields, Trails;
    static int FoodSources;
    static float MotionScale = 1;

    static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(Option(args, "--nuclei-output") ?? "Nuclei-v4/Nuclei4/bin/Release/net7.0-windows");
            IntroProfile = args.Contains("--intro-profile");
            ParticlePreview = args.Contains("--particle-preview");
            DynamicScan = args.Contains("--dynamic-scan");
            CpuFields = args.Contains("--cpu-fields");
            Trails = args.Contains("--trails");
            FoodSources = Integer(args, "--sources", 0);
            MotionScale = float.Parse(Option(args, "--motion-scale") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
            int grid = Integer(args, "--grid", IntroProfile ? 250 : 100), particles = Integer(args, "--particles", IntroProfile ? 1436 : 1000);
            int largeSlimeCount = Integer(args, "--slime-particles", IntroProfile ? 90000 : checked(particles * 60));
            int steps = Integer(args, "--steps", 20), repeats = Integer(args, "--repeats", 3), warmup = Integer(args, "--warmup", 8);
            int range = Integer(args, "--range", 1), scale = Integer(args, "--preview-scale", 1);
            string modes = Option(args, "--preview") ?? "off,single,three";
            string species = Option(args, "--species") ?? "ants,slime,slime60";
            string[] variants = (Option(args, "--variants") ?? "current").Split(',');
            foreach (string variant in variants)
                if (variant is not ("legacy" or "direct-fused" or "tiled-separate" or "prior-optimized" or "paired-only" or "skip-density-only" or "paired-no-sparse" or "current" or "current-particle-deposits" or "generic-movement" or "legacy-preview" or "radius1-tiled" or "before-optimizations" or "radius1-direct"))
                    throw new ArgumentException("Unknown variant " + variant);
            Load(directory);
            string backendPath = Path.Combine(directory, "Nuclei4.Gpu.D3D11.dll");
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backendPath)));
            Console.WriteLine($"ASSEMBLY {backendPath} sha256={hash}");
            Console.WriteLine($"RESEARCH FIXTURE {grid}^3; intro={IntroProfile}; particlePreview={ParticlePreview}; dynamicScan={DynamicScan}; rawCpuFields={CpuFields}; trails={Trails}; sources={FoodSources}; fixed population (death thresholds prevent mutation); no viewport rendering, meshing or Grasshopper output conversion.");
            foreach (string mode in modes.Split(','))
            foreach (string kind in species.Split(','))
            {
                if (mode is not ("off" or "single" or "three")) throw new ArgumentException("Unknown preview mode " + mode);
                if (kind is not ("ants" or "slime" or "slime60")) throw new ArgumentException("Unknown species " + kind);
                for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
                    Run(kind, kind == "slime60" ? largeSlimeCount : particles, grid, mode, range, scale, steps, repeats, warmup, variants[variantIndex], variantIndex + 1);
            }
            string? json = Option(args, "--json");
            if (json != null) File.WriteAllText(Path.GetFullPath(json), JsonSerializer.Serialize(new { BackendSha256 = hash, Grid = grid, Steps = steps, Repeats = repeats, Warmup = warmup, Range = range, Scale = scale, MotionScale, IntroProfile, ParticlePreview, DynamicScan, CpuFields, Trails, FoodSources, Variants = variants, Results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static void Run(string kind, int particleCount, int grid, string mode, int range, int scale, int steps, int repeats, int warmup, string variant, int variantOrdinal)
    {
        string variantLabel = variant;
        bool genericMovement = variant is "generic-movement" or "before-optimizations";
        if (genericMovement || variant is "legacy-preview" or "radius1-tiled" or "radius1-direct") variant = "current";
        bool ants = kind == "ants";
        object input = Input(grid, particleCount, ants), settings = Create("Nuclei4.SolverGpuSettings");
        Set(settings, "WrapBoundaries", true);
        Set(settings, "DiffuseRange", range); Set(settings, "AntDiffuseRange", range);
        if (DynamicScan)
        {
            Set(settings,"DynamicPopulation",true); Set(settings,"Death",true);
            Set(settings,"DeathFrequency",1); Set(settings,"DeathMinimumAge",0);
            Set(settings,"DeathRange",3); Set(settings,"DivisionRange",3);
            Set(settings,"DeathMinimumNeighbours",0); Set(settings,"DeathMaximumNeighbours",int.MaxValue);
            Set(settings,"MinimumPopulation",0); Set(settings,"MaximumPopulation",particleCount);
        }
        if(Trails){Set(settings,"TrailSize",32);Set(settings,"TrailFreq",1);}
        if (IntroProfile)
        {
            Set(settings, "WrapBoundaries", !ants);
            if (ants)
            {
                Set(settings, "AntDiffuseRange", 2);
                Set(settings, "AntFoodDiffuse", .1d); Set(settings, "AntBaseDiffuse", .1d);
                Set(settings, "AntFoodDecay", .0001d); Set(settings, "AntBaseDecay", .0005d);
            }
            else
            {
                Set(settings, "DiffuseRange", 1); Set(settings, "Diffuse", .15d);
                Set(settings, "DiffusionGradual", .5d); Set(settings, "Decay", .01d);
            }
        }
        object dimension = RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution", Static)!.Invoke(null, new object[] { grid, grid, grid })!;
        object sink = RuntimeHelpers.GetUninitializedObject(RequiredType("Nuclei4.Gh1GpuSolverOutputSink"));
        object engine = RequiredType("Nuclei4.GpuFullSlimeSolverEngine").GetConstructors(Instance)
            .Single(c => c.GetParameters().Length == 8).Invoke(new[] { input, sink, settings, mode != "off", ParticlePreview, Trails, Trails ? 32 : 0, scale });
        try
        {
            SetOptionalValidationSwitch(engine, "forceGenericSlimeMovementForValidation", genericMovement, genericMovement);
            bool legacyPreview = variantLabel is "legacy-preview" or "before-optimizations";
            bool directRadiusOne = variantLabel is "radius1-direct" or "before-optimizations";
            SetOptionalValidationSwitch(engine, "forceLegacyDensityPreviewForValidation", legacyPreview, legacyPreview);
            SetOptionalValidationSwitch(engine, "disableRadiusOneTilingForValidation", directRadiusOne, directRadiusOne);
            SetOptionalValidationSwitch(engine, "enableRadiusOneTilingForValidation", variantLabel == "radius1-tiled", variantLabel == "radius1-tiled");
            bool forceDirect = variant is "legacy" or "direct-fused";
            bool disableAntDecayFusion = variant is "legacy" or "tiled-separate";
            SetValidationSwitch(engine, "forceDirectDiffusionForValidation", forceDirect, variant);
            SetValidationSwitch(engine, "disableAntDecayFusionForValidation", disableAntDecayFusion, variant);
            bool disablePaired = variant is not ("paired-only" or "paired-no-sparse" or "current" or "current-particle-deposits");
            bool forceScalar = variant is not ("skip-density-only" or "paired-no-sparse" or "current" or "current-particle-deposits");
            bool? disablePairedApplied = SetOptionalValidationSwitch(engine, "disablePairedAntDiffusionForValidation", disablePaired, variant == "paired-only");
            bool? forceScalarApplied = SetOptionalValidationSwitch(engine, "forceScalarDensityUpdatesForValidation", forceScalar, variant == "skip-density-only");
            bool particleDrivenDeposits = variant == "current-particle-deposits";
            bool? particleDrivenDepositsApplied = SetOptionalValidationSwitch(engine, "forceParticleDrivenDepositForValidation", particleDrivenDeposits, particleDrivenDeposits);
            bool disableAdaptiveDeposits = variant is not ("current" or "current-particle-deposits");
            bool? disableAdaptiveDepositsApplied = SetOptionalValidationSwitch(engine, "disableAdaptiveAntDepositsForValidation", disableAdaptiveDeposits, variant == "paired-no-sparse");
            Console.WriteLine($"VARIANT {variantOrdinal}={variant} directDiffusion={forceDirect} disableAntDecayFusion={disableAntDecayFusion} disablePairedAntDiffusion={disablePairedApplied?.ToString() ?? "unsupported"} forceScalarDensityUpdates={forceScalarApplied?.ToString() ?? "unsupported"} forceParticleDrivenDeposits={particleDrivenDepositsApplied?.ToString() ?? "unsupported"} disableAdaptiveAntDeposits={disableAdaptiveDepositsApplied?.ToString() ?? "unsupported"}");
            object caps = engine.GetType().GetProperty("Capabilities")!.GetValue(engine)!;
            string adapter=Get<string>(caps,"AdapterName"); Console.WriteLine("ADAPTER " + adapter);
            if (Get<bool>(caps, "SoftwareFallback")) throw new InvalidOperationException("Hardware D3D11 required; refusing WARP timings.");
            int active = (int)Invoke(engine, "SynchronizeActiveParticleCount")!;
            if (active != particleCount) throw new InvalidOperationException($"Reset dropped particles: requested {particleCount}, retained {active}.");
            var step = engine.GetType().GetMethods(Instance).Single(m => m.Name == "Step" && m.GetParameters().Length == 6);
            var stepArgs = new object[] { settings, dimension, 0, false, false, false };
            int iteration = 0;
            void Advance()
            {
                stepArgs[2] = iteration++;
                step.Invoke(engine, stepArgs);
                if(CpuFields)
                {
                    Invoke(engine,"ReadBackDensity");
                    if(ants)Invoke(engine,"ReadBackAntFields");
                }
                if (mode != "off")
                    foreach (int field in ants ? mode == "three" ? new[] { 8, 9, 10 } : new[] { 10 } : new[] { 7 })
                        if (Invoke(engine, "CreateVoxelFieldPreviewFrame", field, dimension, 0f, float.MaxValue, scale) == null)
                            throw new InvalidOperationException("Missing requested preview frame " + field);
            }
            for (int i = 0; i < warmup; i++) Advance();
            Invoke(engine, "SynchronizeActiveParticleCount");
            // Allocate pass query storage and exercise profiling before measured samples.
            Invoke(engine, "BeginGpuPassTimestampBatch", steps);
            Advance();
            Invoke(engine, "EndGpuTimestampBatch");
            Invoke(engine, "SynchronizeActiveParticleCount");
            Invoke(engine, "ResolveGpuPassTimestampBatch");
            for (int repeat = 1; repeat <= repeats; repeat++)
            {
                var wall = Stopwatch.StartNew();
                Invoke(engine, "BeginGpuPassTimestampBatch", steps);
                var submit = Stopwatch.StartNew();
                for (int i = 0; i < steps; i++) Advance();
                submit.Stop();
                Invoke(engine, "EndGpuTimestampBatch");
                active = (int)Invoke(engine, "SynchronizeActiveParticleCount")!;
                object batch = Invoke(engine, "ResolveGpuPassTimestampBatch")!;
                wall.Stop();
                if (active != particleCount) throw new InvalidOperationException("Unexpected active population change.");
                var passes = ((IEnumerable)Get<object>(batch, "Samples")).Cast<object>()
                    .GroupBy(p => Get<string>(p, "PassName"))
                    .ToDictionary(g => g.Key, g => g.Sum(p => Get<double>(p, "Milliseconds")) / steps);
                double gpuMs = Get<double>(batch, "TotalMilliseconds") / steps;
                double wallMs = wall.Elapsed.TotalMilliseconds / steps, submitMs = submit.Elapsed.TotalMilliseconds / steps;
                Results.Add(new { Adapter=adapter, Species = kind, Particles = particleCount, ActiveParticles = active, Preview = mode, Variant = variantLabel, VariantOrdinal = variantOrdinal, ForceDirectDiffusion = forceDirect, DisableAntDecayFusion = disableAntDecayFusion, DisablePairedAntDiffusion = disablePairedApplied, ForceScalarDensityUpdates = forceScalarApplied, ForceParticleDrivenDeposits = particleDrivenDepositsApplied, DisableAdaptiveAntDeposits = disableAdaptiveDepositsApplied, Repeat = repeat, GpuMsPerStep = gpuMs, WallMsPerStep = wallMs, SubmitMsPerStep = submitMs, PassMsPerStep = passes });
                Console.WriteLine($"RESULT species={kind} particles={particleCount} preview={mode} variant={variantLabel} variantOrdinal={variantOrdinal} repeat={repeat} gpuMs={gpuMs:F6} wallMs={wallMs:F6} submitMs={submitMs:F6}");
                Console.WriteLine("PASSES " + string.Join(" ", passes.Select(p => $"{p.Key}={p.Value:F6}")));
            }
        }
        finally { ((IDisposable)engine).Dispose(); }
    }

    static object Input(int grid, int count, bool ants)
    {
        if (grid < 8) throw new ArgumentException("Grid must be >= 8.");
        int voxels = checked(grid * grid * grid), span = grid - 4, capacity = checked(span * span * span);
        if (count > capacity) throw new ArgumentException($"{count} particles exceed unique interior placement capacity {capacity}.");
        object input = Create("Nuclei4.GpuSolverInput");
        Set(input, "ResX", grid); Set(input, "ResY", grid); Set(input, "ResZ", grid); Set(input, "VoxelSize", 1f);
        Set(input, "ParticleCount", count); Set(input, "GroupCount", 1);
        Set(input, "HasAntParticles", ants); Set(input, "HasSlimeParticles", !ants);
        float[] positions = new float[count * 3], directions = new float[count * 3], yAxes = new float[count * 3], homes = new float[count * 3];
        int[] parents = new int[count], ages = new int[count]; uint[] states = new uint[count], launchStates = new uint[count];
        int multiplier = 7919;
        while (Gcd(multiplier, capacity) != 1) multiplier++;
        for (int i = 0; i < count; i++)
        {
            int ordinal = (int)(((long)i * multiplier + 104729) % capacity), offset = i * 3;
            int x = ordinal / (span * span) + 2, y = ordinal / span % span + 2, z = ordinal % span + 2;
            positions[offset] = x + .25f; positions[offset + 1] = y + .25f; positions[offset + 2] = z + .25f;
            parents[i] = (x * grid + y) * grid + z;
            double theta = (Hash((uint)i ^ 0x12345678u) & 0xFFFFFFu) * (Math.PI * 2 / 16777216);
            double dz = (Hash((uint)i ^ 0x9e3779b9u) & 0xFFFFFFu) * (2.0 / 16777216) - 1, radius = Math.Sqrt(1 - dz * dz);
            float dx = (float)(Math.Cos(theta) * radius), dy = (float)(Math.Sin(theta) * radius);
            directions[offset] = dx; directions[offset + 1] = dy; directions[offset + 2] = (float)dz;
            double denom = Math.Sqrt(dx * dx + dy * dy);
            yAxes[offset] = (float)(-dy / denom); yAxes[offset + 1] = (float)(dx / denom);
            homes[offset] = homes[offset + 1] = homes[offset + 2] = grid * .5f;
            ages[i] = 100 + i % 43;
            states[i] = ants ? (uint)(i % 2) : 0;
            launchStates[i] = ants ? 1u : 0;
        }
        Set(input, "ParticlePositionsXyz", positions); Set(input, "ParticleDirectionsXyz", directions); Set(input, "ParticleYAxesXyz", yAxes);
        Set(input, "ParticleHomesXyz", homes); Set(input, "ParticleAntStates", states); Set(input, "ParticleAntLaunchBoundaryStates", launchStates);
        Set(input, "ParticleAges", ages); Set(input, "ParticleGroupIndices", new int[count]); Set(input, "ParticleParentIndices", parents);
        Set(input, "GroupData0", new[] { .72f, 2.35f, (float)(37 * Math.PI / 180), 0f });
        Set(input, "GroupData1", new[] { (float)(29 * Math.PI / 180), ants ? 1f : 0f, .85f, 10f });
        if (IntroProfile)
        {
            Set(input, "GroupData0", new[] { 3f, 9f, MathF.PI / 4, ants ? .15f : 0f });
            Set(input, "GroupData1", new[] { MathF.PI / 4, ants ? 1f : 0f, ants ? 10f : 1f, ants ? Math.Max(1f, MathF.Floor(.15f * count / 40)) : 0f });
        }
        Set(input, "GroupColorData", new[] { 1f, .5f, .1f, 1f });
        var motion = Get<float[]>(input, "GroupData0");
        motion[0] *= MotionScale;
        motion[1] *= MotionScale;
        float[] density = new float[voxels];
        for (int i = 0; i < voxels; i++) density[i] = .1f + (Hash((uint)i) & 255) * .001f;
        Set(input, "VoxelDensity", ants ? new float[voxels] : density);
        if (ants) { Set(input, "AntFoodPheromone", (float[])density.Clone()); Set(input, "AntBasePheromone", (float[])density.Clone()); }
        if(FoodSources>0)
        {
            var sourceValues=new float[voxels];
            for(int i=0;i<Math.Min(FoodSources,voxels);i++) sourceValues[(int)((long)i*voxels/Math.Min(FoodSources,voxels))]=ants?100f:.04f;
            Set(input,ants?"InitialAntFood":"InitialFood",sourceValues);
        }
        return input;
    }

    static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; return x ^ (x >> 16); }
    static int Gcd(int a, int b) { while (b != 0) { int r = a % b; a = b; b = r; } return a; }
    static void SetValidationSwitch(object engine, string name, bool value, string variant)
    {
        FieldInfo? field = engine.GetType().GetField(name, Instance);
        // A default current run also supports archived binaries predating the switch.
        if (field == null && variant == "current") return;
        if (field == null) throw new MissingFieldException($"Variant {variant} requires backend validation switch {name}.");
        field.SetValue(engine, value);
    }
    static bool? SetOptionalValidationSwitch(object engine, string name, bool value, bool required)
    {
        FieldInfo? field = engine.GetType().GetField(name, Instance);
        // Archived backends predate pairing/scalar-skipping. Null records that no
        // switch was applied rather than implying the new optimization existed.
        if (field == null && !required) return null;
        if (field == null) throw new MissingFieldException($"This comparison requires backend validation switch {name}.");
        field.SetValue(engine, value);
        return value;
    }
    static object Create(string name) => Activator.CreateInstance(RequiredType(name), nonPublic: true)!;
    static Type RequiredType(string name) => Assemblies.Select(a => a.GetType(name)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance)!.SetValue(target, value);
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance)!.GetValue(target)!;
    static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethods(Instance).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static int Integer(string[] args, string name, int fallback) => Option(args, name) is string value ? int.Parse(value) : fallback;
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
            string? path = Directory.EnumerateFiles(root, name.Name + ".dll", SearchOption.AllDirectories).Where(p => p.Contains(Path.DirectorySeparatorChar + "lib" + Path.DirectorySeparatorChar)).OrderByDescending(p => p.Contains("net7", StringComparison.OrdinalIgnoreCase)).ThenByDescending(p => p).FirstOrDefault();
            return path == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        };
        foreach (string name in new[] { "Nuclei4.gha", "Nuclei4.Core.dll", "Nuclei4.Gpu.Abstractions.dll", "Nuclei4.Gpu.D3D11.dll", "Nuclei4.Display.Abstractions.dll" })
            Assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, name)));
    }
}
