using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

internal static class Program
{
    const string ExpectedComponentGuidHash = "BA5DD56D2DB434E2FEEC0AD489F1DF481FAFC4FA2E3843C3C21E49DED4DCB126";
    const string ExpectedPublicApiHash = "1ADB075EA91D2B043F890CF2249A57EDBB62A1076BE295736C10DAAA0F0AA433";
    const string ExpectedComponentSchemaHash = "83EDE30503D7F16B5EF4788AE0EF7C4E58EA6DFCCB1A1BC0032B5DB08DA2F70F";
    const string ExpectedMainResourceNameHash = "5DFD765D509A50F5942F8E0C8758AD2F8DEC6319AAD2B2A24FF9311798FB77C7";
    const string ExpectedLegacyShaderHash = "BBD3F0049D5A902B774EE45A7B5BACB52C6D20E2C2605C7115144DAB5AE5C88A";
    const string ExpectedShaderHash = "CFE49D9298411644D315E7F7287F639E87A58119E28142D9A9780B3218467592";
    const string ExpectedGpuShaderHash = "5804E55E013B16BEC25ECB90F390C5C8E2916FFB36D252AFAA5C9530F71625E3";
    const string ExpectedDisplayShaderHash = "7962C5E6C8BCAE08EEB74E649239601E884515546EA8E8C926A809224896C695";

    static readonly string[] SupportAssemblyNames =
    {
        "Nuclei4.Core",
        "Nuclei4.Gpu.Abstractions",
        "Nuclei4.Gpu.D3D11",
        "Nuclei4.Display.Abstractions",
        "Nuclei4.Display.D3D11"
    };

    static bool TraceDensity;
    static readonly Dictionary<int, double[]> V3DensitySnapshots = new Dictionary<int, double[]>();
    static readonly Dictionary<int, float[]> V4DensitySnapshots = new Dictionary<int, float[]>();
    static bool RandomHeadings;

    /// <summary>
    /// Starting position for particle i. Both solvers must be seeded from this, or the
    /// comparison measures the layout rather than the solvers: the shared snapshot
    /// builder packs particles into a fixed 17-wide block, which on a larger grid is
    /// far denser than the V3 side and produces a completely different neighbourhood.
    /// </summary>
    static void PositionFor(int i, int grid, out double x, out double y, out double z)
    {
        int span = Math.Max(4, grid - 5);
        x = 2.25 + ((i * 7) % span);
        y = 2.25 + ((i * 5) % span);
        z = 2.25 + ((i * 3) % span);
    }
    static int TraceEvery = 20;

    /// <summary>
    /// Deterministic unit heading for particle i, shared by both solvers so the
    /// comparison starts from identical orientations. Seeding every particle with the
    /// same +X heading is a degenerate initial condition and exaggerates any
    /// difference in how the two disperse it.
    /// </summary>
    static void HeadingFor(int i, out double hx, out double hy, out double hz,
                           out double ux, out double uy, out double uz)
    {
        uint h1 = BenchmarkHash((uint)i * 2654435761u ^ 0x9E3779B9u);
        uint h2 = BenchmarkHash((uint)i * 40503u ^ 0x85EBCA6Bu);
        double theta = (h1 & 0x00FFFFFFu) / 16777216.0 * 2.0 * Math.PI;
        double cosPhi = ((h2 & 0x00FFFFFFu) / 16777216.0) * 2.0 - 1.0;
        double sinPhi = Math.Sqrt(Math.Max(0.0, 1.0 - cosPhi * cosPhi));
        hx = sinPhi * Math.Cos(theta);
        hy = sinPhi * Math.Sin(theta);
        hz = cosPhi;

        // any unit vector perpendicular to the heading
        double ax = Math.Abs(hz) < 0.9 ? 0.0 : 0.0;
        double bx = Math.Abs(hz) < 0.9 ? 0.0 : 1.0;
        double refX = bx, refY = 0.0, refZ = Math.Abs(hz) < 0.9 ? 1.0 : 0.0;
        ux = hy * refZ - hz * refY;
        uy = hz * refX - hx * refZ;
        uz = hx * refY - hy * refX;
        double len = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        if (len < 1e-9) { ux = 0; uy = 1; uz = 0; }
        else { ux /= len; uy /= len; uz /= len; }
        _ = ax;
    }

    static bool TraceBirths;
    static bool TraceDistribution;

