using System.Drawing;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;

internal static class ChecksStage
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> assertions = new();
    static readonly List<object> cases = new();
    static readonly List<GH_Document> documents = new();
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static Type previewType;
    static Type frameType;
    static string outputDirectory;
    static string ghaPath;

    public static void Run(string gha, string output)
    {
        outputDirectory = output;
        ghaPath = gha;
        Directory.CreateDirectory(output);
        string profile = Path.Combine(output, "isolated-profile");
        Directory.CreateDirectory(Path.Combine(profile, "Libraries"));
        typeof(Grasshopper.Folders).GetField("m_appdataFolder", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, profile + Path.DirectorySeparatorChar);
        GH_Document.EnableSolutions = false;
        var server = Grasshopper.Instances.ComponentServer;
        var load = server.GetType().GetMethods(All).Single(m => m.Name == "LoadGHA"
            && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(GH_ExternalFile));
        load.Invoke(server, new object[] { new GH_ExternalFile(gha), false });
        Assembly main = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Nuclei4");
        Require(Path.GetFullPath(main.Location).Equals(gha, StringComparison.OrdinalIgnoreCase), "exact requested GHA loaded");
        previewType = main.GetType("Nuclei4.Preview_Voxel", true);
        Assembly abstraction = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(gha), "Nuclei4.Display.Abstractions.dll"));
        frameType = abstraction.GetType("Nuclei4.GpuDensityFieldPreviewFrame", true);
        try
        {
            DefaultAndMenu();
            AntVolumeStyles();
            RoundTrip("five-default", false, false, 0, false);
            RoundTrip("five-high", false, true, 0, false);
            RoundTrip("five-missing-quality-marker", false, false, 0, true);
            foreach (bool high in new[] { false, true })
                foreach (int retiredMode in new[] { 0, 1 })
                    RoundTrip("six-renderer-" + retiredMode + "-high-" + high.ToString().ToLowerInvariant(), true, high, retiredMode, false);
            Report(true, null);
            Console.WriteLine("PASS: " + assertions.Count + " assertions; " + cases.Count + " cases.");
            Console.WriteLine("Synthetic archives and results: " + output);
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Report(false, error.ToString());
            throw;
        }
        finally
        {
            foreach (GH_Document document in documents) document.Dispose();
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        assertions.Add(message);
    }

    static GH_Component NewPreview()
    {
        var component = (GH_Component)Activator.CreateInstance(previewType);
        component.CreateAttributes();
        component.Attributes.Pivot = new PointF(550, 250);
        return component;
    }

    static object Frame(bool volume)
    {
        var frame = Activator.CreateInstance(frameType, true);
        Set(frame, "AxisMode", volume ? 3 : 0);
        Set(frame, "ResX", 8); Set(frame, "ResY", 8); Set(frame, "ResZ", volume ? 8 : 1);
        Set(frame, "VoxelSize", 1f);
        Set(frame, "Version", 123L);
        // Opaque descriptor used only for style selection; never opened as a GPU resource.
        Set(frame, "GradientSharedHandle", volume ? new IntPtr(1) : IntPtr.Zero);
        return frame;
    }

    static void AssertStyle(GH_Component component, bool high, string label)
    {
        var volume = Frame(true);
        Invoke(component, "applyGpuPreviewStyle", volume, 7);
        Require((bool)Get(volume, "UseNewRenderer") == !high, label + ": volumetric renderer follows High Resolution");
        Require((int)Get(volume, "VolumeSampleCount") == (high ? 256 : 128), label + ": volumetric sample count");
        Require((int)Get(volume, "VolumeRendererVersion") == 2, label + ": existing gradient renderer version preserved");
        Require((long)Get(volume, "Version") == 123L, label + ": frame version unchanged");
        Require(!(bool)Get(volume, "FancyRender"), label + ": fancy mode remains disabled");
        var planar = Frame(false);
        Invoke(component, "applyGpuPreviewStyle", planar, 7);
        Require(!(bool)Get(planar, "UseNewRenderer"), label + ": planar path does not use new renderer");
    }

    static ToolStripMenuItem HighResolutionItem(GH_Component component, ContextMenuStrip menu)
    {
        Invoke(component, "AppendAdditionalComponentMenuItems", menu);
        return menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "High Resolution (3D)");
    }

    static void AntVolumeStyles()
    {
        var component = NewPreview();
        Set(component, "colour", Color.FromArgb(192, 60, 120, 240));
        Set(component, "min", 0.25);
        Set(component, "max", 0.75);
        foreach (bool high in new[] { false, true })
        {
            Set(component, "highResolutionPreview", high);
            foreach (int field in new[] { 7, 8, 9, 10, 11, 12 })
            {
                string label = "field " + field + ", high=" + high;
                var frame = Frame(true);
                Invoke(component, "applyGpuPreviewStyle", frame, field);
                Require((int)Get(frame, "VolumeRendererVersion") == 2, label + ": gradient volume shading");
                Require((bool)Get(frame, "UseNewRenderer") == !high, label + ": quality selects renderer");
                Require((int)Get(frame, "VolumeSampleCount") == (high ? 256 : 128), label + ": sample budget");
                Require((int)Get(frame, "ValueIndex") == (field == 12 ? 7 : field), label + ": selected field");
                Require((bool)Get(frame, "UseCustomColor") == (field != 10 && field != 11), label + ": custom/combined colours");
                Require((float)Get(frame, "MinimumThreshold") == 0.25f && (float)Get(frame, "MaximumThreshold") == 0.75f,
                    label + ": thresholds preserved");
                Set(frame, "GradientSharedHandle", IntPtr.Zero);
                Invoke(component, "applyGpuPreviewStyle", frame, field);
                Require((int)Get(frame, "VolumeRendererVersion") == 1, label + ": missing-gradient fallback");
                var planar = Frame(false);
                Invoke(component, "applyGpuPreviewStyle", planar, field);
                Require((int)Get(planar, "VolumeRendererVersion") == 1 && !(bool)Get(planar, "UseNewRenderer"),
                    label + ": planar preview unchanged");
            }
        }
        cases.Add(new { Name = "ant-and-slime-volume-styles", Passed = true });
        Console.WriteLine("PASS: ant food/base/combined/mixed gradient shading, quality modes, fallback and planar styles.");
    }

    static void DefaultAndMenu()
    {
        var component = NewPreview();
        Schema(component, "new component");
        Require(!(bool)Get(component, "highResolutionPreview"), "High Resolution off by default");
        AssertStyle(component, false, "default");
        using (var menu = new ContextMenuStrip())
        {
            var item = HighResolutionItem(component, menu);
            Require(!item.Checked, "initial right-click checkbox unchecked");
            item.PerformClick();
        }
        Require((bool)Get(component, "highResolutionPreview"), "right-click enables High Resolution");
        AssertStyle(component, true, "menu enabled");
        using (var menu = new ContextMenuStrip())
        {
            var item = HighResolutionItem(component, menu);
            Require(item.Checked, "reopened checkbox reflects enabled state");
            item.PerformClick();
        }
        Require(!(bool)Get(component, "highResolutionPreview"), "right-click disables High Resolution");
        AssertStyle(component, false, "menu disabled");
        cases.Add(new { Name = "default-and-right-click", Passed = true });
        Console.WriteLine("PASS: five inputs, default fast 128, right-click original 256, toggle back, planar unaffected.");
    }

    static void Schema(GH_Component component, string label)
    {
        string[] names = { "Voxels", "Type", "Minimum Treshold", "Maximum Treshold", "Colour" };
        Require(component.Params.Input.Count == 5, label + ": five inputs, no Renderer input");
        for (int i = 0; i < names.Length; i++)
            Require(component.Params.Input[i].Name == names[i], label + ": original input name/index " + i);
    }

    static GH_Document NewDocument()
    {
        var document = new GH_Document { Enabled = false };
        documents.Add(document);
        return document;
    }

    static void Add(GH_Document document, IGH_DocumentObject item)
    {
        item.CreateAttributes();
        document.AddObject(item, false);
    }

    static IGH_Param[] Sources()
    {
        var generic = new Param_GenericObject();
        generic.PersistentData.Append(new GH_String("synthetic archive sentinel"), new GH_Path(0));
        var type = new Param_Integer();
        type.PersistentData.Append(new GH_Integer(7), new GH_Path(0));
        var minimum = new Param_Number();
        minimum.PersistentData.Append(new GH_Number(0.12), new GH_Path(0));
        var maximum = new Param_Number();
        maximum.PersistentData.Append(new GH_Number(0.86), new GH_Path(0));
        var colour = new Param_Colour();
        colour.PersistentData.Append(new GH_Colour(Color.FromArgb(127, 73, 123, 45)), new GH_Path(0));
        return new IGH_Param[] { generic, type, minimum, maximum, colour };
    }

    static void SetPersistentSettings(GH_Component component)
    {
        var type = (Param_Integer)component.Params.Input[1];
        type.PersistentData.Clear(); type.PersistentData.Append(new GH_Integer(7), new GH_Path(0));
        var minimum = (Param_Number)component.Params.Input[2];
        minimum.PersistentData.Clear(); minimum.PersistentData.Append(new GH_Number(0.12), new GH_Path(0));
        var maximum = (Param_Number)component.Params.Input[3];
        maximum.PersistentData.Clear(); maximum.PersistentData.Append(new GH_Number(0.86), new GH_Path(0));
        var colour = (Param_Colour)component.Params.Input[4];
        colour.PersistentData.Clear(); colour.PersistentData.Append(new GH_Colour(Color.FromArgb(127, 73, 123, 45)), new GH_Path(0));
    }

    static void RoundTrip(string name, bool retiredInput, bool high, int retiredMode, bool omitQualityMarker)
    {
        var document = NewDocument();
        var component = NewPreview();
        Set(component, "highResolutionPreview", high);
        SetPersistentSettings(component);
        Add(document, component);
        var sources = Sources();
        for (int i = 0; i < sources.Length; i++)
        {
            Add(document, sources[i]);
            component.Params.Input[i].AddSource(sources[i]);
        }
        Guid[] originalParameterGuids = component.Params.Input.Select(p => p.InstanceGuid).ToArray();
        Guid[] originalSourceGuids = sources.Select(p => p.InstanceGuid).ToArray();
        if (retiredInput)
        {
            var renderer = new Param_Integer { Name = "Renderer", NickName = "renderer", Optional = true, Access = GH_ParamAccess.item };
            renderer.PersistentData.Append(new GH_Integer(retiredMode), new GH_Path(0));
            component.Params.RegisterInputParam(renderer, 5);
            component.Params.OnParametersChanged();
            var list = new GH_ValueList { Name = "Renderer", NickName = "Renderer", ListMode = GH_ValueListMode.DropDown };
            list.ListItems.Clear();
            list.ListItems.Add(new GH_ValueListItem("original renderer", "0") { Selected = retiredMode == 0 });
            list.ListItems.Add(new GH_ValueListItem("new renderer", "1") { Selected = retiredMode == 1 });
            Add(document, list);
            renderer.AddSource(list);
        }
        string firstPath = Path.Combine(outputDirectory, name + "-input.gh");
        var firstIo = new GH_DocumentIO(document);
        Require(firstIo.SaveQuiet(firstPath), name + ": save synthetic input archive");
        if (omitQualityMarker)
        {
            var archive = new GH_Archive();
            Require(archive.ReadFromFile(firstPath), name + ": fixture archive read");
            var xml = XDocument.Parse(archive.Serialize_Xml());
            foreach (var item in xml.Descendants("item").Where(e => (string)e.Attribute("name") == "HighResolutionPreview").ToArray()) item.Remove();
            // Update item counts after the deliberately missing legacy setting.
            foreach (var items in xml.Descendants("items")) items.SetAttributeValue("count", items.Elements("item").Count());
            var changed = new GH_Archive();
            Require(changed.Deserialize_Xml(xml.ToString()), name + ": markerless archive deserialize");
            firstPath = Path.Combine(outputDirectory, name + "-legacy-input.gh");
            Require(changed.WriteToFile(firstPath, false, false), name + ": save markerless input archive");
        }

        var opened = Open(firstPath, name + " first open");
        VerifyDocument(opened.Document, component.InstanceGuid, originalParameterGuids, originalSourceGuids, high, name + " first open");
        Require(opened.Document.ObjectCount == document.ObjectCount, name + ": no unrelated objects lost or automatically added");
        string secondPath = Path.Combine(outputDirectory, name + "-resaved.gh");
        Require(opened.SaveQuiet(secondPath), name + ": save migrated archive");
        var reopened = Open(secondPath, name + " reopen");
        VerifyDocument(reopened.Document, component.InstanceGuid, originalParameterGuids, originalSourceGuids, high, name + " reopen");
        cases.Add(new { Name = name, Passed = true, OriginalInputs = retiredInput ? 6 : 5, OriginalRendererValue = retiredInput ? retiredMode : (int?)null,
            HighResolution = high, InputFile = firstPath, ResavedFile = secondPath,
            InputSha256 = Hash(firstPath), ResavedSha256 = Hash(secondPath) });
        Report(false, "Verification in progress.");
        Console.WriteLine("PASS: " + name + " preserves all five resolved input wires and settings through open/save/reopen.");
    }

    static GH_DocumentIO Open(string path, string label)
    {
        var io = new GH_DocumentIO();
        Require(io.Open(path) && io.Document != null, label + ": GH_DocumentIO opens file");
        io.Document.Enabled = false;
        documents.Add(io.Document);
        Require(!io.Document.Objects.Any(o => o.GetType().Name.Contains("Placeholder") || o.GetType().Name.Contains("UnknownObject")), label + ": no unresolved components");
        return io;
    }

    static void VerifyDocument(GH_Document document, Guid instance, Guid[] parameterGuids, Guid[] sourceGuids, bool high, string label)
    {
        var component = (GH_Component)document.Objects.Single(o => o.InstanceGuid == instance);
        Require(component.GetType() == previewType, label + ": real requested preview type resolved");
        Schema(component, label);
        for (int i = 0; i < 5; i++)
        {
            var parameter = component.Params.Input[i];
            Require(parameter.InstanceGuid == parameterGuids[i], label + ": original parameter GUID " + i);
            Require(parameter.SourceCount == 1, label + ": original source count " + i);
            Require(parameter.Sources[0].InstanceGuid == sourceGuids[i], label + ": original resolved wire GUID " + i);
            Require(ReferenceEquals(parameter.Sources[0], document.Objects.Single(o => o.InstanceGuid == sourceGuids[i])), label + ": actual source object, not dangling proxy " + i);
        }
        Require(((Param_Integer)component.Params.Input[1]).PersistentData.AllData(true).Cast<GH_Integer>().First().Value == 7, label + ": stored Type preserved");
        Require(((Param_Number)component.Params.Input[2]).PersistentData.AllData(true).Cast<GH_Number>().First().Value == 0.12, label + ": stored minimum preserved");
        Require(((Param_Number)component.Params.Input[3]).PersistentData.AllData(true).Cast<GH_Number>().First().Value == 0.86, label + ": stored maximum preserved");
        Require(((Param_Colour)component.Params.Input[4]).PersistentData.AllData(true).Cast<GH_Colour>().First().Value.ToArgb() == Color.FromArgb(127, 73, 123, 45).ToArgb(), label + ": stored colour preserved");
        Require((bool)Get(component, "highResolutionPreview") == high, label + ": saved HighResolutionPreview preserved");
        AssertStyle(component, high, label);
    }

    static object Invoke(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, All).Invoke(target, arguments);
    static object Get(object target, string field) => target.GetType().GetField(field, All).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, All).SetValue(target, value);
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    static void Report(bool passed, string error)
    {
        File.WriteAllText(Path.Combine(outputDirectory, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, Error = error, Gha = ghaPath, GhaSha256 = Hash(ghaPath),
            DependencyFiles = Directory.EnumerateFiles(Path.GetDirectoryName(ghaPath), "*.dll")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new { Path = path, Sha256 = Hash(path) }).ToArray(),
            LoadedNucleiAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name.StartsWith("Nuclei4", StringComparison.Ordinal) && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => new { Name = assembly.GetName().Name, assembly.Location, Sha256 = Hash(assembly.Location) }).ToArray(),
            RhinoVersion = Rhino.RhinoApp.Version.ToString(), GrasshopperVersion = typeof(Grasshopper.Instances).Assembly.GetName().Version.ToString(),
            AssertionCount = assertions.Count, Assertions = assertions, Cases = cases,
            Scope = "Synthetic component/menu/style and full Grasshopper archive migration checks in isolated hidden RhinoCore; no solver, viewport, GPU rendering or user definition opened; no installed files changed."
        }, JsonOptions));
    }
}
