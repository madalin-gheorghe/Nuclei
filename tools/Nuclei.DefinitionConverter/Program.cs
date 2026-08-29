using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GH_IO.Serialization;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (Array.IndexOf(args, "--remove-trail-frequency") >= 0)
                return RemoveTrailFrequency(args);

            string? sourceOption = Option(args, "--source");
            string? targetOption = Option(args, "--target");
            string? repositoryRoot = sourceOption == null || targetOption == null
                ? FindRepositoryRoot()
                : null;
            string sourceDirectory = FullPath(sourceOption
                ?? Path.Combine(repositoryRoot!, "Nuclei Definitions", "v3"));
            string targetDirectory = FullPath(targetOption
                ?? Path.Combine(repositoryRoot!, "Nuclei Definitions", "v4_updated"));
            string mapPath = FullPath(Option(args, "--map") ?? Path.Combine(AppContext.BaseDirectory, "v3.3-to-v4.json"));
            string? targetAssemblyPath = Option(args, "--v4-gha") is string suppliedTargetAssembly
                ? FullPath(suppliedTargetAssembly)
                : null;

            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException("Source directory was not found: " + sourceDirectory);
            if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
                throw new IOException("Target must not already exist (conversion fails closed): " + targetDirectory);

            ConversionMap map = JsonSerializer.Deserialize<ConversionMap>(File.ReadAllText(mapPath), JsonOptions())
                ?? throw new InvalidDataException("The GUID map is empty: " + mapPath);
            map.Validate();
            System.Reflection.AssemblyName? targetAssembly = targetAssemblyPath == null
                ? null
                : ValidateTargetAssembly(map, targetAssemblyPath);

            string[] sources = Directory.GetFiles(sourceDirectory, "*.gh", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sources.Length == 0) throw new InvalidDataException("No .gh files were found in " + sourceDirectory);

            string parent = Path.GetDirectoryName(targetDirectory)
                ?? throw new InvalidOperationException("Target has no parent directory.");
            Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, "." + Path.GetFileName(targetDirectory) + ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);

            try
            {
                List<FileManifest> files = new();
                foreach (string source in sources)
                {
                    string target = Path.Combine(staging, Path.GetFileName(source));
                    FileManifest manifest = ConvertOne(source, target, map);
                    files.Add(manifest);
                    Console.WriteLine($"converted {Path.GetFileName(source)}: {manifest.NucleiObjectCount} Nuclei objects, {manifest.ObjectCount} total objects");
                }

                ConversionManifest conversionManifest = new()
                {
                    FormatVersion = 1,
                    CreatedUtc = DateTime.UtcNow,
                    SourceDirectory = sourceDirectory,
                    TargetDirectory = targetDirectory,
                    MapFile = mapPath,
                    SourceLibraryId = map.SourceLibrary.Id,
                    TargetLibraryId = map.TargetLibrary.Id,
                    TargetAssemblyPath = targetAssemblyPath ?? string.Empty,
                    TargetAssemblyFullName = targetAssembly?.FullName ?? map.TargetLibrary.AssemblyFullName,
                    TargetAssemblyVersion = targetAssembly?.Version?.ToString() ?? map.TargetLibrary.AssemblyVersion,
                    TargetAssemblySha256 = targetAssemblyPath == null ? string.Empty : FileHash(targetAssemblyPath),
                    FileCount = files.Count,
                    SourceWireCount = files.Sum(file => file.SourceWireCount),
                    TargetWireCount = files.Sum(file => file.TargetWireCount),
                    IntentionalDroppedWireCount = files.Sum(file => file.IntentionalDroppedWireCount),
                    ArchiveValidationPassed = true,
                    Files = files
                };
                File.WriteAllText(
                    Path.Combine(staging, "_conversion_manifest.json"),
                    JsonSerializer.Serialize(conversionManifest, JsonOptions()),
                    new UTF8Encoding(false));

                Directory.Move(staging, targetDirectory);
                Console.WriteLine($"created {targetDirectory} with {files.Count} converted definitions");
                return 0;
            }
            catch
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                throw;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("conversion failed: " + error.Message);
            return 1;
        }
    }

    private static int RemoveTrailFrequency(string[] args)
    {
        string sourceDirectory = FullPath(Option(args, "--source")
            ?? throw new ArgumentException("--source is required for trail schema migration."));
        string targetDirectory = FullPath(Option(args, "--target")
            ?? throw new ArgumentException("--target is required for trail schema migration."));
        Guid componentGuid = Guid.Parse(Option(args, "--component-guid")
            ?? throw new ArgumentException("--component-guid is required for trail schema migration."));

        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException("Source directory was not found: " + sourceDirectory);
        if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
            throw new IOException("Target must not already exist (migration fails closed): " + targetDirectory);

        Directory.CreateDirectory(targetDirectory);
        try
        {
            List<TrailMigrationFile> results = new();
            foreach (string sourcePath in Directory.GetFiles(sourceDirectory, "*.gh", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string targetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
                TrailMigrationFile result = RemoveTrailFrequencyFromFile(sourcePath, targetPath, componentGuid);
                results.Add(result);
                Console.WriteLine($"migrated {result.File}: {result.ComponentCount} Trail Settings component(s), {result.RemovedWireCount} retired wire(s)");
            }

            TrailMigrationReport report = new()
            {
                CreatedUtc = DateTime.UtcNow,
                SourceDirectory = sourceDirectory,
                TargetDirectory = targetDirectory,
                ComponentGuid = componentGuid,
                FileCount = results.Count,
                AffectedFileCount = results.Count(item => item.ComponentCount > 0),
                ComponentCount = results.Sum(item => item.ComponentCount),
                RemovedWireCount = results.Sum(item => item.RemovedWireCount),
                ValidationPassed = true,
                Files = results
            };
            File.WriteAllText(Path.Combine(targetDirectory, "_trail_settings_migration.json"),
                JsonSerializer.Serialize(report, JsonOptions()), new UTF8Encoding(false));
            return 0;
        }
        catch
        {
            Directory.Delete(targetDirectory, true);
            throw;
        }
    }

    private static TrailMigrationFile RemoveTrailFrequencyFromFile(string sourcePath, string targetPath, Guid componentGuid)
    {
        GH_Archive sourceArchive = new();
        if (!sourceArchive.ReadFromFile(sourcePath))
            throw new InvalidDataException("GH_IO could not read " + sourcePath);

        XDocument source = XDocument.Parse(sourceArchive.Serialize_Xml(), LoadOptions.PreserveWhitespace);
        XDocument target = XDocument.Parse(source.ToString(SaveOptions.DisableFormatting), LoadOptions.PreserveWhitespace);
        List<XElement> sourceObjects = ObjectChunks(DefinitionObjects(source)).ToList();
        List<XElement> targetObjects = ObjectChunks(DefinitionObjects(target)).ToList();
        List<string> removedConnections = new();
        int componentCount = 0;

        for (int i = 0; i < targetObjects.Count; i++)
        {
            if (GuidItem(targetObjects[i], "GUID") != componentGuid) continue;
            componentCount++;
            XElement container = ChildChunk(targetObjects[i], "Container");
            XElement chunks = container.Element("chunks")
                ?? throw new InvalidDataException("Trail Settings has no parameter chunks.");
            Dictionary<int, XElement> inputs = ChildChunks(container, "param_input")
                .ToDictionary(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture));
            if (inputs.Count != 2 || !inputs.ContainsKey(0) || !inputs.ContainsKey(1)
                || StringItem(inputs[0], "Name") != "Trail Size"
                || StringItem(inputs[1], "Name") != "Trail Frequency")
                throw new InvalidDataException(Path.GetFileName(sourcePath) + " contains an unexpected Trail Settings input schema.");

            Guid retiredInput = GuidItem(inputs[1], "InstanceGuid");
            removedConnections.AddRange(WireSourceValues(inputs[1])
                .Select(sourceGuid => retiredInput.ToString("D").ToLowerInvariant() + "|" + Guid.Parse(sourceGuid).ToString("D").ToLowerInvariant()));
            inputs[1].Remove();
            chunks.SetAttributeValue("count", chunks.Elements("chunk").Count().ToString(CultureInfo.InvariantCulture));
            ApplySingleInputTrailLayout(container);
        }

        List<string> sourceConnections = WireConnectionValues(source).OrderBy(value => value, StringComparer.Ordinal).ToList();
        List<string> expectedConnections = sourceConnections.Except(removedConnections, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();
        List<string> targetConnections = WireConnectionValues(target).OrderBy(value => value, StringComparer.Ordinal).ToList();
        if (!expectedConnections.SequenceEqual(targetConnections, StringComparer.Ordinal))
            throw new InvalidDataException(Path.GetFileName(sourcePath) + " changed a wire outside the retired Trail Frequency input.");

        for (int i = 0; i < sourceObjects.Count; i++)
        {
            if (ContainerInstanceGuid(sourceObjects[i]) != ContainerInstanceGuid(targetObjects[i]))
                throw new InvalidDataException(Path.GetFileName(sourcePath) + " changed an object InstanceGuid.");
            if (GuidItem(sourceObjects[i], "GUID") == componentGuid) continue;
            if (!string.Equals(sourceObjects[i].ToString(SaveOptions.DisableFormatting), targetObjects[i].ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal))
                throw new InvalidDataException(Path.GetFileName(sourcePath) + " changed an unrelated object.");
        }

        GH_Archive targetArchive = new();
        if (!targetArchive.Deserialize_Xml(target.ToString(SaveOptions.DisableFormatting))
            || !targetArchive.WriteToFile(targetPath, true, false))
            throw new InvalidDataException("GH_IO could not write migrated output " + targetPath);

        GH_Archive reloadedArchive = new();
        if (!reloadedArchive.ReadFromFile(targetPath))
            throw new InvalidDataException("GH_IO could not reload migrated output " + targetPath);
        XDocument reloaded = XDocument.Parse(reloadedArchive.Serialize_Xml(), LoadOptions.PreserveWhitespace);
        List<XElement> reloadedObjects = ObjectChunks(DefinitionObjects(reloaded)).ToList();
        if (reloadedObjects.Count != sourceObjects.Count)
            throw new InvalidDataException(Path.GetFileName(sourcePath) + " changed object count after reload.");
        if (!expectedConnections.SequenceEqual(WireConnectionValues(reloaded).OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException(Path.GetFileName(sourcePath) + " changed wires after reload.");

        foreach (XElement trail in reloadedObjects.Where(item => GuidItem(item, "GUID") == componentGuid))
        {
            XElement[] inputs = ChildChunks(ChildChunk(trail, "Container"), "param_input").ToArray();
            if (inputs.Length != 1 || Attr(inputs[0], "index") != "0" || StringItem(inputs[0], "Name") != "Trail Size")
                throw new InvalidDataException(Path.GetFileName(sourcePath) + " did not retain the one-input Trail Settings schema.");
        }

        return new TrailMigrationFile
        {
            File = Path.GetFileName(sourcePath),
            SourceSha256 = FileHash(sourcePath),
            TargetSha256 = FileHash(targetPath),
            ObjectCount = sourceObjects.Count,
            ComponentCount = componentCount,
            SourceWireCount = sourceConnections.Count,
            TargetWireCount = expectedConnections.Count,
            RemovedWireCount = removedConnections.Count,
            RemovedConnections = removedConnections.Order(StringComparer.Ordinal).ToList(),
            ValidationPassed = true
        };
    }

    private static void ApplySingleInputTrailLayout(XElement container)
    {
        XElement componentAttributes = ChildChunk(container, "Attributes");
        XElement componentPivot = ItemElement(componentAttributes, "Pivot");
        double pivotY = DoubleElement(componentPivot, "Y");
        XElement componentBounds = ItemElement(componentAttributes, "Bounds");
        SetElement(componentBounds, "Y", FormatDouble(pivotY - 12));
        SetElement(componentBounds, "H", "24");

        foreach (XElement parameter in ChildChunks(container, "param_input").Concat(ChildChunks(container, "param_output")))
        {
            XElement attributes = ChildChunk(parameter, "Attributes");
            XElement bounds = ItemElement(attributes, "Bounds");
            XElement pivot = ItemElement(attributes, "Pivot");
            SetElement(bounds, "Y", FormatDouble(pivotY - 10));
            SetElement(bounds, "H", "20");
            SetElement(pivot, "Y", FormatDouble(pivotY));
        }
    }

    private static FileManifest ConvertOne(string sourcePath, string targetPath, ConversionMap map)
    {
        GH_Archive sourceArchive = new();
        if (!sourceArchive.ReadFromFile(sourcePath))
            throw new InvalidDataException("GH_IO could not read " + sourcePath);

        XDocument source = XDocument.Parse(sourceArchive.Serialize_Xml(), LoadOptions.PreserveWhitespace);
        XDocument target = XDocument.Parse(source.ToString(SaveOptions.DisableFormatting), LoadOptions.PreserveWhitespace);
        XElement sourceObjects = DefinitionObjects(source);
        XElement targetObjects = DefinitionObjects(target);
        List<XElement> sourceObjectChunks = ObjectChunks(sourceObjects).ToList();
        List<XElement> targetObjectChunks = ObjectChunks(targetObjects).ToList();
        if (sourceObjectChunks.Count != targetObjectChunks.Count) throw new InvalidDataException("Internal object clone failed.");

        int declaredCount = IntItem(sourceObjects, "ObjectCount");
        if (declaredCount != sourceObjectChunks.Count)
            throw new InvalidDataException($"{Path.GetFileName(sourcePath)} declares {declaredCount} objects but stores {sourceObjectChunks.Count}.");

        HashSet<Guid> expectedSourceGuids = map.Components.Select(item => item.Source).ToHashSet();
        Dictionary<Guid, ComponentMap> components = map.Components.ToDictionary(item => item.Source);
        List<ConvertedObject> converted = new();
        List<string> adapters = new();

        ReplaceLibrary(target, map);

        for (int i = 0; i < sourceObjectChunks.Count; i++)
        {
            XElement sourceObject = sourceObjectChunks[i];
            XElement targetObject = targetObjectChunks[i];
            Guid? library = OptionalGuidItem(sourceObject, "Lib");
            if (library != map.SourceLibrary.Id) continue;

            Guid sourceGuid = GuidItem(sourceObject, "GUID");
            if (!components.TryGetValue(sourceGuid, out ComponentMap? component))
                throw new InvalidDataException($"{Path.GetFileName(sourcePath)} contains unmapped Nuclei3 object {sourceGuid} ({StringItem(sourceObject, "Name")}).");

            SetItem(targetObject, "GUID", component.Target.ToString("D"));
            SetItem(targetObject, "Lib", map.TargetLibrary.Id.ToString("D"));
            string instance = ContainerInstanceGuid(targetObject).ToString("D");
            converted.Add(new ConvertedObject
            {
                Index = i,
                InstanceGuid = instance,
                SourceGuid = sourceGuid,
                TargetGuid = component.Target,
                Name = StringItem(sourceObject, "Name"),
                Adapter = component.Adapter,
                ProbabilisticSteering = string.Equals(component.Adapter, "slime-group-schema", StringComparison.Ordinal)
                    ? bool.Parse(ItemElement(ChildChunk(sourceObject, "Container"), "ProbabilisticSteering").Value)
                    : null
            });

            if (string.Equals(component.Adapter, "dendro-schema", StringComparison.Ordinal))
            {
                AdaptDendro(targetObject);
                adapters.Add("Dendro: inputs Voxels 0->0, Iso Value 2->1, Convert 4->Update 4; removed Type and Dendro Settings; added Method=0, Maximum Elements=5000000, Smoothing Iterations=1.");
            }
            else if (string.Equals(component.Adapter, "slime-group-schema", StringComparison.Ordinal))
            {
                AdaptSlimeGroup(targetObject, out string note);
                adapters.Add(note);
            }
            else if (string.Equals(component.Adapter, "slime-settings-schema", StringComparison.Ordinal))
            {
                AdaptSlimeSettings(targetObject, out string note);
                adapters.Add(note);
            }
            else if (string.Equals(component.Adapter, "solver-gpu-extra-status-output", StringComparison.Ordinal))
            {
                AdaptSolver(targetObject);
                adapters.Add("Solver: V3 outputs 0/1 retain their InstanceGuids, data, and wires; added serialized V4 GPU Status output 2 with a deterministic InstanceGuid and applied the native three-output V4 layout at the preserved component pivot.");
            }
        }

        if (converted.Count == 0)
            throw new InvalidDataException(Path.GetFileName(sourcePath) + " contains no Nuclei3 objects.");

        HashSet<Guid> encountered = converted.Select(item => item.SourceGuid).ToHashSet();
        if (!encountered.IsSubsetOf(expectedSourceGuids))
            throw new InvalidDataException(Path.GetFileName(sourcePath) + " contains a Nuclei3 object outside the approved mapping.");

        VerifyContainersPreserved(sourceObjectChunks, targetObjectChunks, map);
        VerifyObjectInstanceGuidsPreserved(sourceObjectChunks, targetObjectChunks);
        VerifyWireConnectionsPreserved(source, target, sourceObjectChunks, map);

        GH_Archive targetArchive = new();
        string targetXml = target.ToString(SaveOptions.DisableFormatting);
        if (!targetArchive.Deserialize_Xml(targetXml))
            throw new InvalidDataException("GH_IO rejected the converted XML for " + Path.GetFileName(sourcePath));
        if (!targetArchive.WriteToFile(targetPath, true, false))
            throw new IOException("GH_IO could not write " + targetPath);

        GH_Archive reloadedArchive = new();
        if (!reloadedArchive.ReadFromFile(targetPath))
            throw new InvalidDataException("GH_IO could not reload converted output " + targetPath);
        XDocument reloaded = XDocument.Parse(reloadedArchive.Serialize_Xml(), LoadOptions.PreserveWhitespace);
        ValidateConvertedArchive(reloaded, map, converted, sourceObjectChunks.Count);

        List<string> sourceWires = WireSourceValues(source).OrderBy(value => value, StringComparer.Ordinal).ToList();
        List<string> targetWires = WireSourceValues(reloaded).OrderBy(value => value, StringComparer.Ordinal).ToList();
        List<string> sourceConnections = WireConnectionValues(source).OrderBy(value => value, StringComparer.Ordinal).ToList();
        List<string> expectedTargetConnections = ExpectedTargetWireConnections(source, sourceObjectChunks, map);
        List<string> targetConnections = WireConnectionValues(reloaded).OrderBy(value => value, StringComparer.Ordinal).ToList();
        if (!expectedTargetConnections.SequenceEqual(targetConnections, StringComparer.Ordinal))
            throw new InvalidDataException(Path.GetFileName(sourcePath) + " changed a wire endpoint after GH_IO reload.");

        string sourcePersistentData = PersistentDataHashExcludingSchemaAdapters(source, map);
        string targetPersistentData = PersistentDataHashExcludingSchemaAdapters(reloaded, map);
        if (!string.Equals(sourcePersistentData, targetPersistentData, StringComparison.Ordinal))
            throw new InvalidDataException(Path.GetFileName(sourcePath) + " changed persistent data outside an approved schema adapter.");
        List<WireMigration> wireMigrations = DendroWireMigrations(source, sourceObjectChunks, map);
        if (sourceConnections.Count - expectedTargetConnections.Count != wireMigrations.Count)
            throw new InvalidDataException(Path.GetFileName(sourcePath) + " has an unrecorded intentional wire change.");
        return new FileManifest
        {
            File = Path.GetFileName(sourcePath),
            SourceSha256 = FileHash(sourcePath),
            TargetSha256 = FileHash(targetPath),
            SourceBytes = new FileInfo(sourcePath).Length,
            TargetBytes = new FileInfo(targetPath).Length,
            ObjectCount = sourceObjectChunks.Count,
            NucleiObjectCount = converted.Count,
            SourceWireCount = sourceWires.Count,
            TargetWireCount = targetWires.Count,
            SourceWireHash = HashStrings(sourceWires),
            TargetWireHash = HashStrings(targetWires),
            SourceWireConnectionHash = HashStrings(sourceConnections),
            ExpectedTargetWireConnectionHash = HashStrings(expectedTargetConnections),
            TargetWireConnectionHash = HashStrings(targetConnections),
            GraphConnectionsPreserved = true,
            IntentionalDroppedWireCount = wireMigrations.Count,
            WireMigrations = wireMigrations,
            ObjectInstanceGuidHash = HashStrings(sourceObjectChunks.Select(chunk => ContainerInstanceGuid(chunk).ToString("D")).Order()),
            SourcePersistentDataHashExcludingSchemaAdapters = sourcePersistentData,
            TargetPersistentDataHashExcludingSchemaAdapters = targetPersistentData,
            PersistentDataPreservedOutsideSchemaAdapters = true,
            ConvertedObjects = converted,
            Adapters = adapters.Distinct(StringComparer.Ordinal).ToList(),
            ArchiveReloadPassed = true,
            NoV3Residue = true
        };
    }

    private static void ReplaceLibrary(XDocument document, ConversionMap map)
    {
        List<XElement> libraries = document.Descendants("chunk")
            .Where(chunk => Attr(chunk, "name") == "Library" && OptionalGuidItem(chunk, "Id") == map.SourceLibrary.Id)
            .ToList();
        if (libraries.Count != 1)
            throw new InvalidDataException($"Expected exactly one Nuclei3 library record, found {libraries.Count}.");

        XElement library = libraries[0];
        SetOrAddItem(library, "AssemblyFullName", "gh_string", "10", map.TargetLibrary.AssemblyFullName);
        SetOrAddItem(library, "AssemblyVersion", "gh_string", "10", map.TargetLibrary.AssemblyVersion);
        SetOrAddItem(library, "Author", "gh_string", "10", map.TargetLibrary.Author);
        SetItem(library, "Id", map.TargetLibrary.Id.ToString("D"));
        SetItem(library, "Name", map.TargetLibrary.Name);
        SetOrAddItem(library, "Version", "gh_string", "10", string.Empty);
        UpdateChildCount(library, "items", "item");
    }

    private static void AdaptDendro(XElement objectChunk)
    {
        XElement container = ChildChunk(objectChunk, "Container");
        XElement chunks = container.Element("chunks") ?? throw new InvalidDataException("Dendro component has no parameter chunks.");
        XElement attributes = chunks.Elements("chunk").Single(chunk => Attr(chunk, "name") == "Attributes");
        Dictionary<int, XElement> inputs = chunks.Elements("chunk")
            .Where(chunk => Attr(chunk, "name") == "param_input")
            .ToDictionary(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture), chunk => chunk);
        XElement output = chunks.Elements("chunk").Single(chunk => Attr(chunk, "name") == "param_output" && Attr(chunk, "index") == "0");
        if (!Enumerable.Range(0, 5).All(inputs.ContainsKey))
            throw new InvalidDataException("Dendro V3 schema is not the expected five-input schema.");

        Guid componentInstance = ContainerInstanceGuid(objectChunk);
        XElement voxels = new(inputs[0]);
        ConfigureParam(voxels, 0, "Voxels", "voxels", "Voxel output from Nuclei4 Solver GPU", false);

        XElement iso = new(inputs[2]);
        ConfigureParam(iso, 1, "Iso Value", "iso", "Density level used to select the volume", false);

        XElement method = new(inputs[1]);
        ConfigureParam(method, 2, "Method", "method", "Continuous uses GPU marching tetrahedra; Discrete uses selected voxel centres as Dendro point kernels", false);
        RemoveSources(method);
        SetIntegerPersistentData(method, 0);

        XElement maximum = new(inputs[3]);
        ConfigureParam(maximum, 3, "Maximum Elements", "max", "Safety limit for triangles in Continuous mode or selected voxel centres in Discrete mode", false);
        RemoveSources(maximum);
        SetItem(maximum, "InstanceGuid", DeterministicGuid(componentInstance, "dendro-input-maximum").ToString("D"));
        SetIntegerPersistentData(maximum, 5_000_000);

        XElement update = new(inputs[4]);
        ConfigureParam(update, 4, "Update", "update", "Rebuild the output whenever the component receives updated inputs", false);

        XElement smoothing = new(inputs[1]);
        ConfigureParam(smoothing, 5, "Smoothing Iterations", "smooth", "GPU volume-smoothing passes used by Continuous mode; 0 disables smoothing", false);
        RemoveSources(smoothing);
        SetItem(smoothing, "InstanceGuid", DeterministicGuid(componentInstance, "dendro-input-smoothing").ToString("D"));
        SetIntegerPersistentData(smoothing, 1);

        XElement adaptedOutput = new(output);
        ConfigureParam(adaptedOutput, 0, "Dendro Volume / Mesh", "volume", "Native Dendro volume, or a Rhino mesh when Dendro is unavailable", false);

        XElement[] adapted = { voxels, iso, method, maximum, update, smoothing };
        for (int i = 0; i < adapted.Length; i++) SetParamBounds(adapted[i], i, isOutput: false);
        SetParamBounds(adaptedOutput, 0, isOutput: true);
        SetComponentBounds(container);

        chunks.ReplaceNodes(new[] { attributes }.Concat(adapted).Append(adaptedOutput));
        chunks.SetAttributeValue("count", chunks.Elements("chunk").Count().ToString(CultureInfo.InvariantCulture));
    }

    private static void AdaptSolver(XElement objectChunk)
    {
        XElement container = ChildChunk(objectChunk, "Container");
        XElement chunks = container.Element("chunks") ?? throw new InvalidDataException("Solver component has no parameter chunks.");
        Dictionary<int, XElement> outputs = chunks.Elements("chunk")
            .Where(chunk => Attr(chunk, "name") == "param_output")
            .ToDictionary(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture));
        if (outputs.Count != 2 || !outputs.ContainsKey(0) || !outputs.ContainsKey(1))
            throw new InvalidDataException("Solver V3 schema is not the expected two-output schema.");

        Guid componentInstance = ContainerInstanceGuid(objectChunk);
        XElement status = new(outputs[1]);
        ConfigureParam(status, 2, "GPU Status", "status", "GPU compute status", false);
        SetItem(status, "InstanceGuid", DeterministicGuid(componentInstance, "solver-output-gpu-status").ToString("D"));
        RemoveSources(status);
        chunks.Add(status);
        chunks.SetAttributeValue("count", chunks.Elements("chunk").Count().ToString(CultureInfo.InvariantCulture));
        ApplySolverNativeLayout(container);
    }

    private static void ApplySolverNativeLayout(XElement container)
    {
        XElement componentAttributes = ChildChunk(container, "Attributes");
        XElement componentPivot = ItemElement(componentAttributes, "Pivot");
        double pivotX = DoubleElement(componentPivot, "X");
        double pivotY = DoubleElement(componentPivot, "Y");

        SetBoundsAndPivot(componentAttributes, pivotX - 80, pivotY - 70, 147, 139, pivotX, pivotY);

        Dictionary<int, XElement> inputs = ChildChunks(container, "param_input")
            .ToDictionary(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture));
        Dictionary<int, XElement> outputs = ChildChunks(container, "param_output")
            .ToDictionary(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture));
        if (inputs.Count != 4 || !Enumerable.Range(0, 4).All(inputs.ContainsKey)
            || outputs.Count != 3 || !Enumerable.Range(0, 3).All(outputs.ContainsKey))
            throw new InvalidDataException("Solver cannot receive the native V4 layout because its parameter schema is incomplete.");

        double[] inputY = { -68, -35, -1, 33 };
        double[] inputHeight = { 33, 34, 34, 34 };
        double[] inputPivotY = { -51.125, -17.375, 16.375, 50.125 };
        for (int i = 0; i < 4; i++)
        {
            SetBoundsAndPivot(ChildChunk(inputs[i], "Attributes"),
                pivotX - 78, pivotY + inputY[i], 63, inputHeight[i],
                pivotX - 37, pivotY + inputPivotY[i]);
        }

        double[] outputY = { -68, -23, 22 };
        double[] outputPivotY = { -45.5, -0.5, 44.5 };
        for (int i = 0; i < 3; i++)
        {
            SetBoundsAndPivot(ChildChunk(outputs[i], "Attributes"),
                pivotX + 15, pivotY + outputY[i], 50, 45,
                pivotX + 40, pivotY + outputPivotY[i]);
        }
    }

    private static void SetBoundsAndPivot(
        XElement attributes,
        double x,
        double y,
        double width,
        double height,
        double pivotX,
        double pivotY)
    {
        XElement bounds = ItemElement(attributes, "Bounds");
        XElement pivot = ItemElement(attributes, "Pivot");
        SetElement(bounds, "X", FormatDouble(x));
        SetElement(bounds, "Y", FormatDouble(y));
        SetElement(bounds, "W", FormatDouble(width));
        SetElement(bounds, "H", FormatDouble(height));
        SetElement(pivot, "X", FormatDouble(pivotX));
        SetElement(pivot, "Y", FormatDouble(pivotY));
    }

    private static void AdaptSlimeGroup(XElement objectChunk, out string note)
    {
        XElement container = ChildChunk(objectChunk, "Container");
        Dictionary<int, XElement> inputs = ChildChunks(container, "param_input")
            .ToDictionary(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture));
        if (inputs.Count != 10 || !Enumerable.Range(0, 10).All(inputs.ContainsKey))
            throw new InvalidDataException("Slime Particle Group is not the expected ten-input schema.");

        string steeringName = StringItem(inputs[8], "Name");
        if (string.Equals(steeringName, "Wander", StringComparison.Ordinal))
        {
            ConfigureParam(inputs[8], 8, "Exploration", "exploration", "Classic wander frequency, or probabilistic steering exploration from 0 (strongest signal) to 1 (uniform positive sensors)", false);
            note = "Slime Particle Group: migrated legacy input 8 Wander metadata to Exploration while preserving its InstanceGuid, source wire, and persistent value.";
        }
        else if (string.Equals(steeringName, "Exploration", StringComparison.Ordinal))
        {
            note = "Slime Particle Group: verified current input 8 Exploration schema; source wire, persistent data, and ProbabilisticSteering state were preserved.";
        }
        else
        {
            throw new InvalidDataException("Slime Particle Group input 8 is neither Wander nor Exploration: " + steeringName);
        }

        XElement? mode = OptionalItemElement(container, "ProbabilisticSteering");
        if (mode == null || !bool.TryParse(mode.Value, out _))
            throw new InvalidDataException("Slime Particle Group has no valid ProbabilisticSteering serialization state.");
    }

    private static void AdaptSlimeSettings(XElement objectChunk, out string note)
    {
        XElement container = ChildChunk(objectChunk, "Container");
        XElement chunks = container.Element("chunks") ?? throw new InvalidDataException("Voxel Settings Slime has no parameter chunks.");
        XElement attributes = chunks.Elements("chunk").Single(chunk => Attr(chunk, "name") == "Attributes");
        List<XElement> outputs = chunks.Elements("chunk").Where(chunk => Attr(chunk, "name") == "param_output").ToList();
        Dictionary<int, XElement> inputs = chunks.Elements("chunk")
            .Where(chunk => Attr(chunk, "name") == "param_input")
            .ToDictionary(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture));
        if (inputs.Count != 4 || !Enumerable.Range(0, 4).All(inputs.ContainsKey))
            throw new InvalidDataException("Voxel Settings Slime is not the expected four-input schema.");

        string[] names = Enumerable.Range(0, 4).Select(index => StringItem(inputs[index], "Name")).ToArray();
        string[] current = { "Diffuse Rate", "Decay Rate", "Falloff", "Diffuse Range" };
        if (names.SequenceEqual(current, StringComparer.Ordinal))
        {
            note = "Voxel Settings Slime: verified current [Diffuse Rate, Decay Rate, Falloff, Diffuse Range] schema; all source wires and persistent values were preserved by index.";
            return;
        }

        string[] legacy = { "Diffuse Rate", "Diffuse Range", "Decay Rate", "Gradual" };
        if (!names.SequenceEqual(legacy, StringComparer.Ordinal))
            throw new InvalidDataException("Voxel Settings Slime has an unknown input schema: [" + string.Join(", ", names) + "].");

        XElement gradual = inputs[3];
        int sourceCount = IntItem(gradual, "SourceCount");
        if (sourceCount != 0)
            throw new InvalidDataException("Legacy Voxel Settings Slime has a wired Gradual input. Conversion fails closed because Falloff = 1 - Gradual requires an explicit inversion node; no wire is silently reinterpreted.");

        XElement diffuseRate = new(inputs[0]);
        XElement decayRate = new(inputs[2]);
        XElement falloff = new(inputs[3]);
        XElement diffuseRange = new(inputs[1]);
        ConfigureParam(diffuseRate, 0, "Diffuse Rate", "diffuse", "Rate of diffusion per iteration", false);
        ConfigureParam(decayRate, 1, "Decay Rate", "decay", "Rate of decay per iteration", false);
        ConfigureParam(falloff, 2, "Falloff", "falloff", "Falloff across the diffusion range", false);
        ConfigureParam(diffuseRange, 3, "Diffuse Range", "range", "Number of neighbouring voxels included in diffusion", false);
        InvertSingleDoublePersistentValue(falloff);

        chunks.ReplaceNodes(new[] { attributes, diffuseRate, decayRate, falloff, diffuseRange }.Concat(outputs));
        chunks.SetAttributeValue("count", chunks.Elements("chunk").Count().ToString(CultureInfo.InvariantCulture));
        note = "Voxel Settings Slime: migrated legacy input order to [Diffuse Rate, Decay Rate, Falloff, Diffuse Range] and inverted the unwired Gradual persistent value as Falloff = 1 - Gradual.";
    }

    private static void InvertSingleDoublePersistentValue(XElement parameter)
    {
        List<XElement> values = parameter.Descendants("chunk")
            .Where(chunk => Attr(chunk, "name") == "Item")
            .SelectMany(chunk => chunk.Element("items")?.Elements("item") ?? Enumerable.Empty<XElement>())
            .Where(item => Attr(item, "name") == "number")
            .ToList();
        if (values.Count != 1 || !double.TryParse(values[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gradual))
            throw new InvalidDataException("Legacy Gradual input does not contain one numeric persistent value.");
        values[0].Value = (1.0 - gradual).ToString("R", CultureInfo.InvariantCulture);
    }

    private static void ConfigureParam(XElement chunk, int index, string name, string nickName, string description, bool optional)
    {
        chunk.SetAttributeValue("index", index.ToString(CultureInfo.InvariantCulture));
        SetOrAddItem(chunk, "Description", "gh_string", "10", description);
        SetOrAddItem(chunk, "Name", "gh_string", "10", name);
        SetOrAddItem(chunk, "NickName", "gh_string", "10", nickName);
        SetOrAddItem(chunk, "Optional", "gh_bool", "1", optional ? "true" : "false");
        UpdateChildCount(chunk, "items", "item");
    }

    private static void RemoveSources(XElement chunk)
    {
        XElement items = chunk.Element("items") ?? throw new InvalidDataException("Parameter has no items.");
        items.Elements("item").Where(item => Attr(item, "name") == "Source").Remove();
        SetOrAddItem(chunk, "SourceCount", "gh_int32", "3", "0");
        UpdateChildCount(chunk, "items", "item");
    }

    private static void SetIntegerPersistentData(XElement parameter, int value)
    {
        XElement chunks = parameter.Element("chunks") ?? new XElement("chunks", new XAttribute("count", "0"));
        if (chunks.Parent == null) parameter.Add(chunks);
        chunks.Elements("chunk").Where(chunk => Attr(chunk, "name") == "PersistentData").Remove();

        XElement data = new("chunk", new XAttribute("name", "PersistentData"),
            new XElement("items", new XAttribute("count", "1"), Item("Count", "gh_int32", "3", "1")),
            new XElement("chunks", new XAttribute("count", "1"),
                new XElement("chunk", new XAttribute("name", "Branch"), new XAttribute("index", "0"),
                    new XElement("items", new XAttribute("count", "2"),
                        Item("Count", "gh_int32", "3", "1"), Item("Path", "gh_string", "10", "{0}")),
                    new XElement("chunks", new XAttribute("count", "1"),
                        new XElement("chunk", new XAttribute("name", "Item"), new XAttribute("index", "0"),
                            new XElement("items", new XAttribute("count", "1"),
                                Item("number", "gh_int32", "3", value.ToString(CultureInfo.InvariantCulture))))))));
        chunks.Add(data);
        chunks.SetAttributeValue("count", chunks.Elements("chunk").Count().ToString(CultureInfo.InvariantCulture));
    }

    private static XElement Item(string name, string typeName, string typeCode, string value) =>
        new("item", new XAttribute("name", name), new XAttribute("type_name", typeName), new XAttribute("type_code", typeCode), value);

    private static void SetParamBounds(XElement parameter, int index, bool isOutput)
    {
        XElement? bounds = parameter.Descendants("item").FirstOrDefault(item => Attr(item, "name") == "Bounds");
        XElement? pivot = parameter.Descendants("item").FirstOrDefault(item => Attr(item, "name") == "Pivot");
        if (bounds == null || pivot == null) return;
        float y = 407 + (20 * index);
        SetElement(bounds, "Y", y.ToString(CultureInfo.InvariantCulture));
        if (isOutput)
        {
            SetElement(bounds, "H", "120");
            SetElement(pivot, "Y", "467");
        }
        else
        {
            SetElement(bounds, "H", "20");
            SetElement(pivot, "Y", (y + 10).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void SetComponentBounds(XElement container)
    {
        XElement? bounds = container.Descendants("item").FirstOrDefault(item => Attr(item, "name") == "Bounds");
        XElement? pivot = container.Descendants("item").FirstOrDefault(item => Attr(item, "name") == "Pivot");
        if (bounds == null || pivot == null) return;
        SetElement(bounds, "H", "124");
        SetElement(pivot, "Y", "467");
    }

    private static void VerifyContainersPreserved(IReadOnlyList<XElement> source, IReadOnlyList<XElement> target, ConversionMap map)
    {
        for (int i = 0; i < source.Count; i++)
        {
            Guid? library = OptionalGuidItem(source[i], "Lib");
            Guid guid = GuidItem(source[i], "GUID");
            ComponentMap? component = map.Components.SingleOrDefault(item => item.Source == guid);
            bool adapter = library == map.SourceLibrary.Id && component?.Adapter == "dendro-schema";
            if (library == map.SourceLibrary.Id && component?.Adapter == "solver-gpu-extra-status-output")
            {
                VerifySolverAdapterPreserved(source[i], target[i]);
                continue;
            }
            if (library == map.SourceLibrary.Id && component?.Adapter == "slime-group-schema")
            {
                XElement input = ChildChunks(ChildChunk(source[i], "Container"), "param_input")
                    .Single(chunk => Attr(chunk, "index") == "8");
                adapter = string.Equals(StringItem(input, "Name"), "Wander", StringComparison.Ordinal);
            }
            if (library == map.SourceLibrary.Id && component?.Adapter == "slime-settings-schema")
            {
                string[] names = ChildChunks(ChildChunk(source[i], "Container"), "param_input")
                    .OrderBy(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture))
                    .Select(chunk => StringItem(chunk, "Name"))
                    .ToArray();
                adapter = names.SequenceEqual(new[] { "Diffuse Rate", "Diffuse Range", "Decay Rate", "Gradual" }, StringComparer.Ordinal);
            }
            if (adapter) continue;

            string a = ChildChunk(source[i], "Container").ToString(SaveOptions.DisableFormatting);
            string b = ChildChunk(target[i], "Container").ToString(SaveOptions.DisableFormatting);
            if (!string.Equals(a, b, StringComparison.Ordinal))
                throw new InvalidDataException($"Container changed unexpectedly for object {i} ({StringItem(source[i], "Name")}).");
        }
    }

    private static void VerifySolverAdapterPreserved(XElement sourceObject, XElement targetObject)
    {
        XElement sourceContainer = ChildChunk(sourceObject, "Container");
        XElement targetContainer = ChildChunk(targetObject, "Container");
        XElement normalizedTarget = new(targetContainer);
        XElement targetChunks = normalizedTarget.Element("chunks")
            ?? throw new InvalidDataException("Converted Solver has no parameter chunks.");
        List<XElement> statuses = targetChunks.Elements("chunk")
            .Where(chunk => Attr(chunk, "name") == "param_output" && Attr(chunk, "index") == "2")
            .ToList();
        if (statuses.Count != 1)
            throw new InvalidDataException("Converted Solver does not contain exactly one GPU Status output at index 2.");

        XElement status = statuses[0];
        Guid expectedGuid = DeterministicGuid(ContainerInstanceGuid(targetObject), "solver-output-gpu-status");
        if (StringItem(status, "Name") != "GPU Status"
            || StringItem(status, "NickName") != "status"
            || GuidItem(status, "InstanceGuid") != expectedGuid
            || IntItem(status, "SourceCount") != 0)
            throw new InvalidDataException("Converted Solver GPU Status output schema is invalid.");

        VerifySolverNativeLayout(targetContainer);

        status.Remove();
        targetChunks.SetAttributeValue("count", targetChunks.Elements("chunk").Count().ToString(CultureInfo.InvariantCulture));
        XElement normalizedSource = new(sourceContainer);
        StripAttributeChunks(normalizedSource);
        StripAttributeChunks(normalizedTarget);
        string expected = normalizedSource.ToString(SaveOptions.DisableFormatting);
        string actual = normalizedTarget.ToString(SaveOptions.DisableFormatting);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidDataException("Solver adapter changed non-layout data other than adding GPU Status output 2.");
    }

    private static IEnumerable<string> SolverLayoutRecords(XElement container) => container.Descendants("chunk")
        .Where(chunk => Attr(chunk, "name") == "Attributes")
        .Select(chunk => chunk.ToString(SaveOptions.DisableFormatting));

    private static void VerifySolverNativeLayout(XElement container)
    {
        XElement canonical = new(container);
        ApplySolverNativeLayout(canonical);
        if (!SolverLayoutRecords(container).SequenceEqual(SolverLayoutRecords(canonical), StringComparer.Ordinal))
            throw new InvalidDataException("Converted Solver attributes do not match the native three-output V4 layout.");
    }

    private static void StripAttributeChunks(XElement container)
    {
        container.Descendants("chunk").Where(chunk => Attr(chunk, "name") == "Attributes").Remove();
        foreach (XElement chunks in container.Descendants("chunks"))
            chunks.SetAttributeValue("count", chunks.Elements("chunk").Count().ToString(CultureInfo.InvariantCulture));
    }

    private static void VerifyObjectInstanceGuidsPreserved(IReadOnlyList<XElement> source, IReadOnlyList<XElement> target)
    {
        for (int i = 0; i < source.Count; i++)
            if (ContainerInstanceGuid(source[i]) != ContainerInstanceGuid(target[i]))
                throw new InvalidDataException("Object InstanceGuid changed at object index " + i);
    }

    private static void VerifyWireConnectionsPreserved(
        XDocument sourceDocument,
        XDocument targetDocument,
        IReadOnlyList<XElement> sourceObjects,
        ConversionMap map)
    {
        List<string> expected = ExpectedTargetWireConnections(sourceDocument, sourceObjects, map);
        List<string> actual = WireConnectionValues(targetDocument).OrderBy(value => value, StringComparer.Ordinal).ToList();
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            throw new InvalidDataException("A wire endpoint changed unexpectedly during schema conversion.");
    }

    private static List<string> ExpectedTargetWireConnections(
        XDocument sourceDocument,
        IReadOnlyList<XElement> sourceObjects,
        ConversionMap map)
    {
        List<string> expected = WireConnectionValues(sourceDocument).ToList();
        Guid dendroSource = map.Components.Single(item => item.Adapter == "dendro-schema").Source;
        foreach (XElement sourceObject in sourceObjects.Where(item => GuidItem(item, "GUID") == dendroSource))
        {
            XElement oldType = ChildChunks(ChildChunk(sourceObject, "Container"), "param_input")
                .Single(chunk => Attr(chunk, "index") == "1");
            string droppedDestination = GuidItem(oldType, "InstanceGuid").ToString("D").ToLowerInvariant() + "|";
            expected.RemoveAll(connection => connection.StartsWith(droppedDestination, StringComparison.Ordinal));
        }
        return expected.OrderBy(value => value, StringComparer.Ordinal).ToList();
    }

    private static List<WireMigration> DendroWireMigrations(
        XDocument sourceDocument,
        IReadOnlyList<XElement> sourceObjects,
        ConversionMap map)
    {
        List<WireMigration> migrations = new();
        Guid dendroSource = map.Components.Single(item => item.Adapter == "dendro-schema").Source;
        foreach (XElement sourceObject in sourceObjects.Where(item => GuidItem(item, "GUID") == dendroSource))
        {
            XElement oldType = ChildChunks(ChildChunk(sourceObject, "Container"), "param_input")
                .Single(chunk => Attr(chunk, "index") == "1");
            string destination = GuidItem(oldType, "InstanceGuid").ToString("D");
            foreach (string source in WireSourceValues(oldType))
            {
                XElement? sourceParameter = sourceDocument.Descendants("chunk")
                    .FirstOrDefault(chunk => OptionalItemElement(chunk, "InstanceGuid")?.Value.Equals(source, StringComparison.OrdinalIgnoreCase) == true);
                XElement? sourceOwner = sourceParameter?.Ancestors("chunk")
                    .FirstOrDefault(chunk => Attr(chunk, "name") == "Object");
                migrations.Add(new WireMigration
                {
                    Change = "removed-obsolete-endpoint",
                    DestinationComponentInstanceGuid = ContainerInstanceGuid(sourceObject).ToString("D"),
                    DestinationParameterInstanceGuid = destination,
                    DestinationInput = "Type",
                    SourceParameterInstanceGuid = Guid.Parse(source).ToString("D"),
                    SourceObject = sourceOwner == null ? string.Empty : StringItem(sourceOwner, "Name"),
                    PreservedSourceObject = true,
                    Reason = "V4 has no Type input and always meshes its slime-density field; the V3 selection Slime Chemoattractants is therefore preserved implicitly."
                });
            }
        }
        return migrations;
    }

    private static void ValidateConvertedArchive(XDocument document, ConversionMap map, IReadOnlyList<ConvertedObject> expected, int expectedObjectCount)
    {
        List<XElement> objects = ObjectChunks(DefinitionObjects(document)).ToList();
        if (objects.Count != expectedObjectCount) throw new InvalidDataException("Object count changed after GH_IO reload.");

        HashSet<Guid> sourceGuids = map.Components.Select(item => item.Source).ToHashSet();
        foreach (XElement item in document.Descendants("item").Where(item => Attr(item, "type_name") == "gh_guid"))
        {
            if (Guid.TryParse(item.Value, out Guid value) && (value == map.SourceLibrary.Id || sourceGuids.Contains(value)))
                throw new InvalidDataException("V3 GUID residue remains after conversion: " + value);
        }

        foreach (XElement obj in objects)
        {
            Guid? library = OptionalGuidItem(obj, "Lib");
            if (library == map.SourceLibrary.Id) throw new InvalidDataException("V3 library residue remains on an object.");
            if (library == map.TargetLibrary.Id && !map.Components.Any(item => item.Target == GuidItem(obj, "GUID")))
                throw new InvalidDataException("Converted V4 library object has an unapproved GUID: " + GuidItem(obj, "GUID"));
        }

        foreach (ConvertedObject conversion in expected)
        {
            XElement obj = objects[conversion.Index];
            if (GuidItem(obj, "GUID") != conversion.TargetGuid || OptionalGuidItem(obj, "Lib") != map.TargetLibrary.Id)
                throw new InvalidDataException("Converted object identity did not survive GH_IO reload at index " + conversion.Index);
            if (ContainerInstanceGuid(obj).ToString("D") != conversion.InstanceGuid)
                throw new InvalidDataException("Converted object InstanceGuid did not survive GH_IO reload at index " + conversion.Index);
            if (string.Equals(conversion.Adapter, "solver-gpu-extra-status-output", StringComparison.Ordinal))
            {
                XElement container = ChildChunk(obj, "Container");
                Dictionary<int, XElement> outputs = ChildChunks(container, "param_output")
                    .ToDictionary(chunk => int.Parse(Attr(chunk, "index"), CultureInfo.InvariantCulture));
                Guid expectedStatusGuid = DeterministicGuid(ContainerInstanceGuid(obj), "solver-output-gpu-status");
                if (outputs.Count != 3 || !Enumerable.Range(0, 3).All(outputs.ContainsKey)
                    || StringItem(outputs[2], "Name") != "GPU Status"
                    || StringItem(outputs[2], "NickName") != "status"
                    || GuidItem(outputs[2], "InstanceGuid") != expectedStatusGuid
                    || IntItem(outputs[2], "SourceCount") != 0)
                    throw new InvalidDataException("Converted Solver GPU Status output did not survive GH_IO reload.");
                VerifySolverNativeLayout(container);
            }
        }
    }

    private static string PersistentDataHashExcludingSchemaAdapters(XDocument document, ConversionMap map)
    {
        HashSet<Guid> adapterGuids = map.Components
            .Where(item => !string.IsNullOrWhiteSpace(item.Adapter))
            .SelectMany(item => new[] { item.Source, item.Target })
            .ToHashSet();
        IEnumerable<string> records = ObjectChunks(DefinitionObjects(document))
            .Where(obj => !adapterGuids.Contains(GuidItem(obj, "GUID")))
            .SelectMany((obj, objectIndex) => obj.Descendants("chunk")
                .Where(chunk => Attr(chunk, "name") == "PersistentData")
                .Select(chunk => objectIndex.ToString(CultureInfo.InvariantCulture) + "|" + chunk.ToString(SaveOptions.DisableFormatting)));
        return HashStrings(records.Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> WireConnectionValues(XDocument document)
    {
        foreach (XElement source in document.Descendants("item").Where(item => Attr(item, "name") == "Source"))
        {
            XElement? destination = source.Ancestors("chunk")
                .FirstOrDefault(chunk => OptionalItemElement(chunk, "InstanceGuid") != null);
            if (destination == null)
                throw new InvalidDataException("A wire source has no destination parameter InstanceGuid.");
            yield return GuidItem(destination, "InstanceGuid").ToString("D").ToLowerInvariant()
                + "|" + Guid.Parse(source.Value).ToString("D").ToLowerInvariant();
        }
    }

    private static IEnumerable<string> WireSourceValues(XContainer container) => container.Descendants("item")
        .Where(item => Attr(item, "name") == "Source")
        .Select(item => item.Value.ToLowerInvariant());

    private static XElement DefinitionObjects(XDocument document) => document.Descendants("chunk")
        .Single(chunk => Attr(chunk, "name") == "DefinitionObjects");

    private static IEnumerable<XElement> ObjectChunks(XElement definitionObjects) =>
        definitionObjects.Element("chunks")?.Elements("chunk").Where(chunk => Attr(chunk, "name") == "Object")
        ?? Enumerable.Empty<XElement>();

    private static IEnumerable<XElement> ChildChunks(XElement parent, string name) =>
        parent.Element("chunks")?.Elements("chunk").Where(chunk => Attr(chunk, "name") == name)
        ?? Enumerable.Empty<XElement>();

    private static XElement ChildChunk(XElement parent, string name) => ChildChunks(parent, name).Single();

    private static Guid ContainerInstanceGuid(XElement objectChunk) =>
        GuidItem(ChildChunk(objectChunk, "Container"), "InstanceGuid");

    private static Guid GuidItem(XElement chunk, string name) =>
        Guid.Parse(ItemElement(chunk, name).Value);

    private static Guid? OptionalGuidItem(XElement chunk, string name)
    {
        XElement? item = OptionalItemElement(chunk, name);
        return item == null ? null : Guid.Parse(item.Value);
    }

    private static int IntItem(XElement chunk, string name) => int.Parse(ItemElement(chunk, name).Value, CultureInfo.InvariantCulture);
    private static string StringItem(XElement chunk, string name) => ItemElement(chunk, name).Value;
    private static double DoubleElement(XElement parent, string name) =>
        double.Parse(parent.Element(name)?.Value ?? throw new InvalidDataException("Missing " + name), CultureInfo.InvariantCulture);
    private static string FormatDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static XElement ItemElement(XElement chunk, string name) => OptionalItemElement(chunk, name)
        ?? throw new InvalidDataException($"Chunk {Attr(chunk, "name")} has no {name} item.");
    private static XElement? OptionalItemElement(XElement chunk, string name) =>
        chunk.Element("items")?.Elements("item").SingleOrDefault(item => Attr(item, "name") == name);

    private static void SetItem(XElement chunk, string name, string value) => ItemElement(chunk, name).Value = value;

    private static void SetOrAddItem(XElement chunk, string name, string typeName, string typeCode, string value)
    {
        XElement? item = OptionalItemElement(chunk, name);
        if (item == null)
        {
            XElement items = chunk.Element("items") ?? new XElement("items", new XAttribute("count", "0"));
            if (items.Parent == null) chunk.AddFirst(items);
            items.Add(Item(name, typeName, typeCode, value));
        }
        else item.Value = value;
    }

    private static void UpdateChildCount(XElement parent, string containerName, string childName)
    {
        XElement container = parent.Element(containerName) ?? throw new InvalidDataException("Missing " + containerName);
        container.SetAttributeValue("count", container.Elements(childName).Count().ToString(CultureInfo.InvariantCulture));
    }

    private static void SetElement(XElement parent, string name, string value)
    {
        XElement element = parent.Element(name) ?? throw new InvalidDataException("Missing " + name);
        element.Value = value;
    }

    private static string Attr(XElement element, string name) => (string?)element.Attribute(name) ?? string.Empty;

    private static Guid DeterministicGuid(Guid seed, string purpose)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString("D") + "|" + purpose));
        byte[] guid = bytes[..16];
        guid[7] = (byte)((guid[7] & 0x0F) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
        return new Guid(guid);
    }

    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string HashStrings(IEnumerable<string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values))));

    private static System.Reflection.AssemblyName ValidateTargetAssembly(ConversionMap map, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The supplied V4 GHA was not found.", path);

        System.Reflection.AssemblyName actual = System.Reflection.AssemblyName.GetAssemblyName(path);
        if (!string.Equals(actual.Name, map.TargetLibrary.Name, StringComparison.Ordinal)
            || !string.Equals(actual.FullName, map.TargetLibrary.AssemblyFullName, StringComparison.Ordinal)
            || !string.Equals(actual.Version?.ToString(), map.TargetLibrary.AssemblyVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Target library metadata does not match the supplied V4 GHA. Map declares '"
                + map.TargetLibrary.AssemblyFullName + "' / " + map.TargetLibrary.AssemblyVersion
                + "; GHA is '" + actual.FullName + "' / " + actual.Version + ".");
        }

        return actual;
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new ArgumentException("Missing value for " + name);
        return args[index + 1];
    }

    private static string FindRepositoryRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DirectoryInfo? directory = new(FullPath(start));
            while (directory != null)
            {
                string map = Path.Combine(
                    directory.FullName,
                    "tools",
                    "Nuclei.DefinitionConverter",
                    "v3.3-to-v4.json");
                string definitions = Path.Combine(directory.FullName, "Nuclei Definitions", "v3");
                if (File.Exists(map) && Directory.Exists(definitions))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "The Nuclei repository root could not be found from the converter or current directory. "
            + "Supply both --source and --target explicitly.");
    }

    private static string FullPath(string path) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}

