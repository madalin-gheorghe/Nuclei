using System.Text.Json;

internal static partial class Program
{
    static void RunFoodFollowing(string[] args)
    {
        var results = new List<object>();
        bool baseline = args.Contains("--baseline");
        foreach (var shape in new[] { (35, 35, 9), (35, 35, 1), (35, 1, 35), (1, 35, 35) })
        foreach (bool mixed in new[] { false, true })
        foreach (string scenario in new[] { "gradient", "none", "flat", "trace", "carrying", "blocked-sensor", "slime" })
        {
            var f = new Fixture("food-following", shape.Item1, shape.Item2, shape.Item3, 2, true,
                0, 0, 0, 0, Masks: false, Limits: false, ParticleLimit: 1);
            object input = CreateInput(f), settings = CreateSettings(f);
            int n = f.X * f.Y * f.Z;
            int forward = f.X == 1 ? 1 : 0, lateral = f.Y == 1 || f.X == 1 ? 2 : 1;
            Set(input, "HasSlimeParticles", mixed); // Exercise generic and specialized ant kernels.
            float[] homes = (float[])Get<float[]>(input, "ParticleHomesXyz").Clone();
            homes[forward] -= 2f;
            Set(input, "ParticleHomesXyz", homes);
            Set(input, "ParticleAges", new[] { 5 });
            Set(input, "ParticleAntStates", new[] { scenario == "carrying" ? 1u : 0u });
            Set(input, "ParticleAntLaunchBoundaryStates", new[] { 0u });
            Set(input, "GroupData0", new[] { 1f, 1.75f, .63f, 0f });
            Set(input, "GroupData1", new[] { MathF.PI / 2, 1f, .15f, 17f });
            Set(input, "VoxelDensity", new float[n]);
            Set(input, "AntBasePheromone", new float[n]);
            Set(input, "InitialAntFood", new float[n]);
            float[] scent = new float[n];
            for (int x = 0; x < f.X; x++) for (int y = 0; y < f.Y; y++) for (int z = 0; z < f.Z; z++)
            {
                float coordinate = lateral == 1 ? y : z;
                scent[(x * f.Y + y) * f.Z + z] = scenario switch
                {
                    "none" => 0f, "flat" or "blocked-sensor" => .2f, "trace" => coordinate * 1e-9f,
                    _ => coordinate * .01f
                };
            }
            Set(input, "AntFoodPheromone", scent);
            if (scenario == "blocked-sensor")
            {
                uint[] valid = Enumerable.Repeat(uint.MaxValue, (n + 31) / 32).ToArray();
                float[] p = Get<float[]>(input, "ParticlePositionsXyz");
                int[] sensor = p.Select(v => (int)v).ToArray();
                sensor[forward] = (int)(p[forward] + MathF.Cos(.63f) * 1.75f);
                sensor[lateral] = (int)(p[lateral] - MathF.Sin(.63f) * 1.75f);
                int index = (sensor[0] * f.Y + sensor[1]) * f.Z + sensor[2];
                valid[index >> 5] &= ~(1u << (index & 31));
                Set(input, "VoxelFlags", valid);
            }
            if (scenario == "slime")
            {
                Set(input, "HasSlimeParticles", true); Set(input, "HasAntParticles", false);
                Set(input, "GroupData1", new[] { MathF.PI / 2, 0f, .15f, 17f });
                Set(input, "VoxelDensity", scent);
            }
            Set(settings, "AntSlime", 0d); Set(settings, "SlimeAntFood", 0d); Set(settings, "SlimeAntBase", 0d);
            Set(settings, "Decay", 0d);
            object dimension = RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution", Static)!
                .Invoke(null, new object[] { f.X, f.Y, f.Z })!;
            using var engine = (IDisposable)FoodEngine(input, settings);
            Invoke(engine, "Step", settings, dimension, 2, false, false, false);
            var state = Snapshot(engine);
            float[] direction = Enumerable.Range(0, 3).Select(i => BitConverter.ToSingle(state["particleDirectionReadback"], i * 4)).ToArray();
            if (!baseline && scenario == "gradient")
                Assert(direction[lateral] > .8f, $"Food following failed for {shape}/{mixed}: {string.Join(",", direction)}");
            float[]? lostDirection = null;
            if (scenario == "gradient")
            {
                // Remove scent without resetting the ant or its launch state.
                Invoke(engine, "ResetFloatFieldPair", new float[n], Get<object>(engine, "antFoodA"),
                    Get<object>(engine, "antFoodAView"), Get<object>(engine, "antFoodB"), Get<object>(engine, "antFoodBView"));
                Invoke(engine, "Step", settings, dimension, 3, false, false, false);
                state = Snapshot(engine);
                lostDirection = Enumerable.Range(0, 3).Select(i => BitConverter.ToSingle(state["particleDirectionReadback"], i * 4)).ToArray();
                if (!baseline) Assert(lostDirection[forward] > .5f, "Exploration did not resume after scent loss.");
            }
            results.Add(new { Shape = shape.ToString(), Mixed = mixed, Scenario = scenario, Direction = direction, LostDirection = lostDirection });
        }
        string? json = Option(args, "--json");
        if (json != null) File.WriteAllText(json, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {results.Count} food-following cases; 2D/3D, generic/specialized kernels, gradient acquisition and loss, absent/flat/trace scent and carrying ants. Baseline={baseline}.");
    }
}
