internal static partial class Program
{
    static void TestParticleDensityBoundaryFlags()
    {
        foreach (int[] resolution in new[]
        {
            new[] { 5, 5, 1 }, new[] { 5, 1, 5 }, new[] { 1, 5, 5 },
            new[] { 5, 5, 5 }, new[] { 1, 5, 1 }
        })
        {
            object data = CreateFullDomain(resolution[0], resolution[1], resolution[2]);
            int count = Field<int>(data, "Count");
            List<double> limits = Enumerable.Range(0, count)
                .Select(index => index % 2 == 0 ? 1.0 : 2.0).ToList();
            object mappedData = Invoke(data, "WithScalarValues", 1, limits);

            foreach (bool wrap in new[] { false, true })
            {
                object snapshot = CaptureVoxelSnapshot(CreateField(data), wrap);
                object mappedSnapshot = CaptureVoxelSnapshot(CreateField(mappedData), wrap);
                uint[] flags = Field<uint[]>(snapshot, "VoxelFlags");
                uint[] mappedFlags = Field<uint[]>(mappedSnapshot, "VoxelFlags");
                object field = Field<object>(snapshot, "Field");
                if (wrap) Null(flags, "wrapped full domain should retain implicit walkability");
                else True(flags != null, "reflective boundaries need explicit GPU walkability flags");

                for (int index = 0; index < count; index++)
                {
                    bool expected = (bool)Invoke(field, "IsSolverWalkableFlatIndex", index);
                    bool actual = flags == null || (flags[index >> 5] & (1u << (index & 31))) != 0;
                    bool mapped = mappedFlags == null || (mappedFlags[index >> 5] & (1u << (index & 31))) != 0;
                    Equal(expected, actual, "effective boundary walkability at " + index);
                    Equal(actual, mapped, "authored-map allocation changed boundary walkability at " + index);
                }
            }
        }

        object blockedData = Invoke(CreateFullDomain(5, 5, 1), "WithScalarValues", 1, new List<double> { 0.0 });
        foreach (bool wrap in new[] { false, true })
        {
            uint[] flags = Field<uint[]>(CaptureVoxelSnapshot(CreateField(blockedData), wrap), "VoxelFlags");
            True(flags != null && flags.All(word => word == 0u), "uniform zero maximum density allowed GPU occupancy");
        }
        Console.WriteLine("Effective maximum-density GPU boundary flags passed.");
    }

    static void TestGpuParticleDensityBoundaries()
    {
        const int grid = 5;
        object data = CreateFullDomain(grid, grid, 1);
        object snapshot = CaptureVoxelSnapshot(CreateField(data), false);
        ConfigureSingleGpuParticleSnapshot(snapshot, 3.75f, 2.5f, 0.5f, 17, 0.5f);
        object settings = CreateParityGpuSettings(RequiredImplementationType("Nuclei4.SolverGpuSettings"));
        SetField(settings, "WrapBoundaries", false);
        object dimensionMode = InvokeStatic(RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"),
            "FromResolution", grid, grid, 1);
        object engine = CreateGpuEngine(RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine"),
            snapshot, settings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            AssertGpuParticleInWalkableVoxel(engine, Field<object>(snapshot, "Field"), grid,
                "movement entered a zero-density reflective boundary");

            Invoke(engine, "FastReset", snapshot, settings);
            object wrappedSnapshot = CaptureVoxelSnapshot(CreateField(data), true);
            True((bool)Invoke(engine, "UpdateVoxelBehaviorFields", wrappedSnapshot), "wrap-on field update failed");
            SetField(settings, "WrapBoundaries", true);
            InvokeGpuStep(engine, wrappedSnapshot, settings, dimensionMode, 2);
            AssertGpuParticleInWalkableVoxel(engine, Field<object>(wrappedSnapshot, "Field"), grid,
                "wrap-on movement did not preserve walkability");
            Near(4.25, ReadBackParticleX(engine), 1e-5, "wrap-on retained obsolete reflective boundary flags");

            True((bool)Invoke(engine, "UpdateVoxelBehaviorFields", snapshot), "wrap-off field update failed");
            SetField(settings, "WrapBoundaries", false);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 3);
            AssertGpuParticleInWalkableVoxel(engine, Field<object>(snapshot, "Field"), grid,
                "wrap-off movement remained in a zero-density reflective boundary");
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }
        Console.WriteLine("GPU movement and wrap toggles respect zero-density boundaries.");
    }

    static void AssertGpuParticleInWalkableVoxel(object engine, object field, int grid, string message)
    {
        Invoke(engine, "ReadBackParticles");
        float[] positions = Field<float[]>(engine, "particlePositionReadback");
        True(positions[3] >= 0, message + ": particle was lost");
        int x = (int)Math.Floor(positions[0]);
        int y = (int)Math.Floor(positions[1]);
        True(x >= 0 && x < grid && y >= 0 && y < grid, message + ": position outside domain");
        int index = x * grid + y;
        True((bool)Invoke(field, "IsSolverWalkableFlatIndex", index), message);
        float[] directions = Field<float[]>(engine, "particleDirectionReadback");
        Equal(index, BitConverter.SingleToInt32Bits(directions[3]), message + ": parent disagreed with position");
    }
}
