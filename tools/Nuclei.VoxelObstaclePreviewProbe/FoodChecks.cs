using System.Drawing;
using System.Reflection;
using Rhino.Display;
using Rhino.Geometry;

internal static class FoodChecks
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly main;
    static int assertions;

    internal static int Run(Assembly assembly, List<string> cases)
    {
        main = assembly;
        foreach (var shape in new[] { (5, 4, 1), (5, 1, 4), (1, 5, 4), (5, 1, 1), (1, 1, 1), (5, 4, 3) })
        {
            int count = shape.Item1 * shape.Item2 * shape.Item3;
            float[] values = Enumerable.Range(0, count).Select(i => new[] { 2f, 0f, -1f, .0001f, float.NaN }[i % 5]).ToArray();
            bool[] active = Enumerable.Range(0, count).Select(i => i % 7 != 4).ToArray();
            object data = Grid(shape.Item1, shape.Item2, shape.Item3);
            data = Invoke(data, "WithActiveMask", active);
            // Food remains visible on blocked cells and below signal display thresholds.
            Set(data, "MaximumDensity", Map(0));
            using var helper = Helper(data, -.125, i => values[i]);
            CheckMembership(helper, shape.Item1, shape.Item2, shape.Item3,
                i => active[i] && values[i] > 0, -.125);
            Invoke(helper, "Draw", new object[] { null });
            helper.Dispose();
            Disposed(helper);
            cases.Add($"food geometry {shape.Item1}x{shape.Item2}x{shape.Item3}, active mask, positive amounts and obstacle overlap");
        }
        using (var empty = Helper(Grid(3, 4, 1), 0, _ => 0))
            CheckMembership(empty, 3, 4, 1, _ => false, 0);
        using (var empty = Helper(null, 0, _ => throw new Exception("Null input must not read food.")))
            Check(Get(empty, "planarMesh") == null && Get(empty, "pointCloud") == null, "null food input emits no geometry");
        ComponentCache();
        cases.Add("CPU food depletion, GPU authored-food cache, no readback, all field selections and disposal");
        GpuOverlayFrame();
        cases.Add("GPU food overlay preserves live texture/quality and leaves the signal frame unchanged");
        return assertions;
    }

    static void GpuOverlayFrame()
    {
        Type type = Assembly.Load("Nuclei4.Display.Abstractions").GetType("Nuclei4.GpuDensityFieldPreviewFrame", true);
        foreach (bool fast in new[] { false, true })
        foreach (int selected in new[] { 7, 8, 9, 10, 11 })
        {
            object source = Activator.CreateInstance(type, true);
            Set(source, "SharedHandle", new IntPtr(1234)); Set(source, "GradientSharedHandle", new IntPtr(4321));
            Set(source, "Version", 73L); Set(source, "Width", 120); Set(source, "Height", 160);
            Set(source, "ResX", 30); Set(source, "ResY", 20); Set(source, "ResZ", 32);
            Set(source, "SourceResX", 60); Set(source, "SourceResY", 40); Set(source, "SourceResZ", 64);
            Set(source, "AtlasColumns", 4); Set(source, "AtlasRows", 8); Set(source, "VoxelSize", 2f);
            Set(source, "ValueIndex", selected); Set(source, "MinimumThreshold", .6f); Set(source, "MaximumThreshold", .7f);
            Set(source, "UseNewRenderer", fast); Set(source, "VolumeSampleCount", fast ? 128 : 256);
            Set(source, "FancyRender", true); Set(source, "UseCustomColor", true);
            var original = type.GetFields(All).ToDictionary(f => f, f => f.GetValue(source));
            object overlay = Invoke(source, "CreateFoodOverlay");
            foreach (var entry in original) Check(Equals(entry.Value, entry.Key.GetValue(source)), "creating food overlay does not mutate selected signal frame");
            foreach (string name in new[] { "SharedHandle", "Version", "Width", "Height", "ResX", "ResY", "ResZ", "SourceResX", "SourceResY",
                "SourceResZ", "AtlasColumns", "AtlasRows", "VoxelSize", "UseNewRenderer", "VolumeSampleCount" })
                Check(Equals(Get(source, name), Get(overlay, name)), "food overlay keeps live source geometry and quality: " + name);
            Check((int)Get(overlay, "ValueIndex") == 13 && (bool)Get(overlay, "FoodOverlay"), "GPU overlay selects remaining food");
            Check((float)Get(overlay, "MinimumThreshold") == 0 && (float)Get(overlay, "MaximumThreshold") == float.MaxValue,
                "signal thresholds cannot hide food");
            Check(!(bool)Get(overlay, "UseCustomColor") && !(bool)Get(overlay, "FancyRender")
                && (IntPtr)Get(overlay, "GradientSharedHandle") == IntPtr.Zero, "signal appearance cannot recolor food");
        }
    }

    static void ComponentCache()
    {
        object component = Activator.CreateInstance(main.GetType("Nuclei4.Preview_Voxel", true));
        object data = Grid(4, 3, 1);
        float[] sources = new float[12], authoredAnt = new float[12], remaining = new float[12];
        sources[0] = .0001f; authoredAnt[1] = 7; remaining[2] = .0001f;
        Set(data, "Food", Map(-1, sources)); Set(data, "AntFood", Map(-1, authoredAnt));
        object dynamic = Activator.CreateInstance(main.GetType("Nuclei4.VoxelDynamicData", true), true);
        Set(dynamic, "RemainingFood", remaining);
        object field = Activator.CreateInstance(main.GetType("Nuclei4.VoxelField", true), All, null, new[] { data, dynamic, null }, null);
        Set(field, "DynamicStateSynchronizer", (Action)(() => throw new Exception("Food overlay triggered a live field readback.")));
        Set(component, "voxel", field);
        Set(component, "min", .9); Set(component, "max", 1d); Set(component, "colour", Color.Red);
        foreach (int fieldIndex in new[] { 7, 8, 9, 10, 11, 12 })
        {
            Set(component, "valueIndex", fieldIndex);
            Invoke(component, "updateFoodPreview", .01, false);
            CheckMembership(Get(component, "foodPreview"), 4, 3, 1, i => i == 0 || i == 2, .01);
        }
        Check(Registered(component), "CPU food joins final foreground display pass");
        Invoke(component, "disableGpuDensityPreview");
        Check(!Registered(component), "disabling a CPU food preview removes display registration");
        Invoke(component, "updateFoodPreview", .01, false);
        object original = Get(component, "foodPreview");
        remaining[2] = 0;
        Invoke(component, "updateFoodPreview", .01, false);
        CheckMembership(Get(component, "foodPreview"), 4, 3, 1, i => i == 0, .01);
        Disposed(original);
        remaining[2] = 3;
        Invoke(component, "updateFoodPreview", .01, true);
        original = Get(component, "foodPreview");
        CheckMembership(original, 4, 3, 1, i => i == 0, .01);
        foreach (int fieldIndex in new[] { 7, 8, 9, 10, 11, 12 })
        {
            Set(component, "valueIndex", fieldIndex);
            Invoke(dynamic, "IncrementVersion");
            Invoke(component, "updateFoodPreview", .01, true);
            Check(ReferenceEquals(original, Get(component, "foodPreview")), "GPU field changes reuse authored slime-food geometry");
        }
        Set(data, "Food", Map(0));
        Invoke(component, "updateFoodPreview", .01, true);
        Check(!ReferenceEquals(original, Get(component, "foodPreview")), "replacing authored food invalidates GPU overlay");
        Disposed(original);
        CheckMembership(Get(component, "foodPreview"), 4, 3, 1, _ => false, .01);
        foreach (int fieldIndex in new[] { 0, 1, 2, 3, 4, 5, 6, 13 })
        {
            Set(component, "valueIndex", fieldIndex);
            Invoke(component, "updateFoodPreview", .01, true);
            Check(Get(component, "foodPreview") == null, "unrelated preview has no food overlay");
        }
        Set(component, "valueIndex", 11);
        Invoke(component, "updateFoodPreview", .01, false);
        original = Get(component, "foodPreview");
        Invoke(component, "clearFoodPreview");
        Check(Get(component, "foodPreview") == null, "clearing component removes food overlay");
        Disposed(original);
        Invoke(component, "clearPreviewCache");
        Check(!Registered(component), "clearing component cache releases foreground registration");
    }

    static bool Registered(object component)
    {
        Type manager = main.GetType("Nuclei4.NucleiGpuDisplayManager", true);
        var previews = (System.Collections.IDictionary)manager.GetField("voxelDensityPreviews", All).GetValue(null);
        return previews.Contains(component.GetType().GetProperty("InstanceGuid").GetValue(component));
    }

    static void CheckMembership(object helper, int nx, int ny, int nz, Func<int, bool> expected, double offset)
    {
        Check(helper != null, "food helper is present");
        var cloud = (PointCloud)Get(helper, "pointCloud");
        var mesh = (Mesh)Get(helper, "planarMesh");
        if (nx > 1 && ny > 1 && nz > 1)
        {
            Check(mesh == null, "3D food uses points");
            var indices = new HashSet<int>();
            if (cloud != null) foreach (var p in cloud)
            {
                int x = (int)p.Location.X, y = (int)p.Location.Y, z = (int)p.Location.Z;
                int i = (x * ny + y) * nz + z;
                Check(indices.Add(i) && expected(i), "food volume points are unique active food voxels");
                Check(p.Location.DistanceTo(new Point3d(x + .5, y + .5, z + .5)) < 1e-8, "food point is centered");
                Check(p.Color.ToArgb() == Color.White.ToArgb(), "food point is white");
            }
            Check(indices.Count == Enumerable.Range(0, nx * ny * nz).Count(expected), "volume contains every food voxel");
            return;
        }
        Check(cloud == null, "planar food uses mesh");
        bool xy = nz == 1, xz = !xy && ny == 1;
        int width = xy || xz ? nx : ny, height = xy ? ny : nz;
        var cells = new HashSet<int>();
        if (mesh != null) foreach (var face in mesh.Faces)
        {
            Check(face.IsQuad, "food face is rectangular");
            var vertices = new[] { mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D] };
            foreach (var p in vertices) Check(Math.Abs((xy ? p.Z : xz ? p.Y : p.X) - offset) < 1e-6, "food offset preserved");
            double U(Point3f p) => xy || xz ? p.X : p.Y;
            double V(Point3f p) => xy ? p.Y : p.Z;
            for (int u = (int)vertices.Min(U); u < vertices.Max(U); u++)
            for (int v = (int)vertices.Min(V); v < vertices.Max(V); v++)
                Check(cells.Add(xy ? u * ny + v : u * nz + v), "food faces never overlap");
        }
        for (int u = 0; u < width; u++)
        for (int v = 0; v < height; v++)
        {
            int i = xy ? u * ny + v : u * nz + v;
            Check(cells.Contains(i) == expected(i), "exact planar food membership");
        }
        if (cells.Count > 0)
        {
            var material = (DisplayMaterial)Get(helper, "planarMaterial");
            Check(material.Diffuse.ToArgb() == Color.White.ToArgb() && material.BackDiffuse.ToArgb() == Color.White.ToArgb()
                && material.IsTwoSided, "food is white from both sides regardless of signal color");
        }
    }

    static object Grid(int x, int y, int z) => main.GetType("Nuclei4.VoxelGridData", true).GetMethod("CreateFullDomain")
        .Invoke(null, new object[] { x, y, z, 1d });
    static object Map(double value, float[] values = null) => Activator.CreateInstance(main.GetType("Nuclei4.VoxelScalarMap", true), new object[] { value, values });
    internal static IDisposable Helper(object data, double offset, Func<int, double> foodAt) => (IDisposable)Activator.CreateInstance(
        main.GetType("Nuclei4.VoxelFoodPreview", true), All, null, new object[] { data, offset, foodAt }, null);
    static object Get(object target, string name) => target.GetType().GetField(name, All).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, All).SetValue(target, value);
    static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethods(All)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static void Disposed(object helper) => Check(Get(helper, "planarMesh") == null && Get(helper, "planarMaterial") == null && Get(helper, "pointCloud") == null, "food disposal releases geometry");
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("FAIL: " + message); assertions++; }
}
