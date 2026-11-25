using System.Runtime.CompilerServices;

internal static partial class Program
{
    static object FoodEngine(object input, object settings) => RequiredType("Nuclei4.GpuFullSlimeSolverEngine")
        .GetConstructors(Instance).Single(c => c.GetParameters().Length == 8)
        .Invoke(new[] { input, RuntimeHelpers.GetUninitializedObject(RequiredType("Nuclei4.Gh1GpuSolverOutputSink")), settings, false, false, false, 0, 1 });

    static void RunFoodSourceEmission()
    {
        int cases = 0;
        foreach (var shape in new[] { (9, 9, 9), (9, 9, 1), (9, 1, 9), (1, 9, 9) })
        foreach (bool mixed in new[] { false, true })
        foreach (bool wrap in new[] { false, true })
        foreach (double diffuse in new[] { 0d, .2d })
        {
            var f = new Fixture("food-emission", shape.Item1, shape.Item2, shape.Item3, 2, wrap, diffuse, .31,
                .031, .073, Masks: false, Limits: false, Particles: false);
            int n = f.X * f.Y * f.Z, center = ((f.X / 2) * f.Y + f.Y / 2) * f.Z + f.Z / 2;
            object settings = CreateSettings(f);
            // Isolate emission from unrelated species coupling.
            Set(settings, "AntSlime", 0d); Set(settings, "SlimeAntFood", 0d); Set(settings, "SlimeAntBase", 0d);
            object dimension = RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution", Static)!
                .Invoke(null, new object[] { f.X, f.Y, f.Z })!;
            object Input(bool reference)
            {
                object input = CreateInput(f);
                Set(input, "HasSlimeParticles", mixed);
                float[] food = new float[n], slime = new float[n], scent = new float[n], density = new float[n];
                food[center] = reference ? 0 : 3.25f;
                // An overlapping source must project each channel only once.
                slime[center] = reference ? 0 : 1.5f;
                if (reference) { scent[center] = 3.25f; if (mixed) density[center] = 1.5f; }
                Set(input, "InitialAntFood", food); Set(input, "InitialFood", slime);
                Set(input, "AntFoodPheromone", scent); Set(input, "AntBasePheromone", new float[n]);
                Set(input, "VoxelDensity", density);
                return input;
            }
            object input = Input(false), expectedInput = Input(true);
            using var actual = (IDisposable)FoodEngine(input, settings);
            using var expected = (IDisposable)FoodEngine(expectedInput, settings);
            Assert(Get<int>(actual, "foodSourceCount") == 1, "Overlapping food sources were not deduplicated.");
            var initial = Snapshot(actual);
            Invoke(actual, "Step", settings, dimension, 0, false, false, false);
            Invoke(expected, "Step", settings, dimension, 0, false, false, false);
            var a = Snapshot(actual); var b = Snapshot(expected);
            foreach (string field in new[] { "antFoodReadback", "antBaseReadback", "densityReadback" })
                Assert(a[field].AsSpan().SequenceEqual(b[field]), $"Emission/settings mismatch {shape}/{mixed}/{wrap}/{diffuse}/{field}.");
            Assert(initial["antFoodRemainingReadback"].AsSpan().SequenceEqual(a["antFoodRemainingReadback"]), "Emission consumed or moved food.");
            if (diffuse > 0)
                Assert(Enumerable.Range(0, n).Any(i => i != center && BitConverter.ToSingle(a["antFoodReadback"], i * 4) > 0), "Scent did not spread.");
            // Simulate exhausted food in its GPU channel; the source index stays
            // cached, but must no longer inject anything. Both engines now decay
            // and diffuse the identical existing fields for three more steps.
            Invoke(actual, "UploadFoodChannel", new float[n], Get<int>(actual, "foodRemainingOffset"));
            Invoke(actual, "UploadFoodChannel", new float[n], Get<int>(actual, "foodSourceOffset"));
            for (int step = 1; step <= 3; step++)
            {
                Invoke(actual, "Step", settings, dimension, step, false, false, false);
                Invoke(expected, "Step", settings, dimension, step, false, false, false);
                a = Snapshot(actual); b = Snapshot(expected);
                foreach (string field in new[] { "antFoodReadback", "antBaseReadback", "densityReadback" })
                    Assert(a[field].AsSpan().SequenceEqual(b[field]), "Exhausted food continued emitting: " + field);
            }
            // Reset must rebuild cached locations, including an empty map.
            float[] moved = new float[n]; moved[center + 1] = 2f;
            Set(input, "InitialAntFood", moved); Set(input, "InitialFood", new float[n]);
            Invoke(actual, "FastReset", input, settings);
            Assert(Get<int>(actual, "foodSourceCount") == 1, "Reset lost relocated source.");
            Invoke(actual, "DispatchFoodSourceProjection", settings, dimension, 0);
            a = Snapshot(actual);
            Assert(BitConverter.ToSingle(a["antFoodReadback"], center * 4) == 0
                && BitConverter.ToSingle(a["antFoodReadback"], (center + 1) * 4) == 2f, "Reset emitted from stale source location.");
            Set(input, "InitialAntFood", new float[n]);
            Invoke(actual, "FastReset", input, settings);
            Assert(Get<int>(actual, "foodSourceCount") == 0, "Empty source map retained projection work.");
            cases++;
        }
        Console.WriteLine($"PASS: {cases} food-emission cases; 2D/3D, fixed/wrapped boundaries, independent field settings, mixed overlapping sources, fixed edible quantities, depletion and reset.");
    }
}
