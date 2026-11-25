using System.Collections;

internal static partial class Program
{
    static void TestParticleSeedingDensity()
    {
        object data = CreateFullDomain(17, 9, 1);
        int count = Field<int>(data, "Count");
        List<double> limits = new List<double>(count);
        int mixedCapacity = 0;
        for (int i = 0; i < count; i++)
        {
            double limit = i % 5 == 0 ? 0 : i % 7 == 0 ? 0.005 : -1;
            limits.Add(limit);
            if (limit == -1) mixedCapacity++;
        }
        object mixedData = Invoke(data, "WithScalarValues", 1, limits);
        AssertScatteredDensityField(CreateField(mixedData), mixedCapacity, "mixed authored density");
        object zeroData = Invoke(data, "WithScalarValues", 1, new List<double> { 0 });
        AssertScatteredDensityField(CreateField(zeroData), 0, "uniform zero density");

        bool[] sparseMask = Enumerable.Range(0, count).Select(i => i % 3 == 0).ToArray();
        object sparseData = Invoke(data, "WithActiveMask", sparseMask);
        int activeCount = Field<int>(sparseData, "ActiveCount");
        List<double> sparseLimits = Enumerable.Range(0, activeCount)
            .Select(i => i % 4 == 0 ? 0.0 : 1.0).ToList();
        sparseData = Invoke(sparseData, "WithScalarValues", 1, sparseLimits);
        AssertScatteredDensityField(CreateField(sparseData), sparseLimits.Count(value => value > 0),
            "sparse authored density by active ordinal");

        object planarData = CreateFullDomain(9, 9, 1);
        object reflectiveField = CreateField(planarData);
        Invoke(reflectiveField, "ConfigureSolverBoundaries", false);
        AssertScatteredDensityField(reflectiveField, 49, "reflective planar solver boundary");
        object wrappedField = CreateField(planarData);
        Invoke(wrappedField, "ConfigureSolverBoundaries", true);
        AssertScatteredDensityField(wrappedField, 81, "wrapped full solver field");
        object spatialField = CreateField(CreateFullDomain(5, 5, 5));
        Invoke(spatialField, "ConfigureSolverBoundaries", false);
        AssertScatteredDensityField(spatialField, 27, "reflective spatial solver boundary");

        bool[] holeMask = Enumerable.Repeat(true, 81).ToArray();
        holeMask[4 * 9 + 4] = false;
        object holeData = Invoke(planarData, "WithActiveMask", holeMask);
        object wrappedHoleField = CreateField(holeData);
        Invoke(wrappedHoleField, "ConfigureSolverBoundaries", true);
        AssertScatteredDensityField(wrappedHoleField, 72, "wrapped sparse solver boundary");
        object reflectiveHoleField = CreateField(holeData);
        Invoke(reflectiveHoleField, "ConfigureSolverBoundaries", false);
        AssertScatteredDensityField(reflectiveHoleField, 40, "reflective sparse solver boundary");

        TestExplicitParticleSeedingDensity(holeData, reflectiveHoleField);
        Console.WriteLine("Particle seeding excludes authored and solver-boundary zero-density voxels.");
    }

    static void AssertScatteredDensityField(object field, int expectedCount, string label)
    {
        object group = Activator.CreateInstance(RequiredCompatibilityType("Nuclei4.ParticleGroup"))!;
        int requestedCount = (int)PropertyValue(field, "Count") + 20;
        IList particles = (IList)InvokeStatic(RequiredCompatibilityType("Nuclei4.ParticleGenerator"),
            "CreateScatteredParticlesForField", requestedCount, group, field);
        Equal(requestedCount, Field<int>(group, "RequestedParticleCount"), label + " requested count");
        Equal(expectedCount, particles.Count, label + " eligible capacity");
        HashSet<int> occupied = new HashSet<int>();
        foreach (object particle in particles)
        {
            int flatIndex = AssertSeededParticleWalkable(field, particle, label);
            True(occupied.Add(flatIndex), label + " duplicated voxel " + flatIndex);
            True(ReferenceEquals(group, Field<object>(particle, "parentParticleGroup")), label + " group");
        }
    }

