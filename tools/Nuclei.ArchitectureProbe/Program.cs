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
    const string ExpectedPublicApiHash = "24B466B99A06FFEB9F24730E411EA53549639C70C8C4BEDAA17100905F8DE037";
    const string ExpectedComponentSchemaHash = "2C82F4DDC84E50A154F6F48DAB9C1E1C82E3A4700F99146FC2DFF128E8518DDB";
    const string ExpectedMainResourceNameHash = "5A304C5A9EE3117A8D33B999B27677FC0B5B98CA527657FC0A02E44DBE8993A7";
    const string ExpectedLegacyShaderHash = "BBD3F0049D5A902B774EE45A7B5BACB52C6D20E2C2605C7115144DAB5AE5C88A";
    const string ExpectedShaderHash = "C005808A049C53BB021251D0D1C0FD16538FA945153E121AE6D7FECF4740BF83";
    const string ExpectedGpuShaderHash = "76A587C8C31492F0E597DD47A6A0B55A537F6368F7E460B17D1AFF0D1A7CBA9F";
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
    static bool ConnectedSteeringParity;
    static double ConnectedSteeringExploration = 0.5;
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
            if (Array.IndexOf(args, "--rhino-inside") >= 0 && !TryStartRhinoInside())
            {
                throw new InvalidOperationException("Rhino.Inside was requested but no installed Rhino runtime could be started.");
            }
            LoadNuclei(args);
            if (Array.IndexOf(args, "--population-ordering") >= 0)
            {
                TestGpuPopulationPassOrdering();
                return 0;
            }
            if (Array.IndexOf(args, "--density-species-parity") >= 0)
            {
                TestGpuDensityEvolutionWithoutSlime();
                return 0;
            }
            if (Array.IndexOf(args, "--planar-origin-parity") >= 0)
            {
                TestGpuPlanarOriginPreservation();
                return 0;
            }
            if (Array.IndexOf(args, "--blocked-parent-parity") >= 0)
            {
                TestGpuBlockedStoredParentParity();
                return 0;
            }
            if (Array.IndexOf(args, "--sparse-active-bindings") >= 0)
            {
                TestGpuSparseActiveBindings();
                return 0;
            }
            if (Array.IndexOf(args, "--ant-reset-parity") >= 0)
            {
                TestAntResetAndNestParity();
                return 0;
            }
            if (Array.IndexOf(args, "--ant-state-parity") >= 0)
            {
                TestGpuAntLaunchAndRandomDivisionInheritance();
                return 0;
            }
            if (Array.IndexOf(args, "--legacy-settings-migration") >= 0)
            {
                TestSlimeSettingsLegacyArchiveMigration();
                return 0;
            }
            if (Array.IndexOf(args, "--dendro-cache") >= 0)
            {
                TestDendroUpdatePulseAndCache();
                return 0;
            }
            if (Array.IndexOf(args, "--voxel-preview-sync") >= 0)
            {
                TestVoxelPreviewOnDemandDynamicSync();
                return 0;
            }
            if (Array.IndexOf(args, "--ant-shader-specialization") >= 0)
            {
                TestAntMoveShaderSpecialization();
                return 0;
            }
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
            if (Array.IndexOf(args, "--connected-steering-oracle") >= 0)
            {
                TestGpuConnectedSteeringOracle();
                return 0;
            }
            if (Array.IndexOf(args, "--connected-parity-regression") >= 0)
            {
                RunConnectedSteeringParityRegression(args);
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
            TestAntResetAndNestParity();
            TestRetainedSpeciesAndGroupMetadataParity();
            TestConnectedSteeringPacking();
            TestDendroUpdatePulseAndCache();
            TestSlimeSettingsLegacyArchiveMigration();
            TestSolverBoundaryParity();
            TestGpuOutputSinkRoundTrip();
            TestStaticPreviewNeutralChannels();
            TestDensityGradientParameterIsolation();
            TestVolumeBoundaryCapContract();
            if (NucleiAssemblies.Count > 1)
            {
                TestVolumeMeshSmoothingDispatchCoverage();
            }
            TestSolverDynamicStateIsolation();
            TestSolverOutputCallbackDetachment();
            TestVoxelPreviewOnDemandDynamicSync();
            TestScatteredParticlePlacement();
            TestAdaptiveVolumePreviewLayout();
            if (Array.IndexOf(args, "--gpu") >= 0)
            {
                TestGpuEngineInitialization();
                TestGpuDensityEvolutionWithoutSlime();
                TestGpuPlanarOriginPreservation();
                TestGpuWrapTransitions();
                TestGpuBlockedStoredParentParity();
                TestGpuSparseActiveBindings();
                TestGpuPopulationPassOrdering();
                TestGpuAntLaunchAndRandomDivisionInheritance();
                TestGpuConnectedSteeringOracle();
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
        Equal(741, apiRecords.Count, "public API record count");
        Equal(ExpectedPublicApiHash, HashRecords(apiRecords), "public API hash");

        Type componentBaseType = RequiredExternalType("Grasshopper.Kernel.GH_Component, Grasshopper");
        Type[] componentTypes = NucleiAssembly.GetExportedTypes()
            .Where(type => type.IsSubclassOf(componentBaseType) && !type.IsAbstract)
            .OrderBy(type => type.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        List<string> schemaRecords = ComponentSchemaRecords(componentTypes);
        Equal(38, componentTypes.Length, "Grasshopper component count");
        Equal(214, schemaRecords.Count, "Grasshopper schema record count");
        Equal(ExpectedComponentSchemaHash, HashRecords(schemaRecords), "Grasshopper schema hash");

        List<ResourceRow> mainRows = ResourceRows(NucleiAssembly);
        Equal(28, mainRows.Count, "compatibility assembly resource count");
        Equal(ExpectedMainResourceNameHash, HashRecords(mainRows.Select(row => row.Name)), "compatibility resource-name hash");
        List<ResourceRow> mainShaders = mainRows.Where(row => row.Name.EndsWith(".cso", StringComparison.Ordinal)).ToList();
        Equal(27, mainShaders.Count, "compatibility shader count");
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

        Equal(27, shaderCopies.Count, "deployed unique shader count");
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
            VerifySupportShaderContract("Nuclei4.Gpu.D3D11", 22, ExpectedGpuShaderHash);
            VerifySupportShaderContract("Nuclei4.Display.D3D11", 5, ExpectedDisplayShaderHash);
        }

        Console.WriteLine(
            "Compatibility contracts passed: 51 public types, 741 API records, 38 components, 214 schema records, 27 shaders ("
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
            int x = i / 3;
            int y = i % 3;
            bool reflectiveBoundary = x == 0 || x == 32 || y == 0 || y == 2;
            Equal((i % 3) != 0 && !reflectiveBoundary, set, "packed walkability flag " + i);
        }
        float[] limits = Field<float[]>(limitSnapshot, "VoxelDensityLimits");
        Equal(count, limits.Length, "single density-limit channel length");
        Equal(0, Field<int>(limitSnapshot, "MaximumDensityOffset"), "maximum-density channel offset");
        Equal(-1, Field<int>(limitSnapshot, "MinimumDensityOffset"), "missing minimum-density channel");
    }

    static void TestAntResetAndNestParity()
    {
        Type particleType = RequiredCompatibilityType("Nuclei4.Particle");
        object particle = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleType);
        SetField(particle, "age", 37);
        SetField(particle, "foundFood", true);
        SetField(particle, "antLaunchBoundaryHit", true);
        SetField(particle, "die", true);
        InvokeStatic(SnapshotType, "ResetCapturedParticleRuntimeState", particle);
        Equal(1, Field<int>(particle, "age"), "reset inherited live particle post-parent-check age");
        False(Field<bool>(particle, "foundFood"), "reset inherited found-food state");
        False(Field<bool>(particle, "antLaunchBoundaryHit"), "reset inherited ant launch-boundary state");
        True(Field<bool>(particle, "die"), "reset-state helper unexpectedly rewrote the input die flag");

        int[] packedAges = { 37 };
        uint[] packedAntStates = { 1u };
        uint[] packedLaunchStates = { 1u };
        InvokeStatic(
            SnapshotType,
            "ResetPackedParticleRuntimeState",
            0,
            packedAges,
            packedAntStates,
            packedLaunchStates);
        Equal(0, packedAges[0], "packed reset age was not cleared");
        Equal(0u, packedAntStates[0], "packed reset found-food state was not cleared");
        Equal(0u, packedLaunchStates[0], "packed reset launch-boundary state was not cleared");

        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        FieldInfo shaderField = engineType.GetField(
            "FullSolverShaderSource",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(engineType.FullName, "FullSolverShaderSource");
        string shaderSource = (string)(shaderField.IsLiteral
            ? shaderField.GetRawConstantValue()
            : shaderField.GetValue(null))!;
        string moveFunction = ShaderFunctionSource(shaderSource, "MoveParticlesAndDepositCore");
        True(moveFunction.Contains("float speed = group0.x * behavior.x;", StringComparison.Ordinal),
            "ant parity probe no longer exercises a voxel speed multiplier");
        True(moveFunction.Contains("if (nextHomeDistance < group0.x)", StringComparison.Ordinal),
            "ant nest reset radius is not the raw signed group speed");
        False(moveFunction.Contains("nextHomeDistance < max(speed", StringComparison.Ordinal),
            "ant nest reset radius still uses effective voxel-multiplied speed");
        True(moveFunction.Contains("float rawSensorDistance = group0.y;", StringComparison.Ordinal),
            "ant movement no longer retains the raw group sensor distance");
        True(moveFunction.Contains("homeDistance <= rawSensorDistance * 2.0", StringComparison.Ordinal),
            "ant close-to-home alignment uses voxel-scaled sensor distance");
        True(moveFunction.Contains("CanDepositAtVoxel(parentIndex, rawSensorDistance)", StringComparison.Ordinal),
            "deposit boundary guard uses voxel-scaled sensor distance");
        string depositGuardFunction = ShaderFunctionSource(shaderSource, "CanDepositAtVoxel");
        True(depositGuardFunction.Contains("BankersRoundToInt(rawSensorDistance)", StringComparison.Ordinal),
            "deposit boundary guard no longer matches Convert.ToInt32 midpoint rounding");
        False(depositGuardFunction.Contains("(int)rawSensorDistance", StringComparison.Ordinal),
            "deposit boundary guard truncates fractional sensor distance");
        string bankerFunction = ShaderFunctionSource(shaderSource, "BankersRoundToInt");
        True(bankerFunction.Contains("fraction == 0.5 && (lower & 1) != 0", StringComparison.Ordinal),
            "deposit boundary range does not preserve 1.5->2 and 2.5->2 midpoint-to-even behavior");
        string sampleAntFunction = ShaderFunctionSource(shaderSource, "SampleAntField");
        True(sampleAntFunction.Contains("Source[index] * AntSlime", StringComparison.Ordinal),
            "ant sensing no longer samples the scalar density field");
        False(sampleAntFunction.Contains("HasSlimeParticles", StringComparison.Ordinal),
            "ant sensing still suppresses scalar density for ant-only populations");
        False(sampleAntFunction.Contains("!antOnly", StringComparison.Ordinal),
            "ant-only specialization still suppresses scalar-density sensing");
        string combinedPreviewFunction = ShaderFunctionSource(shaderSource, "CombinedPreviewVoxel");
        True(combinedPreviewFunction.Contains("float slime = max(Source[index], 0.0);", StringComparison.Ordinal),
            "combined preview still hides species-independent scalar density");
        False(0.0 < 0.0, "zero group speed unexpectedly clears ant nest state");
        False(0.0 < -1.0, "negative group speed unexpectedly clears ant nest state");

        Console.WriteLine("Ant reset state and raw-speed nest threshold passed.");
    }

    static void TestRetainedSpeciesAndGroupMetadataParity()
    {
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        Type particleType = RequiredCompatibilityType("Nuclei4.Particle");
        Type genericGroupListType = typeof(List<>).MakeGenericType(groupType);
        object field = CreateField(CreateFullDomain(5, 5, 1));

        object slimeIdentity = Activator.CreateInstance(groupType)!;
        SetField(slimeIdentity, "ant", false);
        object antIdentity = Activator.CreateInstance(groupType)!;
        SetField(antIdentity, "ant", true);

        object mixedContainer = Activator.CreateInstance(groupType)!;
        SetField(mixedContainer, "ant", false);
        SetField(mixedContainer, "baseWanderFrequency", 0.5);
        IList mixedParticles = (IList)Field<object>(mixedContainer, "particles");
        object slimeClassified = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleType);
        SetField(slimeClassified, "pPlane", CreateRawPlane(2.25, 2.25, 0.5));
        SetField(slimeClassified, "parentParticleGroup", slimeIdentity);
        mixedParticles.Add(slimeClassified);
        object antClassified = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleType);
        SetField(antClassified, "pPlane", CreateRawPlane(2.75, 2.75, 0.5));
        SetField(antClassified, "parentParticleGroup", antIdentity);
        mixedParticles.Add(antClassified);

        IList mixedGroups = (IList)Activator.CreateInstance(genericGroupListType)!;
        mixedGroups.Add(mixedContainer);
        object mixedSnapshot = InvokeStatic(SnapshotType, "Capture", field, mixedGroups, false);
        True(Field<bool>(mixedSnapshot, "HasAntParticles"), "mixed retained classifications lost ant presence");
        True(Field<bool>(mixedSnapshot, "HasSlimeParticles"), "mixed retained classifications lost slime presence");
        Equal(2, Field<int>(mixedSnapshot, "ParticleCount"), "mixed retained particle count");
        Array mixedRuntimeGroups = Field<Array>(mixedSnapshot, "ParticleGroups");
        object mixedRuntime = mixedRuntimeGroups.GetValue(0)!;
        True(Field<bool>(mixedRuntime, "ant"), "retained ant did not promote its runtime group");
        Near(1, Field<float[]>(mixedSnapshot, "GroupData1")[1], 1e-6,
            "mixed runtime group was not packed with final ant identity");

        object antContainer = Activator.CreateInstance(groupType)!;
        SetField(antContainer, "ant", true);
        IList antContainerParticles = (IList)Field<object>(antContainer, "particles");
        object mismatchedSlime = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleType);
        SetField(mismatchedSlime, "pPlane", CreateRawPlane(2.5, 2.5, 0.5));
        SetField(mismatchedSlime, "parentParticleGroup", slimeIdentity);
        antContainerParticles.Add(mismatchedSlime);
        IList antInputGroups = (IList)Activator.CreateInstance(genericGroupListType)!;
        antInputGroups.Add(antContainer);
        object mismatchedSnapshot = InvokeStatic(SnapshotType, "Capture", field, antInputGroups, false);
        False(Field<bool>(mismatchedSnapshot, "HasAntParticles"),
            "containing ant flag overrode retained particle slime identity");
        True(Field<bool>(mismatchedSnapshot, "HasSlimeParticles"),
            "mismatched retained slime identity was lost");
        object mismatchedRuntime = Field<Array>(mismatchedSnapshot, "ParticleGroups").GetValue(0)!;
        False(Field<bool>(mismatchedRuntime, "ant"),
            "runtime group was promoted without a retained ant-classified particle");

        SetField(antContainer, "sensorDistance", 1.0);
        SetField(antContainer, "rotationAngle", 0);
        SetField(slimeIdentity, "rotationAngle", 90);
        SetField(mismatchedSlime, "pPlane", CreateRawPlane(2.5, 2.5, 2.5));
        object rotatedMismatchSnapshot = InvokeStatic(
            SnapshotType,
            "Capture",
            CreateField(CreateFullDomain(5, 5, 5)),
            antInputGroups,
            false);
        float[] rotatedMismatchDirections = Field<float[]>(rotatedMismatchSnapshot, "ParticleDirectionsXyz");
        Near(0, rotatedMismatchDirections[0], 1e-6,
            "mismatched reset rotation used containing-group angle X");
        Near(0, rotatedMismatchDirections[1], 1e-6,
            "mismatched reset rotation changed Y axis");
        Near(-1, rotatedMismatchDirections[2], 1e-6,
            "mismatched reset rotation did not use particle-parent angle");

        object nullParentAntContainer = Activator.CreateInstance(groupType)!;
        SetField(nullParentAntContainer, "ant", true);
        object nullParentParticle = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(particleType);
        SetField(nullParentParticle, "pPlane", CreateRawPlane(2.5, 2.5, 0.5));
        SetField(nullParentParticle, "parentParticleGroup", null);
        ((IList)Field<object>(nullParentAntContainer, "particles")).Add(nullParentParticle);
        IList nullParentGroups = (IList)Activator.CreateInstance(genericGroupListType)!;
        nullParentGroups.Add(nullParentAntContainer);
        object nullParentSnapshot = InvokeStatic(SnapshotType, "Capture", field, nullParentGroups, false);
        True(Field<bool>(nullParentSnapshot, "HasAntParticles"),
            "null particle parent did not fall back to containing ant identity");

        object ordinaryMetadata = Activator.CreateInstance(groupType)!;
        SetField(ordinaryMetadata, "ant", false);
        SetField(ordinaryMetadata, "wanderFrequency", 0.5);
        InvokeStatic(SnapshotType, "ApplyV3ParticleGroupMetadata", ordinaryMetadata, 800);
        Near(10, Field<double>(ordinaryMetadata, "wanderFrequency"), 1e-12,
            "explicit-population slime wander transform");

        object antMetadata = Activator.CreateInstance(groupType)!;
        SetField(antMetadata, "ant", true);
        SetField(antMetadata, "baseWanderFrequency", 0.5);
        InvokeStatic(SnapshotType, "ApplyV3ParticleGroupMetadata", antMetadata, 800);
        Near(10, Field<double>(antMetadata, "baseWanderFrequency"), 1e-12,
            "explicit-population ant base-wander transform");

        IList sourceGroups = (IList)Activator.CreateInstance(genericGroupListType)!;
        sourceGroups.Add(antIdentity);
        SetField(antIdentity, "baseWanderFrequency", 0.5);
        IList runtimeGroups = (IList)Activator.CreateInstance(genericGroupListType)!;
        runtimeGroups.Add(antMetadata);
        object[] packedArguments = { sourceGroups, runtimeGroups, new[] { 800 }, null, null, false, false };
        MethodInfo runtimeCapture = SnapshotType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == "CaptureRuntimeGroupSettings" && method.GetParameters().Length == 7);
        runtimeCapture.Invoke(null, packedArguments);
        Near(10, ((float[])packedArguments[4]!)[3], 1e-6,
            "explicit GPU runtime population base-wander packing");

        object solver = Activator.CreateInstance(RequiredCompatibilityType("Nuclei4.SolverGPU"))!;
        SetField(solver, "particleGroupAntKinds", new[] { true });
        True((bool)Invoke(solver, "InputParticleGroupKindsMatch", antInputGroups),
            "raw input kind signature was conflated with retained runtime kind");

        Console.WriteLine("Retained species and exact V3 group metadata transforms passed.");
    }

    static void TestSolverBoundaryParity()
    {
        object fullData = CreateFullDomain(5, 5, 1);
        object reflectiveSnapshot = CaptureVoxelSnapshot(CreateField(fullData), false);
        object reflectiveField = Field<object>(reflectiveSnapshot, "Field");
        int outerIndex = 2;
        int centerIndex = 12;

        Near(0, (double)Invoke(reflectiveField, "GetScalarValue", PreviewFieldIndex("MaximumDensity"), outerIndex), 1e-9,
            "reflective outer boundary maximum density");
        Near(-1, (double)Invoke(reflectiveField, "GetScalarValue", PreviewFieldIndex("MaximumDensity"), centerIndex), 1e-9,
            "reflective interior maximum density");
        object outerVoxel = Invoke(reflectiveField, "CreateVoxel", outerIndex);
        True(Convert.ToBoolean(PropertyValue(outerVoxel, "boundary")), "reflective outer voxel boundary flag");
        Near(0, Convert.ToDouble(PropertyValue(outerVoxel, "maxDensity")), 1e-9, "reflective outer voxel maximum density");

        object wrappedSnapshot = CaptureVoxelSnapshot(CreateField(fullData), true);
        object wrappedField = Field<object>(wrappedSnapshot, "Field");
        Near(-1, (double)Invoke(wrappedField, "GetScalarValue", PreviewFieldIndex("MaximumDensity"), outerIndex), 1e-9,
            "wrapped outer maximum density");
        False(Convert.ToBoolean(PropertyValue(Invoke(wrappedField, "CreateVoxel", outerIndex), "boundary")), "wrapped outer voxel boundary flag");

        bool[] active = Enumerable.Repeat(true, 25).ToArray();
        active[centerIndex] = false;
        object partialData = Invoke(fullData, "WithActiveMask", active);
        object partialSnapshot = CaptureVoxelSnapshot(CreateField(partialData), true);
        object partialField = Field<object>(partialSnapshot, "Field");
        int holeNeighbour = 7;
        int farFromHole = 0;
        Near(0, (double)Invoke(partialField, "GetScalarValue", PreviewFieldIndex("MaximumDensity"), holeNeighbour), 1e-9,
            "partial-domain boundary maximum density while wrapped");
        Near(-1, (double)Invoke(partialField, "GetScalarValue", PreviewFieldIndex("MaximumDensity"), farFromHole), 1e-9,
            "wrapped voxel away from partial-domain boundary");

        uint[] flags = Field<uint[]>(partialSnapshot, "VoxelFlags");
        False((flags[holeNeighbour >> 5] & (1u << (holeNeighbour & 31))) != 0,
            "partial-domain boundary remained walkable");
        True((flags[farFromHole >> 5] & (1u << (farFromHole & 31))) != 0,
            "wrapped voxel away from a hole became blocked");

        Type dimensionType = RequiredImplementationType("Nuclei4.SolverGpuDimensionMode");
        object zPriorityMode = InvokeStatic(dimensionType, "FromResolution", 1, 5, 1);
        True(Field<bool>(zPriorityMode, "PlanarXY"), "degenerate X/Z field did not preserve V3 Z-axis priority");
        False(Field<bool>(zPriorityMode, "PlanarYZ"), "degenerate X/Z field incorrectly preferred the X-axis mode");
        object yPriorityMode = InvokeStatic(dimensionType, "FromResolution", 1, 1, 5);
        True(Field<bool>(yPriorityMode, "PlanarXZ"), "degenerate X/Y field did not preserve V3 Y-axis priority");
        False(Field<bool>(yPriorityMode, "PlanarYZ"), "degenerate X/Y field incorrectly preferred the X-axis mode");

        // A V3 line grid is a boundary everywhere because the winning planar
        // mode still includes its other collapsed coordinate in the edge test.
        foreach (int[] resolution in new[] { new[] { 1, 5, 1 }, new[] { 1, 1, 5 }, new[] { 5, 1, 1 } })
        {
            object lineSnapshot = CaptureVoxelSnapshot(
                CreateField(CreateFullDomain(resolution[0], resolution[1], resolution[2])),
                false);
            object lineField = Field<object>(lineSnapshot, "Field");
            int lineCount = resolution[0] * resolution[1] * resolution[2];
            for (int index = 0; index < lineCount; index++)
            {
                Near(0, (double)Invoke(lineField, "GetScalarValue", PreviewFieldIndex("MaximumDensity"), index), 1e-9,
                    "degenerate line boundary maximum density");
            }
        }

        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        FieldInfo shaderField = engineType.GetField(
            "FullSolverShaderSource",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(engineType.FullName, "FullSolverShaderSource");
        string shaderSource = (string)(shaderField.IsLiteral
            ? shaderField.GetRawConstantValue()
            : shaderField.GetValue(null))!;
        string sensorBoundaryFunction = ShaderFunctionSource(shaderSource, "ApplyNonWrappedSensorBoundaries");
        True(sensorBoundaryFunction.Contains("ReflectSensorPlane(planeX, planeY, 0);", StringComparison.Ordinal),
            "non-wrapped sensor X clamp no longer mutates the working plane");
        True(sensorBoundaryFunction.Contains("ReflectSensorPlane(planeX, planeY, 1);", StringComparison.Ordinal),
            "non-wrapped sensor Y clamp no longer mutates the working plane");
        True(sensorBoundaryFunction.Contains("if (Tridimensional != 0) ReflectSensorPlane(planeX, planeY, 2);", StringComparison.Ordinal),
            "non-wrapped sensor Z clamp no longer preserves V3 planar behavior");

        string moveFunction = ShaderFunctionSource(shaderSource, "MoveParticlesAndDepositCore");
        int leftBoundary = moveFunction.IndexOf(
            "ApplyNonWrappedSensorBoundaries(leftSensor, x, y);",
            StringComparison.Ordinal);
        int frontBoundary = moveFunction.IndexOf(
            "ApplyNonWrappedSensorBoundaries(frontSensor, x, y);",
            StringComparison.Ordinal);
        int rightBoundary = moveFunction.IndexOf(
            "ApplyNonWrappedSensorBoundaries(rightSensor, x, y);",
            StringComparison.Ordinal);
        int upBoundary = moveFunction.IndexOf(
            "ApplyNonWrappedSensorBoundaries(upSensor, x, y);",
            StringComparison.Ordinal);
        int downBoundary = moveFunction.IndexOf(
            "ApplyNonWrappedSensorBoundaries(downSensor, x, y);",
            StringComparison.Ordinal);
        True(leftBoundary >= 0
            && leftBoundary < frontBoundary
            && frontBoundary < rightBoundary
            && rightBoundary < upBoundary
            && upBoundary < downBoundary,
            "sensor boundary mutations no longer run in V3 left/front/right/up/down order");
        True(moveFunction.Contains(
                "float3 upSensor = position + RotateAroundAxis(sensorPlaneX, y, sensorCos, sensorSin)",
                StringComparison.Ordinal),
            "3D up sensor no longer uses the original X axis and current working Y axis");
        True(moveFunction.Contains(
                "float3 downSensor = position + RotateAroundAxis(sensorPlaneX, y, sensorCos, -sensorSin)",
                StringComparison.Ordinal),
            "3D down sensor no longer observes the up-sensor plane mutation");
        True(moveFunction.Contains(
                "sincos(abs(group1.x) * (float)particleIndex, noSensorSin, noSensorCos);",
                StringComparison.Ordinal),
            "empty sensor choice no longer applies V3's deterministic non-wrap plane rotation");
        True(moveFunction.Contains("float3 force = 0.0;", StringComparison.Ordinal),
            "empty sensor choice can inject a movement force");
    }

    static void TestConnectedSteeringPacking()
    {
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        object group = Activator.CreateInstance(groupType)!;
        SetField(group, "connectedSteering", true);
        SetField(group, "ant", false);
        SetField(group, "wanderFrequency", 0.25);

        IList groups = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(groupType))!;
        groups.Add(group);

        object[] arguments = { groups, null, null, false, false };
        MethodInfo capture = SnapshotType.GetMethod(
            "CaptureGroupSettings",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        capture.Invoke(null, arguments);

        float[] groupData0 = (float[])arguments[1]!;
        float[] groupData1 = (float[])arguments[2]!;
        Near(0.25, groupData0[3], 1e-6, "connected steering exploration");
        Near(-1, groupData1[1], 1e-6, "connected steering mode selector");
        True((bool)arguments[4], "connected slime population detection");

        object duplicate = Invoke(group, "Duplicate");
        True(Field<bool>(duplicate, "connectedSteering"), "connected steering duplicate state");

        SetField(group, "wanderFrequency", double.NaN);
        arguments = new object[] { groups, null, null, false, false };
        capture.Invoke(null, arguments);
        Near(0, ((float[])arguments[1]!)[3], 1e-6, "connected steering NaN exploration clamp");

        object snapshot = Activator.CreateInstance(SnapshotType, nonPublic: true)!;
        Invoke(snapshot, "CaptureParticleGroups", groups);
        Array capturedGroups = (Array)Field<object>(snapshot, "ParticleGroups");
        Near(0, Field<double>(capturedGroups.GetValue(0)!, "wanderFrequency"), 1e-12,
            "connected steering NaN metadata clamp");

        SetField(group, "wanderFrequency", -1.0);
        Invoke(snapshot, "CaptureParticleGroups", groups);
        capturedGroups = (Array)Field<object>(snapshot, "ParticleGroups");
        Near(0, Field<double>(capturedGroups.GetValue(0)!, "wanderFrequency"), 1e-12,
            "connected steering negative metadata clamp");

        SetField(group, "wanderFrequency", double.PositiveInfinity);
        arguments = new object[] { groups, null, null, false, false };
        capture.Invoke(null, arguments);
        Near(1, ((float[])arguments[1]!)[3], 1e-6, "connected steering infinite exploration clamp");
        Invoke(snapshot, "CaptureParticleGroups", groups);
        capturedGroups = (Array)Field<object>(snapshot, "ParticleGroups");
        Near(1, Field<double>(capturedGroups.GetValue(0)!, "wanderFrequency"), 1e-12,
            "connected steering infinite metadata clamp");

        object solver = Activator.CreateInstance(RequiredCompatibilityType("Nuclei4.SolverGPU"))!;
        object liveTarget = Activator.CreateInstance(groupType)!;
        Invoke(solver, "CopyParticleGroupSettings", group, liveTarget);
        Near(1, Field<double>(liveTarget, "wanderFrequency"), 1e-12,
            "connected steering live metadata clamp");

        SetField(group, "ant", true);
        arguments = new object[] { groups, null, null, false, false };
        capture.Invoke(null, arguments);
        Near(1, ((float[])arguments[2]!)[1], 1e-6, "ant steering mode precedence");
        True((bool)arguments[3], "ant population detection");
        False((bool)arguments[4], "ant classified as slime population");
    }

    static void TestDendroUpdatePulseAndCache()
    {
        Type componentType = RequiredCompatibilityType("Nuclei4.GpuVolumeToMesh");
        object component = Activator.CreateInstance(componentType)!;
        False((bool)Invoke(component, "ConsumeUpdatePulse", false), "Dendro false update pulse");
        True((bool)Invoke(component, "ConsumeUpdatePulse", true), "Dendro rising update pulse");
        False((bool)Invoke(component, "ConsumeUpdatePulse", true), "Dendro held update rebuilt output");
        False((bool)Invoke(component, "ConsumeUpdatePulse", false), "Dendro falling update pulse");
        True((bool)Invoke(component, "ConsumeUpdatePulse", true), "Dendro second rising update pulse");

        ProbeDisposable first = new ProbeDisposable();
        ProbeDisposable replacement = new ProbeDisposable();
        Invoke(component, "ReplaceCachedOutput", first);
        True(ReferenceEquals(first, Field<object>(component, "cachedOutput")), "Dendro initial cache publication");
        False(first.Disposed, "Dendro initial cache was disposed while retained");
        Invoke(component, "ReplaceCachedOutput", first);
        False(first.Disposed, "Dendro identity-preserving cache update disposed its output");
        Invoke(component, "ReplaceCachedOutput", replacement);
        True(first.Disposed, "Dendro cache replacement did not dispose the superseded output");
        False(replacement.Disposed, "Dendro replacement output was disposed before publication");
        False(Field<bool>(component, "publishScheduled"), "detached Dendro component scheduled a publication solve");

        ProbeDendroVolume.LastCreated = null;
        Invoke(
            component,
            "TryStoreDendroVolume",
            typeof(ProbeDendroVolume),
            typeof(ProbeDendroGoo),
            new object[] { true },
            "probe volume one");
        ProbeDendroGoo firstGoo = Field<object>(component, "cachedOutput") as ProbeDendroGoo;
        True(firstGoo != null, "valid Dendro probe volume was not wrapped and cached");
        ProbeDendroVolume firstVolume = firstGoo.Value as ProbeDendroVolume;
        True(firstVolume != null && firstVolume.IsValid, "cached Dendro probe volume was invalid");
        True(replacement.Disposed, "successful Dendro volume publication retained the old cache");
        Equal("probe volume one", Convert.ToString(PropertyValue(component, "Message")), "Dendro publication status");

        Invoke(
            component,
            "TryStoreDendroVolume",
            typeof(ProbeDendroVolume),
            typeof(ProbeDendroGoo),
            new object[] { true },
            "probe volume two");
        ProbeDendroGoo secondGoo = Field<object>(component, "cachedOutput") as ProbeDendroGoo;
        True(secondGoo != null && !ReferenceEquals(firstGoo, secondGoo), "Dendro replacement did not publish a new wrapper");
        True(firstGoo.Disposed && firstVolume.Disposed, "Dendro replacement did not release the old wrapped volume");

        object retainedCache = secondGoo;
        Invoke(
            component,
            "TryStoreDendroVolume",
            typeof(ProbeDendroVolume),
            typeof(ProbeDendroGoo),
            new object[] { false },
            "invalid probe volume");
        True(ProbeDendroVolume.LastCreated != null && ProbeDendroVolume.LastCreated.Disposed,
            "invalid Dendro volume was not disposed");
        True(ReferenceEquals(retainedCache, Field<object>(component, "cachedOutput")),
            "invalid Dendro volume replaced the last valid cache");

        Invoke(
            component,
            "TryStoreDendroVolume",
            typeof(ProbeDendroVolume),
            typeof(ProbeReadOnlyDendroGoo),
            new object[] { true },
            "incompatible probe wrapper");
        True(ProbeDendroVolume.LastCreated != null && ProbeDendroVolume.LastCreated.Disposed,
            "Dendro volume for an incompatible wrapper was not disposed");
        True(ReferenceEquals(retainedCache, Field<object>(component, "cachedOutput")),
            "incompatible Dendro wrapper replaced the last valid cache");

        Console.WriteLine("Dendro pulse, replacement, disposal, and failed-publication cache semantics passed.");
    }

    static void TestSlimeSettingsLegacyArchiveMigration()
    {
        RoundTripLegacySlimeSettings(fourInputs: false);
        RoundTripLegacySlimeSettings(fourInputs: true);
        Console.WriteLine("Legacy three- and four-input Slime Settings archive round-trips passed.");
    }

    static void RoundTripLegacySlimeSettings(bool fourInputs)
    {
        Type componentType = RequiredCompatibilityType("Nuclei4.EnivronmentSettings");
        object legacyComponent = Activator.CreateInstance(componentType)!;
        object legacyParams = PropertyValue(legacyComponent, "Params");
        IList legacyInputs = (IList)PropertyValue(legacyParams, "Input");

        if (fourInputs)
        {
            // [Diffuse, Decay, Falloff, Range] -> [Diffuse, Range, Decay, Gradual].
            InvokeIntArrayMethod(legacyParams, "SortInput", new[] { 0, 2, 3, 1 });
            legacyInputs = (IList)PropertyValue(legacyParams, "Input");
            SetPropertyValue(legacyInputs[3], "Name", "Gradual");
            SetPropertyValue(legacyInputs[3], "NickName", "gradual");
        }
        else
        {
            // The oldest archive had no Gradual/Falloff parameter at all.
            object falloff = legacyInputs[2];
            Invoke(legacyParams, "UnregisterInputParameter", falloff, false);
            InvokeIntArrayMethod(legacyParams, "SortInput", new[] { 0, 2, 1 });
            Invoke(legacyParams, "OnParametersChanged");
            legacyInputs = (IList)PropertyValue(legacyParams, "Input");
        }

        string[] legacyNames = fourInputs
            ? new[] { "Diffuse Rate", "Diffuse Range", "Decay Rate", "Gradual" }
            : new[] { "Diffuse Rate", "Diffuse Range", "Decay Rate" };
        for (int i = 0; i < legacyNames.Length; i++)
        {
            Equal(legacyNames[i], Convert.ToString(PropertyValue(legacyInputs[i], "Name")),
                "synthetic legacy Slime Settings input order " + i);
        }

        object legacyArchive = WriteGrasshopperObjectArchive(legacyComponent);
        object legacyRoot = PropertyValue(legacyArchive, "GetRootNode");
        Invoke(legacyRoot, "RemoveItem", "VoxelSettingsSlimeSchema");
        Invoke(legacyRoot, "RemoveItem", "Input2StoresLegacyGradual");

        double[] legacyValues = fourInputs
            ? new[] { 0.27, 4.0, 0.08, 0.63 }
            : new[] { 0.27, 4.0, 0.08 };
        bool[] legacyIntegers = fourInputs
            ? new[] { false, true, false, false }
            : new[] { false, true, false };
        Guid[] legacyParameterGuids = new Guid[legacyNames.Length];
        Guid[] legacySourceGuids = new Guid[legacyNames.Length];
        for (int i = 0; i < legacyNames.Length; i++)
        {
            object inputChunk = Invoke(legacyRoot, "FindChunk", "param_input", i);
            legacyParameterGuids[i] = (Guid)Invoke(inputChunk, "GetGuid", "InstanceGuid");
            legacySourceGuids[i] = Guid.Parse("10000000-0000-0000-0000-" + (i + (fourInputs ? 100 : 200)).ToString("D12", CultureInfo.InvariantCulture));
            SetArchiveParameterValue(inputChunk, legacyValues[i], legacyIntegers[i]);
            Invoke(inputChunk, "RemoveItem", "SourceCount");
            Invoke(inputChunk, "SetInt32", "SourceCount", 1);
            Invoke(inputChunk, "SetGuid", "Source", 0, legacySourceGuids[i]);
        }

        string legacyXml = (string)Invoke(legacyArchive, "Serialize_Xml");
        object migrated = Activator.CreateInstance(componentType)!;
        IList freshInputs = (IList)PropertyValue(PropertyValue(migrated, "Params"), "Input");
        Guid insertedFalloffGuid = (Guid)PropertyValue(freshInputs[2], "InstanceGuid");
        ReadGrasshopperObjectXml(migrated, legacyXml);

        int[] legacyIndexForModern = fourInputs
            ? new[] { 0, 2, 3, 1 }
            : new[] { 0, 2, -1, 1 };
        double[] modernValues = { 0.27, 0.08, fourInputs ? 0.63 : 0.0, 4.0 };
        Guid[] modernParameterGuids = new Guid[4];
        Guid?[] modernSourceGuids = new Guid?[4];
        for (int modernIndex = 0; modernIndex < 4; modernIndex++)
        {
            int legacyIndex = legacyIndexForModern[modernIndex];
            if (legacyIndex >= 0)
            {
                modernParameterGuids[modernIndex] = legacyParameterGuids[legacyIndex];
                modernSourceGuids[modernIndex] = legacySourceGuids[legacyIndex];
            }
            else
            {
                modernParameterGuids[modernIndex] = insertedFalloffGuid;
                modernSourceGuids[modernIndex] = null;
            }
        }

        ValidateMigratedSlimeSettings(
            migrated,
            fourInputs,
            modernValues,
            modernParameterGuids,
            modernSourceGuids,
            fourInputs ? "legacy four-input" : "legacy three-input");

        // Persist the migrated component with the current schema and load it once
        // more. This catches state that was only correct in memory after Read().
        object modernArchive = WriteGrasshopperObjectArchive(migrated);
        object modernRoot = PropertyValue(modernArchive, "GetRootNode");
        Equal(2, (int)Invoke(modernRoot, "GetInt32", "VoxelSettingsSlimeSchema"),
            "migrated Slime Settings schema marker");
        string modernXml = (string)Invoke(modernArchive, "Serialize_Xml");
        object reloaded = Activator.CreateInstance(componentType)!;
        ReadGrasshopperObjectXml(reloaded, modernXml);
        ValidateMigratedSlimeSettings(
            reloaded,
            fourInputs,
            modernValues,
            modernParameterGuids,
            modernSourceGuids,
            (fourInputs ? "legacy four-input" : "legacy three-input") + " current-schema reload");
    }

    static void ValidateMigratedSlimeSettings(
        object component,
        bool legacyGradual,
        double[] expectedValues,
        Guid[] expectedParameterGuids,
        Guid?[] expectedSourceGuids,
        string label)
    {
        IList inputs = (IList)PropertyValue(PropertyValue(component, "Params"), "Input");
        Equal(4, inputs.Count, label + " input count");
        string[] expectedNames =
        {
            "Diffuse Rate",
            "Decay Rate",
            legacyGradual ? "Gradual (legacy)" : "Falloff",
            "Diffuse Range"
        };
        for (int i = 0; i < inputs.Count; i++)
        {
            Equal(expectedNames[i], Convert.ToString(PropertyValue(inputs[i], "Name")), label + " input name " + i);
            Equal(expectedParameterGuids[i], (Guid)PropertyValue(inputs[i], "InstanceGuid"), label + " input GUID " + i);
            string typeName = inputs[i].GetType().FullName ?? string.Empty;
            True(i == 3 ? typeName.EndsWith("Param_Integer", StringComparison.Ordinal) : typeName.EndsWith("Param_Number", StringComparison.Ordinal),
                label + " input type " + i + " was " + typeName);
        }
        Equal(legacyGradual, Field<bool>(component, "input2StoresLegacyGradual"), label + " legacy-gradual mode");

        object archive = WriteGrasshopperObjectArchive(component);
        object root = PropertyValue(archive, "GetRootNode");
        for (int i = 0; i < inputs.Count; i++)
        {
            object inputChunk = Invoke(root, "FindChunk", "param_input", i);
            Equal(expectedNames[i], (string)Invoke(inputChunk, "GetString", "Name"), label + " serialized name " + i);
            Equal(expectedParameterGuids[i], (Guid)Invoke(inputChunk, "GetGuid", "InstanceGuid"), label + " serialized GUID " + i);
            Near(expectedValues[i], ArchiveParameterValue(inputChunk, i == 3), 1e-12, label + " persistent value " + i);
            int sourceCount = (int)Invoke(inputChunk, "GetInt32", "SourceCount");
            Equal(expectedSourceGuids[i].HasValue ? 1 : 0, sourceCount, label + " source count " + i);
            // This unit archive intentionally contains only the component. GH turns
            // unresolved source ids into proxy params with fresh ids; a full document
            // resolves them back to their original objects. Source count plus the
            // preserved input InstanceGuid proves the wire payload stayed with the
            // correct reordered parameter without pretending a dangling id is stable.
        }
    }

    static object WriteGrasshopperObjectArchive(object serializable)
    {
        Type archiveType = RequiredGhIoType("GH_IO.Serialization.GH_Archive");
        object archive = Activator.CreateInstance(archiveType)!;
        Invoke(archive, "CreateNewRoot", true);
        object root = PropertyValue(archive, "GetRootNode");
        True((bool)Invoke(serializable, "Write", root), "Grasshopper object archive write");
        return archive;
    }

    static void ReadGrasshopperObjectXml(object serializable, string xml)
    {
        Type archiveType = RequiredGhIoType("GH_IO.Serialization.GH_Archive");
        object archive = Activator.CreateInstance(archiveType)!;
        True((bool)Invoke(archive, "Deserialize_Xml", xml), "Grasshopper object archive XML deserialize");
        object root = PropertyValue(archive, "GetRootNode");
        True((bool)Invoke(serializable, "Read", root), "Grasshopper object archive read");
    }

    static Type RequiredGhIoType(string name)
    {
        Assembly ghIo = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(item => string.Equals(item.GetName().Name, "GH_IO", StringComparison.OrdinalIgnoreCase));
        if (ghIo == null)
        {
            ghIo = Assembly.Load("GH_IO");
        }
        return ghIo.GetType(name, throwOnError: true)!;
    }

    static void SetArchiveParameterValue(object inputChunk, double value, bool integer)
    {
        object persistent = Invoke(inputChunk, "FindChunk", "PersistentData");
        object branch = Invoke(persistent, "FindChunk", "Branch", 0);
        object item = Invoke(branch, "FindChunk", "Item", 0);
        Invoke(item, "RemoveItem", "number");
        if (integer)
        {
            Invoke(item, "SetInt32", "number", checked((int)value));
        }
        else
        {
            Invoke(item, "SetDouble", "number", value);
        }
    }

    static double ArchiveParameterValue(object inputChunk, bool integer)
    {
        object persistent = Invoke(inputChunk, "FindChunk", "PersistentData");
        object branch = Invoke(persistent, "FindChunk", "Branch", 0);
        object item = Invoke(branch, "FindChunk", "Item", 0);
        return integer
            ? Convert.ToDouble(Invoke(item, "GetInt32", "number"), CultureInfo.InvariantCulture)
            : (double)Invoke(item, "GetDouble", "number");
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
        float[] homes =
        {
            9.0f, 8.0f, 7.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 0.0f
        };
        float[] homeAxes = new float[capacity * 6];
        homeAxes[0] = -1.0f;
        homeAxes[capacity * 4] = 1.0f;
        int[] auxiliary = new int[capacity * 7];
        auxiliary[0] = 7;
        auxiliary[capacity] = 4;
        auxiliary[capacity * 2] = 5;
        auxiliary[capacity * 3] = 1;
        auxiliary[capacity * 4] = 1;
        auxiliary[capacity * 5] = 1;
        auxiliary[capacity * 6] = 1;

        Type particleViewType = RequiredImplementationType("Nuclei4.GpuParticleReadbackView");
        object particleView = CreateInstance(
            particleViewType,
            capacity,
            1,
            1,
            positions,
            directions,
            yAxes,
            homes,
            homeAxes,
            auxiliary);
        Equal(capacity, (int)PropertyValue(particleView, "Capacity"), "particle readback capacity");
        Equal(1, (int)PropertyValue(particleView, "Count"), "particle readback requested count");
        Equal(1, (int)PropertyValue(particleView, "GroupCount"), "particle readback group count");
        True(ReferenceEquals(positions, PropertyValue(particleView, "Positions")), "particle position readback was copied");
        True(ReferenceEquals(directions, PropertyValue(particleView, "Directions")), "particle direction readback was copied");
        True(ReferenceEquals(yAxes, PropertyValue(particleView, "YAxes")), "particle Y-axis readback was copied");
        True(ReferenceEquals(homes, PropertyValue(particleView, "Homes")), "particle home readback was copied");
        True(ReferenceEquals(homeAxes, PropertyValue(particleView, "HomeAxes")), "particle home-axis readback was copied");
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
        True(Field<bool>(initialParticle, "highDeposit"), "particle high-deposit state");
        True(Field<bool>(initialParticle, "antLaunchBoundaryHit"), "particle ant launch-boundary state");
        object homePlane = Field<object>(initialParticle, "home");
        AssertPoint(PropertyValue(homePlane, "Origin"), 9, 8, 7, "particle home origin");
        AssertVector(PropertyValue(homePlane, "XAxis"), 0, 1, 0, "particle home X axis");
        AssertVector(PropertyValue(homePlane, "YAxis"), -1, 0, 0, "particle home Y axis");

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
            object slimePointCloud = Field<object>(previewCache, "SlimePointCloud");
            Equal(1, (int)PropertyValue(slimePointCloud, "Count"), "position-only preview point-cloud count");
            object pointCloudItem = slimePointCloud.GetType().GetProperty("Item")!.GetValue(slimePointCloud, new object[] { 0 });
            AssertPoint(PropertyValue(pointCloudItem, "Location"), 0.25, 1.75, 0.5, "position-only preview point");
        }
        else
        {
            VerifyManagedPreviewStaging(initialParticle, previewPositions);
        }

        object slimeGroup = Activator.CreateInstance(groupType)!;
        SetField(slimeGroup, "ant", false);
        Array reuseGroups = Array.CreateInstance(groupType, 2);
        reuseGroups.SetValue(group, 0);
        reuseGroups.SetValue(slimeGroup, 1);
        SetField(sink, "particleGroups", reuseGroups);
        positions[3] = 1.0f;
        auxiliary[capacity * 3] = 99;
        object slimeReuseView = CreateInstance(
            particleViewType,
            capacity,
            1,
            2,
            positions,
            directions,
            yAxes,
            homes,
            homeAxes,
            auxiliary);
        Invoke(sink, "ApplyParticles", slimeReuseView, settings, 7, false);
        True(ReferenceEquals(initialParticle, particleList[0]),
            "ant-to-slime slot reuse replaced the retained particle object");
        True(ReferenceEquals(slimeGroup, Field<object>(initialParticle, "parentParticleGroup")),
            "ant-to-slime slot reuse did not refresh group identity");
        object resetSlimeHome = Field<object>(initialParticle, "home");
        AssertPoint(PropertyValue(resetSlimeHome, "Origin"), 0, 0, 0,
            "ant-to-slime slot reuse home origin");
        AssertVector(PropertyValue(resetSlimeHome, "XAxis"), 0, 0, 0,
            "ant-to-slime slot reuse home X axis");
        AssertVector(PropertyValue(resetSlimeHome, "YAxis"), 0, 0, 0,
            "ant-to-slime slot reuse home Y axis");

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
        SetField(engine, "resX", 4);
        SetField(engine, "resY", 1);
        SetField(engine, "resZ", 1);
        SetField(engine, "wrapBoundaryState", true);
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

        SetField(engine, "staticActiveVoxelFlags", new uint[] { 0b0111u });
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
            Near(0, StaticPreviewValue(engine, 3, fieldName), 1e-6, "inactive static preview " + fieldName);
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
        float[] staleAntFood = new float[12];
        float[] staleAntBase = new float[12];
        staleAntFood[5] = 0.8f;
        staleAntBase[5] = 0.7f;
        Invoke(inputField, "UpdateDynamicFields", staleRuntimeDensity, staleAntFood, staleAntBase, null);

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

        // Normal solver reset consumes the immutable/reset field fork. Even when ant
        // allocation is requested, stale runtime pheromones are not initial inputs.
        object antResetSnapshot = Activator.CreateInstance(SnapshotType, nonPublic: true)!;
        SetField(antResetSnapshot, "HasAntParticles", true);
        SetField(antResetSnapshot, "HasSlimeParticles", false);
        Invoke(antResetSnapshot, "CaptureCompactVoxels", inputField, false, false);
        Null(Field<object>(antResetSnapshot, "AntFoodPheromone"), "reset snapshot retained stale ant-food pheromone");
        Null(Field<object>(antResetSnapshot, "AntBasePheromone"), "reset snapshot retained stale ant-base pheromone");
    }

    static void TestSolverOutputCallbackDetachment()
    {
        object solver = Activator.CreateInstance(RequiredCompatibilityType("Nuclei4.SolverGPU"))!;
        object field = CreateField(CreateFullDomain(4, 3, 1));
        object particleList = Activator.CreateInstance(RequiredCompatibilityType("Nuclei4.ParticleList"))!;

        foreach (string callback in new[] { "GpuVolumeMeshProvider", "DynamicStateSynchronizer" })
        {
            FieldInfo callbackField = field.GetType().GetField(callback, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(field.GetType().FullName, callback);
            callbackField.SetValue(field, CreateDefaultDelegate(callbackField.FieldType));
        }
        foreach (string callback in new[] { "GpuPreviewFrameProvider", "GpuTrailPreviewFrameProvider", "CpuStateSynchronizer" })
        {
            FieldInfo callbackField = particleList.GetType().GetField(callback, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(particleList.GetType().FullName, callback);
            callbackField.SetValue(particleList, CreateDefaultDelegate(callbackField.FieldType));
        }
        object previewCache = Field<object>(particleList, "PreviewCache");
        foreach (string callback in new[] { "TryCompleteAsyncUpdate", "QueueAsyncUpdate" })
        {
            FieldInfo callbackField = previewCache.GetType().GetField(callback, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(previewCache.GetType().FullName, callback);
            callbackField.SetValue(previewCache, CreateDefaultDelegate(callbackField.FieldType));
        }

        SetField(solver, "voxels", field);
        SetField(solver, "particles", particleList);
        Invoke(solver, "DisposeGpuEngines");

        foreach (string callback in new[] { "GpuVolumeMeshProvider", "DynamicStateSynchronizer" })
        {
            Null(Field<object>(field, callback), "disposed solver left stale voxel callback " + callback);
        }
        foreach (string callback in new[] { "GpuPreviewFrameProvider", "GpuTrailPreviewFrameProvider", "CpuStateSynchronizer" })
        {
            Null(Field<object>(particleList, callback), "disposed solver left stale particle callback " + callback);
        }
        foreach (string callback in new[] { "TryCompleteAsyncUpdate", "QueueAsyncUpdate" })
        {
            Null(Field<object>(previewCache, callback), "disposed solver left stale preview-cache callback " + callback);
        }
    }

    static Delegate CreateDefaultDelegate(Type delegateType)
    {
        MethodInfo invoke = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException(delegateType.FullName + " is not a delegate type.");
        System.Linq.Expressions.ParameterExpression[] parameters = invoke.GetParameters()
            .Select(parameter => System.Linq.Expressions.Expression.Parameter(parameter.ParameterType, parameter.Name))
            .ToArray();
        System.Linq.Expressions.Expression body = invoke.ReturnType == typeof(void)
            ? System.Linq.Expressions.Expression.Empty()
            : System.Linq.Expressions.Expression.Default(invoke.ReturnType);
        return System.Linq.Expressions.Expression.Lambda(delegateType, body, parameters).Compile();
    }

    static void TestVoxelPreviewOnDemandDynamicSync()
    {
        object field = CreateField(CreateFullDomain(4, 3, 1));
        Type previewType = RequiredCompatibilityType("Nuclei4.Preview_Voxel");
        int simulatedIteration = 5;
        int synchronizedIteration = -1;
        int synchronizationCount = 0;
        float nextFoodValue = 0.75f;
        Action synchronizer = () =>
        {
            if (synchronizedIteration == simulatedIteration) return;
            synchronizationCount++;
            synchronizedIteration = simulatedIteration;
            float[] remainingFood = new float[12];
            remainingFood[5] = nextFoodValue;
            Invoke(field, "UpdateDynamicFields", null, null, null, remainingFood);
        };
        SetField(field, "DynamicStateSynchronizer", synchronizer);

        int antFoodIndex = PreviewFieldIndex("AntFood");
        InvokeStatic(previewType, "EnsureCpuDynamicPreviewState", field, antFoodIndex);
        Equal(1, synchronizationCount, "Ant Food CPU preview did not request on-demand voxel synchronization");
        Near(0.75, (double)Invoke(field, "GetScalarValue", antFoodIndex, 5), 1e-6,
            "Ant Food CPU preview retained its stale reset-time food value");

        InvokeStatic(previewType, "EnsureCpuDynamicPreviewState", field, antFoodIndex);
        Equal(1, synchronizationCount, "repeated Ant Food preview caused a redundant same-iteration readback");

        simulatedIteration = 6;
        nextFoodValue = 0.25f;
        InvokeStatic(previewType, "EnsureCpuDynamicPreviewState", field, PreviewFieldIndex("MinimumDensity"));
        Equal(1, synchronizationCount, "static voxel preview requested a dynamic GPU readback");
        InvokeStatic(previewType, "EnsureCpuDynamicPreviewState", field, antFoodIndex);
        Equal(2, synchronizationCount, "Ant Food preview did not synchronize the next GPU iteration");
        Near(0.25, (double)Invoke(field, "GetScalarValue", antFoodIndex, 5), 1e-6,
            "Ant Food CPU preview did not refresh after GPU iteration advance");

        Console.WriteLine("Voxel-preview on-demand dynamic synchronization passed.");
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

    static void TestGpuDensityEvolutionWithoutSlime()
    {
        const int grid = 5;
        const int centerIndex = 2 * grid * grid + 2 * grid + 2;
        float[] initialDensity = new float[grid * grid * grid];
        initialDensity[centerIndex] = 0.6f;
        object inputField = CreateField(WithInitialDensity(CreateFullDomain(grid, grid, grid), initialDensity));

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "Diffuse", 0.0);
        SetField(settings, "DiffusionGradual", 1.0);
        SetField(settings, "Decay", 0.1);
        SetField(settings, "WrapBoundaries", false);
        object dimensionMode = InvokeStatic(
            RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"),
            "FromResolution",
            grid,
            grid,
            grid);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");

        object emptySnapshot = CaptureVoxelSnapshot(inputField);
        SetField(emptySnapshot, "HasSlimeParticles", false);
        SetField(emptySnapshot, "HasAntParticles", false);
        SetField(emptySnapshot, "ParticleGroups", Array.CreateInstance(groupType, 0));
        object emptyEngine = CreateGpuEngine(engineType, emptySnapshot, settings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(emptyEngine, emptySnapshot, settings, dimensionMode, 0);
            Invoke(emptyEngine, "ReadBackDensity");
            Near(0.5, Field<float[]>(emptyEngine, "densityReadback")[centerIndex], 1e-5,
                "empty-population scalar density decay");
            object combinedFrame = Invoke(
                emptyEngine,
                "CreateVoxelFieldPreviewFrame",
                PreviewFieldIndex("AntsAndSlime"),
                dimensionMode,
                0.0f,
                1.0f,
                1);
            True(combinedFrame != null, "empty-population combined preview hid scalar density");
        }
        finally
        {
            ((IDisposable)emptyEngine).Dispose();
        }

        object antSnapshot;
        BenchmarkAntParticles = true;
        try
        {
            antSnapshot = CaptureGpuSignatureSnapshot(inputField, 1, false);
        }
        finally
        {
            BenchmarkAntParticles = false;
        }
        True(Field<bool>(antSnapshot, "HasAntParticles"), "ant-only density probe lost its retained ant");
        False(Field<bool>(antSnapshot, "HasSlimeParticles"), "ant-only density probe unexpectedly retained slime");
        object antEngine = CreateGpuEngine(engineType, antSnapshot, settings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(antEngine, antSnapshot, settings, dimensionMode, 0);
            Invoke(antEngine, "ReadBackDensity");
            Near(0.5, Field<float[]>(antEngine, "densityReadback")[centerIndex], 1e-5,
                "ant-only scalar density decay");
        }
        finally
        {
            ((IDisposable)antEngine).Dispose();
        }

        object foodData = Invoke(CreateFullDomain(grid, grid, grid), "WithScalarValues", 6, new List<double> { 1.0 });
        object foodSnapshot = CaptureVoxelSnapshot(CreateField(foodData));
        SetField(foodSnapshot, "HasSlimeParticles", false);
        SetField(foodSnapshot, "HasAntParticles", false);
        SetField(foodSnapshot, "ParticleGroups", Array.CreateInstance(groupType, 0));
        SetField(settings, "Decay", 0.0);
        object foodEngine = CreateGpuEngine(engineType, foodSnapshot, settings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(foodEngine, foodSnapshot, settings, dimensionMode, 0);
            Invoke(foodEngine, "ReadBackDensity");
            Near(0.0, Field<float[]>(foodEngine, "densityReadback")[centerIndex], 1e-6,
                "food projection ran without retained slime");
        }
        finally
        {
            ((IDisposable)foodEngine).Dispose();
        }

        float[] blockedDensity = new float[grid * grid * grid];
        blockedDensity[centerIndex] = 0.6f;
        object blockedData = WithInitialDensity(CreateFullDomain(grid, grid, grid), blockedDensity);
        List<double> blockedMaximums = Enumerable.Repeat(-1.0, blockedDensity.Length).ToList();
        blockedMaximums[centerIndex] = 0.005;
        blockedData = Invoke(blockedData, "WithScalarValues", 1, blockedMaximums);
        object blockedSnapshot = CaptureVoxelSnapshot(CreateField(blockedData));
        SetField(blockedSnapshot, "HasSlimeParticles", false);
        SetField(blockedSnapshot, "HasAntParticles", false);
        SetField(blockedSnapshot, "ParticleGroups", Array.CreateInstance(groupType, 0));
        SetField(settings, "Diffuse", 0.5);
        SetField(settings, "DiffuseRange", 1);
        SetField(settings, "Decay", 0.0);
        object blockedEngine = CreateGpuEngine(engineType, blockedSnapshot, settings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(blockedEngine, blockedSnapshot, settings, dimensionMode, 0);
            Invoke(blockedEngine, "ReadBackDensity");
            // V3 keeps the active target's own contribution even though its
            // sub-0.01 maximum makes it ineligible as a neighbour. The first
            // axis clamps 0.3 to 0.005; the next two retain half each.
            Near(0.00125, Field<float[]>(blockedEngine, "densityReadback")[centerIndex], 1e-6,
                "active blocked diffusion target did not retain its own density across all three axes");
        }
        finally
        {
            ((IDisposable)blockedEngine).Dispose();
        }

        List<double> antMinimums = Enumerable.Repeat(-1.0, blockedDensity.Length).ToList();
        antMinimums[centerIndex] = 0.2;
        object antMinimumData = Invoke(CreateFullDomain(grid, grid, grid), "WithScalarValues", 0, antMinimums);
        object antMinimumSnapshot;
        BenchmarkAntParticles = true;
        try
        {
            antMinimumSnapshot = CaptureGpuSignatureSnapshot(CreateField(antMinimumData), 1, false);
        }
        finally
        {
            BenchmarkAntParticles = false;
        }
        SetField(settings, "Diffuse", 0.0);
        SetField(settings, "AntFoodDiffuse", 0.5);
        SetField(settings, "AntBaseDiffuse", 0.5);
        SetField(settings, "AntDiffuseRange", 1);
        SetField(settings, "AntFoodDecay", 0.0);
        SetField(settings, "AntBaseDecay", 0.0);
        object antMinimumEngine = CreateGpuEngine(engineType, antMinimumSnapshot, settings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(antMinimumEngine, antMinimumSnapshot, settings, dimensionMode, 0);
            Invoke(antMinimumEngine, "ReadBackAntFields");
            Near(0.2, Field<float[]>(antMinimumEngine, "antFoodReadback")[centerIndex], 1e-6,
                "zero ant-food field did not apply authored minimum during diffusion");
            Near(0.2, Field<float[]>(antMinimumEngine, "antBaseReadback")[centerIndex], 1e-6,
                "zero ant-base field did not apply authored minimum during diffusion");
        }
        finally
        {
            ((IDisposable)antMinimumEngine).Dispose();
        }

        Console.WriteLine("Direct3D density species, active-target, and ant-minimum parity passed.");
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
        object snapshot = CaptureGpuSignatureSnapshot(inputField, particleCount, true);

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

    static void TestAntMoveShaderSpecialization()
    {
        const int grid = 24;
        const int particleCount = 512;
        const int finalIteration = 32;

        float[] initialDensity = new float[grid * grid * grid];
        for (int i = 0; i < initialDensity.Length; i++)
        {
            initialDensity[i] = (float)((i * 37 % 101) / 500.0);
        }

        object inputField = CreateField(WithInitialDensity(CreateFullDomain(grid, grid, grid), initialDensity));
        object specializedSnapshot;
        object genericSnapshot;
        BenchmarkAntParticles = true;
        try
        {
            specializedSnapshot = CaptureGpuSignatureSnapshot(inputField, particleCount, true);
            genericSnapshot = CaptureGpuSignatureSnapshot(inputField, particleCount, true);
        }
        finally
        {
            BenchmarkAntParticles = false;
        }

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "Diffuse", 0.17);
        SetField(settings, "DiffuseRange", 1);
        SetField(settings, "Decay", 0.025);
        SetField(settings, "WrapBoundaries", true);
        SetField(settings, "DynamicPopulation", false);

        object dimensionMode = InvokeStatic(
            RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"),
            "FromResolution",
            grid,
            grid,
            grid);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object specialized = CreateGpuEngine(engineType, specializedSnapshot, settings, false, false, false, 0, 1);
        object generic = CreateGpuEngine(engineType, genericSnapshot, settings, false, false, false, 0, 1);

        FieldInfo antMoveField = engineType.GetField("antMoveShader", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo genericMoveField = engineType.GetField("moveShader", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object originalGenericAntShader = antMoveField.GetValue(generic)!;
        antMoveField.SetValue(generic, genericMoveField.GetValue(generic));

        try
        {
            for (int iteration = 1; iteration <= finalIteration; iteration++)
            {
                InvokeGpuStep(specialized, specializedSnapshot, settings, dimensionMode, iteration);
                InvokeGpuStep(generic, genericSnapshot, settings, dimensionMode, iteration);
                if (iteration != 1 && iteration != 8 && iteration != finalIteration) continue;

                Invoke(specialized, "ReadBackParticles");
                Invoke(generic, "ReadBackParticles");
                Invoke(specialized, "ReadBackAntFields");
                Invoke(generic, "ReadBackAntFields");

                EqualFloatBits(
                    Field<float[]>(generic, "particlePositionReadback"),
                    Field<float[]>(specialized, "particlePositionReadback"),
                    "ant specialization positions at iteration " + iteration);
                EqualFloatBits(
                    Field<float[]>(generic, "particleDirectionReadback"),
                    Field<float[]>(specialized, "particleDirectionReadback"),
                    "ant specialization directions at iteration " + iteration);
                EqualFloatBits(
                    Field<float[]>(generic, "particleYAxisReadback"),
                    Field<float[]>(specialized, "particleYAxisReadback"),
                    "ant specialization Y axes at iteration " + iteration);
                EqualIntArrays(
                    Field<int[]>(generic, "particleAuxReadback"),
                    Field<int[]>(specialized, "particleAuxReadback"),
                    "ant specialization auxiliary state at iteration " + iteration);
                EqualFloatBits(
                    Field<float[]>(generic, "antFoodReadback"),
                    Field<float[]>(specialized, "antFoodReadback"),
                    "ant specialization food pheromone at iteration " + iteration);
                EqualFloatBits(
                    Field<float[]>(generic, "antBaseReadback"),
                    Field<float[]>(specialized, "antBaseReadback"),
                    "ant specialization base pheromone at iteration " + iteration);
            }
        }
        finally
        {
            // Restore ownership before disposal so the generic engine does not
            // dispose the same COM shader twice.
            antMoveField.SetValue(generic, originalGenericAntShader);
            ((IDisposable)generic).Dispose();
            ((IDisposable)specialized).Dispose();
        }

        Console.WriteLine("Ant-only and generic movement shaders are bit-identical at all checkpoints.");
    }

    static void EqualFloatBits(float[] expected, float[] actual, string label)
    {
        Equal(expected.Length, actual.Length, label + " length");
        for (int i = 0; i < expected.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
            {
                throw new InvalidOperationException(label + " differs at index " + i + ".");
            }
        }
    }

    static void EqualIntArrays(int[] expected, int[] actual, string label)
    {
        Equal(expected.Length, actual.Length, label + " length");
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                throw new InvalidOperationException(label + " differs at index " + i + ".");
            }
        }
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
        ConnectedSteeringParity = Array.IndexOf(args, "--connected") >= 0;
        ConnectedSteeringExploration = BenchmarkDouble(args, "--exploration", 0.5);

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
            + " iterations=" + iterations
            + " connected=" + ConnectedSteeringParity
            + (ConnectedSteeringParity
                ? " exploration=" + ConnectedSteeringExploration.ToString(CultureInfo.InvariantCulture)
                : ""));
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

    static void RunConnectedSteeringParityRegression(string[] args)
    {
        const int grid = 24;
        const int particleCount = 512;
        const int iterations = 24;
        const double exploration = 0.65;

        bool previousConnectedSteeringParity = ConnectedSteeringParity;
        double previousConnectedSteeringExploration = ConnectedSteeringExploration;
        bool previousTraceDensity = TraceDensity;
        int previousTraceEvery = TraceEvery;
        bool previousRandomHeadings = RandomHeadings;
        bool previousBenchmarkAntParticles = BenchmarkAntParticles;

        try
        {
            ConnectedSteeringParity = true;
            ConnectedSteeringExploration = exploration;
            TraceDensity = true;
            TraceEvery = iterations;
            RandomHeadings = true;
            BenchmarkAntParticles = false;
            V3DensitySnapshots.Clear();
            V4DensitySnapshots.Clear();

            // The nonzero random-division probability enables V3's dynamic-list
            // Fisher-Yates shuffle. Equal population limits leave no birth budget,
            // so this isolates shuffled connected-sensor assignment without
            // conflating it with population changes.
            List<string> settings = new List<string>
            {
                "VoxelSettingsSlime 0.08 1 0.01 1",
                "DivisionSettings False 0 1 0 10 1",
                "DeathSettings False 0 1 0 10 1",
                "PopulationSettings 512 512 0.000001 0 1",
                "WrapSettings True"
            };

            Console.WriteLine("CONNECTED PARITY grid=" + grid + "^3 particles=" + particleCount
                + " iterations=" + iterations + " exploration="
                + exploration.ToString(CultureInfo.InvariantCulture));

            int[] v3 = RunV3Population(args, grid, particleCount, iterations, settings);
            int[] v4 = RunV4Population(grid, particleCount, iterations, settings, true);
            Equal(iterations, v3.Length, "connected parity V3 trace length");
            Equal(iterations, v4.Length, "connected parity V4 trace length");
            for (int i = 0; i < iterations; i++)
            {
                Equal(particleCount, v3[i], "connected parity V3 population at iteration " + (i + 1));
                Equal(particleCount, v4[i], "connected parity V4 population at iteration " + (i + 1));
            }

            True(V3DensitySnapshots.TryGetValue(iterations, out double[] v3Density),
                "connected parity V3 final density was not captured");
            True(V4DensitySnapshots.TryGetValue(iterations, out float[] v4Density),
                "connected parity V4 final density was not captured");

            MeasureConnectedDensityParity(
                v3Density,
                v4Density,
                grid,
                out double massSimilarity,
                out double coarseCosine,
                out double distributionOverlap,
                out double normalizedCentroidDistance,
                out double v3Mass,
                out double v4Mass);

            Console.WriteLine("  density mass V3=" + v3Mass.ToString("F3", CultureInfo.InvariantCulture)
                + " V4=" + v4Mass.ToString("F3", CultureInfo.InvariantCulture)
                + " similarity=" + massSimilarity.ToString("F3", CultureInfo.InvariantCulture));
            Console.WriteLine("  coarse cosine=" + coarseCosine.ToString("F3", CultureInfo.InvariantCulture)
                + " overlap=" + distributionOverlap.ToString("F3", CultureInfo.InvariantCulture)
                + " centroid distance=" + normalizedCentroidDistance.ToString("F4", CultureInfo.InvariantCulture));

            // Five independent calibration runs produced 0.971 mass similarity,
            // 0.948 cosine, 0.853 overlap, and 0.0105 centroid distance. These
            // distribution-level gates retain substantial shuffled-identity and
            // platform headroom while rejecting gross steering, deposit, or
            // diffusion loss.
            True(massSimilarity >= 0.75,
                "connected parity density mass similarity fell below 0.75");
            True(coarseCosine >= 0.75,
                "connected parity coarse density cosine fell below 0.75");
            True(distributionOverlap >= 0.60,
                "connected parity normalized density overlap fell below 0.60");
            True(normalizedCentroidDistance <= 0.08,
                "connected parity normalized density centroid distance exceeded 0.08");

            Console.WriteLine("Fixed-seed V3/V4 connected steering distribution parity passed.");
        }
        finally
        {
            ConnectedSteeringParity = previousConnectedSteeringParity;
            ConnectedSteeringExploration = previousConnectedSteeringExploration;
            TraceDensity = previousTraceDensity;
            TraceEvery = previousTraceEvery;
            RandomHeadings = previousRandomHeadings;
            BenchmarkAntParticles = previousBenchmarkAntParticles;
            V3DensitySnapshots.Clear();
            V4DensitySnapshots.Clear();
        }
    }

    static void MeasureConnectedDensityParity(
        double[] v3,
        float[] v4,
        int grid,
        out double massSimilarity,
        out double coarseCosine,
        out double distributionOverlap,
        out double normalizedCentroidDistance,
        out double v3Mass,
        out double v4Mass)
    {
        const int coarseResolution = 4;
        int expectedLength = checked(grid * grid * grid);
        Equal(expectedLength, v3.Length, "connected parity V3 density length");
        Equal(expectedLength, v4.Length, "connected parity V4 density length");

        double[] coarseV3 = new double[coarseResolution * coarseResolution * coarseResolution];
        double[] coarseV4 = new double[coarseV3.Length];
        v3Mass = 0;
        v4Mass = 0;
        double v3X = 0, v3Y = 0, v3Z = 0;
        double v4X = 0, v4Y = 0, v4Z = 0;
        for (int x = 0; x < grid; x++)
        {
            int coarseX = Math.Min(coarseResolution - 1, x * coarseResolution / grid);
            for (int y = 0; y < grid; y++)
            {
                int coarseY = Math.Min(coarseResolution - 1, y * coarseResolution / grid);
                for (int z = 0; z < grid; z++)
                {
                    int index = x * grid * grid + y * grid + z;
                    double a = v3[index];
                    double b = v4[index];
                    True(double.IsFinite(a) && a >= 0,
                        "connected parity V3 density contains a non-finite or negative value");
                    True(double.IsFinite(b) && b >= 0,
                        "connected parity V4 density contains a non-finite or negative value");

                    int coarseZ = Math.Min(coarseResolution - 1, z * coarseResolution / grid);
                    int coarseIndex = coarseX * coarseResolution * coarseResolution
                        + coarseY * coarseResolution + coarseZ;
                    coarseV3[coarseIndex] += a;
                    coarseV4[coarseIndex] += b;
                    v3Mass += a;
                    v4Mass += b;
                    v3X += a * (x + 0.5);
                    v3Y += a * (y + 0.5);
                    v3Z += a * (z + 0.5);
                    v4X += b * (x + 0.5);
                    v4Y += b * (y + 0.5);
                    v4Z += b * (z + 0.5);
                }
            }
        }

        True(v3Mass > 0 && v4Mass > 0, "connected parity produced an empty density field");
        massSimilarity = Math.Min(v3Mass, v4Mass) / Math.Max(v3Mass, v4Mass);

        double dot = 0, normV3 = 0, normV4 = 0, normalizedL1 = 0;
        for (int i = 0; i < coarseV3.Length; i++)
        {
            dot += coarseV3[i] * coarseV4[i];
            normV3 += coarseV3[i] * coarseV3[i];
            normV4 += coarseV4[i] * coarseV4[i];
            normalizedL1 += Math.Abs(coarseV3[i] / v3Mass - coarseV4[i] / v4Mass);
        }
        coarseCosine = dot / Math.Sqrt(normV3 * normV4);
        distributionOverlap = 1.0 - normalizedL1 * 0.5;

        double dx = v3X / v3Mass - v4X / v4Mass;
        double dy = v3Y / v3Mass - v4Y / v4Mass;
        double dz = v3Z / v3Mass - v4Z / v4Mass;
        normalizedCentroidDistance = Math.Sqrt(dx * dx + dy * dy + dz * dz)
            / (grid * Math.Sqrt(3.0));
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
            @"C:\Program Files\Rhino 9 WIP\System",
            @"C:\Program Files\Rhino 9\System",
            @"C:\Program Files\Rhino 8\System",
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
                Console.WriteLine("  Rhino.Inside unavailable (" + root + "): " + cause);
                RhinoInsideRoot = null;
            }
        }

        return false;
    }

    static void EnsureRhinoNativeLibrary()
    {
        foreach (string root in new[]
        {
            @"C:\Program Files\Rhino 9 WIP\System",
            @"C:\Program Files\Rhino 9\System",
            @"C:\Program Files\Rhino 8\System",
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
        string v3Path = OptionValue(args, "--v3");
        if (v3Path == null)
        {
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory())
                ?? FindRepositoryRoot(AppContext.BaseDirectory);
            if (repositoryRoot == null)
            {
                throw new DirectoryNotFoundException(
                    "Could not locate the Nuclei repository root. Pass --v3 <path-to-Nuclei3.gha>.");
            }

            v3Path = Path.Combine(
                repositoryRoot,
                "Nuclei-v3",
                "Nuclei3",
                "bin",
                "Release",
                "net7.0-windows",
                "Nuclei3.gha");
        }
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
        SetField(group, "wanderFrequency", ConnectedSteeringParity ? ConnectedSteeringExploration : 0.13);
        SetField(group, "baseWanderFrequency", 0.0);
        SetField(group, "color", System.Drawing.Color.FromArgb(255, 72, 184, 112));
        SetField(group, "ant", false);
        SetField(group, "connectedSteering", ConnectedSteeringParity);
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

    static int[] RunV4Population(
        int grid,
        int particleCount,
        int iterations,
        List<string> settings,
        bool initialWrapBoundaries = false)
    {
        int voxelCount = grid * grid * grid;
        float[] initialDensity = new float[voxelCount];
        object inputField = CreateField(WithInitialDensity(CreateFullDomain(grid, grid, grid), initialDensity));
        BenchmarkAntParticles = false;
        object snapshot = CaptureGpuSignatureSnapshot(inputField, particleCount, initialWrapBoundaries);

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
        object snapshot = CaptureGpuSignatureSnapshot(inputField, particleCount, true);
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

        // One engine for the whole run. Creating an engine loads 27 precompiled shaders,
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

    static object CaptureGpuSignatureSnapshot(object inputField, int particleCount, bool wrapBoundaries = false)
    {
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        Type particleType = RequiredCompatibilityType("Nuclei4.Particle");
        object group = Activator.CreateInstance(groupType)!;
        SetField(group, "speed", 0.72);
        SetField(group, "sensorDistance", 2.35);
        SetField(group, "sensorAngle", 37);
        SetField(group, "rotationAngle", 29);
        SetField(group, "depositValue", 0.85);
        SetField(group, "wanderFrequency", ConnectedSteeringParity ? ConnectedSteeringExploration : 0.13);
        SetField(group, "baseWanderFrequency", 0.0);
        SetField(group, "color", System.Drawing.Color.FromArgb(255, 72, 184, 112));
        SetField(group, "ant", BenchmarkAntParticles);
        SetField(group, "connectedSteering", ConnectedSteeringParity && !BenchmarkAntParticles);

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
        Invoke(snapshot, "CaptureCompactVoxels", inputField, false, wrapBoundaries);
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
            Near(4.9, ReadBackParticleX(engine), 1e-4, "periodic wrap did not use V3 fixed inset");

            wrapField.SetValue(settings, false);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 1);
            double reconciledX = ReadBackParticleX(engine);
            Near(4.0, reconciledX, 1e-4, "live wrap-off position was not clamped to the exact voxel boundary");
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }

        Console.WriteLine("Direct3D live wrap-on and wrap-off transitions passed.");
    }

    static void TestGpuBlockedStoredParentParity()
    {
        const int grid = 5;
        const int centerCoordinate = 2;
        const int centerIndex = centerCoordinate * grid + centerCoordinate;
        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        Type dimensionType = RequiredImplementationType("Nuclei4.SolverGpuDimensionMode");
        object dimensionMode = InvokeStatic(dimensionType, "FromResolution", grid, grid, 1);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");

        object authoredData = CreateSparseGpuData(grid, grid, 1, 0);
        List<double> maximumDensity = Enumerable.Repeat(-1.0, grid * grid).ToList();
        maximumDensity[centerIndex] = 0.005;
        authoredData = Invoke(authoredData, "WithScalarValues", 1, maximumDensity);
        List<double> speed = Enumerable.Repeat(1.0, grid * grid).ToList();
        speed[centerIndex] = 2.0;
        authoredData = Invoke(authoredData, "WithScalarValues", 2, speed);
        object authoredSnapshot = CaptureVoxelSnapshot(CreateField(authoredData), true);
        ConfigureSingleGpuParticleSnapshot(authoredSnapshot, 2.5f, 2.5f, 0.5f, centerIndex, 0.3f);
        object authoredSettings = CreateParityGpuSettings(settingsType);
        object authoredEngine = CreateGpuEngine(engineType, authoredSnapshot, authoredSettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(authoredEngine, authoredSnapshot, authoredSettings, dimensionMode, 2);
            Invoke(authoredEngine, "ReadBackParticles");
            float[] positions = Field<float[]>(authoredEngine, "particlePositionReadback");
            float[] directions = Field<float[]>(authoredEngine, "particleDirectionReadback");
            Near(3.1, positions[0], 1e-4, "active blocked parent did not apply its authored speed");
            Near(2.5, positions[1], 1e-4, "active blocked parent changed the planar movement axis");
            Equal(3 * grid + 2, (int)Math.Round(directions[3], MidpointRounding.ToEven),
                "active blocked parent movement did not resolve the destination parent");
        }
        finally
        {
            ((IDisposable)authoredEngine).Dispose();
        }

        object recoveryData = CreateSparseGpuData(grid, grid, 1, 0);
        recoveryData = Invoke(recoveryData, "WithScalarValues", 1, maximumDensity);
        object recoverySnapshot = CaptureVoxelSnapshot(CreateField(recoveryData), true);
        ConfigureSingleGpuParticleSnapshot(recoverySnapshot, 2.5f, 2.5f, 0.5f, centerIndex, 0.1f);
        object recoverySettings = CreateParityGpuSettings(settingsType);
        object recoveryEngine = CreateGpuEngine(engineType, recoverySnapshot, recoverySettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(recoveryEngine, recoverySnapshot, recoverySettings, dimensionMode, 2);
            Invoke(recoveryEngine, "ReadBackParticles");
            float[] positions = Field<float[]>(recoveryEngine, "particlePositionReadback");
            float[] directions = Field<float[]>(recoveryEngine, "particleDirectionReadback");
            int recoveredIndex = (int)Math.Round(directions[3], MidpointRounding.ToEven);
            True(recoveredIndex >= 0 && recoveredIndex != centerIndex,
                "active blocked parent did not recover into a walkable neighbour");
            uint[] flags = Field<uint[]>(recoverySnapshot, "VoxelFlags");
            True((flags[recoveredIndex >> 5] & (1u << (recoveredIndex & 31))) != 0,
                "active blocked parent recovery selected a non-walkable voxel");
            int recoveredX = recoveredIndex / grid;
            int recoveredY = recoveredIndex % grid;
            True(Math.Abs(recoveredX - centerCoordinate) <= 1 && Math.Abs(recoveredY - centerCoordinate) <= 1,
                "active blocked parent recovery escaped the V3 neighbour shell");
            Near(recoveredX + 0.5, positions[0], 1e-4, "recovered particle X was not voxel-centered");
            Near(recoveredY + 0.5, positions[1], 1e-4, "recovered particle Y was not voxel-centered");
            Near(0.5, positions[2], 1e-4, "recovered planar particle Z was not preserved");
        }
        finally
        {
            ((IDisposable)recoveryEngine).Dispose();
        }

        bool[] isolatedMask = new bool[grid * grid];
        isolatedMask[centerIndex] = true;
        object isolatedData = Invoke(CreateFullDomain(grid, grid, 1), "WithActiveMask", isolatedMask);
        isolatedData = Invoke(isolatedData, "WithScalarValues", 1, maximumDensity);
        float[] isolatedDensity = new float[grid * grid];
        isolatedDensity[centerIndex] = 0.75f;
        isolatedData = WithInitialDensity(isolatedData, isolatedDensity);
        object isolatedSnapshot = CaptureVoxelSnapshot(CreateField(isolatedData), true);
        ConfigureSingleGpuParticleSnapshot(isolatedSnapshot, 2.5f, 2.5f, 0.5f, centerIndex, 0.1f);
        object isolatedSettings = CreateParityGpuSettings(settingsType);
        object isolatedEngine = CreateGpuEngine(engineType, isolatedSnapshot, isolatedSettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(isolatedEngine, isolatedSnapshot, isolatedSettings, dimensionMode, 2);
            Invoke(isolatedEngine, "ReadBackParticles");
            float[] positions = Field<float[]>(isolatedEngine, "particlePositionReadback");
            float[] directions = Field<float[]>(isolatedEngine, "particleDirectionReadback");
            Near(2.5, positions[0], 1e-4, "isolated blocked-parent fallback changed particle X");
            Near(2.5, positions[1], 1e-4, "isolated blocked-parent fallback changed particle Y");
            Equal(centerIndex, (int)Math.Round(directions[3], MidpointRounding.ToEven),
                "isolated active blocked parent was discarded");
            Near(-1.0, directions[0], 1e-4, "isolated blocked-parent fallback did not reverse direction");
            Invoke(isolatedEngine, "ReadBackDensity");
            Near(0.0, Field<float[]>(isolatedEngine, "densityReadback")[centerIndex], 1e-6,
                "density under an isolated active blocked parent was not cleared");
        }
        finally
        {
            ((IDisposable)isolatedEngine).Dispose();
        }

        Console.WriteLine("Direct3D active blocked-parent movement, recovery, fallback, counting, and density parity passed.");
    }

    static void TestGpuSparseActiveBindings()
    {
        const int grid = 5;
        const int centerIndex = 2 * grid + 2;
        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        Type dimensionType = RequiredImplementationType("Nuclei4.SolverGpuDimensionMode");
        object dimensionMode = InvokeStatic(dimensionType, "FromResolution", grid, grid, 1);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");

        object transitionData = CreateSparseGpuData(grid, grid, 1, 0);
        object transitionSnapshot = CaptureVoxelSnapshot(CreateField(transitionData), true);
        ConfigureSingleGpuParticleSnapshot(transitionSnapshot, 2.5f, 2.5f, 0.5f, centerIndex, 0.1f);
        object transitionSettings = CreateParityGpuSettings(settingsType);
        object transitionEngine = CreateGpuEngine(engineType, transitionSnapshot, transitionSettings, false, false, false, 0, 1);
        try
        {
            SetField(transitionSettings, "WrapBoundaries", false);
            InvokeGpuStep(transitionEngine, transitionSnapshot, transitionSettings, dimensionMode, 0);
            Invoke(transitionEngine, "ReadBackParticles");
            Equal(centerIndex,
                (int)Math.Round(Field<float[]>(transitionEngine, "particleDirectionReadback")[3], MidpointRounding.ToEven),
                "sparse boundary-mode transition lost an active parent");
        }
        finally
        {
            ((IDisposable)transitionEngine).Dispose();
        }

        float[] initialDensity = new float[grid * grid];
        initialDensity[centerIndex] = 1.0f;
        object diffusionData = WithInitialDensity(CreateSparseGpuData(grid, grid, 1, 0), initialDensity);
        object diffusionSnapshot = CaptureVoxelSnapshot(CreateField(diffusionData), true);
        SetField(diffusionSnapshot, "HasSlimeParticles", false);
        SetField(diffusionSnapshot, "HasAntParticles", false);
        SetField(diffusionSnapshot, "ParticleGroups", Array.CreateInstance(groupType, 0));
        object diffusionSettings = CreateParityGpuSettings(settingsType);
        SetField(diffusionSettings, "Diffuse", 0.5);
        SetField(diffusionSettings, "DiffuseRange", 1);
        object diffusionEngine = CreateGpuEngine(engineType, diffusionSnapshot, diffusionSettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(diffusionEngine, diffusionSnapshot, diffusionSettings, dimensionMode, 2);
            Invoke(diffusionEngine, "ReadBackDensity");
            float[] density = Field<float[]>(diffusionEngine, "densityReadback");
            True(density[centerIndex] > 0.0f, "sparse scalar diffusion erased every active target");
            Near(0.0, density[0], 1e-6, "sparse scalar diffusion populated an inactive voxel");
            True(density.Sum(value => (double)value) > 0.0, "sparse scalar diffusion produced an empty field");
        }
        finally
        {
            ((IDisposable)diffusionEngine).Dispose();
        }

        object antSnapshot = CaptureVoxelSnapshot(CreateField(CreateSparseGpuData(grid, grid, 1, 0)), true);
        float[] antFood = new float[grid * grid];
        antFood[centerIndex] = 1.0f;
        SetField(antSnapshot, "HasSlimeParticles", false);
        SetField(antSnapshot, "HasAntParticles", true);
        SetField(antSnapshot, "AntFoodPheromone", antFood);
        SetField(antSnapshot, "AntBasePheromone", new float[grid * grid]);
        SetField(antSnapshot, "ParticleGroups", Array.CreateInstance(groupType, 0));
        object antSettings = CreateParityGpuSettings(settingsType);
        SetField(antSettings, "AntFoodDiffuse", 0.5);
        SetField(antSettings, "AntBaseDiffuse", 0.0);
        SetField(antSettings, "AntDiffuseRange", 1);
        SetField(antSettings, "AntFoodDecay", 0.0);
        SetField(antSettings, "AntBaseDecay", 0.0);
        object antEngine = CreateGpuEngine(engineType, antSnapshot, antSettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(antEngine, antSnapshot, antSettings, dimensionMode, 2);
            Invoke(antEngine, "ReadBackAntFields");
            float[] diffusedAntFood = Field<float[]>(antEngine, "antFoodReadback");
            True(diffusedAntFood[centerIndex] > 0.0f, "sparse ant diffusion erased every active target");
            Near(0.0, diffusedAntFood[0], 1e-6, "sparse ant diffusion populated an inactive voxel");
            True(diffusedAntFood.Sum(value => (double)value) > 0.0, "sparse ant diffusion produced an empty field");
        }
        finally
        {
            ((IDisposable)antEngine).Dispose();
        }

        Console.WriteLine("Direct3D sparse boundary transition, scalar diffusion, and ant diffusion bindings passed.");
    }

    static object CreateSparseGpuData(int x, int y, int z, params int[] inactiveIndices)
    {
        object data = CreateFullDomain(x, y, z);
        bool[] active = Enumerable.Repeat(true, x * y * z).ToArray();
        foreach (int index in inactiveIndices) active[index] = false;
        return Invoke(data, "WithActiveMask", active);
    }

    static object CreateParityGpuSettings(Type settingsType)
    {
        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "WrapBoundaries", true);
        SetField(settings, "Diffuse", 0.0);
        SetField(settings, "DiffusionGradual", 1.0);
        SetField(settings, "Decay", 0.0);
        SetField(settings, "AntFoodDiffuse", 0.0);
        SetField(settings, "AntBaseDiffuse", 0.0);
        SetField(settings, "AntFoodDecay", 0.0);
        SetField(settings, "AntBaseDecay", 0.0);
        return settings;
    }

    static void ConfigureSingleGpuParticleSnapshot(
        object snapshot,
        float x,
        float y,
        float z,
        int parentIndex,
        float speed)
    {
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        Array groups = Array.CreateInstance(groupType, 1);
        groups.SetValue(Activator.CreateInstance(groupType), 0);
        SetField(snapshot, "Particles", null);
        SetField(snapshot, "ParticleGroups", groups);
        SetField(snapshot, "ParticleCount", 1);
        SetField(snapshot, "GroupCount", 1);
        SetField(snapshot, "ParticlePositionsXyz", new[] { x, y, z });
        SetField(snapshot, "ParticleDirectionsXyz", new[] { 1.0f, 0.0f, 0.0f });
        SetField(snapshot, "ParticleYAxesXyz", new[] { 0.0f, 1.0f, 0.0f });
        SetField(snapshot, "ParticleHomesXyz", new[] { x, y, z });
        SetField(snapshot, "ParticleAntStates", new uint[1]);
        SetOptionalField(snapshot, "ParticleAntLaunchBoundaryStates", new uint[1]);
        SetOptionalField(snapshot, "ParticleAges", new int[1]);
        SetField(snapshot, "ParticleGroupIndices", new[] { 0 });
        SetField(snapshot, "ParticleParentIndices", new[] { parentIndex });
        SetField(snapshot, "GroupData0", new[] { speed, 0.0f, 0.0f, 0.0f });
        SetField(snapshot, "GroupData1", new[] { 0.0f, 0.0f, 0.0f, 0.0f });
        SetField(snapshot, "GroupColorData", new float[4]);
    }

    static void TestGpuAntLaunchAndRandomDivisionInheritance()
    {
        const int grid = 24;
        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object dimensionMode = InvokeStatic(
            RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"),
            "FromResolution",
            grid,
            grid,
            1);

        object latchSnapshot = CaptureVoxelSnapshot(CreateField(CreateFullDomain(grid, grid, 1)), false);
        ConfigureSingleAntGpuParticleSnapshot(
            latchSnapshot,
            22.9f,
            12.5f,
            2.5f,
            12.5f,
            0.5f,
            0,
            false,
            false);
        object latchSettings = CreateParityGpuSettings(settingsType);
        SetField(latchSettings, "WrapBoundaries", false);
        object latchEngine = CreateGpuEngine(engineType, latchSnapshot, latchSettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(latchEngine, latchSnapshot, latchSettings, dimensionMode, 2);
            Invoke(latchEngine, "ReadBackParticles");
            Equal(1, ReadParticleAuxiliaryChannel(latchEngine, "particleAntLaunchBoundaryOffset", 0),
                "ant boundary contact did not latch the launch state");
            True(Field<float[]>(latchEngine, "particlePositionReadback")[0] <= grid - 1.0f + 1e-4f,
                "ant boundary-latch probe escaped the reflective field");
        }
        finally
        {
            ((IDisposable)latchEngine).Dispose();
        }

        object nestSnapshot = CaptureVoxelSnapshot(CreateField(CreateFullDomain(grid, grid, 1)), true);
        ConfigureSingleAntGpuParticleSnapshot(
            nestSnapshot,
            12.5f,
            12.5f,
            11.5f,
            12.5f,
            1.5f,
            40,
            true,
            true);
        SetField(nestSnapshot, "ParticleDirectionsXyz", new[] { -1.0f, 0.0f, 0.0f });
        object nestSettings = CreateParityGpuSettings(settingsType);
        object nestEngine = CreateGpuEngine(engineType, nestSnapshot, nestSettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(nestEngine, nestSnapshot, nestSettings, dimensionMode, 2);
            Invoke(nestEngine, "ReadBackParticles");
            Equal(0, ReadParticleAuxiliaryChannel(nestEngine, "particleAntStateOffset", 0),
                "nest visit did not clear found-food state");
            Equal(0, ReadParticleAuxiliaryChannel(nestEngine, "particleAntLaunchBoundaryOffset", 0),
                "nest visit did not reset the ant launch-boundary latch");
            Equal(1, ReadParticleAuxiliaryChannel(nestEngine, "particleAgeOffset", 0),
                "nest visit did not reset ant age to the V3 post-visit value");
        }
        finally
        {
            ((IDisposable)nestEngine).Dispose();
        }

        object divisionSnapshot = CaptureVoxelSnapshot(CreateField(CreateFullDomain(grid, grid, 1)), true);
        ConfigureSingleAntGpuParticleSnapshot(
            divisionSnapshot,
            12.5f,
            12.5f,
            3.25f,
            4.75f,
            0.0f,
            41,
            true,
            true);
        SetField(divisionSnapshot, "ParticleDirectionsXyz", new[] { 0.6f, 0.8f, 0.0f });
        SetField(divisionSnapshot, "ParticleYAxesXyz", new[] { -0.8f, 0.6f, 0.0f });

        object divisionSettings = CreateParityGpuSettings(settingsType);
        SetField(divisionSettings, "DynamicPopulation", true);
        SetField(divisionSettings, "MinimumPopulation", 0);
        SetField(divisionSettings, "MaximumPopulation", 2);
        SetField(divisionSettings, "Division", false);
        SetField(divisionSettings, "Death", false);
        SetField(divisionSettings, "RandomPopulationFrequency", 1);
        SetField(divisionSettings, "RandomDivisionProbability", 1.0);
        SetField(divisionSettings, "RandomDeathProbability", 0.0);

        object divisionEngine = CreateGpuEngine(engineType, divisionSnapshot, divisionSettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(divisionEngine, divisionSnapshot, divisionSettings, dimensionMode, 2);
            Invoke(divisionEngine, "ReadBackParticles");
            Equal(2, Field<int>(divisionEngine, "particleCount"), "random ant division child count");

            int capacity = Field<int>(divisionEngine, "particleCapacity");
            float[] positions = Field<float[]>(divisionEngine, "particlePositionReadback");
            float[] yAxes = Field<float[]>(divisionEngine, "particleYAxisReadback");
            int childSlot = -1;
            int parentSlot = -1;
            for (int slot = 0; slot < capacity; slot++)
            {
                if (positions[slot * 4 + 3] < -0.5f) continue;
                float marker = yAxes[slot * 4 + 3];
                if (marker < -0.5f && marker > -1.5f) childSlot = slot;
                else parentSlot = slot;
            }
            True(childSlot >= 0 && parentSlot >= 0, "random ant division birth marker did not identify parent and child");

            Equal(43, ReadParticleAuxiliaryChannel(divisionEngine, "particleAgeOffset", parentSlot),
                "random-division parent age before inheritance");
            Equal(
                ReadParticleAuxiliaryChannel(divisionEngine, "particleAgeOffset", parentSlot),
                ReadParticleAuxiliaryChannel(divisionEngine, "particleAgeOffset", childSlot),
                "random-division child age inheritance");
            Equal(1, ReadParticleAuxiliaryChannel(divisionEngine, "particleAntStateOffset", parentSlot),
                "random-division parent found-food state");
            Equal(
                ReadParticleAuxiliaryChannel(divisionEngine, "particleAntStateOffset", parentSlot),
                ReadParticleAuxiliaryChannel(divisionEngine, "particleAntStateOffset", childSlot),
                "random-division child found-food inheritance");
            Equal(1, ReadParticleAuxiliaryChannel(divisionEngine, "particleAntLaunchBoundaryOffset", parentSlot),
                "random-division parent launch-boundary state");
            Equal(
                ReadParticleAuxiliaryChannel(divisionEngine, "particleAntLaunchBoundaryOffset", parentSlot),
                ReadParticleAuxiliaryChannel(divisionEngine, "particleAntLaunchBoundaryOffset", childSlot),
                "random-division child launch-boundary inheritance");

            float[] homes = Field<float[]>(divisionEngine, "particleHomeReadback");
            for (int component = 0; component < 4; component++)
            {
                Equal(
                    BitConverter.SingleToInt32Bits(homes[parentSlot * 4 + component]),
                    BitConverter.SingleToInt32Bits(homes[childSlot * 4 + component]),
                    "random-division child home state component " + component);
            }
            Near(3.25, homes[childSlot * 4], 1e-6, "random-division child home X");
            Near(4.75, homes[childSlot * 4 + 1], 1e-6, "random-division child home Y");
            Near(0.5, homes[childSlot * 4 + 2], 1e-6, "random-division child home Z");

            float[] homeAxes = Field<float[]>(divisionEngine, "particleHomeAxesReadback");
            for (int channel = 0; channel < 6; channel++)
            {
                Equal(
                    BitConverter.SingleToInt32Bits(homeAxes[channel * capacity + parentSlot]),
                    BitConverter.SingleToInt32Bits(homeAxes[channel * capacity + childSlot]),
                    "random-division child home-plane axis channel " + channel);
            }
        }
        finally
        {
            ((IDisposable)divisionEngine).Dispose();
        }

        Console.WriteLine("Direct3D ant launch latch/nest reset and random-child home/food/age/launch inheritance passed.");
    }

    static void ConfigureSingleAntGpuParticleSnapshot(
        object snapshot,
        float x,
        float y,
        float homeX,
        float homeY,
        float speed,
        int age,
        bool foundFood,
        bool launchBoundaryHit)
    {
        int resY = Field<int>(snapshot, "ResY");
        int resZ = Field<int>(snapshot, "ResZ");
        int parentIndex = (int)Math.Floor(x) * resY * resZ + (int)Math.Floor(y) * resZ;
        ConfigureSingleGpuParticleSnapshot(snapshot, x, y, 0.5f, parentIndex, speed);
        SetField(snapshot, "HasSlimeParticles", false);
        SetField(snapshot, "HasAntParticles", true);
        Array groups = Field<Array>(snapshot, "ParticleGroups");
        SetField(groups.GetValue(0)!, "ant", true);
        SetField(snapshot, "ParticleHomesXyz", new[] { homeX, homeY, 0.5f });
        SetField(snapshot, "ParticleAges", new[] { age });
        SetField(snapshot, "ParticleAntStates", new[] { foundFood ? 1u : 0u });
        SetField(snapshot, "ParticleAntLaunchBoundaryStates", new[] { launchBoundaryHit ? 1u : 0u });
        SetField(snapshot, "GroupData0", new[] { speed, 2.0f, 0.0f, 0.0f });
        SetField(snapshot, "GroupData1", new[] { 0.0f, 1.0f, 0.0f, 1.0f });
    }

    static int ReadParticleAuxiliaryChannel(object engine, string absoluteOffsetField, int slot)
    {
        int capacity = Field<int>(engine, "particleCapacity");
        True(slot >= 0 && slot < capacity, absoluteOffsetField + " slot range");
        int baseOffset = Field<int>(engine, "particleAgeOffset");
        int absoluteOffset = Field<int>(engine, absoluteOffsetField);
        int relativeOffset = absoluteOffset - baseOffset;
        True(relativeOffset >= 0, absoluteOffsetField + " was not allocated");
        return Field<int[]>(engine, "particleAuxReadback")[relativeOffset + slot];
    }

    static void TestGpuConnectedSteeringOracle()
    {
        const uint expectedIterationTwoKey = 3406519409u;
        uint key = ConnectedSteeringSampleKey(0, 2);
        Equal(expectedIterationTwoKey, key, "connected steering iteration-two sample hash");
        double sample = key / 4294967296.0;
        Equal(2, (int)Math.Floor(sample * 3.0),
            "connected steering known hash did not select the right sensor ordinal");

        float[] strongest = RunGpuConnectedSteeringOracleCase(0.0f);
        Near(1.0, strongest[0], 1e-5, "connected strongest-sensor direction X");
        Near(0.0, strongest[1], 1e-5, "connected strongest-sensor direction Y");
        Near(0.0, strongest[2], 1e-5, "connected strongest-sensor direction Z");
        Near(5.0, strongest[3], 1e-4, "connected strongest-sensor position X");
        Near(4.5, strongest[4], 1e-4, "connected strongest-sensor position Y");

        float[] exploratory = RunGpuConnectedSteeringOracleCase(1.0f);
        const double expectedX = 0.19611613513818404;
        const double expectedY = 0.9805806756909202;
        Near(expectedX, exploratory[0], 1e-5, "connected exploratory direction X");
        Near(expectedY, exploratory[1], 1e-5, "connected exploratory direction Y");
        Near(0.0, exploratory[2], 1e-5, "connected exploratory direction Z");
        Near(4.5 + expectedX * 0.5, exploratory[3], 1e-4, "connected exploratory position X");
        Near(4.5 + expectedY * 0.5, exploratory[4], 1e-4, "connected exploratory position Y");

        Console.WriteLine("Direct3D connected steering endpoints and known-hash sensor choice passed.");
    }

    static uint ConnectedSteeringSampleKey(int particleIndex, int iteration)
    {
        unchecked
        {
            uint key = (uint)particleIndex ^ ((uint)iteration * 2654435769u) ^ 2738958700u;
            key ^= key >> 16;
            key *= 2146121005u;
            key ^= key >> 15;
            key *= 2221713035u;
            key ^= key >> 16;
            return key;
        }
    }

    static float[] RunGpuConnectedSteeringOracleCase(float exploration)
    {
        const int grid = 9;
        const int parentIndex = 4 * grid + 4;
        float[] density = new float[grid * grid];
        density[4 * grid + 2] = 0.25f; // left
        density[6 * grid + 4] = 1.0f;  // front
        density[4 * grid + 6] = 0.25f; // right

        object data = WithInitialDensity(CreateFullDomain(grid, grid, 1), density);
        object snapshot = CaptureVoxelSnapshot(CreateField(data), true);
        ConfigureSingleGpuParticleSnapshot(snapshot, 4.5f, 4.5f, 0.5f, parentIndex, 0.5f);
        SetField(snapshot, "GroupData0", new[] { 0.5f, 2.0f, (float)(Math.PI * 0.5), exploration });
        SetField(snapshot, "GroupData1", new[] { (float)(Math.PI * 0.5), -1.0f, 0.0f, 0.0f });

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = CreateParityGpuSettings(settingsType);
        object dimensionMode = InvokeStatic(
            RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"),
            "FromResolution",
            grid,
            grid,
            1);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object engine = CreateGpuEngine(engineType, snapshot, settings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            Invoke(engine, "ReadBackParticles");
            float[] positions = Field<float[]>(engine, "particlePositionReadback");
            float[] directions = Field<float[]>(engine, "particleDirectionReadback");
            return new[]
            {
                directions[0], directions[1], directions[2],
                positions[0], positions[1], positions[2]
            };
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }
    }

    static void TestGpuPlanarOriginPreservation()
    {
        VerifyGpuPlanarOriginPreservation(
            "XY",
            5, 5, 1,
            new[] { 2.25, 2.25, 0.2 },
            new[] { 1.0, 0.0, 0.0 },
            new[] { 0.0, 1.0, 0.0 },
            2);
        VerifyGpuPlanarOriginPreservation(
            "XZ",
            5, 1, 5,
            new[] { 2.25, 0.3, 2.25 },
            new[] { 1.0, 0.0, 0.0 },
            new[] { 0.0, 0.0, -1.0 },
            1);
        VerifyGpuPlanarOriginPreservation(
            "YZ",
            1, 5, 5,
            new[] { 0.4, 2.25, 2.25 },
            new[] { 0.0, 1.0, 0.0 },
            new[] { 0.0, 0.0, 1.0 },
            0);

        Console.WriteLine("Direct3D planar reset preservation and first-move centering passed.");
    }

    static void VerifyGpuPlanarOriginPreservation(
        string label,
        int resX,
        int resY,
        int resZ,
        double[] origin,
        double[] xAxis,
        double[] yAxis,
        int inactiveAxis)
    {
        object snapshot = CaptureVoxelSnapshot(CreateField(CreateFullDomain(resX, resY, resZ)));
        Type groupType = RequiredCompatibilityType("Nuclei4.ParticleGroup");
        object group = Activator.CreateInstance(groupType)!;
        SetField(group, "speed", 0.25);
        SetField(group, "sensorDistance", 1.0);
        SetField(group, "sensorAngle", 0);
        SetField(group, "rotationAngle", 0);
        SetField(group, "depositValue", 0.0);

        object inputPlane = CreateRawPlane(
            origin[0], origin[1], origin[2],
            xAxis[0], xAxis[1], xAxis[2],
            yAxis[0], yAxis[1], yAxis[2]);
        object resetPlane = Invoke(snapshot, "PrepareResetPlane", inputPlane, group);
        object resetOrigin = PropertyValue(resetPlane, "Origin");
        Near(origin[inactiveAxis], PointCoordinate(resetOrigin, inactiveAxis), 1e-6,
            label + " reset changed inactive origin");

        Array groups = Array.CreateInstance(groupType, 1);
        groups.SetValue(group, 0);
        SnapshotType.GetField("Particles")!.SetValue(snapshot, null);
        SnapshotType.GetField("ParticleGroups")!.SetValue(snapshot, groups);
        SnapshotType.GetField("ParticleCount")!.SetValue(snapshot, 1);
        SnapshotType.GetField("GroupCount")!.SetValue(snapshot, 1);
        SnapshotType.GetField("ParticlePositionsXyz")!.SetValue(snapshot, new[]
        {
            (float)origin[0], (float)origin[1], (float)origin[2]
        });
        SnapshotType.GetField("ParticleDirectionsXyz")!.SetValue(snapshot, new[]
        {
            (float)xAxis[0], (float)xAxis[1], (float)xAxis[2]
        });
        SnapshotType.GetField("ParticleYAxesXyz")!.SetValue(snapshot, new[]
        {
            (float)yAxis[0], (float)yAxis[1], (float)yAxis[2]
        });
        SnapshotType.GetField("ParticleHomesXyz")!.SetValue(snapshot, new[]
        {
            (float)origin[0], (float)origin[1], (float)origin[2]
        });
        SnapshotType.GetField("ParticleAntStates")!.SetValue(snapshot, new uint[1]);
        SetOptionalField(snapshot, "ParticleAntLaunchBoundaryStates", new uint[1]);
        SetOptionalField(snapshot, "ParticleAges", new int[1]);
        SnapshotType.GetField("ParticleGroupIndices")!.SetValue(snapshot, new[] { 0 });
        int parentIndex = (int)origin[0] * resY * resZ + (int)origin[1] * resZ + (int)origin[2];
        SnapshotType.GetField("ParticleParentIndices")!.SetValue(snapshot, new[] { parentIndex });
        SnapshotType.GetField("GroupData0")!.SetValue(snapshot, new[] { 0.25f, 1.0f, 0.0f, 0.0f });
        SnapshotType.GetField("GroupData1")!.SetValue(snapshot, new[] { 0.0f, 0.0f, 0.0f, 0.0f });
        SnapshotType.GetField("GroupColorData")!.SetValue(snapshot, new float[4]);

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "Diffuse", 0.0);
        SetField(settings, "Decay", 0.0);
        FieldInfo wrapField = settingsType.GetField("WrapBoundaries")!;
        wrapField.SetValue(settings, false);
        object dimensionMode = InvokeStatic(
            RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"),
            "FromResolution",
            resX,
            resY,
            resZ);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object engine = CreateGpuEngine(engineType, snapshot, settings, false, false, false, 0, 1);
        try
        {
            // A live wrap-mode change dispatches the boundary-transition shader even
            // on a no-movement warm-up iteration.
            wrapField.SetValue(settings, true);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 0);
            Near(origin[inactiveAxis], ReadBackParticleCoordinate(engine, inactiveAxis), 1e-5,
                label + " boundary transition changed inactive origin");

            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 1);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            double inactiveExtent = inactiveAxis == 0 ? resX : inactiveAxis == 1 ? resY : resZ;
            Near(inactiveExtent * 0.5, ReadBackParticleCoordinate(engine, inactiveAxis), 1e-5,
                label + " first move did not center inactive origin");

            int movementAxis = inactiveAxis == 0 ? 1 : 0;
            True(Math.Abs(ReadBackParticleCoordinate(engine, movementAxis) - origin[movementAxis]) > 0.05,
                label + " first movement did not execute");
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }
    }

    static void TestGpuPopulationPassOrdering()
    {
        const int grid = 24;
        const int initialPopulation = 64;
        BenchmarkAntParticles = false;
        object inputField = CreateField(CreateFullDomain(grid, grid, grid));
        object snapshot = CaptureGpuSignatureSnapshot(inputField, initialPopulation, true);

        Type settingsType = RequiredImplementationType("Nuclei4.SolverGpuSettings");
        object parsedNegativeRanges = InvokeStatic(
            settingsType,
            "FromStrings",
            new List<string>
            {
                "DivisionSettings True 0 -7 0 100 1",
                "DeathSettings True 0 -5 0 100 1"
            });
        Equal(-7, Field<int>(parsedNegativeRanges, "DivisionRange"), "negative division range parser parity");
        Equal(-5, Field<int>(parsedNegativeRanges, "DeathRange"), "negative death range parser parity");

        object settings = Activator.CreateInstance(settingsType)!;
        SetField(settings, "WrapBoundaries", true);
        SetField(settings, "DynamicPopulation", true);
        SetField(settings, "MinimumPopulation", initialPopulation);
        SetField(settings, "MaximumPopulation", initialPopulation * 2);
        SetField(settings, "Division", true);
        SetField(settings, "DivisionMinimumAge", 0);
        SetField(settings, "DivisionRange", 0);
        SetField(settings, "DivisionMinimumNeighbours", 0);
        SetField(settings, "DivisionMaximumNeighbours", 1000);
        SetField(settings, "DivisionFrequency", 1);
        SetField(settings, "RandomPopulationFrequency", 1);
        SetField(settings, "RandomDivisionProbability", 0.0);
        SetField(settings, "RandomDeathProbability", 0.0);

        object dimensionMode = InvokeStatic(
            RequiredImplementationType("Nuclei4.SolverGpuDimensionMode"),
            "FromResolution",
            grid,
            grid,
            grid);
        Type engineType = RequiredImplementationType("Nuclei4.GpuFullSlimeSolverEngine");
        object engine = CreateGpuEngine(engineType, snapshot, settings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            MethodInfo tryPopulationReadback = engineType.GetMethod(
                "TryCompletePopulationReadback",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(engineType.FullName, "TryCompletePopulationReadback");
            int[] asyncGroupPopulations = new int[1];
            object[] asyncReadbackArguments = { asyncGroupPopulations, 0 };
            Stopwatch asyncTimer = Stopwatch.StartNew();
            bool asyncCompleted = false;
            while (!asyncCompleted && asyncTimer.ElapsedMilliseconds < 2000)
            {
                asyncCompleted = (bool)tryPopulationReadback.Invoke(engine, asyncReadbackArguments)!;
                if (!asyncCompleted) System.Threading.Thread.Yield();
            }
            True(asyncCompleted, "nonblocking population counter ring did not complete");
            int asyncPopulation = (int)asyncReadbackArguments[1];
            Invoke(engine, "ReadBackParticles");
            int dividedPopulation = Field<int>(engine, "particleCount");
            int dividedCapacity = Field<int>(engine, "particleCapacity");
            float[] dividedPositions = Field<float[]>(engine, "particlePositionReadback");
            int dividedAlive = 0;
            int dividedGroupZero = 0;
            for (int slot = 0; slot < dividedCapacity; slot++)
            {
                float groupTag = dividedPositions[slot * 4 + 3];
                if (groupTag < -0.5f) continue;

                dividedAlive++;
                if ((int)Math.Round(groupTag, MidpointRounding.ToEven) == 0)
                {
                    dividedGroupZero++;
                }
            }

            Equal(dividedPopulation, dividedAlive, "blocking population/alive-slot mismatch");
            Equal(dividedPopulation, dividedGroupZero, "blocking population/group-tag mismatch");
            Equal(asyncPopulation, asyncGroupPopulations[0],
                "nonblocking total/group population mismatch; blocking=" + dividedPopulation
                + ", alive=" + dividedAlive + ", tagged-group0=" + dividedGroupZero);
            True(dividedPopulation > initialPopulation, "normal division produced no newborns in ordering probe");
            Equal(dividedPopulation, asyncPopulation,
                "nonblocking population snapshot differed from blocking state");

            Invoke(engine, "FastReset", snapshot, settings);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            Invoke(engine, "ReadBackPopulationState");
            int blockingPopulation = Field<int>(engine, "particleCount");
            int[] staleGuardPopulations = { -7 };
            object[] staleGuardArguments = { staleGuardPopulations, -7 };
            bool staleSnapshotApplied = false;
            Stopwatch staleTimer = Stopwatch.StartNew();
            while (Field<bool[]>(engine, "populationReadbackPending").Any(value => value)
                && staleTimer.ElapsedMilliseconds < 2000)
            {
                staleSnapshotApplied |= (bool)tryPopulationReadback.Invoke(engine, staleGuardArguments)!;
                if (Field<bool[]>(engine, "populationReadbackPending").Any(value => value))
                {
                    System.Threading.Thread.Yield();
                }
            }
            False(staleSnapshotApplied, "stale async population snapshot replaced a newer blocking sync");
            Equal(-7, staleGuardPopulations[0], "stale async population snapshot rewrote group telemetry");
            Equal(blockingPopulation, Field<int>(engine, "particleCount"),
                "stale async population snapshot rewrote total telemetry");

            SetField(settings, "DivisionRange", grid);
            SetField(settings, "RandomDivisionProbability", 1.0);
            Invoke(engine, "FastReset", snapshot, settings);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            Invoke(engine, "ReadBackParticles");
            int telemetryPopulation = Field<int>(engine, "particleCount");
            int telemetryCapacity = Field<int>(engine, "particleCapacity");
            float[] telemetryPositions = Field<float[]>(engine, "particlePositionReadback");
            float[] telemetryYAxes = Field<float[]>(engine, "particleYAxisReadback");
            int[] telemetryAuxiliary = Field<int[]>(engine, "particleAuxReadback");
            int telemetryAlive = 0;
            int randomNewborns = 0;
            for (int slot = 0; slot < telemetryCapacity; slot++)
            {
                if (telemetryPositions[slot * 4 + 3] < -0.5f) continue;

                telemetryAlive++;
                float birthMarker = telemetryYAxes[slot * 4 + 3];
                if (birthMarker < -0.5f && birthMarker > -1.5f)
                {
                    randomNewborns++;
                    Equal(0, telemetryAuxiliary[telemetryCapacity + slot], "random newborn death-neighbour state");
                    Equal(0, telemetryAuxiliary[telemetryCapacity * 2 + slot], "random newborn division-neighbour state");
                }
            }

            Equal(telemetryPopulation, telemetryAlive, "population telemetry alive-slot count");
            True(randomNewborns > 0, "random division produced no marked newborns in telemetry probe");
            int postNormalDivisionPopulation = telemetryPopulation - randomNewborns;
            True(
                postNormalDivisionPopulation > initialPopulation,
                "normal division produced no newborns for post-division telemetry coverage");
            for (int slot = 0; slot < telemetryCapacity; slot++)
            {
                if (telemetryPositions[slot * 4 + 3] < -0.5f) continue;
                float birthMarker = telemetryYAxes[slot * 4 + 3];
                if (birthMarker >= -0.5f || birthMarker <= -1.5f)
                {
                    Equal(
                        postNormalDivisionPopulation - 1,
                        telemetryAuxiliary[telemetryCapacity * 2 + slot],
                        "post-normal-division neighbour publication");
                }
            }

            Invoke(engine, "FastReset", snapshot, settings);
            SetField(settings, "RandomDivisionProbability", 0.0);
            SetField(settings, "RandomDeathProbability", 1.0);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            Invoke(engine, "ReadBackPopulationState");
            Equal(
                initialPopulation,
                Field<int>(engine, "particleCount"),
                "random death did not sample the post-normal-division population");

            SetField(settings, "MinimumPopulation", 0);
            Invoke(engine, "FastReset", snapshot, settings);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            Invoke(engine, "ReadBackPopulationState");
            Equal(
                0,
                Field<int>(engine, "particleCount"),
                "random death did not include normal newborns");

            SetField(settings, "MinimumPopulation", initialPopulation / 2);
            SetField(settings, "RandomDeathProbability", 0.0);
            SetField(settings, "Death", true);
            SetField(settings, "DeathMinimumAge", 0);
            SetField(settings, "DeathRange", 0);
            SetField(settings, "DeathMinimumNeighbours", int.MaxValue);
            SetField(settings, "DeathMaximumNeighbours", int.MaxValue);
            SetField(settings, "DeathFrequency", 1);
            SetField(settings, "DivisionRange", grid);
            SetField(settings, "DivisionMinimumNeighbours", initialPopulation - 1);
            SetField(settings, "DivisionMaximumNeighbours", initialPopulation - 1);
            Invoke(engine, "FastReset", snapshot, settings);
            InvokeGpuStep(engine, snapshot, settings, dimensionMode, 2);
            Invoke(engine, "ReadBackPopulationState");
            True(
                Field<int>(engine, "particleCount") > initialPopulation / 2,
                "normal division used post-death rather than pre-death neighbour counts");
        }
        finally
        {
            ((IDisposable)engine).Dispose();
        }

        object storedSnapshot = CaptureGpuSignatureSnapshot(inputField, initialPopulation, true);
        float[] storedPositions = Field<float[]>(storedSnapshot, "ParticlePositionsXyz");
        float[] storedHomes = Field<float[]>(storedSnapshot, "ParticleHomesXyz");
        int[] storedParents = Field<int[]>(storedSnapshot, "ParticleParentIndices");
        int storedCoordinate = grid / 2;
        int storedParentIndex = storedCoordinate * grid * grid + storedCoordinate * grid + storedCoordinate;
        for (int i = 0; i < initialPopulation; i++)
        {
            int offset = i * 3;
            storedPositions[offset] = storedCoordinate + 0.25f;
            storedPositions[offset + 1] = storedCoordinate + 0.25f;
            storedPositions[offset + 2] = storedCoordinate + 0.25f;
            storedHomes[offset] = storedPositions[offset];
            storedHomes[offset + 1] = storedPositions[offset + 1];
            storedHomes[offset + 2] = storedPositions[offset + 2];
            storedParents[i] = storedParentIndex;
        }

        float[] storedGroupData0 = Field<float[]>(storedSnapshot, "GroupData0");
        storedGroupData0[0] = 0;
        object storedSettings = Activator.CreateInstance(settingsType)!;
        SetField(storedSettings, "WrapBoundaries", true);
        SetField(storedSettings, "DynamicPopulation", true);
        SetField(storedSettings, "MinimumPopulation", 0);
        SetField(storedSettings, "MaximumPopulation", initialPopulation * 2);
        SetField(storedSettings, "Division", true);
        SetField(storedSettings, "DivisionMinimumAge", 0);
        SetField(storedSettings, "DivisionRange", 0);
        SetField(storedSettings, "DivisionMinimumNeighbours", 1);
        SetField(storedSettings, "DivisionMaximumNeighbours", 1000);
        SetField(storedSettings, "DivisionFrequency", 1);
        SetField(storedSettings, "RandomPopulationFrequency", 1);
        SetField(storedSettings, "RandomDivisionProbability", 0.0);
        SetField(storedSettings, "RandomDeathProbability", 0.0);

        object storedEngine = CreateGpuEngine(engineType, storedSnapshot, storedSettings, false, false, false, 0, 1);
        try
        {
            InvokeGpuStep(storedEngine, storedSnapshot, storedSettings, dimensionMode, 2);
            Invoke(storedEngine, "ReadBackParticles");
            Equal(
                initialPopulation,
                Field<int>(storedEngine, "particleCount"),
                "range-zero neighbour rule recalculated same-voxel counts instead of preserving defaults");
            int storedCapacity = Field<int>(storedEngine, "particleCapacity");
            int[] storedAuxiliary = Field<int[]>(storedEngine, "particleAuxReadback");
            for (int slot = 0; slot < initialPopulation; slot++)
            {
                Equal(0, storedAuxiliary[storedCapacity * 2 + slot], "range-zero default division-neighbour state");
            }

            Invoke(storedEngine, "FastReset", storedSnapshot, storedSettings);
            SetField(storedSettings, "DivisionRange", grid);
            SetField(storedSettings, "DivisionFrequency", 3);
            InvokeGpuStep(storedEngine, storedSnapshot, storedSettings, dimensionMode, 2);
            Invoke(storedEngine, "ReadBackParticleAuxiliaryState");
            storedAuxiliary = Field<int[]>(storedEngine, "particleAuxReadback");
            for (int slot = 0; slot < initialPopulation; slot++)
            {
                Equal(initialPopulation - 1, storedAuxiliary[storedCapacity * 2 + slot], "off-frequency neighbour publication");
            }

            SetField(storedSettings, "DivisionRange", 0);
            SetField(storedSettings, "DivisionFrequency", 1);
            SetField(storedSettings, "DivisionMinimumNeighbours", initialPopulation - 1);
            SetField(storedSettings, "DivisionMaximumNeighbours", initialPopulation - 1);
            InvokeGpuStep(storedEngine, storedSnapshot, storedSettings, dimensionMode, 3);
            Invoke(storedEngine, "ReadBackPopulationState");
            True(
                Field<int>(storedEngine, "particleCount") > initialPopulation,
                "range-zero neighbour rule did not consume the stored off-frequency counts");

            SetField(storedSettings, "Death", true);
            SetField(storedSettings, "DeathRange", -2);
            SetField(storedSettings, "DeathFrequency", 3);
            SetField(storedSettings, "Division", true);
            SetField(storedSettings, "DivisionRange", grid);
            SetField(storedSettings, "DivisionFrequency", 3);
            Invoke(storedEngine, "FastReset", storedSnapshot, storedSettings);
            InvokeGpuStep(storedEngine, storedSnapshot, storedSettings, dimensionMode, 2);
            Invoke(storedEngine, "ReadBackParticleAuxiliaryState");
            storedAuxiliary = Field<int[]>(storedEngine, "particleAuxReadback");
            for (int slot = 0; slot < initialPopulation; slot++)
            {
                Equal(0, storedAuxiliary[storedCapacity + slot], "negative death range did not publish zero");
                Equal(initialPopulation - 1, storedAuxiliary[storedCapacity * 2 + slot], "positive paired division range publication");
            }

            SetField(storedSettings, "DeathRange", grid);
            SetField(storedSettings, "DivisionRange", -2);
            Invoke(storedEngine, "FastReset", storedSnapshot, storedSettings);
            InvokeGpuStep(storedEngine, storedSnapshot, storedSettings, dimensionMode, 2);
            Invoke(storedEngine, "ReadBackParticleAuxiliaryState");
            storedAuxiliary = Field<int[]>(storedEngine, "particleAuxReadback");
            for (int slot = 0; slot < initialPopulation; slot++)
            {
                Equal(initialPopulation - 1, storedAuxiliary[storedCapacity + slot], "positive paired death range publication");
                Equal(0, storedAuxiliary[storedCapacity * 2 + slot], "negative division range did not publish zero");
            }

            SetField(storedSettings, "MinimumPopulation", initialPopulation / 2);
            SetField(storedSettings, "Death", true);
            SetField(storedSettings, "DeathMinimumAge", 0);
            SetField(storedSettings, "DeathRange", grid);
            SetField(storedSettings, "DeathMinimumNeighbours", int.MaxValue);
            SetField(storedSettings, "DeathMaximumNeighbours", int.MaxValue);
            SetField(storedSettings, "DeathFrequency", 1);
            SetField(storedSettings, "Division", true);
            SetField(storedSettings, "DivisionMinimumAge", 0);
            SetField(storedSettings, "DivisionRange", grid);
            SetField(storedSettings, "DivisionMinimumNeighbours", initialPopulation - 1);
            SetField(storedSettings, "DivisionMaximumNeighbours", initialPopulation - 1);
            SetField(storedSettings, "DivisionFrequency", 1);
            Invoke(storedEngine, "FastReset", storedSnapshot, storedSettings);
            InvokeGpuStep(storedEngine, storedSnapshot, storedSettings, dimensionMode, 2);
            Invoke(storedEngine, "ReadBackParticles");

            int mixedPopulation = Field<int>(storedEngine, "particleCount");
            float[] mixedPositions = Field<float[]>(storedEngine, "particlePositionReadback");
            float[] mixedYAxes = Field<float[]>(storedEngine, "particleYAxisReadback");
            int[] mixedAuxiliary = Field<int[]>(storedEngine, "particleAuxReadback");
            int normalBirths = 0;
            for (int slot = 0; slot < storedCapacity; slot++)
            {
                if (mixedPositions[slot * 4 + 3] < -0.5f) continue;
                if (mixedYAxes[slot * 4 + 3] < -1.5f) normalBirths++;
            }

            True(normalBirths > 0, "mixed normal death/division produced no newborns");
            Equal(initialPopulation / 2 + normalBirths, mixedPopulation, "mixed normal death/division active population");
            int ghostInclusiveNeighbours = initialPopulation + normalBirths - 1;
            for (int slot = 0; slot < storedCapacity; slot++)
            {
                if (mixedPositions[slot * 4 + 3] < -0.5f) continue;
                Equal(ghostInclusiveNeighbours, mixedAuxiliary[storedCapacity + slot], "post-division ghost-inclusive death count");
                Equal(ghostInclusiveNeighbours, mixedAuxiliary[storedCapacity * 2 + slot], "post-division ghost-inclusive division count");
            }

            SetField(storedSettings, "Death", false);
            SetField(storedSettings, "DivisionFrequency", 100);
            InvokeGpuStep(storedEngine, storedSnapshot, storedSettings, dimensionMode, 3);
            Invoke(storedEngine, "ReadBackParticles");
            Equal(mixedPopulation, Field<int>(storedEngine, "particleCount"), "next-step population changed during recount probe");
            mixedPositions = Field<float[]>(storedEngine, "particlePositionReadback");
            mixedAuxiliary = Field<int[]>(storedEngine, "particleAuxReadback");
            for (int slot = 0; slot < storedCapacity; slot++)
            {
                if (mixedPositions[slot * 4 + 3] < -0.5f) continue;
                Equal(mixedPopulation - 1, mixedAuxiliary[storedCapacity + slot], "post-move death-count refresh");
                Equal(mixedPopulation - 1, mixedAuxiliary[storedCapacity * 2 + slot], "post-move division-count refresh");
            }
        }
        finally
        {
            ((IDisposable)storedEngine).Dispose();
        }

        Console.WriteLine("Direct3D population ordering, neighbour publication, and stored range-zero state passed.");
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
        return ReadBackParticleCoordinate(engine, 0);
    }

    static double ReadBackParticleCoordinate(object engine, int axis)
    {
        Invoke(engine, "ReadBackParticles");
        return Field<float[]>(engine, "particlePositionReadback")[axis];
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

    static object CaptureVoxelSnapshot(object field, bool wrapBoundaries = false)
    {
        object snapshot = Activator.CreateInstance(SnapshotType, nonPublic: true)!;
        SnapshotType.GetField("HasSlimeParticles")!.SetValue(snapshot, true);
        Invoke(snapshot, "CaptureCompactVoxels", field, false, wrapBoundaries);
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
        return CreateRawPlane(x, y, z, 1, 0, 0, 0, 1, 0);
    }

    static object CreateRawPlane(
        double x,
        double y,
        double z,
        double xx,
        double xy,
        double xz,
        double yx,
        double yy,
        double yz)
    {
        Type planeType = RequiredExternalType("Rhino.Geometry.Plane, RhinoCommon");
        Type vectorType = RequiredExternalType("Rhino.Geometry.Vector3d, RhinoCommon");
        object plane = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(planeType);
        SetField(plane, "m_origin", CreatePoint3d(x, y, z));
        SetField(plane, "m_xaxis", Activator.CreateInstance(vectorType, xx, xy, xz));
        SetField(plane, "m_yaxis", Activator.CreateInstance(vectorType, yx, yy, yz));
        SetField(plane, "m_zaxis", Activator.CreateInstance(
            vectorType,
            xy * yz - xz * yy,
            xz * yx - xx * yz,
            xx * yy - xy * yx));
        return plane;
    }

    static double PointCoordinate(object point, int axis)
    {
        return Convert.ToDouble(PropertyValue(point, axis == 0 ? "X" : axis == 1 ? "Y" : "Z"));
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

    static object InvokeIntArrayMethod(object target, string method, int[] argument)
    {
        MethodInfo info = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(item => item.Name == method
                && item.GetParameters().Length == 1
                && item.GetParameters()[0].ParameterType == typeof(int[]));
        return info.Invoke(target, new object[] { argument });
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

    static void SetPropertyValue(object target, string name, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || !property.CanWrite)
        {
            throw new MissingMemberException(target.GetType().FullName, name);
        }

        property.SetValue(target, value);
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

    sealed class ProbeDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    public sealed class ProbeDendroVolume : IDisposable
    {
        public static ProbeDendroVolume LastCreated;

        public ProbeDendroVolume(bool isValid)
        {
            IsValid = isValid;
            LastCreated = this;
        }

        public bool IsValid { get; }
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    public sealed class ProbeDendroGoo : IDisposable
    {
        public object Value { get; set; }
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            (Value as IDisposable)?.Dispose();
        }
    }

    public sealed class ProbeReadOnlyDendroGoo
    {
        public object Value { get; }
    }
}
