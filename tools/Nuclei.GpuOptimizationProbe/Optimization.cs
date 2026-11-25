using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class GpuOptimizationEntry
{
    static int Main(string[] args) => Program.OptimizationMain(args);
}

internal static partial class Program
{
    internal static int OptimizationMain(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(Option(args, "--nuclei-output") ?? "Nuclei-v4/Nuclei4/bin/Release/net7.0-windows");
            Load(directory);
            if (args.Contains("--shader-hashes"))
            {
                var assembly = RequiredType("Nuclei4.GpuFullSlimeSolverEngine").Assembly;
                var hashes = new SortedDictionary<string,string>();
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    using var stream = assembly.GetManifestResourceStream(name)!;
                    hashes[name] = Convert.ToHexString(SHA256.HashData(stream));
                }
                File.WriteAllText(Option(args,"--json")!,JsonSerializer.Serialize(hashes,new JsonSerializerOptions{WriteIndented=true}));
                return 0;
            }
            if (args.Contains("--diffusion")) return DiffusionChecks(args, directory);
            if (args.Contains("--networks")) return NetworkChecks(args, directory);
            var results = new List<object>();
            foreach (var shape in new[] { (35, 19, 9), (35, 19, 1), (35, 1, 19), (1, 35, 19) })
            foreach (bool wrap in new[] { false, true })
            foreach (float scale in new[] { .5f, 1f, 2f, 4f })
            {
                var fixture = new Fixture($"slime-{shape}-{wrap}-{scale}", shape.Item1, shape.Item2, shape.Item3, 1, wrap, .1, .1, Masks: true, Limits: true, ParticleLimit: 1);
                object input = CreateInput(fixture), settings = CreateSettings(fixture);
                MakeSlimeInput(input, scale);
                // Exercise spatially varying behaviour and vectors on all layouts.
                int n = fixture.X * fixture.Y * fixture.Z;
                Set(input, "VoxelBehaviorData", Enumerable.Range(0, n * 4).Select(i => .75f + (i % 7) * .05f).ToArray());
                Set(input, "SpeedOffset", 0); Set(input, "SensorDistanceOffset", n);
                Set(input, "SensorAngleOffset", n * 2); Set(input, "RotationAngleOffset", n * 3);
                Set(input, "VoxelVectorDefaultX", .03f); Set(input, "VoxelVectorDefaultY", .02f);
                Set(settings, "Diffuse", .15d); Set(settings, "Decay", .01d);
                object dimension = RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution", Static)!.Invoke(null, new object[] { fixture.X, fixture.Y, fixture.Z })!;
                object reference = OptimizationEngine(input, settings, true), candidate = OptimizationEngine(input, settings, false);
                try
                {
                    for (int iteration = 0; iteration < 32; iteration++)
                    {
                        if (iteration == 16) Set(settings, "WrapBoundaries", !wrap);
                        Invoke(reference, "Step", settings, dimension, iteration, false, false, false);
                        Invoke(candidate, "Step", settings, dimension, iteration, false, false, false);
                        Compare(SlimeSnapshot(reference), SlimeSnapshot(candidate), fixture.Name, "specialized", iteration);
                    }
                    results.Add(new { fixture.Name, Scale = scale, Steps = 32, BitExact = true });
                    Console.WriteLine("PASS " + fixture.Name);
                }
                finally { ((IDisposable)reference).Dispose(); ((IDisposable)candidate).Dispose(); }
            }
            string backendHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "Nuclei4.Gpu.D3D11.dll"))));
            string? output = Option(args, "--json");
            if (output != null) File.WriteAllText(output, JsonSerializer.Serialize(new { BackendSha256 = backendHash, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {results.Count} movement fixtures, all state buffers bit-exact after every step.");
            return 0;
        }
        catch (Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static void MakeSlimeInput(object input, float scale)
    {
        Set(input, "HasAntParticles", false); Set(input, "HasSlimeParticles", true);
        Set(input, "AntFoodPheromone", null); Set(input, "AntBasePheromone", null); Set(input, "InitialAntFood", null);
        Set(input, "GroupData0", new[] { 3f * scale, 9f * scale, MathF.PI / 4, 0f });
        Set(input, "GroupData1", new[] { MathF.PI / 4, 0f, 1f, 0f });
        Set(input, "ParticleAntStates", new uint[Get<int>(input, "ParticleCount")]);
        Set(input, "ParticleAntLaunchBoundaryStates", new uint[Get<int>(input, "ParticleCount")]);
    }

    static int DiffusionChecks(string[] args, string directory)
    {
        var results = new List<object>();
        foreach(var shape in new[]{(35,19,9),(35,19,1),(35,1,19),(1,35,19),(3,4,2),(128,128,128)})
        foreach(bool wrap in new[]{false,true})
        foreach(bool ants in new[]{false,true})
        {
            bool automatic = shape.Item1 == 128;
            var f = new Fixture($"radius1-{shape}-{wrap}-{ants}",shape.Item1,shape.Item2,shape.Item3,1,wrap,.17,.31,Particles:false,Masks:!automatic);
            object input=CreateInput(f), settings=CreateSettings(f);
            if(!ants)MakeSlimeInput(input,1);
            Set(settings,"Diffuse",.15d);Set(settings,"Decay",.01d);
            object dim=RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",Static)!.Invoke(null,new object[]{f.X,f.Y,f.Z})!;
            object reference=OptimizationEngine(input,settings,false), candidate=OptimizationEngine(input,settings,false);
            Set(reference,"forceScalarDensityUpdatesForValidation",true);Set(candidate,"forceScalarDensityUpdatesForValidation",true);
            Set(reference,"disableRadiusOneTilingForValidation",true);Set(candidate,"enableRadiusOneTilingForValidation",!automatic);
            try
            {
                for(int iteration=0;iteration<12;iteration++)
                {
                    if(iteration==6)Set(settings,"WrapBoundaries",!wrap);
                    Invoke(reference,"Step",settings,dim,iteration,false,false,false);
                    Invoke(candidate,"Step",settings,dim,iteration,false,false,false);
                    Compare(ants?Snapshot(reference):SlimeSnapshot(reference),ants?Snapshot(candidate):SlimeSnapshot(candidate),f.Name,"radius1-tiled",iteration);
                }
                results.Add(new{f.Name,Steps=12,BitExact=true});Console.WriteLine("PASS "+f.Name);
            }
            finally{((IDisposable)reference).Dispose();((IDisposable)candidate).Dispose();}
        }
        File.WriteAllText(Option(args,"--json")!,JsonSerializer.Serialize(new {BackendSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"Nuclei4.Gpu.D3D11.dll")))),Results=results},new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }

    static int NetworkChecks(string[] args, string directory)
    {
        var results = new List<object>();
        string output = Option(args, "--json") ?? "networks.json";
        string snapshots = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "network-projections");
        Directory.CreateDirectory(snapshots);
        int ensemble = int.Parse(Option(args, "--ensemble") ?? "0");
        Assert(ensemble == 0 || ensemble >= 2, "Use at least two pairs for --ensemble.");
        var runs = new List<object>();
        foreach (int grid in new[] { 64, 128 })
        foreach (float scale in new[] { .5f, 2f })
        {
            int particles = grid == 64 ? 3000 : 24000;
            object input = Create("Nuclei4.GpuSolverInput"), settings = Create("Nuclei4.SolverGpuSettings");
            Set(input, "ResX", grid); Set(input, "ResY", grid); Set(input, "ResZ", grid); Set(input, "VoxelSize", 1f);
            Set(input, "ParticleCount", particles); Set(input, "GroupCount", 1);
            MakeSlimeInput(input, scale);
            float[] pos = new float[particles * 3], dir = new float[pos.Length], axes = new float[pos.Length];
            int[] parents = new int[particles];
            int span = grid - 4, capacity = span * span * span, multiplier = 7919;
            while (System.Numerics.BigInteger.GreatestCommonDivisor(multiplier, capacity) != 1) multiplier++;
            for (int i = 0; i < particles; i++)
            {
                int ordinal = (int)(((long)i * multiplier + 104729) % capacity);
                int x = ordinal / (span * span) + 2, y = ordinal / span % span + 2, z = ordinal % span + 2;
                pos[3*i] = x + .25f; pos[3*i+1] = y + .25f; pos[3*i+2] = z + .25f;
                parents[i] = (x * grid + y) * grid + z;
                double theta = (Hash((uint)i ^ 0x12345678u) & 0xffffff) * (2 * Math.PI / 16777216);
                double dz = (Hash((uint)i ^ 0x9e3779b9u) & 0xffffff) * (2.0 / 16777216) - 1;
                double r = Math.Sqrt(1 - dz * dz);
                dir[3*i] = (float)(Math.Cos(theta) * r); dir[3*i+1] = (float)(Math.Sin(theta) * r); dir[3*i+2] = (float)dz;
                axes[3*i] = (float)-Math.Sin(theta); axes[3*i+1] = (float)Math.Cos(theta);
            }
            Set(input, "ParticlePositionsXyz", pos); Set(input, "ParticleDirectionsXyz", dir); Set(input, "ParticleYAxesXyz", axes);
            Set(input, "ParticleParentIndices", parents); Set(input, "ParticleGroupIndices", new int[particles]);
            Set(input, "ParticleAges", Enumerable.Repeat(100, particles).ToArray());
            Set(input, "GroupColorData", new[] { 1f, .5f, .1f, 1f });
            Set(input, "VoxelDensity", Enumerable.Range(0, grid*grid*grid).Select(i => .1f + (Hash((uint)i) & 255) * .001f).ToArray());
            Set(settings, "WrapBoundaries", true); Set(settings, "DiffuseRange", 1);
            Set(settings, "Diffuse", .15d); Set(settings, "DiffusionGradual", .5d); Set(settings, "Decay", .01d);
            object dimension = RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution", Static)!.Invoke(null, new object[] { grid, grid, grid })!;
            var states = new List<(float[] Field, float[] Positions)>();
            for (int variant = 0; variant < (ensemble > 0 ? ensemble * 2 : 3); variant++)
            {
                bool generic = ensemble > 0 ? variant % 2 == 0 : variant < 2;
                object engine = OptimizationEngine(input, settings, generic);
                try
                {
                    for (int iteration = 0; iteration < 800; iteration++)
                        Invoke(engine, "Step", settings, dimension, iteration, false, false, false);
                    Assert((int)Invoke(engine, "SynchronizeActiveParticleCount")! == particles, "Population changed.");
                    Invoke(engine, "ReadBackDensity"); Invoke(engine, "ReadBackParticles");
                    float[] field = (float[])Get<float[]>(engine, "densityReadback").Clone();
                    float[] positions = (float[])Get<float[]>(engine, "particlePositionReadback").Clone();
                    Assert(field.All(float.IsFinite) && positions.All(float.IsFinite), "Non-finite state.");
                    states.Add((field, positions));
                    runs.Add(new { Grid=grid, Scale=scale, Particles=particles, Variant=variant, Generic=generic, Steps=800,
                        Mass=field.Sum(x=>(double)x), FractionAbove005=field.Count(x=>x>.05f)/(double)field.Length,
                        FieldSquaredSum=field.Sum(x=>(double)x*x) });
                    File.WriteAllText(output + ".runs.json", JsonSerializer.Serialize(runs,new JsonSerializerOptions{WriteIndented=true}));
                    float[] projection = new float[grid * grid];
                    for (int x=0;x<grid;x++) for(int y=0;y<grid;y++) for(int z=0;z<grid;z++)
                        projection[y*grid+x] = Math.Max(projection[y*grid+x], field[(x*grid+y)*grid+z]);
                    File.WriteAllBytes(Path.Combine(snapshots, $"g{grid}-s{scale}-v{variant}.f32"), Bytes(projection));
                    Console.WriteLine($"EVOLVED grid={grid} scale={scale} variant={variant} original={generic} steps=800 mass={field.Sum(x=>(double)x):F3}");
                }
                finally { ((IDisposable)engine).Dispose(); }
            }
            double RelativeL1(float[] a, float[] b) => a.Zip(b, (x,y) => Math.Abs((double)x-y)).Sum() / Math.Max(1e-20, a.Sum(x => Math.Abs((double)x)));
            double Mass(float[] a) => a.Sum(x => (double)x);
            double occupancy(float[] a) => a.Count(x=>x>.05f)/(double)a.Length;
            double massA=Mass(states[0].Field), massB=Mass(states[ensemble > 0 ? 2 : 1].Field), massC=Mass(states[ensemble > 0 ? 1 : 2].Field);
            double noise=RelativeL1(states[0].Field, states[ensemble > 0 ? 2 : 1].Field), delta=RelativeL1(states[0].Field, states[ensemble > 0 ? 1 : 2].Field);
            // Aggregate guardrails supplement exact collision-free tests. Parallel
            // occupancy races can make dense trajectories differ even on replay.
            if (ensemble == 0)
            {
                Assert(Math.Abs(massC-massA)/Math.Max(1e-20,massA) <= Math.Max(.03, 2*Math.Abs(massB-massA)/Math.Max(1e-20,massA)), "Network mass regression.");
                Assert(delta <= Math.Max(.03, 1.5*noise), "Field change exceeds baseline replay envelope.");
            }
            results.Add(new { Grid=grid, Scale=scale, Particles=particles, Steps=800, BaselineReplayRelativeL1=noise, CandidateRelativeL1=delta,
                Mass=new[]{massA,massB,massC}, FractionAbove005=states.Select(x=>occupancy(x.Field)).ToArray(),
                PositionHashes=states.Select(x=>Convert.ToHexString(SHA256.HashData(Bytes(x.Positions)))).ToArray() });
        }
        File.WriteAllText(output,JsonSerializer.Serialize(new { BackendSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"Nuclei4.Gpu.D3D11.dll")))) ,Results=results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(ensemble > 0 ? "COMPLETE network ensemble; population/finite checks passed. Analyze recorded distributions separately." : "PASS evolved networks, small/large domains and jointly scaled sensors/speed.");
        return 0;
    }

    static object OptimizationEngine(object input, object settings, bool generic)
    {
        object sink = RuntimeHelpers.GetUninitializedObject(RequiredType("Nuclei4.Gh1GpuSolverOutputSink"));
        object engine = RequiredType("Nuclei4.GpuFullSlimeSolverEngine").GetConstructors(Instance).Single(c => c.GetParameters().Length == 8)
            .Invoke(new[] { input, sink, settings, false, false, false, 0, 1 });
        object capabilities = engine.GetType().GetProperty("Capabilities")!.GetValue(engine)!;
        Assert(!Get<bool>(capabilities, "SoftwareFallback"), "Hardware GPU required.");
        Set(engine, "forceGenericSlimeMovementForValidation", generic);
        Set(engine, "disableRadiusOneTilingForValidation", generic);
        Assert((int)Invoke(engine, "SynchronizeActiveParticleCount")! == Get<int>(input, "ParticleCount"), "Reset dropped particles.");
        return engine;
    }

    static Dictionary<string, byte[]> SlimeSnapshot(object engine)
    {
        Invoke(engine, "ReadBackDensity"); Invoke(engine, "ReadBackParticles");
        var state = new Dictionary<string, byte[]>();
        foreach (string name in new[] { "densityReadback", "particlePositionReadback", "particleDirectionReadback", "particleYAxisReadback", "particleHomeReadback", "particleHomeAxesReadback", "particleAuxReadback", "populationStateReadback" })
        {
            object? data = engine.GetType().GetField(name, Instance)!.GetValue(engine);
            state.Add(name, data is Array a ? Bytes(a) : Array.Empty<byte>());
        }
        foreach (string name in new[] { "ParticleCounts", "ParticleOwners", "Deposits" })
            state.Add(name, Bytes((Array)Invoke(engine, "ReadBackValidation" + name)!));
        return state;
    }
}
