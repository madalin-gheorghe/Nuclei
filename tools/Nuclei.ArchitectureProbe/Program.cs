using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    static Assembly NucleiAssembly;
    static Type GridType;
    static Type FieldType;
    static Type SnapshotType;

    static int Main(string[] args)
    {
        try
        {
            LoadNuclei();
            TestLargeEmptyField();
            TestAdaptiveSelections();
            TestScalarMapsAndBlockedThreshold();
            TestVectorPacking();
            TestBooleanFieldMerges();
            TestGpuSnapshotPacking();
            TestSolverDynamicStateIsolation();
            TestScatteredParticlePlacement();
            TestAdaptiveVolumePreviewLayout();
            if (Array.IndexOf(args, "--gpu") >= 0)
            {
                TestGpuEngineInitialization();
                TestGpuWrapTransitions();
            }
            Console.WriteLine("Architecture probe passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    static void TestLargeEmptyField()
    {
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        object data = CreateFullDomain(300, 300, 300);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Equal(27_000_000, Field<int>(data, "Count"), "300^3 cell count");
        Equal(27_000_000, Field<int>(data, "ActiveCount"), "300^3 active count");
        True(allocated < 2 * 1024 * 1024, "Constructing an empty 300^3 field allocated dense storage.");

        GC.Collect();
        before = GC.GetAllocatedBytesForCurrentThread();
        object snapshot = CaptureVoxelSnapshot(CreateField(data));
        long snapshotAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        True(snapshotAllocated < 2 * 1024 * 1024, "Capturing an empty 300^3 field allocated dense CPU channels.");
        Null(Field<object>(snapshot, "VoxelDensity"), "empty 300^3 density should remain implicit");
        Null(Field<object>(snapshot, "VoxelBehaviorData"), "empty 300^3 behavior should remain implicit");
        Console.WriteLine("300^3 empty field allocation: " + (allocated / 1024.0).ToString("0.0") + " KiB");
        Console.WriteLine("300^3 empty GPU snapshot allocation: " + (snapshotAllocated / 1024.0).ToString("0.0") + " KiB");
    }

    static void TestAdaptiveSelections()
    {
        object baseData = CreateFullDomain(100, 50, 10);
        int count = Field<int>(baseData, "Count");

        bool[] denseMask = new bool[count];
        List<int> denseExpected = new List<int>();
        for (int i = 0; i < count; i++)
        {
            denseMask[i] = i % 7 != 0;
            if (denseMask[i]) denseExpected.Add(i);
        }
        object dense = Invoke(baseData, "WithActiveMask", denseMask);
        VerifySelection(dense, denseMask, denseExpected, "dense selection");

        bool[] sparseMask = new bool[count];
        int[] sparseExpected = { 3, 999, 23_456, 49_999 };
        foreach (int index in sparseExpected) sparseMask[index] = true;
        object sparse = Invoke(baseData, "WithActiveMask", sparseMask);
        VerifySelection(sparse, sparseMask, sparseExpected, "sparse selection");

        Type builderType = RequiredType("Nuclei4.VoxelSelectionBuilder");
        object differenceBuilder = Activator.CreateInstance(builderType, count)!;
        Invoke(differenceBuilder, "UnionWith", dense);
        Invoke(differenceBuilder, "ExceptWith", sparse);
        object difference = Invoke(differenceBuilder, "ApplyTo", baseData);
        bool[] differenceMask = (bool[])denseMask.Clone();
        foreach (int index in sparseExpected) differenceMask[index] = false;
        List<int> differenceExpected = new List<int>();
        for (int i = 0; i < count; i++) if (differenceMask[i]) differenceExpected.Add(i);
        VerifySelection(difference, differenceMask, differenceExpected, "packed difference");

        object intersectionBuilder = Activator.CreateInstance(builderType, count)!;
        Invoke(intersectionBuilder, "Fill");
        Invoke(intersectionBuilder, "IntersectWith", dense);
        Invoke(intersectionBuilder, "IntersectWith", sparse);
        object intersection = Invoke(intersectionBuilder, "ApplyTo", baseData);
        bool[] intersectionMask = new bool[count];
        List<int> intersectionExpected = new List<int>();
        foreach (int index in sparseExpected)
        {
            if (!denseMask[index]) continue;
            intersectionMask[index] = true;
            intersectionExpected.Add(index);
        }
        VerifySelection(intersection, intersectionMask, intersectionExpected, "packed intersection");
    }

    static void TestScalarMapsAndBlockedThreshold()
    {
        object data = CreateFullDomain(16, 8, 2);
        int count = Field<int>(data, "Count");
        List<double> speed = new List<double>(count);
        for (int i = 0; i < count; i++) speed.Add(i / 17.0);
        object withSpeed = Invoke(data, "WithScalarValues", 2, speed);
        for (int i = 0; i < count; i++)
        {
            double actual = (double)Invoke(withSpeed, "GetScalarValue", 2, i);
            Near(speed[i], actual, 1e-5, "speed map value " + i);
        }

        List<double> uniformSpeed = new List<double>(count);
        for (int i = 0; i < count; i++) uniformSpeed.Add(1.25);
        object withUniformSpeed = Invoke(data, "WithScalarValues", 2, uniformSpeed);
        object uniformSpeedMap = Field<object>(withUniformSpeed, "Speed");
        Null(Field<object>(uniformSpeedMap, "Values"), "uniform full scalar list should not allocate a map");
        Near(1.25, Field<double>(uniformSpeedMap, "DefaultValue"), 1e-6, "uniform full scalar default");

        object blocked = Invoke(withSpeed, "WithScalarValues", 1, new List<double> { 0.005 });
        for (int i = 0; i < count; i++)
        {
            False((bool)Invoke(blocked, "IsWalkableFlatIndex", i), "blocked threshold at " + i);
        }

        object walkable = Invoke(withSpeed, "WithScalarValues", 1, new List<double> { 0.01 });
        for (int i = 0; i < count; i++)
        {
            True((bool)Invoke(walkable, "IsWalkableFlatIndex", i), "walkable threshold at " + i);
        }
    }

    static void TestGpuSnapshotPacking()
    {
        object data = CreateFullDomain(33, 3, 1);

        object emptySnapshot = CaptureVoxelSnapshot(CreateField(data));
        Null(Field<object>(emptySnapshot, "VoxelDensity"), "zero initial density should be implicit");
        Null(Field<object>(emptySnapshot, "VoxelBehaviorData"), "default behavior should be implicit");
        Null(Field<object>(emptySnapshot, "VoxelFlags"), "full walkable field should have implicit flags");

        object speedData = Invoke(data, "WithScalarValues", 2, new List<double> { 1.75 });
        object speedSnapshot = CaptureVoxelSnapshot(CreateField(speedData));
        Null(Field<object>(speedSnapshot, "VoxelBehaviorData"), "uniform speed should remain a scalar");
        Equal(-1, Field<int>(speedSnapshot, "SpeedOffset"), "uniform speed channel offset");
        Near(1.75, Field<float>(speedSnapshot, "SpeedDefault"), 1e-6, "uniform speed default");
        Equal(-1, Field<int>(speedSnapshot, "SensorDistanceOffset"), "missing sensor-distance channel");

        int count = Field<int>(data, "Count");
        List<double> varyingSpeed = new List<double>(count);
        for (int i = 0; i < count; i++) varyingSpeed.Add(1.0 + i / 100.0);
        object varyingSpeedData = Invoke(data, "WithScalarValues", 2, varyingSpeed);
        object varyingSpeedSnapshot = CaptureVoxelSnapshot(CreateField(varyingSpeedData));
        float[] behavior = Field<float[]>(varyingSpeedSnapshot, "VoxelBehaviorData");
        Equal(count, behavior.Length, "varying behavior channel length");
        Equal(0, Field<int>(varyingSpeedSnapshot, "SpeedOffset"), "varying speed channel offset");
        Near(varyingSpeed[count - 1], behavior[count - 1], 1e-6, "packed varying speed value");

        List<double> maximumDensity = new List<double>(count);
        for (int i = 0; i < count; i++) maximumDensity.Add((i % 3) == 0 ? 0.005 : 1.0);
        object limitsData = Invoke(data, "WithScalarValues", 1, maximumDensity);
        object limitSnapshot = CaptureVoxelSnapshot(CreateField(limitsData));
        uint[] flags = Field<uint[]>(limitSnapshot, "VoxelFlags");
        Equal((count + 31) / 32, flags.Length, "packed flag word count");
        for (int i = 0; i < count; i++)
        {
            bool set = (flags[i >> 5] & (1u << (i & 31))) != 0;
            Equal((i % 3) != 0, set, "packed walkability flag " + i);
        }
        float[] limits = Field<float[]>(limitSnapshot, "VoxelDensityLimits");
        Equal(count, limits.Length, "single density-limit channel length");
        Equal(0, Field<int>(limitSnapshot, "MaximumDensityOffset"), "maximum-density channel offset");
        Equal(-1, Field<int>(limitSnapshot, "MinimumDensityOffset"), "missing minimum-density channel");
    }

    static void TestSolverDynamicStateIsolation()
    {
        float[] initialDensity = new float[12];
        initialDensity[5] = 0.25f;
        object inputField = CreateField(WithInitialDensity(CreateFullDomain(4, 3, 1), initialDensity));
        float[] staleRuntimeDensity = new float[12];
        staleRuntimeDensity[5] = 0.9f;
        Invoke(inputField, "UpdateDynamicFields", staleRuntimeDensity, null, null, null);

        object firstSnapshot = CaptureVoxelSnapshot(inputField);
        object solverField = Field<object>(firstSnapshot, "Field");
        False(ReferenceEquals(inputField, solverField), "GPU solver output must not alias its reset input");
        Near(0.25, (double)Invoke(solverField, "GetScalarValue", 7, 5), 1e-6, "solver reset field initial density");
        Near(0.25, Field<float[]>(firstSnapshot, "VoxelDensity")[5], 1e-6, "reset snapshot ignores evolved density");

        float[] evolvedDensity = new float[12];
        evolvedDensity[5] = 0.7f;
        Invoke(solverField, "UpdateDynamicFields", evolvedDensity, null, null, null);
        Near(0.9, (double)Invoke(inputField, "GetScalarValue", 7, 5), 1e-6, "solver evolution changed its reset input");

        object resetSnapshot = CaptureVoxelSnapshot(inputField);
        Near(0.25, Field<float[]>(resetSnapshot, "VoxelDensity")[5], 1e-6, "reset snapshot density");
    }

    static void TestVectorPacking()
    {
        object data = CreateFullDomain(12, 4, 1);
        int count = Field<int>(data, "Count");
        Type vectorType = Type.GetType("Rhino.Geometry.Vector3d, RhinoCommon", throwOnError: true)!;
        IList uniformVectors = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vectorType))!;
        uniformVectors.Add(Activator.CreateInstance(vectorType, 1.0, 2.0, 3.0));
        IList frequencies = new List<int> { 5 };

        object withUniformVector = Invoke(data, "WithVectorValues", uniformVectors, frequencies);
        Null(Field<object>(withUniformVector, "VectorData"), "uniform vector should not allocate an XYZ map");
        Near(1, Field<float>(withUniformVector, "VectorDefaultX"), 1e-6, "uniform vector X");
        Near(2, Field<float>(withUniformVector, "VectorDefaultY"), 1e-6, "uniform vector Y");
        Near(0, Field<float>(withUniformVector, "VectorDefaultZ"), 1e-6, "planar uniform vector Z");
        object frequencyMap = Field<object>(withUniformVector, "VectorFrequency");
        Equal(5, Field<int>(frequencyMap, "DefaultValue"), "uniform vector frequency");
        Null(Field<object>(frequencyMap, "Values"), "uniform vector frequency should not allocate a map");

        object uniformSnapshot = CaptureVoxelSnapshot(CreateField(withUniformVector));
        Null(Field<object>(uniformSnapshot, "VoxelVectorData"), "uniform GPU vector should be implicit");
        Near(1, Field<float>(uniformSnapshot, "VoxelVectorDefaultX"), 1e-6, "GPU uniform vector X");
        Near(2, Field<float>(uniformSnapshot, "VoxelVectorDefaultY"), 1e-6, "GPU uniform vector Y");
        Near(0, Field<float>(uniformSnapshot, "VoxelVectorDefaultZ"), 1e-6, "GPU planar uniform vector Z");
        Equal(5, Field<int>(uniformSnapshot, "VoxelVectorDefaultFrequency"), "GPU default vector frequency");
        Null(Field<object>(uniformSnapshot, "VoxelVectorFrequencies"), "uniform GPU vector frequency should be implicit");

        IList varyingVectors = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vectorType))!;
        for (int i = 0; i < count; i++)
        {
            varyingVectors.Add(Activator.CreateInstance(vectorType, (double)i, (double)(i + 1), 9.0));
        }

        object withVaryingVectors = Invoke(data, "WithVectorValues", varyingVectors, frequencies);
        float[] packed = Field<float[]>(withVaryingVectors, "VectorData");
        Equal(count * 3, packed.Length, "varying packed XYZ vector length");
        Near(count - 1, packed[(count - 1) * 3], 1e-6, "varying packed final X");
        Near(count, packed[(count - 1) * 3 + 1], 1e-6, "varying packed final Y");
        Near(0, packed[(count - 1) * 3 + 2], 1e-6, "varying packed planar Z");

        object varyingSnapshot = CaptureVoxelSnapshot(CreateField(withVaryingVectors));
        Equal(count * 3, Field<float[]>(varyingSnapshot, "VoxelVectorData").Length, "GPU varying XYZ vector length");
        Near(0, Field<float>(varyingSnapshot, "VoxelVectorDefaultX"), 1e-6, "GPU varying default X");
    }

    static void TestBooleanFieldMerges()
    {
        object first = CreateFullDomain(4, 1, 1);
        first = Invoke(first, "WithScalarValues", 2, new List<double> { 1, -1, 3, 5 });
        object second = CreateFullDomain(4, 1, 1);
        second = Invoke(second, "WithScalarValues", 2, new List<double> { 2, 4, 6, 8 });
        second = Invoke(second, "WithActiveMask", new[] { true, true, false, true });

        Type combinerType = RequiredType("Nuclei4.VoxelGridCombiner");
        Type modeType = RequiredType("Nuclei4.VoxelGridMergeMode");
        object average = Enum.Parse(modeType, "Average");
        IList inputs = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(GridType))!;
        inputs.Add(first);
        inputs.Add(second);

        object union = InvokeStatic(combinerType, "Union", inputs, average);
        Equal(4, Field<int>(union, "ActiveCount"), "union active count");
        VerifyMergedSpeed(union, new[] { 1.5, 4.0, 3.0, 6.5 }, "union");

        object intersection = InvokeStatic(combinerType, "Intersection", inputs, average);
        Equal(3, Field<int>(intersection, "ActiveCount"), "intersection active count");
        Equal(0, (int)Invoke(intersection, "ActiveFlatIndexAt", 0), "intersection order 0");
        Equal(1, (int)Invoke(intersection, "ActiveFlatIndexAt", 1), "intersection order 1");
        Equal(3, (int)Invoke(intersection, "ActiveFlatIndexAt", 2), "intersection order 2");
        VerifyMergedSpeed(intersection, new[] { 1.5, 4.0, -1.0, 6.5 }, "intersection");

        object uniformFirst = Invoke(CreateFullDomain(4, 1, 1), "WithScalarValues", 2, new List<double> { 2.0 });
        object uniformSecond = Invoke(CreateFullDomain(4, 1, 1), "WithScalarValues", 2, new List<double> { 4.0 });
        IList uniformInputs = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(GridType))!;
        uniformInputs.Add(uniformFirst);
        uniformInputs.Add(uniformSecond);
        object uniformUnion = InvokeStatic(combinerType, "Union", uniformInputs, average);
        object uniformMergedSpeed = Field<object>(uniformUnion, "Speed");
        Null(Field<object>(uniformMergedSpeed, "Values"), "uniform boolean merge should remain implicit");
        Near(3, Field<double>(uniformMergedSpeed, "DefaultValue"), 1e-6, "uniform boolean merge value");
    }

    static void TestGpuEngineInitialization()
    {
        float[] initialDensity = new float[17 * 9 * 3];
        int densityIndex = 8 * 9 * 3 + 4 * 3 + 1;
        initialDensity[densityIndex] = 0.6f;
        object inputField = CreateField(WithInitialDensity(CreateFullDomain(17, 9, 3), initialDensity));
        object snapshot = CaptureVoxelSnapshot(inputField);
        Type particleGroupType = RequiredType("Nuclei4.ParticleGroup");
        SnapshotType.GetField("ParticleGroups")!.SetValue(snapshot, Array.CreateInstance(particleGroupType, 0));

        Type settingsType = RequiredType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        Type engineType = RequiredType("Nuclei4.GpuFullSlimeSolverEngine");
        ConstructorInfo constructor = engineType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(item => item.GetParameters().Length == 7);

        object engine = constructor.Invoke(new object[] { snapshot, settings, true, false, false, 0, 1 });
        try
        {
            Type dimensionType = RequiredType("Nuclei4.SolverGpuDimensionMode");
            object dimensionMode = InvokeStatic(dimensionType, "FromResolution", 17, 9, 3);
            Invoke(engine, "Step", Field<object>(snapshot, "Field"), null, settings, dimensionMode, 2, false, false, false);
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }

        float[] staleRuntimeDensity = new float[initialDensity.Length];
        staleRuntimeDensity[densityIndex] = 0.95f;
        Invoke(inputField, "UpdateDynamicFields", staleRuntimeDensity, null, null, null);
        object resetSnapshot = CaptureVoxelSnapshot(inputField);
        SnapshotType.GetField("ParticleGroups")!.SetValue(resetSnapshot, Array.CreateInstance(particleGroupType, 0));
        object resetEngine = constructor.Invoke(new object[] { resetSnapshot, settings, true, false, false, 0, 1 });
        try
        {
            Invoke(resetEngine, "ReadBackDensity");
            Near(0.6, Field<float[]>(resetEngine, "densityReadback")[densityIndex], 1e-6, "GPU density after hard reset");
            object frame = Invoke(resetEngine, "CreateDensityFieldPreviewFrame");
            True(frame != null, "Direct3D density preview frame was not created.");
            True((bool)frame.GetType().GetProperty("IsValid")!.GetValue(frame)!, "Direct3D density preview frame is invalid.");
        }
        finally
        {
            ((IDisposable)resetEngine).Dispose();
        }

        Console.WriteLine("Direct3D initialization, reset, and density preview passed.");
    }

    static void TestGpuWrapTransitions()
    {
        object snapshot = CaptureVoxelSnapshot(CreateField(CreateFullDomain(5, 5, 1)));
        Type groupType = RequiredType("Nuclei4.ParticleGroup");
        object group = Activator.CreateInstance(groupType)!;
        Array groups = Array.CreateInstance(groupType, 1);
        groups.SetValue(group, 0);
        SnapshotType.GetField("Particles")!.SetValue(snapshot, null);
        SnapshotType.GetField("ParticleGroups")!.SetValue(snapshot, groups);
        SnapshotType.GetField("ParticleCount")!.SetValue(snapshot, 1);
        SnapshotType.GetField("GroupCount")!.SetValue(snapshot, 1);
        SnapshotType.GetField("ParticlePositionsXyz")!.SetValue(snapshot, new[] { 0.1f, 2.5f, 0.5f });
        SnapshotType.GetField("ParticleDirectionsXyz")!.SetValue(snapshot, new[] { -1.0f, 0.0f, 0.0f });
        SnapshotType.GetField("ParticleYAxesXyz")!.SetValue(snapshot, new[] { 0.0f, 1.0f, 0.0f });
        SnapshotType.GetField("ParticleHomesXyz")!.SetValue(snapshot, new[] { 0.1f, 2.5f, 0.5f });
        SnapshotType.GetField("ParticleAntStates")!.SetValue(snapshot, new uint[1]);
        SnapshotType.GetField("ParticleGroupIndices")!.SetValue(snapshot, new[] { 0 });
        SnapshotType.GetField("ParticleParentIndices")!.SetValue(snapshot, new[] { 2 });
        SnapshotType.GetField("GroupData0")!.SetValue(snapshot, new[] { 0.25f, 1.0f, 0.0f, 0.0f });
        SnapshotType.GetField("GroupData1")!.SetValue(snapshot, new[] { 0.0f, 0.0f, 0.0f, 100.0f });
        SnapshotType.GetField("GroupColorData")!.SetValue(snapshot, new float[4]);

        Type settingsType = RequiredType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        FieldInfo wrapField = settingsType.GetField("WrapBoundaries")!;
        wrapField.SetValue(settings, false);
        Type dimensionType = RequiredType("Nuclei4.SolverGpuDimensionMode");
        object dimensionMode = InvokeStatic(dimensionType, "FromResolution", 5, 5, 1);
        Type engineType = RequiredType("Nuclei4.GpuFullSlimeSolverEngine");
        ConstructorInfo constructor = engineType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(item => item.GetParameters().Length == 7);

        object engine = constructor.Invoke(new object[] { snapshot, settings, false, false, false, 0, 1 });
        try
        {
            object solverField = Field<object>(snapshot, "Field");
            object particles = null;

            wrapField.SetValue(settings, true);
            Invoke(engine, "Step", solverField, particles, settings, dimensionMode, 2, false, false, false);
            Near(4.85, ReadBackParticleX(engine), 1e-4, "periodic wrap preserves movement overshoot");

            wrapField.SetValue(settings, false);
            Invoke(engine, "Step", solverField, particles, settings, dimensionMode, 1, false, false, false);
            double reconciledX = ReadBackParticleX(engine);
            True(reconciledX > 3.2 && reconciledX < 4.0, "live wrap-off position was not inset from the boundary");
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }

        Console.WriteLine("Direct3D live wrap-on and wrap-off transitions passed.");
    }

    static void TestScatteredParticlePlacement()
    {
        object data = CreateFullDomain(8, 8, 1);
        Type generatorType = RequiredType("Nuclei4.ParticleGenerator");
        int flatIndex = 3 * 8 + 4;
        bool movedFromCenter = false;
        double firstX = 0;
        double firstY = 0;

        for (uint seed = 1; seed <= 32; seed++)
        {
            object point = InvokeStatic(generatorType, "ScatteredPointInVoxel", data, flatIndex, seed);
            double x = Convert.ToDouble(point.GetType().GetProperty("X")!.GetValue(point));
            double y = Convert.ToDouble(point.GetType().GetProperty("Y")!.GetValue(point));
            double z = Convert.ToDouble(point.GetType().GetProperty("Z")!.GetValue(point));
            True(x > 3.0 && x < 4.0, "scattered particle left its X voxel");
            True(y > 4.0 && y < 5.0, "scattered particle left its Y voxel");
            Near(0.5, z, 1e-6, "planar scattered particle Z");
            if (seed == 1)
            {
                firstX = x;
                firstY = y;
            }
            else if (Math.Abs(x - firstX) > 1e-4 || Math.Abs(y - firstY) > 1e-4)
            {
                movedFromCenter = true;
            }
        }

        True(movedFromCenter, "scattered particle seeds produced identical positions");
    }

    static double ReadBackParticleX(object engine)
    {
        Invoke(engine, "ReadBackParticles");
        return Field<float[]>(engine, "particlePositionReadback")[0];
    }

    static void TestAdaptiveVolumePreviewLayout()
    {
        int[] layout300 = ResolveVolumePreviewLayout(300, 300, 300);
        Equal(300, layout300[0], "300^3 preview X resolution");
        Equal(300, layout300[1], "300^3 preview Y resolution");
        Equal(300, layout300[2], "300^3 preview Z resolution");

        int[] layout500 = ResolveVolumePreviewLayout(500, 500, 500);
        True(layout500[0] > 1 && layout500[0] < 500, "500^3 preview should be adaptively downsampled");
        Equal(layout500[0], layout500[1], "500^3 preview should preserve cubic proportions");
        Equal(layout500[1], layout500[2], "500^3 preview should preserve cubic proportions");
        True(layout500[5] <= 16384 && layout500[6] <= 16384, "500^3 preview exceeds texture dimensions");
        True((long)layout500[5] * layout500[6] <= 33_554_432, "500^3 preview exceeds the texture pixel budget");
        Console.WriteLine(
            "500^3 preview atlas: " + layout500[0] + "^3 samples, "
            + layout500[5] + " x " + layout500[6] + " pixels");
    }

    static int[] ResolveVolumePreviewLayout(int x, int y, int z)
    {
        Type engineType = RequiredType("Nuclei4.GpuFullSlimeSolverEngine");
        object engine = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(engineType);
        engineType.GetField("resX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, x);
        engineType.GetField("resY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, y);
        engineType.GetField("resZ", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, z);
        MethodInfo method = engineType.GetMethod("ResolveAdaptiveVolumeAtlasLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object[] arguments = { 0, 0, 0, 0, 0, 0, 0 };
        method.Invoke(engine, arguments);
        return Array.ConvertAll(arguments, Convert.ToInt32);
    }

    static void VerifyMergedSpeed(object data, IReadOnlyList<double> expected, string label)
    {
        for (int i = 0; i < expected.Count; i++)
        {
            double actual = (double)Invoke(data, "GetScalarValue", 2, i);
            Near(expected[i], actual, 1e-6, label + " speed " + i);
        }
    }

    static void VerifySelection(object data, bool[] mask, IReadOnlyList<int> expected, string label)
    {
        Equal(expected.Count, Field<int>(data, "ActiveCount"), label + " count");
        for (int ordinal = 0; ordinal < expected.Count; ordinal++)
        {
            int flatIndex = (int)Invoke(data, "ActiveFlatIndexAt", ordinal);
            Equal(expected[ordinal], flatIndex, label + " ordering " + ordinal);
            Equal(ordinal, (int)Invoke(data, "ActiveOrdinalFromFlatIndex", flatIndex), label + " rank " + ordinal);
        }

        for (int i = 0; i < mask.Length; i++)
        {
            Equal(mask[i], (bool)Invoke(data, "IsActive", i), label + " membership " + i);
        }
    }

    static object CreateFullDomain(int x, int y, int z)
    {
        return InvokeStatic(GridType, "CreateFullDomain", x, y, z, 1.0);
    }

    static object CreateField(object data)
    {
        ConstructorInfo constructor = FieldType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(item => item.GetParameters().Length == 3);
        return constructor.Invoke(new[] { data, null, null });
    }

    static object WithInitialDensity(object data, float[] density)
    {
        Type mapType = RequiredType("Nuclei4.VoxelScalarMap");
        object map = Activator.CreateInstance(mapType, new object[] { 0.0, density })!;
        GridType.GetField("Density")!.SetValue(data, map);
        return data;
    }

    static object CaptureVoxelSnapshot(object field)
    {
        object snapshot = Activator.CreateInstance(SnapshotType, nonPublic: true)!;
        SnapshotType.GetField("HasSlimeParticles")!.SetValue(snapshot, true);
        Invoke(snapshot, "CaptureCompactVoxels", field, false);
        return snapshot;
    }

    static Type RequiredType(string name)
    {
        return NucleiAssembly.GetType(name, throwOnError: true)!;
    }

    static void LoadNuclei()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string nucleiDirectory = Path.Combine(root, "Nuclei-v4", "Nuclei4", "bin", "Release", "net7.0-windows");
        string packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string[] candidates =
            {
                Path.Combine(nucleiDirectory, name.Name + ".dll"),
                Path.Combine(packageRoot, "rhinocommon", "8.0.23304.9001", "lib", "net48", name.Name + ".dll"),
                Path.Combine(packageRoot, "grasshopper", "8.0.23304.9001", "lib", "net48", name.Name + ".dll")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i])) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidates[i]);
            }

            return null;
        };

        NucleiAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(nucleiDirectory, "Nuclei4.gha"));
        GridType = RequiredType("Nuclei4.VoxelGridData");
        FieldType = RequiredType("Nuclei4.VoxelField");
        SnapshotType = RequiredType("Nuclei4.SolverGpuInputSnapshot");
    }

    static object Invoke(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(item => item.Name == method && item.GetParameters().Length == arguments.Length);
        return info.Invoke(target, arguments);
    }

    static object InvokeStatic(Type type, string method, params object[] arguments)
    {
        MethodInfo info = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(item => item.Name == method && item.GetParameters().Length == arguments.Length);
        return info.Invoke(null, arguments);
    }

    static T Field<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;
    }

    static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    static void False(bool value, string message) => True(!value, message);

    static void Null(object value, string message) => True(value == null, message);

    static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }
    }

    static void Near(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }
    }
}