internal sealed class ConversionMap
{
    public LibraryMap SourceLibrary { get; set; } = new();
    public LibraryMap TargetLibrary { get; set; } = new();
    public List<ComponentMap> Components { get; set; } = new();

    public void Validate()
    {
        if (SourceLibrary.Id == Guid.Empty || TargetLibrary.Id == Guid.Empty) throw new InvalidDataException("Library GUIDs must not be empty.");
        if (SourceLibrary.Id == TargetLibrary.Id) throw new InvalidDataException("Source and target library GUIDs must differ.");
        if (string.IsNullOrWhiteSpace(TargetLibrary.Name)
            || string.IsNullOrWhiteSpace(TargetLibrary.AssemblyFullName)
            || string.IsNullOrWhiteSpace(TargetLibrary.AssemblyVersion))
            throw new InvalidDataException("Target library assembly metadata must be complete.");
        System.Reflection.AssemblyName declaredAssembly;
        try
        {
            declaredAssembly = new System.Reflection.AssemblyName(TargetLibrary.AssemblyFullName);
        }
        catch (Exception error)
        {
            throw new InvalidDataException("Target library AssemblyFullName is invalid.", error);
        }
        if (!Version.TryParse(TargetLibrary.AssemblyVersion, out Version? declaredVersion)
            || !string.Equals(declaredAssembly.Name, TargetLibrary.Name, StringComparison.Ordinal)
            || declaredAssembly.Version != declaredVersion)
            throw new InvalidDataException("Target library name, AssemblyFullName, and AssemblyVersion disagree.");
        if (Components.Count == 0) throw new InvalidDataException("Component map is empty.");
        if (Components.Any(item => item.Source == Guid.Empty || item.Target == Guid.Empty)) throw new InvalidDataException("Component GUIDs must not be empty.");
        if (Components.Select(item => item.Source).Distinct().Count() != Components.Count) throw new InvalidDataException("Source component GUIDs are not unique.");
        if (Components.Select(item => item.Target).Distinct().Count() != Components.Count) throw new InvalidDataException("Target component GUIDs are not unique.");
    }
}