    static void TestExplicitParticleSeedingDensity(object holeData, object reflectiveHoleField)
    {
        List<double> limits = Enumerable.Repeat(1.0, 81).ToList();
        limits[1 * 9 + 1] = 0;
        object field = CreateField(Invoke(holeData, "WithScalarValues", 1, limits));
        AssertExplicitDensityField(field, new[]
        {
            new[] { 2.25, 2.25, 0.5 },
            new[] { 1.5, 1.5, 0.5 },
            new[] { 4.5, 4.5, 0.5 },
            new[] { -0.25, 2.5, 0.5 },
            new[] { 9.0, 2.5, 0.5 },
            new[] { 2.75, 2.75, 0.5 },
            new[] { double.NaN, 2.5, 0.5 },
            new[] { double.PositiveInfinity, 2.5, 0.5 }
        }, new[] { 0, 5 }, "explicit authored blocked, inactive, and outside positions");
        AssertExplicitDensityField(reflectiveHoleField, new[]
        {
            new[] { 0.5, 4.5, 0.5 },
            new[] { 4.5, 3.5, 0.5 },
            new[] { 2.5, 2.5, 0.5 }
        }, new[] { 2 }, "explicit effective solver boundary");
    }

    static void AssertExplicitDensityField(object field, double[][] coordinates, int[] retained, string label)
    {
        Type pointType = RequiredExternalType("Rhino.Geometry.Point3d, RhinoCommon");
        IList points = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pointType))!;
        foreach (double[] point in coordinates) points.Add(CreatePoint3d(point[0], point[1], point[2]));
        object group = Activator.CreateInstance(RequiredCompatibilityType("Nuclei4.ParticleGroup"))!;
        IList particles = (IList)InvokeStatic(RequiredCompatibilityType("Nuclei4.ParticleGenerator"),
            "CreateFromPointsInField", points, group, field);
        Equal(coordinates.Length, Field<int>(group, "RequestedParticleCount"), label + " requested count");
        Equal(retained.Length, particles.Count, label + " retained count");
        for (int i = 0; i < particles.Count; i++)
        {
            AssertSeededParticleWalkable(field, particles[i]!, label);
            object origin = PropertyValue(Field<object>(particles[i]!, "pPlane"), "Origin");
            double[] expected = coordinates[retained[i]];
            AssertPoint(origin, expected[0], expected[1], expected[2], label + " preserved position " + i);
        }
    }

    static int AssertSeededParticleWalkable(object field, object particle, string label)
    {
        object origin = PropertyValue(Field<object>(particle, "pPlane"), "Origin");
        int resY = (int)PropertyValue(field, "ResY");
        int resZ = (int)PropertyValue(field, "ResZ");
        double voxelSize = (double)PropertyValue(field, "VoxelSize");
        int x = (int)Math.Floor(PointCoordinate(origin, 0) / voxelSize);
        int y = (int)Math.Floor(PointCoordinate(origin, 1) / voxelSize);
        int z = (int)Math.Floor(PointCoordinate(origin, 2) / voxelSize);
        True(x >= 0 && x < (int)PropertyValue(field, "ResX")
            && y >= 0 && y < resY && z >= 0 && z < resZ, label + " outside field");
        int flatIndex = x * resY * resZ + y * resZ + z;
        object data = PropertyValue(field, "Data");
        True((bool)Invoke(data, "IsActive", flatIndex), label + " inactive voxel " + flatIndex);
        double maximumDensity = (double)Invoke(field, "GetScalarValue", 1, flatIndex);
        True(maximumDensity < 0 || maximumDensity >= 0.01,
            label + " blocked maximum density " + maximumDensity + " at voxel " + flatIndex);
        return flatIndex;
    }
}
