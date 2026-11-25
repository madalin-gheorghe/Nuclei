using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// A headless, real D3D11 regression for exact parent indices. It deliberately
// crosses the float32 consecutive-integer limit with a tiny particle workload.
// No Rhino process, Grasshopper solution or native preview is required.
internal static class Program
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const int Grid = 350;
    const float Speed = 1.3f;
    static readonly int[] Seeds = { 1, 1025, 8_388_609, 16_777_195, 16_777_215, 16_777_300, 33_554_433, 40_000_001 };
    static readonly List<Assembly> Assemblies = new();
    static bool EncodedParents;

    static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(Option(args, "--nuclei-output")
                ?? "Nuclei-v4/Nuclei4/bin/Release/net7.0-windows");
            Load(directory);
            EncodedParents = Assemblies.Any(assembly => assembly.GetType("Nuclei4.GpuVoxelIndexEncoding") != null);
            bool expectFreeze = args.Contains("--expect-freeze");
            string selection = Option(args, "--selection") ?? "both";
            if (selection is not ("full" or "selected" or "both"))
                throw new ArgumentException("--selection must be full, selected, or both.");
            int steps = int.Parse(Option(args, "--steps") ?? "4");
            if (steps < 1 || steps > 8) throw new ArgumentException("--steps must be between 1 and 8.");
            Console.WriteLine($"Assembly: {directory}");
            Console.WriteLine($"Parent payload: {(EncodedParents ? "exact int32 bits" : "legacy numeric float32")}");
            bool reproduced = false;
            foreach (bool selected in selection == "both" ? new[] { false, true } : new[] { selection == "selected" })
            {
                reproduced |= Run(selected, steps, expectFreeze);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            if (expectFreeze && !reproduced)
                throw new InvalidOperationException("The legacy high-index freeze did not reproduce.");
            Console.WriteLine(expectFreeze ? "PASS: legacy high-index freeze reproduced." : "PASS: all low/high-index particles move and retain exact parents; inactive sentinel remains intact.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    static bool Run(bool selected, int steps, bool expectFreeze)
    {
        object input = Create("Nuclei4.GpuSolverInput");
        int slots = Seeds.Length + 1; // Last slot is deliberately inactive.
        float[] positions = new float[slots * 3];
        float[] directions = new float[slots * 3];
        float[] axes = new float[slots * 3];
        int[] parents = new int[slots];
        int[] groups = new int[slots];
        uint[]? flags = selected ? new uint[(Grid * Grid * Grid + 31) / 32] : null;
        for (int i = 0; i < Seeds.Length; i++)
        {
            Coordinates(Seeds[i], out int x, out int y, out int z);
            positions[i * 3] = x + 0.25f;
            positions[i * 3 + 1] = y + 0.25f;
            positions[i * 3 + 2] = z + 0.25f;
            directions[i * 3 + 2] = 1;
            axes[i * 3 + 1] = 1;
            parents[i] = Seeds[i];
            if (flags != null)
            {
                for (int xx = Math.Max(0, x - 1); xx <= Math.Min(Grid - 1, x + 1); xx++)
                for (int yy = Math.Max(0, y - 1); yy <= Math.Min(Grid - 1, y + 1); yy++)
                for (int zz = Math.Max(0, z - 1); zz <= Math.Min(Grid - 1, z + 14); zz++)
                {
                    int index = xx * Grid * Grid + yy * Grid + zz;
                    flags[index >> 5] |= 1u << (index & 31);
                }
            }
        }
        parents[^1] = -1;
        groups[^1] = -1;
        Set(input, "ResX", Grid); Set(input, "ResY", Grid); Set(input, "ResZ", Grid);
        Set(input, "VoxelSize", 1f);
        Set(input, "ParticleCount", slots); Set(input, "GroupCount", 1);
        Set(input, "HasSlimeParticles", true);
        Set(input, "HasAntParticles", false);
        Set(input, "ParticlePositionsXyz", positions);
        Set(input, "ParticleDirectionsXyz", directions);
        Set(input, "ParticleYAxesXyz", axes);
        Set(input, "ParticleHomesXyz", positions.Clone());
        Set(input, "ParticleParentIndices", parents);
        Set(input, "ParticleGroupIndices", groups);
        Set(input, "ParticleAntStates", new uint[slots]);
        Set(input, "ParticleAntLaunchBoundaryStates", new uint[slots]);
        Set(input, "ParticleAges", new int[slots]);
        Set(input, "GroupData0", new[] { Speed, 0f, 0f, 0f });
        Set(input, "GroupData1", new float[4]);
        Set(input, "GroupColorData", new float[4]);
        Set(input, "ActiveVoxelFlags", flags);
        Set(input, "VoxelFlags", flags);

        object settings = Create("Nuclei4.SolverGpuSettings");
        Set(settings, "WrapBoundaries", true);
        Set(settings, "Diffuse", 0d); Set(settings, "Decay", 0d);
        Set(settings, "DiffuseRange", 0);
        Set(settings, "MaximumPopulation", slots);
        object dimension = RequiredType("Nuclei4.SolverGpuDimensionMode")
            .GetMethod("FromResolution", Static)!.Invoke(null, new object[] { Grid, Grid, Grid })!;
        object sink = RuntimeHelpers.GetUninitializedObject(RequiredType("Nuclei4.Gh1GpuSolverOutputSink"));
        Type engineType = RequiredType("Nuclei4.GpuFullSlimeSolverEngine");
        Stopwatch watch = Stopwatch.StartNew();
        object engine = engineType.GetConstructors(Instance).Single(ctor => ctor.GetParameters().Length == 8)
            .Invoke(new[] { input, sink, settings, false, false, false, 0, 1 });
        try
        {
            object capabilities = engineType.GetProperty("Capabilities")!.GetValue(engine)!;
            bool software = (bool)capabilities.GetType().GetField("SoftwareFallback")!.GetValue(capabilities)!;
            if (software) throw new InvalidOperationException("Hardware GPU required; D3D11 selected WARP software fallback.");
            Console.WriteLine($"350³ {(selected ? "selected corridors" : "full domain")}: {Seeds.Length} live seeds + inactive slot; initialized {watch.Elapsed.TotalMilliseconds:F0} ms");
            bool highFailure = false;
            int mismatches = 0;
            Invoke(engine, "ReadBackParticles");
            Validate(engine, positions, 0, expectFreeze, ref highFailure, ref mismatches);
            watch.Restart();
            for (int step = 1; step <= steps; step++)
            {
                Invoke(engine, "Step", settings, dimension, step + 1, false, false, false);
                Invoke(engine, "ReadBackParticles");
                Validate(engine, positions, step, expectFreeze, ref highFailure, ref mismatches);
            }
            Console.WriteLine($"  {steps} steps incl. particle readback: {watch.Elapsed.TotalMilliseconds:F0} ms; high-index mismatches: {mismatches}");
            ValidateOwnership(engine);
            return highFailure;
        }
        finally { ((IDisposable)engine).Dispose(); }
    }

    static void ValidateOwnership(object engine)
    {
        int[] owners = (int[])Invoke(engine, "ReadBackValidationParticleOwners")!;
        float[] positions = Get<float[]>(engine, "particlePositionReadback");
        for (int i = 0; i < Seeds.Length; i++)
        {
            int offset = i * 4;
            int parent = (int)Math.Floor(positions[offset]) * Grid * Grid
                + (int)Math.Floor(positions[offset + 1]) * Grid + (int)Math.Floor(positions[offset + 2]);
            Assert(owners[parent] == i, $"Seed {Seeds[i]} lost ownership of its current voxel.");
            if (parent != Seeds[i]) Assert(owners[Seeds[i]] == -1, $"Seed {Seeds[i]} left its original voxel occupied.");
        }
    }

    static void Validate(object engine, float[] origins, int step, bool expectFreeze, ref bool highFailure, ref int mismatches)
    {
        float[] positions = Get<float[]>(engine, "particlePositionReadback");
        float[] directions = Get<float[]>(engine, "particleDirectionReadback");
        Assert(Get<int>(engine, "particleCount") == Seeds.Length, "Live population changed.");
        for (int i = 0; i < Seeds.Length; i++)
        {
            int offset = i * 4;
            Assert(positions[offset + 3] >= 0, $"Seed {Seeds[i]} was deactivated.");
            float expectedZ = origins[i * 3 + 2];
            for (int s = 0; s < step; s++) expectedZ += Speed;
            bool moved = Math.Abs(positions[offset] - origins[i * 3]) < 0.0001f
                && Math.Abs(positions[offset + 1] - origins[i * 3 + 1]) < 0.0001f
                && Math.Abs(positions[offset + 2] - expectedZ) < 0.0001f;
            int positionParent = (int)Math.Floor(positions[offset]) * Grid * Grid
                + (int)Math.Floor(positions[offset + 1]) * Grid + (int)Math.Floor(positions[offset + 2]);
            int storedParent = Decode(directions[offset + 3]);
            bool valid = moved && positionParent == storedParent;
            if (!valid)
            {
                string message = $"step={step} seed={Seeds[i]} z={positions[offset + 2]:F4} expectedZ={expectedZ:F4} parent={storedParent} positionParent={positionParent}";
                int expectedParent = (int)Math.Floor(origins[i * 3]) * Grid * Grid
                    + (int)Math.Floor(origins[i * 3 + 1]) * Grid + (int)Math.Floor(expectedZ);
                if (!expectFreeze || expectedParent < 16_777_216) throw new InvalidOperationException(message);
                Console.WriteLine("  LEGACY FAILURE " + message);
                mismatches++;
                highFailure = true;
            }
        }
        int inactive = Seeds.Length * 4;
        Assert(positions[inactive + 3] < 0, "Inactive slot became live.");
        Assert(Decode(directions[inactive + 3]) == -1, "Inactive parent sentinel changed.");
    }

    static int Decode(float value) => EncodedParents ? BitConverter.SingleToInt32Bits(value) : (int)Math.Round(value);
    static void Coordinates(int index, out int x, out int y, out int z)
    { x = index / (Grid * Grid); y = index / Grid % Grid; z = index % Grid; }
    static object Create(string name) => Activator.CreateInstance(RequiredType(name), nonPublic: true)!;
    static Type RequiredType(string name) => Assemblies.Select(a => a.GetType(name)).FirstOrDefault(t => t != null)
        ?? throw new TypeLoadException(name);
    static void Set(object target, string name, object? value) => target.GetType().GetField(name, Instance)!.SetValue(target, value);
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance)!.GetValue(target)!;
    static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethods(Instance)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
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