internal sealed class LibraryMap
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AssemblyFullName { get; set; } = string.Empty;
    public string AssemblyVersion { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
}

internal sealed class ComponentMap
{
    public Guid Source { get; set; }
    public Guid Target { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? Adapter { get; set; }
}

internal sealed class ConversionManifest
{
    public int FormatVersion { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string SourceDirectory { get; set; } = string.Empty;
    public string TargetDirectory { get; set; } = string.Empty;
    public string MapFile { get; set; } = string.Empty;
    public Guid SourceLibraryId { get; set; }
    public Guid TargetLibraryId { get; set; }
    public string TargetAssemblyPath { get; set; } = string.Empty;
    public string TargetAssemblyFullName { get; set; } = string.Empty;
    public string TargetAssemblyVersion { get; set; } = string.Empty;
    public string TargetAssemblySha256 { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public int SourceWireCount { get; set; }
    public int TargetWireCount { get; set; }
    public int IntentionalDroppedWireCount { get; set; }
    public bool ArchiveValidationPassed { get; set; }
    public List<FileManifest> Files { get; set; } = new();
}

internal sealed class FileManifest
{
    public string File { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string TargetSha256 { get; set; } = string.Empty;
    public long SourceBytes { get; set; }
    public long TargetBytes { get; set; }
    public int ObjectCount { get; set; }
    public int NucleiObjectCount { get; set; }
    public int SourceWireCount { get; set; }
    public int TargetWireCount { get; set; }
    public string SourceWireHash { get; set; } = string.Empty;
    public string TargetWireHash { get; set; } = string.Empty;
    public string SourceWireConnectionHash { get; set; } = string.Empty;
    public string ExpectedTargetWireConnectionHash { get; set; } = string.Empty;
    public string TargetWireConnectionHash { get; set; } = string.Empty;
    public bool GraphConnectionsPreserved { get; set; }
    public int IntentionalDroppedWireCount { get; set; }
    public List<WireMigration> WireMigrations { get; set; } = new();
    public string ObjectInstanceGuidHash { get; set; } = string.Empty;
    public string SourcePersistentDataHashExcludingSchemaAdapters { get; set; } = string.Empty;
    public string TargetPersistentDataHashExcludingSchemaAdapters { get; set; } = string.Empty;
    public bool PersistentDataPreservedOutsideSchemaAdapters { get; set; }
    public List<ConvertedObject> ConvertedObjects { get; set; } = new();
    public List<string> Adapters { get; set; } = new();
    public bool ArchiveReloadPassed { get; set; }
    public bool NoV3Residue { get; set; }
}

internal sealed class ConvertedObject
{
    public int Index { get; set; }
    public string InstanceGuid { get; set; } = string.Empty;
    public Guid SourceGuid { get; set; }
    public Guid TargetGuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Adapter { get; set; }
    public bool? ProbabilisticSteering { get; set; }
}

internal sealed class WireMigration
{
    public string Change { get; set; } = string.Empty;
    public string DestinationComponentInstanceGuid { get; set; } = string.Empty;
    public string DestinationParameterInstanceGuid { get; set; } = string.Empty;
    public string DestinationInput { get; set; } = string.Empty;
    public string SourceParameterInstanceGuid { get; set; } = string.Empty;
    public string SourceObject { get; set; } = string.Empty;
    public bool PreservedSourceObject { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed class TrailMigrationReport
{
    public DateTime CreatedUtc { get; set; }
    public string SourceDirectory { get; set; } = string.Empty;
    public string TargetDirectory { get; set; } = string.Empty;
    public Guid ComponentGuid { get; set; }
    public int FileCount { get; set; }
    public int AffectedFileCount { get; set; }
    public int ComponentCount { get; set; }
    public int RemovedWireCount { get; set; }
    public bool ValidationPassed { get; set; }
    public List<TrailMigrationFile> Files { get; set; } = new();
}

internal sealed class TrailMigrationFile
{
    public string File { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string TargetSha256 { get; set; } = string.Empty;
    public int ObjectCount { get; set; }
    public int ComponentCount { get; set; }
    public int SourceWireCount { get; set; }
    public int TargetWireCount { get; set; }
    public int RemovedWireCount { get; set; }
    public List<string> RemovedConnections { get; set; } = new();
    public bool ValidationPassed { get; set; }
}