    /// <summary>
    /// Percentile summary of a neighbour-count sample, so the two solvers' spatial
    /// distributions can be compared directly instead of inferring from populations.
    /// </summary>
    static string DistributionSummary(List<int> values)
    {
        if (values.Count == 0) return "(empty)";
        values.Sort();
        double mean = 0;
        for (int i = 0; i < values.Count; i++) mean += values[i];
        mean /= values.Count;
        int P(double f)
        {
            int idx = (int)Math.Round(f * (values.Count - 1));
            return values[Math.Max(0, Math.Min(values.Count - 1, idx))];
        }
        int inBand = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] >= DistributionBandLow && values[i] <= DistributionBandHigh) inBand++;
        }
        return "n=" + values.Count.ToString().PadLeft(6)
            + "  mean " + mean.ToString("F2", CultureInfo.InvariantCulture).PadLeft(7)
            + "  p5 " + P(0.05).ToString().PadLeft(4)
            + "  p25 " + P(0.25).ToString().PadLeft(4)
            + "  median " + P(0.50).ToString().PadLeft(4)
            + "  p75 " + P(0.75).ToString().PadLeft(4)
            + "  p95 " + P(0.95).ToString().PadLeft(5)
            + "  inBand " + inBand.ToString().PadLeft(6)
            + " (" + (100.0 * inBand / values.Count).ToString("F1", CultureInfo.InvariantCulture) + "%)";
    }

    static int DistributionBandLow;
    static int DistributionBandHigh;
    static string RhinoInsideRoot;
    static object RhinoInsideCore;
    static bool BenchmarkAntParticles;
    static Assembly NucleiAssembly;
    static IReadOnlyList<Assembly> NucleiAssemblies;
    static string NucleiDirectory;
    static Type GridType;
    static Type FieldType;
    static Type SnapshotType;

    static int Main(string[] args)
    {
        try
        {
            LoadNuclei(args);
            if (Array.IndexOf(args, "--mesh-smoothing-coverage") >= 0)
            {
                TestVolumeMeshSmoothingDispatchCoverage();
                return 0;
            }
            if (Array.IndexOf(args, "--gpu-signature") >= 0)
            {
                WriteGpuSimulationSignature();
                return 0;
            }
            if (Array.IndexOf(args, "--benchmark") >= 0)
            {
                RunGpuBenchmark(args);
                return 0;
            }
            if (Array.IndexOf(args, "--parity") >= 0)
            {
                RunParity(args);
                return 0;
            }
            if (Array.IndexOf(args, "--gh-xml") >= 0)
            {
                DumpGrasshopperXml(args);
                return 0;
            }
            TestPreservationContracts();
            TestLargeEmptyField();
            TestAdaptiveSelections();
            TestScalarMapsAndBlockedThreshold();
            TestVectorPacking();
            TestBooleanFieldMerges();
            TestGpuSnapshotPacking();
            TestGpuOutputSinkRoundTrip();
            TestStaticPreviewNeutralChannels();
            TestDensityGradientParameterIsolation();
            TestVolumeBoundaryCapContract();
            if (NucleiAssemblies.Count > 1)
            {
                TestVolumeMeshSmoothingDispatchCoverage();
            }
            TestSolverDynamicStateIsolation();
            TestScatteredParticlePlacement();
            TestAdaptiveVolumePreviewLayout();
            if (Array.IndexOf(args, "--gpu") >= 0)
            {
                TestGpuEngineInitialization();
                TestGpuWrapTransitions();
            }
            if (Array.IndexOf(args, "--benchmark-gpu") >= 0)
            {
                BenchmarkGpuNoReadbackSteps();
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

    static void TestPreservationContracts()
    {
        string expectedShaderHash = NucleiAssemblies.Count > 1
            ? ExpectedShaderHash
            : ExpectedLegacyShaderHash;
        Equal("Nuclei4", NucleiAssembly.GetName().Name, "compatibility assembly name");
        Equal(new Version(4, 1, 0, 0), NucleiAssembly.GetName().Version, "compatibility assembly version");

        Type infoType = NucleiAssembly.GetExportedTypes().Single(type => type.Name == "Nuclei4Info");
        Equal("Nuclei4.Nuclei4Info", infoType.FullName, "Grasshopper assembly-info type");
        object info = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(infoType);
        Guid assemblyId = (Guid)infoType.GetProperty("Id")!.GetValue(info)!;
        Equal(new Guid("a4810f34-10b6-480c-a6d0-607aac4e8d2a"), assemblyId, "Grasshopper assembly ID");
        GuidAttribute typeLibraryGuid = NucleiAssembly.GetCustomAttribute<GuidAttribute>();
        Equal("67e300d2-061e-4987-b6e6-ffbb4810a624", typeLibraryGuid?.Value?.ToLowerInvariant(), "type-library GUID");

        List<string> guidRecords = ComponentGuidRecords(NucleiAssembly);
        Equal(40, guidRecords.Count, "component/parameter GUID count");
        Equal(ExpectedComponentGuidHash, HashRecords(guidRecords), "component/parameter GUID hash");

        List<string> apiRecords = VisibleApiRecords(NucleiAssembly);
        Equal(51, NucleiAssembly.GetExportedTypes().Length, "exported public type count");
        Equal(728, apiRecords.Count, "public API record count");
        Equal(ExpectedPublicApiHash, HashRecords(apiRecords), "public API hash");

        Type componentBaseType = RequiredExternalType("Grasshopper.Kernel.GH_Component, Grasshopper");
        Type[] componentTypes = NucleiAssembly.GetExportedTypes()
            .Where(type => type.IsSubclassOf(componentBaseType) && !type.IsAbstract)
            .OrderBy(type => type.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        List<string> schemaRecords = ComponentSchemaRecords(componentTypes);
        Equal(38, componentTypes.Length, "Grasshopper component count");
        Equal(215, schemaRecords.Count, "Grasshopper schema record count");
        Equal(ExpectedComponentSchemaHash, HashRecords(schemaRecords), "Grasshopper schema hash");

        List<ResourceRow> mainRows = ResourceRows(NucleiAssembly);
        Equal(26, mainRows.Count, "compatibility assembly resource count");
        Equal(ExpectedMainResourceNameHash, HashRecords(mainRows.Select(row => row.Name)), "compatibility resource-name hash");
        List<ResourceRow> mainShaders = mainRows.Where(row => row.Name.EndsWith(".cso", StringComparison.Ordinal)).ToList();
        Equal(25, mainShaders.Count, "compatibility shader count");
        Equal(expectedShaderHash, HashRecords(mainShaders.Select(row => row.Canonical)), "compatibility shader hash");

        Dictionary<string, HashSet<string>> shaderCopies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (int i = 0; i < NucleiAssemblies.Count; i++)
        {
            foreach (ResourceRow row in ResourceRows(NucleiAssemblies[i]).Where(row => row.Name.EndsWith(".cso", StringComparison.Ordinal)))
            {
                if (!shaderCopies.TryGetValue(row.Name, out HashSet<string> hashes))
                {
                    hashes = new HashSet<string>(StringComparer.Ordinal);
                    shaderCopies.Add(row.Name, hashes);
                }
                hashes.Add(row.Hash);
            }
        }

        Equal(25, shaderCopies.Count, "deployed unique shader count");
        foreach (KeyValuePair<string, HashSet<string>> pair in shaderCopies)
        {
            Equal(1, pair.Value.Count, "identical deployed copies of " + pair.Key);
        }
        Equal(
            expectedShaderHash,
            HashRecords(shaderCopies.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase).Select(pair => pair.Key + " " + pair.Value.Single())),
            "deployed shader union hash");

        if (NucleiAssemblies.Count > 1)
        {
            VerifySupportShaderContract("Nuclei4.Gpu.D3D11", 20, ExpectedGpuShaderHash);
            VerifySupportShaderContract("Nuclei4.Display.D3D11", 5, ExpectedDisplayShaderHash);
        }

        Console.WriteLine(
            "Compatibility contracts passed: 51 public types, 38 components, 215 schema records, 25 shaders ("
            + (NucleiAssemblies.Count > 1 ? "split deployment" : "legacy deployment") + ").");
    }

    static void VerifySupportShaderContract(string assemblyName, int expectedCount, string expectedHash)
    {
        Assembly assembly = NucleiAssemblies.Single(item => item.GetName().Name == assemblyName);
        List<ResourceRow> rows = ResourceRows(assembly);
        Equal(expectedCount, rows.Count, assemblyName + " shader count");
        Equal(expectedHash, HashRecords(rows.Select(row => row.Canonical)), assemblyName + " shader hash");
    }

    static List<string> ComponentGuidRecords(Assembly assembly)
    {
        List<string> records = new List<string>();
        foreach (Type type in assembly.GetExportedTypes())
        {
            PropertyInfo property = type.GetProperty("ComponentGuid", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || type.IsAbstract)
            {
                continue;
            }

            object instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
            Guid guid = (Guid)property.GetValue(instance)!;
            records.Add(type.Name + " " + guid.ToString().ToLowerInvariant());
        }

        records.Sort(StringComparer.CurrentCultureIgnoreCase);
        return records;
    }

    static List<string> VisibleApiRecords(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        List<string> records = new List<string>();
        foreach (Type type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.CurrentCultureIgnoreCase))
        {
            records.Add("T|" + type.FullName + "|base=" + type.BaseType?.FullName);
            IEnumerable<MemberInfo> members = type.GetMembers(flags)
                .Where(IsVisibleApiMember)
                .OrderBy(member => member.MemberType)
                .ThenBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(member => member.ToString(), StringComparer.CurrentCultureIgnoreCase);
            foreach (MemberInfo member in members)
            {
                records.Add("M|" + type.FullName + "|" + member.MemberType + "|" + member);
            }
        }

        return records;
    }

    static bool IsVisibleApiMember(MemberInfo member)
    {
        if (member is MethodBase method)
        {
            return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
        }
        if (member is FieldInfo field)
        {
            return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
        }
        if (member is PropertyInfo property)
        {
            return IsVisibleAccessor(property.GetMethod) || IsVisibleAccessor(property.SetMethod);
        }
        if (member is EventInfo eventInfo)
        {
            return IsVisibleAccessor(eventInfo.AddMethod);
        }
        return false;
    }

    static bool IsVisibleAccessor(MethodInfo method)
    {
        return method != null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);
    }

    static List<string> ComponentSchemaRecords(IEnumerable<Type> componentTypes)
    {
        List<string> records = new List<string>();
        foreach (Type type in componentTypes)
        {
            if (type.FullName == "Nuclei4.Preview_Particle")
            {
                records.Add("C|Nuclei4.Preview_Particle|60649521-0784-4a2e-8dfa-27e4a04600ac|Particle Preview|Particle Preview|Displays particles in the Rhino viewport|Nuclei4|Preview|secondary");
                records.Add("I|0|Grasshopper.Kernel.Parameters.Param_GenericObject|Particles|particles|Input Particles|item|optional=False|mapping=None|hidden=False|reverse=False|simplify=False|defaults=");
                records.Add("I|1|Grasshopper.Kernel.Parameters.Param_Number|Point Size|size|Point Display Size|item|optional=True|mapping=None|hidden=False|reverse=False|simplify=False|defaults=Grasshopper.Kernel.Types.GH_Number:2");
                continue;
            }

            object component = Activator.CreateInstance(type)!;
            records.Add(
                "C|" + type.FullName
                + "|" + PropertyValue(component, "ComponentGuid").ToString().ToLowerInvariant()
                + "|" + PropertyValue(component, "Name")
                + "|" + PropertyValue(component, "NickName")
                + "|" + PropertyValue(component, "Description")
                + "|" + PropertyValue(component, "Category")
                + "|" + PropertyValue(component, "SubCategory")
                + "|" + PropertyValue(component, "Exposure"));

            object parameterServer = PropertyValue(component, "Params");
            AppendParameterSchema(records, "I", (IEnumerable)PropertyValue(parameterServer, "Input"));
            AppendParameterSchema(records, "O", (IEnumerable)PropertyValue(parameterServer, "Output"));
        }

        return records;
    }

    static void AppendParameterSchema(List<string> records, string prefix, IEnumerable parameters)
    {
        int index = 0;
        foreach (object parameter in parameters)
        {
            object persistentData = OptionalPropertyValue(parameter, "PersistentData");
            string defaults = "";
            if (persistentData != null)
            {
                MethodInfo allData = persistentData.GetType().GetMethod("AllData", new[] { typeof(bool) });
                if (allData != null && allData.Invoke(persistentData, new object[] { true }) is IEnumerable values)
                {
                    List<string> defaultValues = new List<string>();
                    foreach (object value in values)
                    {
                        defaultValues.Add(value.GetType().FullName + ":" + value);
                    }
                    defaults = string.Join(",", defaultValues);
                }
            }

            records.Add(
                prefix + "|" + index
                + "|" + parameter.GetType().FullName
                + "|" + PropertyValue(parameter, "Name")
                + "|" + PropertyValue(parameter, "NickName")
                + "|" + PropertyValue(parameter, "Description")
                + "|" + PropertyValue(parameter, "Access")
                + "|optional=" + OptionalPropertyValue(parameter, "Optional")
                + "|mapping=" + OptionalPropertyValue(parameter, "DataMapping")
                + "|hidden=" + OptionalPropertyValue(parameter, "Hidden")
                + "|reverse=" + OptionalPropertyValue(parameter, "Reverse")
                + "|simplify=" + OptionalPropertyValue(parameter, "Simplify")
                + "|defaults=" + defaults);
            index++;
        }
    }

    static object PropertyValue(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(target);
    }

    static object OptionalPropertyValue(object target, string name)
    {
        return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
    }

    static List<ResourceRow> ResourceRows(Assembly assembly)
    {
        List<ResourceRow> rows = new List<ResourceRow>();
        foreach (string name in assembly.GetManifestResourceNames().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            rows.Add(new ResourceRow(name, Convert.ToHexString(SHA256.HashData(stream))));
        }
        return rows;
    }

    static string HashRecords(IEnumerable<string> records)
    {
        string text = string.Join("\n", records) + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    readonly struct ResourceRow
    {
        public ResourceRow(string name, string hash)
        {
            Name = name;
            Hash = hash;
        }

        public string Name { get; }
        public string Hash { get; }
        public string Canonical => Name + " " + Hash;
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

        Type builderType = RequiredCompatibilityType("Nuclei4.VoxelSelectionBuilder");
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

    static void TestGpuOutputSinkRoundTrip()
    {
        if (NucleiAssemblies.Count == 1)
        {
            return;
        }

        object inputField = CreateField(CreateFullDomain(2, 2, 1));
        object snapshot = CaptureVoxelSnapshot(inputField);
        object solverField = Field<object>(snapshot, "Field");
        Invoke(
            solverField,
            "UpdateDynamicFields",
            new float[4],
            new float[4],
            new float[4],
            new float[4]);

        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        object group = Activator.CreateInstance(groupType)!;
        SetField(group, "ant", true);
        SetField(group, "color", System.Drawing.Color.FromArgb(255, 80, 120, 160));

        Type particleType = RequiredCompatibilityType("Nuclei4.Particle");
        object initialParticle = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleType);
        Type pointType = RequiredExternalType("Rhino.Geometry.Point3d, RhinoCommon");
        IList nativeFreeTrails = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pointType))!;
        SetField(initialParticle, "trails", nativeFreeTrails);
        IList initialTrails = (IList)Field<object>(initialParticle, "trails");
        initialTrails.Add(CreatePoint3d(-1, -1, -1));

        Type particleListType = RequiredCompatibilityType("Nuclei4.ParticleList");
        IList particleList = (IList)CreateNativeFreeParticleList(particleListType, particleType, 2);
        particleList.Add(initialParticle);
        IList groupParticles = (IList)Field<object>(group, "particles");
        groupParticles.Add(initialParticle);

        Array groups = Array.CreateInstance(groupType, 1);
        groups.SetValue(group, 0);
        SetField(snapshot, "Particles", particleList);
        SetField(snapshot, "ParticleGroups", groups);
        SetField(snapshot, "ParticleCount", 1);
        SetField(snapshot, "GroupCount", 1);

        Type sinkType = RequiredCompatibilityType("Nuclei4.Gh1GpuSolverOutputSink");
        ConstructorInfo sinkConstructor = sinkType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 2);
        object sink = sinkConstructor.Invoke(new object[] { snapshot, 2 });
        Array slots = Field<Array>(sink, "particleSlots");
        True(ReferenceEquals(initialParticle, slots.GetValue(0)), "output sink did not retain the initial particle slot");
        Equal(1, (int)PropertyValue(sink, "ParticleCount"), "output sink initial particle count");

        float[] density = { 0.1f, 0.2f, 0.3f, 0.4f };
        float[] antFood = { 1.1f, 1.2f, 1.3f, 1.4f };
        float[] antBase = { 2.1f, 2.2f, 2.3f, 2.4f };
        float[] remainingFood = { 3.1f, 3.2f, 3.3f, 3.4f };
        Type voxelViewType = RequiredImplementationType("Nuclei4.GpuVoxelReadbackView");
        object voxelView = CreateInstance(
            voxelViewType,
            density,
            antFood,
            antBase,
            remainingFood,
            true,
            true);
        Invoke(sink, "ApplyVoxelFields", voxelView);

        object dynamicData = PropertyValue(solverField, "Dynamic");
        True(ReferenceEquals(density, Field<float[]>(dynamicData, "Density")), "voxel density readback was copied or lost");
        True(ReferenceEquals(antFood, Field<float[]>(dynamicData, "AntFoodPheromone")), "ant-food readback was copied or lost");
        True(ReferenceEquals(antBase, Field<float[]>(dynamicData, "AntBasePheromone")), "ant-base readback was copied or lost");
        True(ReferenceEquals(remainingFood, Field<float[]>(dynamicData, "RemainingFood")), "food readback was copied or lost");
        Near(0.3, (double)Invoke(solverField, "GetScalarValue", PreviewFieldIndex("SlimeChemoattractants"), 2), 1e-6, "voxel density materialization");
        Near(1.3, (double)Invoke(solverField, "GetScalarValue", PreviewFieldIndex("AntFoodPheromones"), 2), 1e-6, "ant-food materialization");
        Near(2.3, (double)Invoke(solverField, "GetScalarValue", PreviewFieldIndex("AntBasePheromones"), 2), 1e-6, "ant-base materialization");
        // Remaining food is the ant-consumable map after the V3 food split;
        // slime Food is now an immutable source projected into density.
        Near(3.3, (double)Invoke(solverField, "GetScalarValue", PreviewFieldIndex("AntFood"), 2), 1e-6, "remaining-food materialization");

        const int capacity = 2;
        float[] positions =
        {
            1.25f, 0.5f, 0.5f, 0.0f,
            0.0f, 0.0f, 0.0f, -1.0f
        };
        float[] directions =
        {
            2.0f, 0.0f, 0.0f, 2.0f,
            0.0f, 0.0f, 0.0f, -1.0f
        };
        float[] yAxes =
        {
            1.0f, 3.0f, 0.0f, 1.0f,
            0.0f, 0.0f, 0.0f, 0.0f
        };
        int[] auxiliary = new int[capacity * 5];
        auxiliary[0] = 7;
        auxiliary[capacity] = 4;
        auxiliary[capacity * 2] = 5;
        auxiliary[capacity * 3] = 1;
        auxiliary[capacity * 4] = 1;

        Type particleViewType = RequiredImplementationType("Nuclei4.GpuParticleReadbackView");
        object particleView = CreateInstance(
            particleViewType,
            capacity,
            1,
            1,
            positions,
            directions,
            yAxes,
            auxiliary);
        Equal(capacity, (int)PropertyValue(particleView, "Capacity"), "particle readback capacity");
        Equal(1, (int)PropertyValue(particleView, "Count"), "particle readback requested count");
        Equal(1, (int)PropertyValue(particleView, "GroupCount"), "particle readback group count");
        True(ReferenceEquals(positions, PropertyValue(particleView, "Positions")), "particle position readback was copied");
        True(ReferenceEquals(directions, PropertyValue(particleView, "Directions")), "particle direction readback was copied");
        True(ReferenceEquals(yAxes, PropertyValue(particleView, "YAxes")), "particle Y-axis readback was copied");
        True(ReferenceEquals(auxiliary, PropertyValue(particleView, "Auxiliary")), "particle auxiliary readback was copied");
        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "TrailSize", 3);
        SetField(settings, "TrailFreq", 2);

        bool nativePreviewAvailable = TryApplyParticlesThroughPreviewBoundary(
            sink,
            particleView,
            settings,
            2,
            buildPreviewCache: true);
        if (!nativePreviewAvailable)
        {
            TryApplyParticlesThroughPreviewBoundary(
                sink,
                particleView,
                settings,
                2,
                buildPreviewCache: false);
        }
        True(ReferenceEquals(group, Field<object>(initialParticle, "parentParticleGroup")), "particle group reference");
        Equal(7, Field<int>(initialParticle, "age"), "particle age");
        True(Field<bool>(initialParticle, "foundFood"), "particle ant state");

        object previewCache = Field<object>(particleList, "PreviewCache");
        if (nativePreviewAvailable)
        {
            Equal(1, (int)PropertyValue(sink, "ParticleCount"), "active particle count after readback");
            Equal(1, particleList.Count, "materialized particle-list count");
            True(ReferenceEquals(initialParticle, particleList[0]), "particle slot identity changed during readback");
            Equal(1, groupParticles.Count, "particle-group membership count");
            True(ReferenceEquals(initialParticle, groupParticles[0]), "particle group did not receive the retained slot");
            Equal(4, Field<int>(initialParticle, "neighbourCount_Die"), "particle death-neighbour count");
            Equal(5, Field<int>(initialParticle, "neighbourCount_Div"), "particle division-neighbour count");
            object parentVoxel = Field<object>(initialParticle, "parentVoxel");
            AssertParentVoxel(parentVoxel);
            AssertParticlePlane(initialParticle, 1.25, 0.5, 0.5, 1, 0, 0, 0, 1, 0);

            IList trails = (IList)Field<object>(initialParticle, "trails");
            Equal(1, trails.Count, "trail reset-marker sample count");
            AssertPoint(trails[0], 1.25, 0.5, 0.5, "trail reset-marker point");
            Equal(1, Field<int>(previewCache, "ParticleCount"), "full-readback preview particle count");
            True(Field<bool>(previewCache, "HasPoint"), "full-readback preview cache has no point");
            True(Field<bool>(previewCache, "IsValid"), "full-readback preview cache is invalid");

            yAxes[3] = 0;
            positions[0] = 1.4f;
            True(TryApplyParticlesThroughPreviewBoundary(sink, particleView, settings, 3, false), "native trail update failed");
            Equal(1, trails.Count, "non-sampled trail count");
            AssertPoint(trails[0], 1.4, 0.5, 0.5, "non-sampled trail head update");

            positions[0] = 1.5f;
            True(TryApplyParticlesThroughPreviewBoundary(sink, particleView, settings, 4, false), "native trail sample failed");
            Equal(2, trails.Count, "sampled trail count");
            AssertPoint(trails[0], 1.5, 0.5, 0.5, "sampled trail head");

            auxiliary[capacity * 3] = 2;
            positions[0] = 1.6f;
            True(TryApplyParticlesThroughPreviewBoundary(sink, particleView, settings, 6, false), "native generation reset failed");
            Equal(1, trails.Count, "generation-change trail reset count");
            AssertPoint(trails[0], 1.6, 0.5, 0.5, "generation-change trail point");
            True(ReferenceEquals(initialParticle, particleList[0]), "generation change replaced the particle object");
        }
        else
        {
            VerifyNativeFreeParticleHelpers(sink, particleList, groupParticles, initialParticle, settings);
        }

        float[] previewPositions =
        {
            0.25f, 1.75f, 0.5f, 0.0f,
            0.0f, 0.0f, 0.0f, -1.0f
        };
        Type previewViewType = RequiredImplementationType("Nuclei4.GpuParticlePreviewReadbackView");
        object previewView = CreateInstance(previewViewType, capacity, 1, 1, previewPositions);
        Equal(capacity, (int)PropertyValue(previewView, "Capacity"), "position-only preview capacity");
        Equal(1, (int)PropertyValue(previewView, "Count"), "position-only preview requested count");
        Equal(1, (int)PropertyValue(previewView, "GroupCount"), "position-only preview group count");
        True(ReferenceEquals(previewPositions, PropertyValue(previewView, "Positions")), "position-only preview array was copied");

        bool previewApplied = TryApplyPreviewPositionsThroughNativeBoundary(sink, previewView);
        if (previewApplied)
        {
            Equal(1, Field<int>(previewCache, "ParticleCount"), "position-only preview particle count");
            True(Field<bool>(previewCache, "IsValid"), "position-only preview cache is invalid");
            object antPointCloud = Field<object>(previewCache, "AntPointCloud2");
            Equal(1, (int)PropertyValue(antPointCloud, "Count"), "position-only preview point-cloud count");
            object pointCloudItem = antPointCloud.GetType().GetProperty("Item")!.GetValue(antPointCloud, new object[] { 0 });
            AssertPoint(PropertyValue(pointCloudItem, "Location"), 0.25, 1.75, 0.5, "position-only preview point");
        }
        else
        {
            VerifyManagedPreviewStaging(initialParticle, previewPositions);
        }

        Console.WriteLine(
            "GH1 GPU output-sink native-free checks passed"
            + (previewApplied ? " with Rhino preview-cache materialization." : "; native preview-cache materialization requires a Rhino-hosted gate."));
    }

    static void TestStaticPreviewNeutralChannels()
    {
        if (NucleiAssemblies.Count == 1)
        {
            return;
        }

        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object engine = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(engineType);
        SetField(engine, "voxelCount", 4);
        SetField(engine, "hasStaticPreviewInput", true);
        SetField(engine, "staticActiveVoxelFlags", null);
        SetField(engine, "staticMinimumDensityValues", null);
        SetField(engine, "staticMaximumDensityValues", null);
        SetField(engine, "staticSpeedValues", null);
        SetField(engine, "staticSensorDistanceValues", null);
        SetField(engine, "staticSensorAngleValues", null);
        SetField(engine, "staticRotationAngleValues", null);
        SetField(engine, "staticMinimumDensityDefault", -1.0);
        SetField(engine, "staticMaximumDensityDefault", -1.0);
        SetField(engine, "staticSpeedDefault", -1.0);
        SetField(engine, "staticSensorDistanceDefault", -1.0);
        SetField(engine, "staticSensorAngleDefault", -1.0);
        SetField(engine, "staticRotationAngleDefault", -1.0);

        SetField(engine, "hasStaticPreviewInput", false);
        Near(0, StaticPreviewValue(engine, 2, "Speed"), 1e-6, "missing static-preview input");
        SetField(engine, "hasStaticPreviewInput", true);
        foreach (string fieldName in new[] { "MinimumDensity", "MaximumDensity", "Speed", "SensorDistance", "SensorAngle", "RotationAngle" })
        {
            Near(0, StaticPreviewValue(engine, 2, fieldName), 1e-6, "legacy negative default preview " + fieldName);
        }

        SetField(engine, "staticMinimumDensityDefault", 0.25);
        SetField(engine, "staticMaximumDensityDefault", 9.0);
        SetField(engine, "staticSpeedDefault", 1.5);
        SetField(engine, "staticSensorDistanceDefault", 2.5);
        SetField(engine, "staticSensorAngleDefault", 3.5);
        SetField(engine, "staticRotationAngleDefault", 4.5);
        Near(0.25, StaticPreviewValue(engine, 2, "MinimumDensity"), 1e-6, "default minimum-density preview");
        Near(9.0, StaticPreviewValue(engine, 2, "MaximumDensity"), 1e-6, "default maximum-density preview");
        Near(1.5, StaticPreviewValue(engine, 2, "Speed"), 1e-6, "default speed preview");
        Near(2.5, StaticPreviewValue(engine, 2, "SensorDistance"), 1e-6, "default sensor-distance preview");
        Near(3.5, StaticPreviewValue(engine, 2, "SensorAngle"), 1e-6, "default sensor-angle preview");
        Near(4.5, StaticPreviewValue(engine, 2, "RotationAngle"), 1e-6, "default rotation-angle preview");

        SetField(engine, "staticActiveVoxelFlags", new uint[] { 0b1101u });
        SetField(engine, "staticMinimumDensityValues", new[] { 0.1f, 0.2f, -0.3f, 0.4f });
        SetField(engine, "staticMaximumDensityValues", new[] { 5.0f, 6.0f, float.PositiveInfinity, 8.0f });
        SetField(engine, "staticSpeedValues", new[] { 1.0f, 2.0f, 3.0f, -4.0f });
        SetField(engine, "staticSensorDistanceValues", new[] { 10.0f, 11.0f, 12.0f, float.NaN });
        SetField(engine, "staticSensorAngleValues", new[] { 20.0f, 21.0f, 22.0f, float.PositiveInfinity });
        SetField(engine, "staticRotationAngleValues", new[] { 30.0f, 31.0f, 32.0f, -33.0f });

        Near(0.1, StaticPreviewValue(engine, 0, "MinimumDensity"), 1e-6, "dense minimum-density preview");
        Near(5.0, StaticPreviewValue(engine, 0, "MaximumDensity"), 1e-6, "dense maximum-density preview");
        Near(1.0, StaticPreviewValue(engine, 0, "Speed"), 1e-6, "dense speed preview");
        Near(10.0, StaticPreviewValue(engine, 0, "SensorDistance"), 1e-6, "dense sensor-distance preview");
        Near(20.0, StaticPreviewValue(engine, 0, "SensorAngle"), 1e-6, "dense sensor-angle preview");
        Near(30.0, StaticPreviewValue(engine, 0, "RotationAngle"), 1e-6, "dense rotation-angle preview");

        foreach (string fieldName in new[] { "MinimumDensity", "MaximumDensity", "Speed", "SensorDistance", "SensorAngle", "RotationAngle" })
        {
            Near(0, StaticPreviewValue(engine, 1, fieldName), 1e-6, "inactive static preview " + fieldName);
        }
        Near(0, StaticPreviewValue(engine, 2, "MinimumDensity"), 1e-6, "negative minimum-density sanitization");
        Near(0, StaticPreviewValue(engine, 2, "MaximumDensity"), 1e-6, "infinite maximum-density sanitization");
        Near(0, StaticPreviewValue(engine, 3, "Speed"), 1e-6, "negative speed sanitization");
        Near(0, StaticPreviewValue(engine, 3, "SensorDistance"), 1e-6, "NaN sensor-distance sanitization");
        Near(0, StaticPreviewValue(engine, 3, "SensorAngle"), 1e-6, "infinite sensor-angle sanitization");
        Near(0, StaticPreviewValue(engine, 3, "RotationAngle"), 1e-6, "negative rotation-angle sanitization");

        Console.WriteLine("Neutral static-preview channel parity passed.");
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
        Type vectorType = RequiredExternalType("Rhino.Geometry.Vector3d, RhinoCommon");
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

        Type combinerType = RequiredCompatibilityType("Nuclei4.VoxelGridCombiner");
        Type modeType = RequiredCompatibilityType("Nuclei4.VoxelGridMergeMode");
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
        Type particleGroupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        SnapshotType.GetField("ParticleGroups")!.SetValue(snapshot, Array.CreateInstance(particleGroupType, 0));

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");

        object engine = CreateGpuEngine(engineType, snapshot, settings, true, false, false, 0, 1);
        try
        {
            Type dimensionType = RequiredImplementationType("Nuclei4.SolverGpuDimensionMode");
            object dimensionMode = InvokeStatic(dimensionType, "FromResolution", 17, 9, 3);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
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
        object resetEngine = CreateGpuEngine(engineType, resetSnapshot, settings, true, false, false, 0, 1);
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

    static void TestDensityGradientParameterIsolation()
    {
        if (NucleiAssemblies.Count <= 1)
        {
            return;
        }

        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        MethodInfo ensureGradient = engineType.GetMethod(
            "EnsureDensityGradientPreview",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(engineType.FullName, "EnsureDensityGradientPreview");
        MethodInfo createParameters = engineType.GetMethod(
            "CreateParameters",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(engineType.FullName, "CreateParameters");
        MethodInfo updateParameters = engineType.GetMethod(
            "UpdateParameters",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(engineType.FullName, "UpdateParameters");

        True(MethodCalls(ensureGradient, createParameters),
            "density gradient generation does not restore density preview parameters");
        True(MethodCalls(ensureGradient, updateParameters),
            "density gradient generation does not upload restored density preview parameters");

        object engine = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(engineType);
        SetField(engine, "resX", 100);
        SetField(engine, "resY", 100);
        SetField(engine, "resZ", 100);
        SetField(engine, "voxelSize", 1.0f);
        SetField(engine, "densityPreviewWidth", 1000);
        SetField(engine, "densityPreviewHeight", 1000);
        SetField(engine, "densityPreviewAxisMode", 3);
        SetField(engine, "densityPreviewResX", 100);
        SetField(engine, "densityPreviewResY", 100);
        SetField(engine, "densityPreviewResZ", 100);
        SetField(engine, "densityPreviewAtlasColumns", 10);
        SetField(engine, "densityPreviewAtlasRows", 10);
        SetField(engine, "particleTrailPreviewWidth", 16384);
        SetField(engine, "particleTrailPreviewHeight", 20);
        SetField(engine, "particleTrailPreviewHeadIndex", 0);
        SetField(engine, "particleTrailPreviewValidCount", 2);

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        Type dimensionType = RequiredImplementationType("Nuclei4.SolverGpuDimensionMode");
        object dimensionMode = InvokeStatic(dimensionType, "FromResolution", 100, 100, 100);
        object parameters = createParameters.Invoke(engine, new[] { (object)0, settings, dimensionMode, 0 })!;
        Equal(1000, Field<int>(parameters, "PreviewWidth"), "restored density preview width");
        Equal(1000, Field<int>(parameters, "PreviewHeight"), "restored density preview height");
        Equal(100, Field<int>(parameters, "PreviewSlice"), "restored density preview slice count");
        Equal(10, Field<int>(parameters, "PreviewAtlasColumns"), "restored density preview atlas columns");
        Equal(10, Field<int>(parameters, "PreviewAtlasRows"), "restored density preview atlas rows");
        Equal(100, Field<int>(parameters, "PreviewPadding0"), "restored density preview X padding");
        Equal(100, Field<int>(parameters, "PreviewPadding1"), "restored density preview Y padding");
        Console.WriteLine("Density gradient parameters are isolated from particle-trail preview state.");
    }

    static void TestVolumeBoundaryCapContract()
    {
        Type rendererType = RequiredImplementationType("Nuclei4.GpuDensityFieldD3DRenderer");
        FieldInfo shaderField = rendererType.GetField(
            "ShaderSource",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(rendererType.FullName, "ShaderSource");
        string shaderSource = (string)(shaderField.IsLiteral
            ? shaderField.GetRawConstantValue()
            : shaderField.GetValue(null))!;

        True(shaderSource.Contains("float4 SampleBoundaryCap(", StringComparison.Ordinal),
            "density-aware volume boundary cap helper is missing");
        True(shaderSource.Contains("boundaryPosition - outwardNormal * voxelSize * 0.55", StringComparison.Ordinal),
            "boundary cap does not sample half a voxel inside the domain");
        True(shaderSource.Contains("float presence = sqrt(saturate(VolumeTransfer(normalizedValue)));", StringComparison.Ordinal),
            "boundary cap opacity is not weighted by the existing volume transfer function");
        True(shaderSource.Contains("return float4(color, presence * 0.28 * solidityScale);", StringComparison.Ordinal),
            "boundary cap maximum solidity changed from 0.18");
        True(shaderSource.Contains("float4 entryCap = SampleBoundaryCap(startPosition, direction, voxelSize, 1.0)", StringComparison.Ordinal),
            "front-facing volume boundary contacts are not capped");
        True(shaderSource.Contains("float4 exitCap = SampleBoundaryCap(exitPosition, direction, voxelSize, 0.60)", StringComparison.Ordinal),
            "rear-facing volume boundary contacts do not use the reduced contribution");
        Console.WriteLine("Volume boundary contacts use transfer-weighted caps at 0.28 front / 0.168 rear maximum solidity.");
    }

    static void TestVolumeMeshSmoothingDispatchCoverage()
    {
        const long resolution = 300;
        const long threadsPerGroup = 256;
        const long maximumGroupsX = 65535;
        const long voxelCount = resolution * resolution * resolution;

        long groupCount = (voxelCount + threadsPerGroup - 1) / threadsPerGroup;
        long groupsX = Math.Min(maximumGroupsX, groupCount);
        long groupsY = (groupCount + groupsX - 1) / groupsX;
        long oneRowCoverage = groupsX * threadsPerGroup;
        long missingWithXOnly = voxelCount - Math.Min(voxelCount, oneRowCoverage);
        long linearCoverage = Math.Min(voxelCount, groupsX * groupsY * threadsPerGroup);

        Equal(27000000L, voxelCount, "300-cubed smoothing workload size");
        Equal(2L, groupsY, "300-cubed smoothing dispatch row count");
        Equal(16776960L, oneRowCoverage, "single D3D11 dispatch-row coverage");
        Equal(10223040L, missingWithXOnly, "voxels missed by id.x-only smoothing");
        Equal(voxelCount, linearCoverage, "two-dimensional linear dispatch coverage");

        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        FieldInfo shaderField = engineType.GetField(
            "FullSolverShaderSource",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(engineType.FullName, "FullSolverShaderSource");
        string shaderSource = (string)(shaderField.IsLiteral
            ? shaderField.GetRawConstantValue()
            : shaderField.GetValue(null))!;
        string linearIndexFunction = ShaderFunctionSource(shaderSource, "LinearIndex256");
        string smoothingFunction = ShaderFunctionSource(shaderSource, "SmoothVolumeForMesh");

        True(
            linearIndexFunction.Contains("dispatchThreadId.y * (65535u * 256u)", StringComparison.Ordinal),
            "LinearIndex256 does not span D3D11 dispatch rows");
        True(
            smoothingFunction.Contains("uint index = LinearIndex256(id);", StringComparison.Ordinal),
            "SmoothVolumeForMesh uses id.x and leaves " + missingWithXOnly.ToString("N0", CultureInfo.InvariantCulture)
                + " of " + voxelCount.ToString("N0", CultureInfo.InvariantCulture)
                + " voxels untouched for a 300^3 volume");
        False(
            smoothingFunction.Contains("uint index = id.x;", StringComparison.Ordinal),
            "SmoothVolumeForMesh regressed to one-dimensional id.x indexing");

        Console.WriteLine(
            "Volume-mesh smoothing covers 27,000,000 voxels across 2 D3D11 dispatch rows; id.x-only indexing would miss 10,223,040.");
    }

    static string ShaderFunctionSource(string shaderSource, string functionName)
    {
        int nameIndex = shaderSource.IndexOf(functionName + "(", StringComparison.Ordinal);
        if (nameIndex < 0)
        {
            throw new InvalidOperationException("Shader function '" + functionName + "' was not found.");
        }

        int openingBrace = shaderSource.IndexOf('{', nameIndex);
        if (openingBrace < 0)
        {
            throw new InvalidOperationException("Shader function '" + functionName + "' has no body.");
        }

        int depth = 0;
        for (int index = openingBrace; index < shaderSource.Length; index++)
        {
            if (shaderSource[index] == '{')
            {
                depth++;
            }
            else if (shaderSource[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return shaderSource.Substring(nameIndex, index - nameIndex + 1);
                }
            }
        }

        throw new InvalidOperationException("Shader function '" + functionName + "' has an unterminated body.");
    }

    static bool MethodCalls(MethodInfo caller, MethodInfo callee)
    {
        byte[] body = caller.GetMethodBody()?.GetILAsByteArray();
        if (body == null || body.Length < 5)
        {
            return false;
        }

        byte[] token = BitConverter.GetBytes(callee.MetadataToken);
        for (int index = 1; index <= body.Length - token.Length; index++)
        {
            if (body[index - 1] != 0x28 && body[index - 1] != 0x6F)
            {
                continue;
            }

            bool match = true;
            for (int offset = 0; offset < token.Length; offset++)
            {
                if (body[index + offset] == token[offset]) continue;
                match = false;
                break;
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    static void WriteGpuSimulationSignature()
    {
        const int resolutionX = 22;
        const int resolutionY = 18;
        const int resolutionZ = 12;
        const int particleCount = 64;
        const int finalIteration = 12;

        float[] initialDensity = new float[resolutionX * resolutionY * resolutionZ];
        for (int x = 0; x < resolutionX; x++)
        {
            for (int y = 0; y < resolutionY; y++)
            {
                for (int z = 0; z < resolutionZ; z++)
                {
                    double dx = x - 10.5;
                    double dy = y - 8.5;
                    double dz = z - 5.5;
                    double centralBlob = 0.42 * Math.Exp(-(dx * dx + dy * dy + dz * dz) / 31.0);
                    double offsetBlob = 0.19 * Math.Exp(-((dx - 4) * (dx - 4) + (dy + 3) * (dy + 3) + (dz - 2) * (dz - 2)) / 12.0);
                    int flatIndex = x * resolutionY * resolutionZ + y * resolutionZ + z;
                    initialDensity[flatIndex] = (float)(centralBlob + offsetBlob);
                }
            }
        }

        object inputField = CreateField(WithInitialDensity(
            CreateFullDomain(resolutionX, resolutionY, resolutionZ),
            initialDensity));
        object snapshot = CaptureGpuSignatureSnapshot(inputField, particleCount);

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "Diffuse", 0.17);
        SetField(settings, "DiffuseRange", 2);
        SetField(settings, "Decay", 0.025);
        SetField(settings, "WrapBoundaries", true);
        SetField(settings, "DynamicPopulation", false);

        Type dimensionType = RequiredImplementationType("Nuclei4.SolverGpuDimensionMode");
        object dimensionMode = InvokeStatic(
            dimensionType,
            "FromResolution",
            resolutionX,
            resolutionY,
            resolutionZ);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object engine = CreateGpuEngine(engineType, snapshot, settings, false, false, false, 0, 1);
        List<string> checkpointRecords = new List<string>();

        Console.WriteLine(
            "GPU_SIGNATURE version=1 deployment="
            + (NucleiAssemblies.Count > 1 ? "split" : "legacy")
            + " grid=" + resolutionX + "x" + resolutionY + "x" + resolutionZ
            + " particles=" + particleCount
            + " steps=" + finalIteration);

        try
        {
            for (int iteration = 1; iteration <= finalIteration; iteration++)
            {
                InvokeGpuStep(engine, snapshot, settings, dimensionMode, iteration);
                if (iteration == 1 || iteration == 4 || iteration == finalIteration)
                {
                    Invoke(engine, "ReadBackDensity");
                    float[] density = Field<float[]>(engine, "densityReadback");
                    string record = GpuDensitySignatureRecord(iteration, density);
                    checkpointRecords.Add(record);
                    Console.WriteLine(record);
                }
            }
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }

        Console.WriteLine("GPU_SIGNATURE_HASH " + HashRecords(checkpointRecords));
    }

    /// <summary>
    /// Headless GPU solver benchmark. Drives the real D3D11 engine with no Rhino or
    /// Grasshopper host, so a regression can be measured and attributed to a stage.
    /// Methodology matches docs/performance: warmup steps are discarded, each repeat
    /// contributes its median step, and the reported figure is the median of those.
    /// </summary>
    /// <summary>
    /// Runs the V3 CPU solver and the V4 GPU solver side by side on identical
    /// settings strings and reports the population per iteration. Both toolsets
    /// parse the same strings the Grasshopper components emit, so the comparison
    /// cannot drift through hand-transcribed settings.
    /// </summary>
    /// <summary>
    /// Serializes a Grasshopper definition to XML so two files can be diffed as text.
    /// </summary>
    static void DumpGrasshopperXml(string[] args)
    {
        int index = Array.IndexOf(args, "--gh-xml");
        string source = args[index + 1];
        string target = args[index + 2];

        Assembly ghIo = null;
        foreach (AssemblyName reference in NucleiAssembly.GetReferencedAssemblies())
        {
            if (string.Equals(reference.Name, "GH_IO", StringComparison.OrdinalIgnoreCase))
            {
                ghIo = Assembly.Load(reference);
                break;
            }
        }

        if (ghIo == null) ghIo = Assembly.Load("GH_IO");

        Type archiveType = ghIo.GetType("GH_IO.Serialization.GH_Archive", true);
        object archive = Activator.CreateInstance(archiveType);
        object read = archiveType.GetMethod("ReadFromFile", new[] { typeof(string) }).Invoke(archive, new object[] { source });
        if (read is bool ok && !ok)
        {
            Console.WriteLine("could not read " + source);
            return;
        }

        MethodInfo serialize = archiveType.GetMethod("Serialize_Xml", Type.EmptyTypes);
        string xml = (string)serialize.Invoke(archive, null);
        File.WriteAllText(target, xml);
        Console.WriteLine("wrote " + target + " (" + xml.Length.ToString("N0") + " chars) from " + source);
    }

    static void RunParity(string[] args)
    {
        int grid = BenchmarkInt(args, "--grid", 24);
        int particleCount = BenchmarkInt(args, "--particles", 2000);
        int iterations = BenchmarkInt(args, "--iterations", 100);
        int minPop = BenchmarkInt(args, "--min-population", 100);
        int maxPop = BenchmarkInt(args, "--max-population", 200000);
        int minN = BenchmarkInt(args, "--min-neighbours", 0);
        int maxN = BenchmarkInt(args, "--max-neighbours", 10);
        int range = BenchmarkInt(args, "--range", 3);
        int minAge = BenchmarkInt(args, "--min-age", 10);
        int freq = BenchmarkInt(args, "--frequency", 5);
        // Death and division can be configured independently, as they are in the
        // shipped example definitions.
        TraceBirths = Array.IndexOf(args, "--trace-births") >= 0;
        TraceDistribution = Array.IndexOf(args, "--trace-distribution") >= 0;
        RandomHeadings = Array.IndexOf(args, "--random-headings") >= 0;
        TraceDensity = Array.IndexOf(args, "--trace-density") >= 0;
        TraceEvery = Math.Max(1, BenchmarkInt(args, "--trace-every", 20));
        DistributionBandLow = BenchmarkInt(args, "--division-min-neighbours", minN);
        DistributionBandHigh = BenchmarkInt(args, "--division-max-neighbours", maxN);
        int deathMinN = BenchmarkInt(args, "--death-min-neighbours", minN);
        int deathMaxN = BenchmarkInt(args, "--death-max-neighbours", maxN);
        int deathRange = BenchmarkInt(args, "--death-range", range);
        int deathMinAge = BenchmarkInt(args, "--death-min-age", minAge);
        int deathFreq = BenchmarkInt(args, "--death-frequency", freq);
        int divMinN = BenchmarkInt(args, "--division-min-neighbours", minN);
        int divMaxN = BenchmarkInt(args, "--division-max-neighbours", maxN);
        int divRange = BenchmarkInt(args, "--division-range", range);
        int divMinAge = BenchmarkInt(args, "--division-min-age", minAge);
        int divFreq = BenchmarkInt(args, "--division-frequency", freq);
        double diffuse = BenchmarkDouble(args, "--diffuse", 0.1);
        double decay = BenchmarkDouble(args, "--decay", 0.03);
        bool division = Array.IndexOf(args, "--no-division") < 0;
        bool death = Array.IndexOf(args, "--no-death") < 0;

        List<string> settings = new List<string>
        {
            "VoxelSettingsSlime " + diffuse.ToString(CultureInfo.InvariantCulture) + " 1 "
                + decay.ToString(CultureInfo.InvariantCulture) + " 1",
            "DivisionSettings " + division + " " + divMinAge + " " + divRange + " " + divMinN + " " + divMaxN + " " + divFreq,
            "DeathSettings " + death + " " + deathMinAge + " " + deathRange + " " + deathMinN + " " + deathMaxN + " " + deathFreq,
            "PopulationSettings " + minPop + " " + maxPop + " 0 0 1",
            "WrapSettings False"
        };

        Console.WriteLine("PARITY grid=" + grid + "^3 particles=" + particleCount
            + " iterations=" + iterations);
        foreach (string line in settings) Console.WriteLine("  setting: " + line);
        Console.WriteLine();

        int[] v3;
        try
        {
            v3 = RunV3Population(args, grid, particleCount, iterations, settings);
        }
        catch (Exception error)
        {
            Exception root = error;
            while (root.InnerException != null) root = root.InnerException;
            Console.WriteLine("  V3 could not run headlessly: " + root.Message);
            Console.WriteLine(root.StackTrace);
            Console.WriteLine();
            v3 = new int[0];
        }
        int[] v4 = RunV4Population(grid, particleCount, iterations, settings);

        if (TraceDensity)
        {
            Console.WriteLine();
            Console.WriteLine("  density field comparison (deposit + diffusion)");
            Console.WriteLine("  iteration     V3 sum     V4 sum   ratio   maxAbsDiff   voxels>1e-4");
            foreach (int key in V3DensitySnapshots.Keys.OrderBy(k => k))
            {
                if (!V4DensitySnapshots.ContainsKey(key)) continue;
                double[] a = V3DensitySnapshots[key];
                float[] b = V4DensitySnapshots[key];
                int n = Math.Min(a.Length, b.Length);
                double sumA = 0, sumB = 0, maxDiff = 0;
                int differing = 0;
                for (int i = 0; i < n; i++)
                {
                    sumA += a[i];
                    sumB += b[i];
                    double d = Math.Abs(a[i] - b[i]);
                    if (d > maxDiff) maxDiff = d;
                    if (d > 1e-4) differing++;
                }
                Console.WriteLine("  " + key.ToString().PadLeft(9)
                    + sumA.ToString("F1", CultureInfo.InvariantCulture).PadLeft(11)
                    + sumB.ToString("F1", CultureInfo.InvariantCulture).PadLeft(11)
                    + (sumA > 0 ? (sumB / sumA).ToString("F3", CultureInfo.InvariantCulture) : "-").PadLeft(8)
                    + maxDiff.ToString("F5", CultureInfo.InvariantCulture).PadLeft(13)
                    + differing.ToString().PadLeft(14));
            }
        }

        Console.WriteLine();
        Console.WriteLine("  iteration        V3        V4     ratio");
        for (int i = 0; i < iterations; i++)
        {
            bool show = i < 10 || (i + 1) % 10 == 0 || i == iterations - 1;
            if (!show) continue;
            int a = i < v3.Length ? v3[i] : -1;
            int b = i < v4.Length ? v4[i] : -1;
            string ratio = a > 0 && b >= 0 ? ((double)b / a).ToString("F2", CultureInfo.InvariantCulture) : "-";
            Console.WriteLine("  " + (i + 1).ToString().PadLeft(9)
                + a.ToString("N0").PadLeft(10) + b.ToString("N0").PadLeft(10) + ratio.PadLeft(10));
        }
    }

    /// <summary>
    /// V3's ParticleList builds a Rhino PointCloud, which needs the native
    /// rhcommon_c library. Pre-loading it from the Rhino install lets the real V3
    /// code paths run headlessly instead of being stubbed out.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectory(string path);

    /// <summary>
    /// RhinoCommon's native core refuses to initialize outside a Rhino host, so V3 --
    /// whose solver uses Rhino geometry throughout -- can only run headlessly through
    /// an in-process Rhino. Returns false when that is not possible, in which case the
    /// V3 half of a parity run is simply skipped.
    /// </summary>
    static bool TryStartRhinoInside()
    {
        if (RhinoInsideCore != null) return true;

        foreach (string root in new[]
        {
            @"C:\Program Files\Rhino 8\System",
            @"C:\Program Files\Rhino 9\System",
            @"C:\Program Files\Rhino WIP\System"
        })
        {
            if (!File.Exists(Path.Combine(root, "RhinoCommon.dll"))) continue;

            RhinoInsideRoot = root;
            SetDllDirectory(root);
            Environment.SetEnvironmentVariable("PATH", root + ";" + Environment.GetEnvironmentVariable("PATH"));

            try
            {
                // RhinoCore lives in the installed RhinoCommon; the Rhino.Inside package
                // only supplies the resolver that points assembly loading at the install.
                Assembly rhinoCommon = LoadAssemblyOnce(Path.Combine(root, "RhinoCommon.dll"));
                Type coreType = rhinoCommon.GetType("Rhino.Runtime.InProcess.RhinoCore", false);
                if (coreType == null)
                {
                    Console.WriteLine("  RhinoCommon at " + root + " has no in-process RhinoCore.");
                    RhinoInsideRoot = null;
                    continue;
                }

                ConstructorInfo withArgs = coreType.GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Length == 1
                        && c.GetParameters()[0].ParameterType == typeof(string[]));
                RhinoInsideCore = withArgs != null
                    ? withArgs.Invoke(new object[] { new[] { "/NOSPLASH" } })
                    : Activator.CreateInstance(coreType);
                Console.WriteLine("  Rhino.Inside started from " + root);
                return true;
            }
            catch (Exception error)
            {
                Exception cause = error;
                while (cause.InnerException != null) cause = cause.InnerException;
                Console.WriteLine("  Rhino.Inside unavailable (" + root + "): " + cause.Message);
                RhinoInsideRoot = null;
            }
        }

        return false;
    }

    static void EnsureRhinoNativeLibrary()
    {
        foreach (string root in new[]
        {
            @"C:\Program Files\Rhino 8\System",
            @"C:\Program Files\Rhino 9\System",
            @"C:\Program Files\Rhino WIP\System"
        })
        {
            string candidate = Path.Combine(root, "rhcommon_c.dll");
            if (!File.Exists(candidate)) continue;
            try
            {
                // The native library pulls in siblings from the Rhino System folder,
                // so that directory has to be searchable before it will initialize.
                SetDllDirectory(root);
                Environment.SetEnvironmentVariable("PATH", root + ";" + Environment.GetEnvironmentVariable("PATH"));
                System.Runtime.InteropServices.NativeLibrary.Load(candidate);
                Console.WriteLine("  rhcommon_c loaded from " + root);
                return;
            }
            catch (Exception error)
            {
                Console.WriteLine("  could not load " + candidate + ": " + error.Message);
            }
        }

        Console.WriteLine("  WARNING: rhcommon_c not found; V3 construction will fail.");
    }

    static int[] RunV3Population(string[] args, int grid, int particleCount, int iterations, List<string> settings)
    {
        if (!TryStartRhinoInside())
        {
            EnsureRhinoNativeLibrary();
        }
        string v3Path = OptionValue(args, "--v3") ?? @"C:\Nuclei\Nuclei-v3\Nuclei3\bin\Release\net7.0-windows\Nuclei3.gha";
        Assembly v3 = LoadAssemblyOnce(v3Path);

        Type voxelType = v3.GetType("Nuclei3.Voxel", true);
        Type solverType = v3.GetType("Nuclei3.Solver", true);
        Type groupType = v3.GetType("Nuclei3.ParticleGroup", true);
        Type particleType = v3.GetType("Nuclei3.Particle", true);
        Type particleListType = v3.GetType("Nuclei3.ParticleList", true);

        Array voxels = Array.CreateInstance(voxelType, grid, grid, grid);
        for (int x = 0; x < grid; x++)
            for (int y = 0; y < grid; y++)
                for (int z = 0; z < grid; z++)
                    voxels.SetValue(Activator.CreateInstance(voxelType, 1.0, x, y, z), x, y, z);

        object solver = Activator.CreateInstance(solverType);
        SetField(solver, "iteration", 0);
        SetField(solver, "antParticles", false);
        SetField(solver, "slimeParticles", true);
        SetField(solver, "random", new Random(89));
        Invoke(solver, "precomputeAngles");
        Invoke(solver, "createWanderVectors");
        SetField(solver, "settings", settings);
        Invoke(solver, "readSolverSettings");
        SetField(solver, "inputVoxels", voxels);
        Invoke(solver, "inheritVoxels");

        object group = Activator.CreateInstance(groupType);
        SetField(group, "speed", 0.72);
        SetField(group, "sensorDistance", 2.35);
        SetField(group, "sensorAngle", 37);
        SetField(group, "rotationAngle", 29);
        SetField(group, "depositValue", 0.85);
        SetField(group, "wanderFrequency", 0.13);
        SetField(group, "baseWanderFrequency", 0.0);
        SetField(group, "color", System.Drawing.Color.FromArgb(255, 72, 184, 112));
        SetField(group, "ant", false);
        IList groupParticles = (IList)Field<object>(group, "particles");
        for (int i = 0; i < particleCount; i++)
        {
            double x, y, z;
            PositionFor(i, grid, out x, out y, out z);
            object plane = RandomHeadings ? CreateOrientedPlane(i, x, y, z) : CreateRawPlane(x, y, z);
            object particle = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleType);
            SetField(particle, "pPlane", plane);
            SetField(particle, "home", plane);
            SetField(particle, "age", 0);
            SetField(particle, "foundFood", false);
            SetField(particle, "parentParticleGroup", group);
            FieldInfo v3Trails = particleType.GetField("trails", BindingFlags.Instance | BindingFlags.Public);
            if (v3Trails != null) v3Trails.SetValue(particle, Activator.CreateInstance(v3Trails.FieldType));
            groupParticles.Add(particle);
        }

        IList inputGroups = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(groupType));
        inputGroups.Add(group);
        SetField(solver, "inputParticleGroups", inputGroups);
        SetField(solver, "particles", Activator.CreateInstance(particleListType));
        Invoke(solver, "inheritParticleGroups");
        Invoke(solver, "particleCheckParentVoxel");

        MethodInfo sense = solverType.GetMethod("particleSenseValuesAndVectors", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo move = solverType.GetMethod("particleMoveAndDeposit", BindingFlags.Instance | BindingFlags.NonPublic);

        List<int> trace = new List<int>();
        for (int iteration = 1; iteration <= iterations; iteration++)
        {
            SetField(solver, "iteration", iteration);
            // V3's solve loop refreshes group settings every iteration, which also
            // recomputes wander frequency from the live population. Omitting it left
            // the harness wandering at the initial rate and dispersing too fast.
            Invoke(solver, "updateParticleGroups");
            if (iteration > 1)
            {
                object[] senseArgs = new object[] { 0L, 0L };
                sense.Invoke(solver, senseArgs);
                object[] moveArgs = new object[] { 0L, 0L };
                move.Invoke(solver, moveArgs);
            }

            Invoke(solver, "particleRecordTrail");
            Invoke(solver, "projectFoodSources");
            Invoke(solver, "diffuseVoxels");
            Invoke(solver, "particleCheckParentVoxel");

            bool dynPop = Field<bool>(solver, "dynPop");
            if (iteration > 1 && dynPop)
            {
                bool deathOn = Field<bool>(solver, "death");
                bool divisionOn = Field<bool>(solver, "division");
                if (deathOn || divisionOn)
                {
                    Invoke(solver, "particleCheckNeighbourCount");
                    int beforeKill = ((IList)Field<object>(solver, "particles")).Count;

                    int eligible = 0;
                    long neighbourTotal = 0;
                    if (TraceBirths)
                    {
                        IList pool = (IList)Field<object>(solver, "particles");
                        int divMinAgeV3 = Field<int>(solver, "divMinAge");
                        int minDivNV3 = Field<int>(solver, "minDivN");
                        int maxDivNV3 = Field<int>(solver, "maxDivN");
                        for (int q = 0; q < pool.Count; q++)
                        {
                            object particle = pool[q];
                            int n = Field<int>(particle, "neighbourCount_Div");
                            neighbourTotal += n;
                            if (Field<int>(particle, "age") >= divMinAgeV3 && n >= minDivNV3 && n <= maxDivNV3)
                            {
                                eligible++;
                            }
                        }
                    }

                    if (TraceDistribution && iteration % TraceEvery == 0)
                    {
                        IList sample = (IList)Field<object>(solver, "particles");
                        List<int> counts = new List<int>(sample.Count);
                        for (int q = 0; q < sample.Count; q++) counts.Add(Field<int>(sample[q], "neighbourCount_Div"));
                        Console.WriteLine("    V3 iter " + iteration.ToString().PadLeft(4) + "  " + DistributionSummary(counts));
                    }

                    Invoke(solver, "killParticles");
                    int afterKill = ((IList)Field<object>(solver, "particles")).Count;
                    Invoke(solver, "divideParticles");
                    int afterDivide = ((IList)Field<object>(solver, "particles")).Count;

                    if (TraceBirths && afterDivide != afterKill)
                    {
                        double meanN = beforeKill > 0 ? (double)neighbourTotal / beforeKill : 0;
                        Console.WriteLine("    V3 iter " + iteration.ToString().PadLeft(4)
                            + "  live " + beforeKill.ToString().PadLeft(6)
                            + "  eligible " + eligible.ToString().PadLeft(6)
                            + "  births " + (afterDivide - afterKill).ToString().PadLeft(6)
                            + "  meanNeighbours " + meanN.ToString("F2", CultureInfo.InvariantCulture));
                    }
                }
                Invoke(solver, "applyRandomPopulationChanges");
            }

            IList live = (IList)Field<object>(solver, "particles");
            trace.Add(live.Count);

            if (TraceDensity && iteration % TraceEvery == 0)
            {
                Invoke(solver, "ensureScalarDensityAuthoritative");
                double[] density = Field<double[]>(solver, "scalarVoxelDensity");

                // Cross-check the scalar array against the voxel objects themselves; if
                // they disagree the array is not the authoritative copy at this point
                // and any comparison built on it would be meaningless.
                if (iteration == TraceEvery)
                {
                    Array flat = Field<Array>(solver, "voxelFlat");
                    double viaVoxels = 0;
                    if (flat != null)
                    {
                        for (int v = 0; v < flat.Length; v++)
                        {
                            object voxel = flat.GetValue(v);
                            if (voxel != null) viaVoxels += Convert.ToDouble(PropertyValue(voxel, "density"));
                        }
                    }
                    double viaArray = 0;
                    if (density != null) for (int v = 0; v < density.Length; v++) viaArray += density[v];
                    Console.WriteLine("    V3 density cross-check: scalarArray " + viaArray.ToString("F3", CultureInfo.InvariantCulture)
                        + "  voxelObjects " + viaVoxels.ToString("F3", CultureInfo.InvariantCulture));
                }
                if (density != null)
                {
                    double[] copy = new double[density.Length];
                    Array.Copy(density, copy, density.Length);
                    V3DensitySnapshots[iteration] = copy;
                }
            }
        }

        return trace.ToArray();
    }

    static int[] RunV4Population(int grid, int particleCount, int iterations, List<string> settings)
    {
        int voxelCount = grid * grid * grid;
        float[] initialDensity = new float[voxelCount];
        object inputField = CreateField(WithInitialDensity(CreateFullDomain(grid, grid, grid), initialDensity));
        BenchmarkAntParticles = false;
        object snapshot = CaptureGpuSignatureSnapshot(inputField, particleCount);

        // V3 rebuilds its particles on inherit, so they start at age 0. Match that here
        // or the age gates fire at different iterations and the comparison is worthless.
        int[] ages = Field<int[]>(snapshot, "ParticleAges");
        if (ages != null) Array.Clear(ages, 0, ages.Length);

        // Re-seed positions from the shared layout, and recompute the parent voxel
        // indices that go with them (the domain uses a voxel size of 1).
        float[] positions = Field<float[]>(snapshot, "ParticlePositionsXyz");
        float[] homes = Field<float[]>(snapshot, "ParticleHomesXyz");
        int[] parents = Field<int[]>(snapshot, "ParticleParentIndices");
        for (int i = 0; i < particleCount; i++)
        {
            double px, py, pz;
            PositionFor(i, grid, out px, out py, out pz);
            int o = i * 3;
            if (positions != null && o + 2 < positions.Length)
            {
                positions[o] = (float)px; positions[o + 1] = (float)py; positions[o + 2] = (float)pz;
            }
            if (homes != null && o + 2 < homes.Length)
            {
                homes[o] = (float)px; homes[o + 1] = (float)py; homes[o + 2] = (float)pz;
            }
            if (parents != null && i < parents.Length)
            {
                parents[i] = (int)Math.Floor(px) * grid * grid + (int)Math.Floor(py) * grid + (int)Math.Floor(pz);
            }
        }

        if (RandomHeadings)
        {
            float[] dirs = Field<float[]>(snapshot, "ParticleDirectionsXyz");
            float[] ups = Field<float[]>(snapshot, "ParticleYAxesXyz");
            for (int i = 0; i < particleCount; i++)
            {
                double hx, hy, hz, ux, uy, uz;
                HeadingFor(i, out hx, out hy, out hz, out ux, out uy, out uz);
                int o = i * 3;
                if (dirs != null && o + 2 < dirs.Length)
                {
                    dirs[o] = (float)hx; dirs[o + 1] = (float)hy; dirs[o + 2] = (float)hz;
                }
                if (ups != null && o + 2 < ups.Length)
                {
                    ups[o] = (float)ux; ups[o + 1] = (float)uy; ups[o + 2] = (float)uz;
                }
            }
        }

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object gpuSettings = InvokeStatic(settingsType, "FromStrings", settings);

        object dimensionMode = InvokeStatic(
            RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"), "FromResolution", grid, grid, grid);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object engine = CreateGpuEngine(engineType, snapshot, gpuSettings, false, false, false, 0, 1);

        List<int> trace = new List<int>();
        try
        {
            MethodInfo step = RequiredNoReadbackStepMethod(engine.GetType());
            bool sixArguments = step.GetParameters().Length == 6;
            for (int iteration = 1; iteration <= iterations; iteration++)
            {
                int before = Field<int>(engine, "particleCount");
                step.Invoke(engine, BenchmarkStepArguments(sixArguments, snapshot, gpuSettings, dimensionMode, iteration));
                Invoke(engine, "ReadBackPopulationState");
                int after = Field<int>(engine, "particleCount");
                trace.Add(after);
                if (TraceDensity && iteration % TraceEvery == 0)
                {
                    Invoke(engine, "ReadBackDensity");
                    float[] density = Field<float[]>(engine, "densityReadback");
                    if (density != null)
                    {
                        float[] copy = new float[density.Length];
                        Array.Copy(density, copy, density.Length);
                        V4DensitySnapshots[iteration] = copy;
                    }
                }

                if (TraceDistribution && iteration % TraceEvery == 0)
                {
                    Invoke(engine, "EnsureParticleReadbackResources");
                    Invoke(engine, "ReadBackParticleAuxiliaryState");
                    Invoke(engine, "ReadBackParticlePositions");
                    int[] aux = Field<int[]>(engine, "particleAuxReadback");
                    float[] pos = Field<float[]>(engine, "particlePositionReadback");
                    int capacity = Field<int>(engine, "particleCapacity");
                    if (aux != null && pos != null && aux.Length >= capacity * 3)
                    {
                        List<int> counts = new List<int>();
                        for (int slot = 0; slot < capacity; slot++)
                        {
                            if (pos[slot * 4 + 3] < -0.5f) continue;   // dead slot
                            counts.Add(aux[capacity * 2 + slot]);      // division neighbour count
                        }
                        Console.WriteLine("    V4 iter " + iteration.ToString().PadLeft(4) + "  " + DistributionSummary(counts));
                    }
                }

                if (TraceBirths && after != before)
                {
                    Console.WriteLine("    V4 iter " + iteration.ToString().PadLeft(4)
                        + "  live " + before.ToString().PadLeft(6)
                        + "  change " + (after - before).ToString().PadLeft(6));
                }
            }
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }

        return trace.ToArray();
    }

    static void RunGpuBenchmark(string[] args)
    {
        int grid = BenchmarkInt(args, "--grid", 64);
        int particleCount = BenchmarkInt(args, "--particles", 262144);
        int steps = BenchmarkInt(args, "--steps", 120);
        int repeats = BenchmarkInt(args, "--repeats", 5);
        int warmup = BenchmarkInt(args, "--warmup", 20);
        double gradual = BenchmarkDouble(args, "--gradual", 1.0);
        bool slimeFood = Array.IndexOf(args, "--food") >= 0;
        bool antFood = Array.IndexOf(args, "--ant-food") >= 0;
        bool randomPopulation = Array.IndexOf(args, "--random-population") >= 0;
        int tracePopulation = BenchmarkInt(args, "--trace-population", 0);
        double randomDeath = BenchmarkDouble(args, "--random-death", 0.002);
        double randomDivision = BenchmarkDouble(args, "--random-division", 0.002);
        int minimumPopulation = BenchmarkInt(args, "--min-population", -1);
        int maximumPopulation = BenchmarkInt(args, "--max-population", -1);
        bool densityPreview = Array.IndexOf(args, "--density-preview") >= 0;
        BenchmarkAntParticles = Array.IndexOf(args, "--ants") >= 0;
        BenchmarkSyncVoxels = Array.IndexOf(args, "--sync-voxels") >= 0;
        BenchmarkSyncParticles = Array.IndexOf(args, "--sync-particles") >= 0;
        int traceFood = BenchmarkInt(args, "--trace-food", 0);
        int previewScale = BenchmarkInt(args, "--preview-scale", 1);

        int voxelCount = grid * grid * grid;
        float[] initialDensity = new float[voxelCount];
        double centre = (grid - 1) / 2.0;
        double spread = Math.Max(1.0, grid * grid / 12.0);
        for (int x = 0; x < grid; x++)
        {
            for (int y = 0; y < grid; y++)
            {
                for (int z = 0; z < grid; z++)
                {
                    double dx = x - centre;
                    double dy = y - centre;
                    double dz = z - centre;
                    int flatIndex = (x * grid + y) * grid + z;
                    initialDensity[flatIndex] = (float)(0.45 * Math.Exp(-(dx * dx + dy * dy + dz * dz) / spread));
                }
            }
        }

        object inputField = CreateField(WithInitialDensity(CreateFullDomain(grid, grid, grid), initialDensity));
        object snapshot = CaptureGpuSignatureSnapshot(inputField, particleCount);
        if (slimeFood) SetField(snapshot, "InitialFood", SparseFoodMap(voxelCount, grid, 8191u));
        if (antFood) SetField(snapshot, "InitialAntFood", SparseFoodMap(voxelCount, grid, 5003u));

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "Diffuse", 0.17);
        SetField(settings, "DiffuseRange", 1);
        SetField(settings, "Decay", 0.025);
        SetField(settings, "WrapBoundaries", true);
        SetField(settings, "DiffusionGradual", gradual);
        SetField(settings, "DynamicPopulation", randomPopulation);
        if (randomPopulation)
        {
            SetField(settings, "RandomDivisionProbability", randomDivision);
            SetField(settings, "RandomDeathProbability", randomDeath);
            SetField(settings, "RandomPopulationFrequency", BenchmarkInt(args, "--frequency", 1));
            SetField(settings, "MinimumPopulation", minimumPopulation >= 0 ? minimumPopulation : particleCount / 2);
            SetField(settings, "MaximumPopulation", maximumPopulation > 0 ? maximumPopulation : particleCount);
        }

        object dimensionMode = InvokeStatic(
            RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"), "FromResolution", grid, grid, grid);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");

        Console.WriteLine(
            "GPU_BENCHMARK grid=" + grid + "^3 (" + voxelCount.ToString("N0") + " voxels)"
            + " particles=" + particleCount.ToString("N0")
            + " steps=" + steps + " repeats=" + repeats + " warmup=" + warmup
            + " gradual=" + gradual.ToString(CultureInfo.InvariantCulture)
            + " food=" + slimeFood + " antFood=" + antFood + " randomPopulation=" + randomPopulation
            + " densityPreview=" + densityPreview + " previewScale=" + previewScale);

        List<double> totals = new List<double>();
        List<double> wallTimes = new List<double>();
        List<double> particleStage = new List<double>();
        List<double> populationStage = new List<double>();
        List<double> diffusionStage = new List<double>();
        int passes = 0;

        // One engine for the whole run. Creating an engine recompiles 25 shaders,
        // which costs far more than a step and swamped per-repeat measurements.
        // The real sink needs Rhino natives (ParticleList allocates PointClouds),
        // so it is opt-in and only usable where those can load.
        if (Array.IndexOf(args, "--real-sink") >= 0)
        {
            BenchmarkRealSinkSnapshot = snapshot;
            BenchmarkRealSinkCapacity = Math.Max(particleCount, maximumPopulation > 0 ? maximumPopulation : particleCount);
        }

        object engine = CreateGpuEngine(engineType, snapshot, settings, densityPreview, false, false, 0, previewScale);
        try
        {
            object capabilities = PropertyValue(engine, "Capabilities");
            bool software = Convert.ToBoolean(Field<object>(capabilities, "SoftwareFallback"));
            Console.WriteLine("  device: " + (software
                ? "WARP SOFTWARE FALLBACK - timings are NOT representative of your GPU"
                : "D3D11 hardware"));

            Console.WriteLine("  channels: foodRemainingOffset=" + Field<int>(engine, "foodRemainingOffset")
                + " foodSourceOffset=" + Field<int>(engine, "foodSourceOffset")
                + " | snapshot.InitialFood=" + (Field<object>(snapshot, "InitialFood") == null ? "null" : "set")
                + " snapshot.InitialAntFood=" + (Field<object>(snapshot, "InitialAntFood") == null ? "null" : "set"));

            MethodInfo step = RequiredNoReadbackStepMethod(engine.GetType());
            bool sixArguments = step.GetParameters().Length == 6;
            int iteration = 0;

            if (traceFood > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  remaining-food trace (ants=" + BenchmarkAntParticles + ")");
                for (int i = 0; i < traceFood; i++)
                {
                    iteration++;
                    step.Invoke(engine, BenchmarkStepArguments(sixArguments, snapshot, settings, dimensionMode, iteration));
                    Invoke(engine, "ReadBackAntFields");
                    int[] remaining = Field<int[]>(engine, "antFoodRemainingReadback");
                    long total = 0;
                    int cells = 0;
                    if (remaining != null)
                    {
                        for (int v = 0; v < remaining.Length; v++)
                        {
                            if (remaining[v] > 0) { total += remaining[v]; cells++; }
                        }
                    }
                    Console.WriteLine("    iteration " + iteration.ToString().PadLeft(4)
                        + "  food cells " + cells + "  total " + total);
                }

                Console.WriteLine();
                return;
            }

            if (tracePopulation > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  DIAG settings.Death=" + Field<object>(settings, "Death")
                    + " settings.Division=" + Field<object>(settings, "Division")
                    + " DeathMinimumAge=" + Field<object>(settings, "DeathMinimumAge")
                    + " DeathMinNeighbours=" + Field<object>(settings, "DeathMinimumNeighbours")
                    + " DeathMaxNeighbours=" + Field<object>(settings, "DeathMaximumNeighbours"));
                Console.WriteLine("  population trace (minPopulation="
                    + Field<object>(settings, "MinimumPopulation") + ", randomDeath=" + randomDeath
                    + ", randomDivision=" + randomDivision + ")");
                for (int i = 0; i < tracePopulation; i++)
                {
                    iteration++;
                    step.Invoke(engine, BenchmarkStepArguments(sixArguments, snapshot, settings, dimensionMode, iteration));
                    Invoke(engine, "ReadBackPopulationState");
                    int live = Field<int>(engine, "particleCount");
                    if (i < 12 || i % 25 == 0 || i == tracePopulation - 1)
                    {
                        Console.WriteLine("    iteration " + iteration.ToString().PadLeft(5) + "  particles " + live.ToString("N0"));
                    }
                }

                Console.WriteLine();
                return;
            }

            // Warm the driver, shader cache and JIT before any timing starts.
            for (int i = 0; i < warmup; i++)
            {
                iteration++;
                step.Invoke(engine, BenchmarkStepArguments(sixArguments, snapshot, settings, dimensionMode, iteration));
            }

            Invoke(engine, "ReadBackDensity");

            for (int repeat = 0; repeat < repeats; repeat++)
            {
                List<double> t = new List<double>();
                List<double> pa = new List<double>();
                List<double> po = new List<double>();
                List<double> di = new List<double>();
                Stopwatch wall = Stopwatch.StartNew();

                for (int i = 0; i < steps; i++)
                {
                    iteration++;
                    object result = step.Invoke(
                        engine, BenchmarkStepArguments(sixArguments, snapshot, settings, dimensionMode, iteration));
                    if (result == null) continue;
                    t.Add(Field<double>(result, "TotalMilliseconds"));
                    pa.Add(Field<double>(result, "ParticleMilliseconds"));
                    po.Add(Field<double>(result, "PopulationMilliseconds"));
                    di.Add(Field<double>(result, "DiffusionMilliseconds"));
                    passes = Field<int>(result, "Passes");
                }

                // Close the batch on a real GPU sync so wall time includes execution.
                Invoke(engine, "ReadBackDensity");
                wall.Stop();

                double wallPerStep = steps > 0 ? wall.Elapsed.TotalMilliseconds / steps : 0;
                wallTimes.Add(wallPerStep);
                totals.Add(Median(t));
                particleStage.Add(Median(pa));
                populationStage.Add(Median(po));
                diffusionStage.Add(Median(di));
                Console.WriteLine(
                    "  repeat " + (repeat + 1) + "/" + repeats
                    + "  wall " + wallPerStep.ToString("F3", CultureInfo.InvariantCulture) + " ms/step");
            }
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }

        double spreadPercent = 0;
        if (wallTimes.Count > 1)
        {
            List<double> sortedWall = new List<double>(wallTimes);
            sortedWall.Sort();
            double lowest = sortedWall[0];
            if (lowest > 0) spreadPercent = (sortedWall[sortedWall.Count - 1] - lowest) / lowest * 100.0;
        }

        Console.WriteLine();
        Console.WriteLine("  WALL ms/step    " + Median(wallTimes).ToString("F3", CultureInfo.InvariantCulture)
            + "   (repeat spread " + spreadPercent.ToString("F1", CultureInfo.InvariantCulture)
            + "%; treat a result as usable only well below the 5% gate)");
        Console.WriteLine();
        Console.WriteLine("  CPU submission only (async dispatch, not GPU time):");
        Console.WriteLine("    particles     " + Median(particleStage).ToString("F3", CultureInfo.InvariantCulture));
        Console.WriteLine("    population    " + Median(populationStage).ToString("F3", CultureInfo.InvariantCulture));
        Console.WriteLine("    diffusion     " + Median(diffusionStage).ToString("F3", CultureInfo.InvariantCulture));
        Console.WriteLine("    total         " + Median(totals).ToString("F3", CultureInfo.InvariantCulture));
        Console.WriteLine();
        Console.WriteLine(
            "GPU_BENCHMARK_RESULT wallMsPerStep="
            + Median(wallTimes).ToString("F4", CultureInfo.InvariantCulture)
            + " submitMedianOfMedians="
            + Median(totals).ToString("F4", CultureInfo.InvariantCulture)
            + " particles=" + Median(particleStage).ToString("F4", CultureInfo.InvariantCulture)
            + " population=" + Median(populationStage).ToString("F4", CultureInfo.InvariantCulture)
            + " diffusion=" + Median(diffusionStage).ToString("F4", CultureInfo.InvariantCulture)
            + " passes=" + passes);
    }

    static bool BenchmarkSyncVoxels;
    static bool BenchmarkSyncParticles;

    static object[] BenchmarkStepArguments(
        bool sixArguments, object snapshot, object settings, object dimensionMode, int iteration)
    {
        bool sv = BenchmarkSyncVoxels;
        bool sp = BenchmarkSyncParticles;
        return sixArguments
            ? new object[] { settings, dimensionMode, iteration, sv, sp, false }
            : new object[] { Field<object>(snapshot, "Field"), null, settings, dimensionMode, iteration, sv, sp, false };
    }

    static float[] SparseFoodMap(int voxelCount, int grid, uint salt)
    {
        // A handful of point sources, which is how food is authored in practice.
        float[] map = new float[voxelCount];
        int sources = Math.Max(1, grid / 8);
        for (int i = 0; i < sources; i++)
        {
            uint scattered = BenchmarkHash((uint)i * 2654435761u ^ salt);
            map[(int)(scattered % (uint)voxelCount)] = 1.0f;
        }

        return map;
    }

    static double Median(List<double> values)
    {
        if (values == null || values.Count == 0) return 0;
        List<double> sorted = new List<double>(values);
        sorted.Sort();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    static uint BenchmarkHash(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }

    static int BenchmarkInt(string[] args, string option, int fallback)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0 || index + 1 >= args.Length) return fallback;
        return int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    }

    static double BenchmarkDouble(string[] args, string option, double fallback)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0 || index + 1 >= args.Length) return fallback;
        return double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
    }

    static object CaptureGpuSignatureSnapshot(object inputField, int particleCount)
    {
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        Type particleType = RequiredCompatibilityType("Nuclei4.Particle");
        object group = Activator.CreateInstance(groupType)!;
        SetField(group, "speed", 0.72);
        SetField(group, "sensorDistance", 2.35);
        SetField(group, "sensorAngle", 37);
        SetField(group, "rotationAngle", 29);
        SetField(group, "depositValue", 0.85);
        SetField(group, "wanderFrequency", 0.13);
        SetField(group, "baseWanderFrequency", 0.0);
        SetField(group, "color", System.Drawing.Color.FromArgb(255, 72, 184, 112));
        SetField(group, "ant", BenchmarkAntParticles);

        IList particles = (IList)Field<object>(group, "particles");
        for (int i = 0; i < particleCount; i++)
        {
            double x = 2.25 + ((i * 7) % 17);
            double y = 2.25 + ((i * 5) % 13);
            double z = 2.25 + ((i * 3) % 8);
            object plane = CreateRawPlane(x, y, z);
            object particle = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleType);
            SetField(particle, "pPlane", plane);
            SetField(particle, "home", plane);
            SetField(particle, "age", (i * 11) % 43);
            SetField(particle, "foundFood", false);
            FieldInfo trailsField = particleType.GetField("trails", BindingFlags.Instance | BindingFlags.Public);
            if (trailsField != null) trailsField.SetValue(particle, Activator.CreateInstance(trailsField.FieldType));
            particles.Add(particle);
        }

        IList groups = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(groupType))!;
        groups.Add(group);

        object snapshot = Activator.CreateInstance(SnapshotType, nonPublic: true)!;
        Invoke(snapshot, "DetectPopulationKinds", groups);
        Invoke(snapshot, "CaptureCompactVoxels", inputField, false);
        Invoke(snapshot, "CaptureParticleGroups", groups);

        int resolutionY = Field<int>(snapshot, "ResY");
        int resolutionZ = Field<int>(snapshot, "ResZ");
        float[] positions = new float[particleCount * 3];
        float[] directions = new float[particleCount * 3];
        float[] yAxes = new float[particleCount * 3];
        float[] homes = new float[particleCount * 3];
        uint[] antStates = new uint[particleCount];
        int[] ages = new int[particleCount];
        int[] groupIndices = new int[particleCount];
        int[] parentIndices = new int[particleCount];
        Type particleListType = RequiredCompatibilityType("Nuclei4.ParticleList");
        IList snapshotParticles = (IList)CreateNativeFreeParticleList(particleListType, particleType, particleCount);
        for (int i = 0; i < particleCount; i++)
        {
            object particle = particles[i]!;
            object plane = Field<object>(particle, "pPlane");
            object origin = PropertyValue(plane, "Origin");
            double x = Convert.ToDouble(PropertyValue(origin, "X"));
            double y = Convert.ToDouble(PropertyValue(origin, "Y"));
            double z = Convert.ToDouble(PropertyValue(origin, "Z"));
            int offset = i * 3;
            positions[offset] = (float)x;
            positions[offset + 1] = (float)y;
            positions[offset + 2] = (float)z;
            directions[offset] = 1;
            yAxes[offset + 1] = 1;
            homes[offset] = (float)x;
            homes[offset + 1] = (float)y;
            homes[offset + 2] = (float)z;
            ages[i] = Field<int>(particle, "age");
            parentIndices[i] = (int)Math.Floor(x) * resolutionY * resolutionZ
                + (int)Math.Floor(y) * resolutionZ
                + (int)Math.Floor(z);
            snapshotParticles.Add(particle);
        }

        SetField(snapshot, "Particles", snapshotParticles);
        SetField(snapshot, "ParticleCount", particleCount);
        SetField(snapshot, "ParticlePositionsXyz", positions);
        SetField(snapshot, "ParticleDirectionsXyz", directions);
        SetField(snapshot, "ParticleYAxesXyz", yAxes);
        SetField(snapshot, "ParticleHomesXyz", homes);
        SetField(snapshot, "ParticleAntStates", antStates);
        SetOptionalField(snapshot, "ParticleAges", ages);
        SetField(snapshot, "ParticleGroupIndices", groupIndices);
        SetField(snapshot, "ParticleParentIndices", parentIndices);
        return snapshot;
    }

    static string GpuDensitySignatureRecord(int iteration, float[] density)
    {
        ReadOnlySpan<byte> exactBytes = MemoryMarshal.AsBytes<float>(density.AsSpan());
        string exactHash = Convert.ToHexString(SHA256.HashData(exactBytes));
        int[] quantized = new int[density.Length];
        int nonzero = 0;
        double sum = 0;
        double weightedSum = 0;
        float maximum = float.MinValue;
        for (int i = 0; i < density.Length; i++)
        {
            float value = density[i];
            quantized[i] = checked((int)Math.Round(value * 1_000_000.0, MidpointRounding.AwayFromZero));
            if (value != 0) nonzero++;
            sum += value;
            weightedSum += value * (i + 1.0);
            if (value > maximum) maximum = value;
        }

        ReadOnlySpan<byte> quantizedBytes = MemoryMarshal.AsBytes<int>(quantized.AsSpan());
        string quantizedHash = Convert.ToHexString(SHA256.HashData(quantizedBytes));
        return "GPU_DENSITY iteration=" + iteration
            + " exact=" + exactHash
            + " q1e6=" + quantizedHash
            + " count=" + density.Length
            + " nonzero=" + nonzero
            + " sum=" + sum.ToString("R", CultureInfo.InvariantCulture)
            + " weighted=" + weightedSum.ToString("R", CultureInfo.InvariantCulture)
            + " max=" + maximum.ToString("R", CultureInfo.InvariantCulture);
    }

    static void TestGpuWrapTransitions()
    {
        object snapshot = CaptureVoxelSnapshot(CreateField(CreateFullDomain(5, 5, 1)));
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
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

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        FieldInfo wrapField = settingsType.GetField("WrapBoundaries")!;
        wrapField.SetValue(settings, false);
        Type dimensionType = RequiredImplementationType("Nuclei4.SolverGpuDimensionMode");
        object dimensionMode = InvokeStatic(dimensionType, "FromResolution", 5, 5, 1);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");

        object engine = CreateGpuEngine(engineType, snapshot, settings, false, false, false, 0, 1);
        try
        {
            wrapField.SetValue(settings, true);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            Near(4.85, ReadBackParticleX(engine), 1e-4, "periodic wrap preserves movement overshoot");

            wrapField.SetValue(settings, false);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 1);
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
        Type generatorType = RequiredCompatibilityType("Nuclei4.ParticleGenerator");
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

    static void BenchmarkGpuNoReadbackSteps()
    {
        const int resolutionX = 512;
        const int resolutionY = 512;
        const int resolutionZ = 1;
        const int particleCount = 262_144;
        const int warmupCount = 8;
        const int batchCount = 7;
        const int stepsPerBatch = 16;

        object snapshot = CaptureVoxelSnapshot(CreateField(CreateFullDomain(resolutionX, resolutionY, resolutionZ)));
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        Array groups = Array.CreateInstance(groupType, 1);
        groups.SetValue(Activator.CreateInstance(groupType), 0);
        SetField(snapshot, "Particles", null);
        SetField(snapshot, "ParticleGroups", groups);
        SetField(snapshot, "ParticleCount", particleCount);
        SetField(snapshot, "GroupCount", 1);

        float[] positions = new float[particleCount * 3];
        float[] directions = new float[particleCount * 3];
        float[] yAxes = new float[particleCount * 3];
        float[] homes = new float[particleCount * 3];
        int[] parentIndices = new int[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            int x = i % resolutionX;
            int y = (i / resolutionX) % resolutionY;
            int offset = i * 3;
            positions[offset] = homes[offset] = x + 0.5f;
            positions[offset + 1] = homes[offset + 1] = y + 0.5f;
            positions[offset + 2] = homes[offset + 2] = 0.5f;
            directions[offset] = 1.0f;
            yAxes[offset + 1] = 1.0f;
            parentIndices[i] = x * resolutionY + y;
        }

        SetField(snapshot, "ParticlePositionsXyz", positions);
        SetField(snapshot, "ParticleDirectionsXyz", directions);
        SetField(snapshot, "ParticleYAxesXyz", yAxes);
        SetField(snapshot, "ParticleHomesXyz", homes);
        SetField(snapshot, "ParticleAntStates", new uint[particleCount]);
        SetOptionalField(snapshot, "ParticleAges", new int[particleCount]);
        SetField(snapshot, "ParticleGroupIndices", new int[particleCount]);
        SetField(snapshot, "ParticleParentIndices", parentIndices);
        SetField(snapshot, "GroupData0", new[] { 1.0f, 1.0f, 0.0f, 0.0f });
        SetField(snapshot, "GroupData1", new[] { 0.0f, 0.0f, 0.0f, 100.0f });
        SetField(snapshot, "GroupColorData", new float[4]);

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "WrapBoundaries", true);
        SetField(settings, "DynamicPopulation", false);
        Type dimensionType = RequiredImplementationType("Nuclei4.SolverGpuDimensionMode");
        object dimensionMode = InvokeStatic(dimensionType, "FromResolution", resolutionX, resolutionY, resolutionZ);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object engine = CreateGpuEngine(engineType, snapshot, settings, false, false, false, 0, 1);
        try
        {
            MethodInfo step = RequiredNoReadbackStepMethod(engine.GetType());
            MethodInfo synchronize = engine.GetType().GetMethod(
                "SynchronizeActiveParticleCount",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(engine.GetType().FullName, "SynchronizeActiveParticleCount");
            for (int i = 0; i < warmupCount; i++)
            {
                InvokeGpuStep(step, engine, snapshot, settings, dimensionMode, i + 2);
                synchronize.Invoke(engine, null);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            double[] samples = new double[batchCount];
            for (int batch = 0; batch < batchCount; batch++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int item = 0; item < stepsPerBatch; item++)
                {
                    int iteration = warmupCount + batch * stepsPerBatch + item + 2;
                    InvokeGpuStep(step, engine, snapshot, settings, dimensionMode, iteration);
                }
                synchronize.Invoke(engine, null);
                samples[batch] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / stepsPerBatch;
            }

            Array.Sort(samples);
            double median = samples[samples.Length / 2];
            string backend = BackendDescription(engine);
            Console.WriteLine(
                "GPU synchronized-step benchmark: median=" + median.ToString("0.000", CultureInfo.InvariantCulture)
                + " ms, p95=" + samples[(int)Math.Floor((samples.Length - 1) * 0.95)].ToString("0.000", CultureInfo.InvariantCulture)
                + " ms, batches=" + batchCount
                + ", stepsPerBatch=" + stepsPerBatch
                + ", warmups=" + warmupCount
                + ", particles=" + particleCount
                + ", voxels=" + (resolutionX * resolutionY * resolutionZ)
                + ", grid=" + resolutionX + "x" + resolutionY + "x" + resolutionZ
                + ", backend=" + backend
                + ", engineAssembly=" + engine.GetType().Assembly.GetName().Name);
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }
    }

    static string BackendDescription(object engine)
    {
        PropertyInfo property = engine.GetType().GetProperty(
            "Capabilities",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object capabilities = property?.GetValue(engine);
        if (capabilities == null)
        {
            return "Direct3D11 (legacy capability metadata unavailable)";
        }

        string name = Field<string>(capabilities, "BackendName") ?? "Direct3D11";
        bool hardware = Field<bool>(capabilities, "HardwareAccelerated");
        bool softwareFallback = Field<bool>(capabilities, "SoftwareFallback");
        return name + (hardware ? " hardware" : softwareFallback ? " WARP" : " unknown-adapter");
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
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
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
        Type mapType = RequiredCompatibilityType("Nuclei4.VoxelScalarMap");
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

    static object CreateInstance(Type type, params object[] arguments)
    {
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: arguments,
            culture: CultureInfo.InvariantCulture)!;
    }

    static object CreateNativeFreeParticleList(Type particleListType, Type particleType, int capacity)
    {
        object list = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleListType);
        Type listBaseType = particleListType.BaseType!;
        listBaseType.GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(list, Array.CreateInstance(particleType, Math.Max(1, capacity)));
        listBaseType.GetField("_size", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(list, 0);
        listBaseType.GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(list, 0);

        Type previewCacheType = RequiredCompatibilityType("Nuclei4.ParticlePreviewCache");
        object previewCache = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(previewCacheType);
        SetField(list, "PreviewCache", previewCache);
        return list;
    }

    static bool TryApplyParticlesThroughPreviewBoundary(
        object sink,
        object view,
        object settings,
        int iteration,
        bool buildPreviewCache)
    {
        try
        {
            Invoke(sink, "ApplyParticles", view, settings, iteration, buildPreviewCache);
            return true;
        }
        catch (Exception exception) when (IsRhinoNativePreviewUnavailable(exception))
        {
            // The materializer is managed throughout now; PointCloud is the only Rhino
            // native surface left, and it is only reached when a preview is built.
            string expectedBoundary = "Rhino.Geometry.PointCloud";
            True(
                ExceptionContains(exception, expectedBoundary),
                "particle materialization reached an unexpected Rhino native boundary; expected " + expectedBoundary);
            return false;
        }
    }

    static bool TryApplyPreviewPositionsThroughNativeBoundary(object sink, object view)
    {
        try
        {
            return (bool)Invoke(sink, "ApplyPreviewPositions", view);
        }
        catch (Exception exception) when (IsRhinoNativePreviewUnavailable(exception))
        {
            True(
                ExceptionContains(exception, "Rhino.Geometry.PointCloud"),
                "position-only preview reached an unexpected Rhino native boundary");
            return false;
        }
    }

    static bool IsRhinoNativePreviewUnavailable(Exception exception)
    {
        for (Exception current = exception; current != null; current = current.InnerException)
        {
            if (current is DllNotFoundException || current is EntryPointNotFoundException)
            {
                return true;
            }
        }
        return false;
    }

    static bool ExceptionContains(Exception exception, string value)
    {
        for (Exception current = exception; current != null; current = current.InnerException)
        {
            if (current.ToString().IndexOf(value, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    static void VerifyManagedPreviewStaging(object particle, float[] previewPositions)
    {
        Type buildCacheType = RequiredCompatibilityType("Nuclei4.ParticlePreviewBuildCache");
        object buildCache = CreateInstance(buildCacheType, 1);
        object point = CreatePoint3d(previewPositions[0], previewPositions[1], previewPositions[2]);
        Invoke(buildCache, "AddParticlePoint", particle, point);
        True(Field<bool>(buildCache, "HasPoint"), "managed preview staging did not include its point");
        object clippingBox = Field<object>(buildCache, "ClippingBox");
        AssertPoint(PropertyValue(clippingBox, "Min"), 0.25, 1.75, 0.5, "managed preview clipping minimum");
        AssertPoint(PropertyValue(clippingBox, "Max"), 0.25, 1.75, 0.5, "managed preview clipping maximum");
    }

    static void VerifyNativeFreeParticleHelpers(
        object sink,
        IList particles,
        IList groupParticles,
        object particle,
        object settings)
    {
        object parentVoxel = Invoke(sink, "VoxelFromFlatIndex", 2);
        AssertParentVoxel(parentVoxel);
        SetField(particle, "parentVoxel", parentVoxel);

        IList trails = (IList)Field<object>(particle, "trails");
        trails.Clear();
        SetField(particle, "pPlane", CreateRawPlane(1.25, 0.5, 0.5));
        // Start from an empty list so this exercises exactly one particle. The
        // materializer is managed further than it used to be, so it can legitimately
        // leave entries here before this helper runs.
        particles.Clear();
        groupParticles.Clear();
        particles.Add(particle);
        groupParticles.Add(particle);
        Invoke(sink, "RecordTrails", settings, 2);
        Equal(1, trails.Count, "native-free sampled trail count");
        AssertPoint(trails[0], 1.25, 0.5, 0.5, "native-free sampled trail point");

        SetField(particle, "pPlane", CreateRawPlane(1.4, 0.5, 0.5));
        Invoke(sink, "RecordTrails", settings, 3);
        Equal(1, trails.Count, "native-free non-sampled trail count");
        AssertPoint(trails[0], 1.4, 0.5, 0.5, "native-free non-sampled trail head");

        SetField(particle, "pPlane", CreateRawPlane(1.5, 0.5, 0.5));
        Invoke(sink, "RecordTrails", settings, 4);
        Equal(2, trails.Count, "native-free second trail sample count");
        AssertPoint(trails[0], 1.5, 0.5, 0.5, "native-free second trail sample");
    }

    static object CreateOrientedPlane(int i, double x, double y, double z)
    {
        double hx, hy, hz, ux, uy, uz;
        HeadingFor(i, out hx, out hy, out hz, out ux, out uy, out uz);
        Type planeType = RequiredExternalType("Rhino.Geometry.Plane, RhinoCommon");
        Type vectorType = RequiredExternalType("Rhino.Geometry.Vector3d, RhinoCommon");
        object plane = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(planeType);
        SetField(plane, "m_origin", CreatePoint3d(x, y, z));
        SetField(plane, "m_xaxis", Activator.CreateInstance(vectorType, hx, hy, hz));
        SetField(plane, "m_yaxis", Activator.CreateInstance(vectorType, ux, uy, uz));
        SetField(plane, "m_zaxis", Activator.CreateInstance(vectorType,
            hy * uz - hz * uy, hz * ux - hx * uz, hx * uy - hy * ux));
        return plane;
    }

    static object CreateRawPlane(double x, double y, double z)
    {
        Type planeType = RequiredExternalType("Rhino.Geometry.Plane, RhinoCommon");
        Type vectorType = RequiredExternalType("Rhino.Geometry.Vector3d, RhinoCommon");
        object plane = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(planeType);
        SetField(plane, "m_origin", CreatePoint3d(x, y, z));
        SetField(plane, "m_xaxis", Activator.CreateInstance(vectorType, 1.0, 0.0, 0.0));
        SetField(plane, "m_yaxis", Activator.CreateInstance(vectorType, 0.0, 1.0, 0.0));
        SetField(plane, "m_zaxis", Activator.CreateInstance(vectorType, 0.0, 0.0, 1.0));
        return plane;
    }

    static void AssertParentVoxel(object parentVoxel)
    {
        True(parentVoxel != null, "particle parent voxel was not materialized");
        Equal(1, Field<int>(parentVoxel, "idX"), "parent voxel X");
        Equal(0, Field<int>(parentVoxel, "idY"), "parent voxel Y");
        Equal(0, Field<int>(parentVoxel, "idZ"), "parent voxel Z");
    }

    static object CreatePoint3d(double x, double y, double z)
    {
        Type pointType = RequiredExternalType("Rhino.Geometry.Point3d, RhinoCommon");
        return Activator.CreateInstance(pointType, x, y, z)!;
    }

    static void AssertParticlePlane(
        object particle,
        double originX,
        double originY,
        double originZ,
        double xAxisX,
        double xAxisY,
        double xAxisZ,
        double yAxisX,
        double yAxisY,
        double yAxisZ)
    {
        object plane = Field<object>(particle, "pPlane");
        AssertPoint(PropertyValue(plane, "Origin"), originX, originY, originZ, "particle plane origin");
        AssertVector(PropertyValue(plane, "XAxis"), xAxisX, xAxisY, xAxisZ, "particle plane X axis");
        AssertVector(PropertyValue(plane, "YAxis"), yAxisX, yAxisY, yAxisZ, "particle plane Y axis");
    }

    static void AssertPoint(object point, double x, double y, double z, string message)
    {
        Near(x, Convert.ToDouble(PropertyValue(point, "X")), 1e-6, message + " X");
        Near(y, Convert.ToDouble(PropertyValue(point, "Y")), 1e-6, message + " Y");
        Near(z, Convert.ToDouble(PropertyValue(point, "Z")), 1e-6, message + " Z");
    }

    static void AssertVector(object vector, double x, double y, double z, string message)
    {
        Near(x, Convert.ToDouble(PropertyValue(vector, "X")), 1e-6, message + " X");
        Near(y, Convert.ToDouble(PropertyValue(vector, "Y")), 1e-6, message + " Y");
        Near(z, Convert.ToDouble(PropertyValue(vector, "Z")), 1e-6, message + " Z");
    }

    static int PreviewFieldIndex(string name)
    {
        Type previewFieldType = RequiredImplementationType("Nuclei4.VoxelPreviewField");
        return (int)previewFieldType.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    }

    static double StaticPreviewValue(object engine, int flatIndex, string fieldName)
    {
        return Convert.ToDouble(Invoke(engine, "StaticVoxelFieldValue", flatIndex, PreviewFieldIndex(fieldName)));
    }

    static object CreateGpuEngine(
        Type engineType,
        object snapshot,
        object settings,
        bool densityPreview,
        bool particlePreview,
        bool trailPreview,
        int trailSize,
        int densityScale)
    {
        ConstructorInfo splitConstructor = engineType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(item => item.GetParameters().Length == 8);
        if (splitConstructor != null)
        {
            object outputSink = CreateGpuOutputSink();
            return splitConstructor.Invoke(new[]
            {
                snapshot,
                outputSink,
                settings,
                (object)densityPreview,
                particlePreview,
                trailPreview,
                trailSize,
                densityScale
            });
        }

        ConstructorInfo legacyConstructor = engineType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(item => item.GetParameters().Length == 7);
        if (legacyConstructor != null)
        {
            return legacyConstructor.Invoke(new[]
            {
                snapshot,
                settings,
                (object)densityPreview,
                particlePreview,
                trailPreview,
                trailSize,
                densityScale
            });
        }

        throw new MissingMethodException(engineType.FullName, "supported GPU engine constructor");
    }

    static object BenchmarkRealSinkSnapshot;
    static int BenchmarkRealSinkCapacity;

    static object CreateGpuOutputSink()
    {
        Type sinkType = RequiredCompatibilityType("Nuclei4.Gh1GpuSolverOutputSink");
        if (BenchmarkRealSinkSnapshot != null)
        {
            // The real GH1 sink, so a readback measurement includes the managed
            // particle materialization the stub skips entirely.
            ConstructorInfo real = sinkType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(c => c.GetParameters().Length == 2);
            return real.Invoke(new object[] { BenchmarkRealSinkSnapshot, BenchmarkRealSinkCapacity });
        }

        return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(sinkType);
    }

    static void InvokeGpuStep(
        object engine,
        object snapshot,
        object settings,
        object dimensionMode,
        int iteration)
    {
        MethodInfo step = RequiredNoReadbackStepMethod(engine.GetType());
        InvokeGpuStep(step, engine, snapshot, settings, dimensionMode, iteration);
    }

    static MethodInfo RequiredNoReadbackStepMethod(Type engineType)
    {
        MethodInfo split = engineType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(item => item.Name == "Step" && item.GetParameters().Length == 6);
        if (split != null)
        {
            return split;
        }

        MethodInfo legacy = engineType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(item => item.Name == "Step" && item.GetParameters().Length == 8);
        return legacy ?? throw new MissingMethodException(engineType.FullName, "supported no-readback Step overload");
    }

    static void InvokeGpuStep(
        MethodInfo step,
        object engine,
        object snapshot,
        object settings,
        object dimensionMode,
        int iteration)
    {
        object[] arguments = step.GetParameters().Length == 6
            ? new[] { settings, dimensionMode, (object)iteration, false, false, false }
            : new[] { Field<object>(snapshot, "Field"), null, settings, dimensionMode, (object)iteration, false, false, false };
        step.Invoke(engine, arguments);
    }

    static Type RequiredCompatibilityType(string name)
    {
        Type type = NucleiAssembly.GetType(name, throwOnError: false);
        if (type == null)
        {
            throw new TypeLoadException(
                "Compatibility type '" + name + "' was not found in " + NucleiAssembly.Location + ".");
        }

        return type;
    }

    static Type RequiredImplementationType(string name)
    {
        for (int i = 0; i < NucleiAssemblies.Count; i++)
        {
            Type type = NucleiAssemblies[i].GetType(name, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        throw new TypeLoadException(
            "Implementation type '" + name + "' was not found in the V4 compatibility or support assemblies in "
            + NucleiDirectory + ".");
    }

    static Type RequiredExternalType(string assemblyQualifiedName)
    {
        Type type = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (type == null)
        {
            throw new TypeLoadException("External type '" + assemblyQualifiedName + "' could not be loaded.");
        }

        return type;
    }

    static void LoadNuclei(string[] args)
    {
        string explicitOutput = OptionValue(args, "--nuclei-output");
        NucleiDirectory = explicitOutput != null
            ? Path.GetFullPath(explicitOutput)
            : ResolveDefaultNucleiDirectory();
        string nucleiPath = Directory.Exists(NucleiDirectory)
            ? Path.Combine(NucleiDirectory, "Nuclei4.gha")
            : NucleiDirectory;
        if (!File.Exists(nucleiPath))
        {
            throw new FileNotFoundException(
                "Nuclei4.gha was not found. Build the net7.0-windows Release target or pass --nuclei-output <directory-or-gha>.",
                nucleiPath);
        }

        NucleiDirectory = Path.GetDirectoryName(nucleiPath)!;
        string packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string localCandidate = Path.Combine(NucleiDirectory, name.Name + ".dll");
            if (File.Exists(localCandidate))
            {
                return LoadAssemblyOnce(localCandidate);
            }

            if (RhinoInsideRoot != null
                && (string.Equals(name.Name, "RhinoCommon", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name.Name, "Rhino.Runtime.InProcess", StringComparison.OrdinalIgnoreCase)))
            {
                string installed = Path.Combine(RhinoInsideRoot, name.Name + ".dll");
                if (File.Exists(installed))
                {
                    return LoadAssemblyOnce(installed);
                }
            }

            string packageId = string.Equals(name.Name, "RhinoCommon", StringComparison.OrdinalIgnoreCase)
                ? "rhinocommon"
                : string.Equals(name.Name, "Grasshopper", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name.Name, "GH_IO", StringComparison.OrdinalIgnoreCase)
                    ? "grasshopper"
                    : name.Name.ToLowerInvariant();
            if (packageId != null)
            {
                string packageCandidate = FindPackageAssembly(packageRoot, packageId, name.Name + ".dll");
                if (packageCandidate != null)
                {
                    return LoadAssemblyOnce(packageCandidate);
                }
            }

            return null;
        };

        NucleiAssembly = LoadAssemblyOnce(nucleiPath);
        List<Assembly> assemblies = new List<Assembly> { NucleiAssembly };
        List<string> missingSupportAssemblies = new List<string>();
        for (int i = 0; i < SupportAssemblyNames.Length; i++)
        {
            string path = Path.Combine(NucleiDirectory, SupportAssemblyNames[i] + ".dll");
            if (!File.Exists(path))
            {
                missingSupportAssemblies.Add(SupportAssemblyNames[i]);
                continue;
            }

            assemblies.Add(LoadAssemblyOnce(path));
        }

        if (missingSupportAssemblies.Count != 0 && missingSupportAssemblies.Count != SupportAssemblyNames.Length)
        {
            throw new FileNotFoundException(
                "The V4 deployment has only part of the architecture support set. Missing next to Nuclei4.gha: "
                + string.Join(", ", missingSupportAssemblies) + ".");
        }

        NucleiAssemblies = assemblies;
        GridType = RequiredCompatibilityType("Nuclei4.VoxelGridData");
        FieldType = RequiredCompatibilityType("Nuclei4.VoxelField");
        SnapshotType = RequiredCompatibilityType("Nuclei4.SolverGpuInputSnapshot");
    }

    static Assembly LoadAssemblyOnce(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Assembly loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
            item => !string.IsNullOrEmpty(item.Location)
                && string.Equals(Path.GetFullPath(item.Location), fullPath, StringComparison.OrdinalIgnoreCase));
        return loaded ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    static string ResolveDefaultNucleiDirectory()
    {
        string root = FindRepositoryRoot(Directory.GetCurrentDirectory())
            ?? FindRepositoryRoot(AppContext.BaseDirectory);
        if (root == null)
        {
            throw new DirectoryNotFoundException(
                "Could not locate the Nuclei repository root. Pass --nuclei-output <directory-or-gha>.");
        }

        string releaseRoot = Path.Combine(root, "Nuclei-v4", "Nuclei4", "bin", "Release");
        string preferred = Path.Combine(releaseRoot, "net7.0-windows");
        if (File.Exists(Path.Combine(preferred, "Nuclei4.gha")))
        {
            return preferred;
        }

        if (Directory.Exists(releaseRoot))
        {
            string discovered = Directory.EnumerateFiles(releaseRoot, "Nuclei4.gha", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (discovered != null)
            {
                return Path.GetDirectoryName(discovered)!;
            }
        }

        return preferred;
    }

    static string FindRepositoryRoot(string start)
    {
        DirectoryInfo directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory != null)
        {
            string marker = Path.Combine(directory.FullName, "Nuclei-v4", "Nuclei4", "Nuclei4.csproj");
            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    static string FindPackageAssembly(string packageRoot, string packageId, string fileName)
    {
        string packageDirectory = Path.Combine(packageRoot, packageId);
        if (!Directory.Exists(packageDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(packageDirectory, fileName, SearchOption.AllDirectories)
            .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "lib" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(path => path.IndexOf(Path.DirectorySeparatorChar + "net7", StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    static string OptionValue(string[] args, string option)
    {
        int index = Array.FindIndex(args, item => string.Equals(item, option, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(option + " requires a directory or GHA path.");
        }

        return args[index + 1];
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

    static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
        {
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        field.SetValue(target, value);
    }

    static void SetOptionalField(object target, string name, object value)
    {
        target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value);
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
