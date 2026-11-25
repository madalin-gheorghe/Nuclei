using System.Drawing;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

internal static class ChecksStage
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly main;
    static Type gridType, mapType, fieldType, helperType;
    static int assertions;
    static readonly List<string> cases = new();
    static readonly List<string> deferredFailures = new();
    static readonly Color Purple = Color.FromArgb(29, 19, 53);

    public static void Run(string gha, string output)
    {
        Directory.CreateDirectory(output);
        string profile = Path.Combine(output, "isolated-profile");
        Directory.CreateDirectory(Path.Combine(profile, "Libraries"));
        typeof(Grasshopper.Folders).GetField("m_appdataFolder", All).SetValue(null, profile + Path.DirectorySeparatorChar);
        GH_Document.EnableSolutions = false;
        var server = Grasshopper.Instances.ComponentServer;
        server.GetType().GetMethods(All).Single(m => m.Name == "LoadGHA" && m.GetParameters().Length == 2
            && m.GetParameters()[0].ParameterType == typeof(GH_ExternalFile)).Invoke(server, new object[] { new GH_ExternalFile(gha), false });
        main = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Nuclei4");
        Check(Path.GetFullPath(main.Location).Equals(gha, StringComparison.OrdinalIgnoreCase), "exact requested assembly loaded");
        gridType = main.GetType("Nuclei4.VoxelGridData", true);
        mapType = main.GetType("Nuclei4.VoxelScalarMap", true);
        fieldType = main.GetType("Nuclei4.VoxelField", true);
        helperType = main.GetType("Nuclei4.VoxelObstaclePreview", true);

        foreach (var shape in new[] { (5, 4, 1), (5, 1, 4), (1, 5, 4), (5, 1, 1), (1, 1, 1) })
            Planar(shape.Item1, shape.Item2, shape.Item3);
        Volume(5, 4, 3);
        SampledVolume();
        EmptyInputs();
        ComponentCache();
        assertions += FoodChecks.Run(main, cases);
        if (Environment.GetCommandLineArgs().Contains("--capture"))
        {
            object data = Grid(20, 20, 1, 1, Enumerable.Range(0, 400).Select(i => i / 20 >= 6 && i / 20 < 14 && i % 20 >= 6 && i % 20 < 14 ? 0f : -1f).ToArray());
            using var helper = Helper(data, .0001);
            using var food = FoodChecks.Helper(data, -2, i => i / 20 >= 8 && i / 20 < 12 && i % 20 >= 8 && i % 20 < 12 ? .0001 : 0);
            CaptureStage.Run(main, helper, food, output);
            cases.Add("food behind CPU field and purple obstacle remains visible on top in viewport capture");
        }
        if (deferredFailures.Count > 0) throw new InvalidOperationException(string.Join("; ", deferredFailures));
        string summary = $"PASS: {assertions} assertions across {cases.Count} obstacle and food preview cases.";
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new {
            Passed = true, Assertions = assertions, Cases = cases, Gha = gha,
            GhaSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gha)))
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(summary);
    }

    static void Planar(int nx, int ny, int nz)
    {
        const double size = 2.5, offset = .125;
        int count = nx * ny * nz;
        float[] max = Enumerable.Range(0, count).Select(i => new[] { 0f, -1f, .005f, .02f, .75f, 0f }[i % 6]).ToArray();
        bool[] active = Enumerable.Range(0, count).Select(i => i % 7 != 5).ToArray();
        object data = Grid(nx, ny, nz, size, max, active);
        using var helper = Helper(data, offset);
        var mesh = (Mesh)Get(helper, "planarMesh");
        Check(Get(helper, "pointCloud") == null, "planar field has no volume cloud");
        bool xy = nz == 1, xz = !xy && ny == 1;
        int width = xy || xz ? nx : ny, height = xy ? ny : nz;
        double area = 0;
        var rectangles = new List<(double MinU, double MaxU, double MinV, double MaxV)>();
        foreach (MeshFace face in mesh.Faces)
        {
            Check(face.IsQuad, "planar obstacle face is a rectangle");
            var vertices = new[] { mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D] };
            foreach (var p in vertices) Check(Math.Abs((xy ? p.Z : xz ? p.Y : p.X) - offset) < 1e-6, "plane offset preserved");
            double U(Point3f p) => xy || xz ? p.X : p.Y;
            double V(Point3f p) => xy ? p.Y : p.Z;
            var r = (vertices.Min(U), vertices.Max(U), vertices.Min(V), vertices.Max(V));
            rectangles.Add(r);
            area += (r.Item2 - r.Item1) * (r.Item4 - r.Item3);
        }
        int blocked = 0;
        for (int u = 0; u < width; u++)
        for (int v = 0; v < height; v++)
        {
            int i = xy ? (u * ny + v) * nz : xz ? u * nz + v : u * nz + v;
            bool expected = active[i] && max[i] >= 0 && max[i] < .01;
            double pu = (u + .5) * size, pv = (v + .5) * size;
            int covering = rectangles.Count(r => pu > r.MinU && pu < r.MaxU && pv > r.MinV && pv < r.MaxV);
            Check(covering == (expected ? 1 : 0), $"{nx}x{ny}x{nz} cell {u},{v}: exact obstacle coverage");
            if (expected) blocked++;
        }
        Check(Math.Abs(area - blocked * size * size) < 1e-5, "mesh has exactly the area of active obstacles");
        var material = (DisplayMaterial)Get(helper, "planarMaterial");
        Check(material.Diffuse.ToArgb() == Purple.ToArgb(), "planar material matches maxDensity zero color");
        if (material.BackDiffuse.ToArgb() != Purple.ToArgb()) deferredFailures.Add($"{nx}x{ny}x{nz}: back-facing obstacle material is {material.BackDiffuse}, expected {Purple}.");
        else Check(true, "back-facing obstacle material matches maxDensity zero color");
        Check(material.IsTwoSided, "obstacles visible from both sides of a planar field");
        Invoke(helper, "Draw", new object[] { null });
        helper.Dispose();
        Disposed(helper);
        cases.Add($"planar {nx}x{ny}x{nz}");
    }

    static void Volume(int nx, int ny, int nz)
    {
        const double size = 2;
        int count = nx * ny * nz;
        float[] max = Enumerable.Range(0, count).Select(i => new[] { 0f, -1f, .005f, .02f, .5f }[i % 5]).ToArray();
        bool[] active = Enumerable.Range(0, count).Select(i => i % 7 != 0).ToArray();
        object data = Grid(nx, ny, nz, size, max, active);
        using var helper = Helper(data, 0);
        Check(Get(helper, "planarMesh") == null, "volume has no planar mesh");
        var cloud = (PointCloud)Get(helper, "pointCloud");
        var expected = Enumerable.Range(0, count).Where(i => active[i] && max[i] >= 0 && max[i] < .01).ToHashSet();
        Check(cloud.Count == expected.Count, "all active blocked volume voxels are present");
        foreach (var p in cloud)
        {
            int x = (int)(p.Location.X / size), y = (int)(p.Location.Y / size), z = (int)(p.Location.Z / size);
            int i = (x * ny + y) * nz + z;
            Check(expected.Remove(i), "volume obstacle membership is unique and correct");
            Check(p.Location.DistanceTo(new Point3d((x + .5) * size, (y + .5) * size, (z + .5) * size)) < 1e-8,
                "volume point lies at voxel center");
            Check(p.Color.ToArgb() == Purple.ToArgb(), "volume point uses obstacle color");
        }
        Check(expected.Count == 0, "volume contains every expected obstacle");
        helper.Dispose(); Disposed(helper);
        cases.Add("mixed active 3D obstacles");
    }

    static void SampledVolume()
    {
        // More than the budget of blocked cells, interleaved with walkable cells.
        const int nx = 101, ny = 101, nz = 64;
        int count = nx * ny * nz;
        float[] values = Enumerable.Range(0, count).Select(i => i % 2 == 0 ? 0f : -1f).ToArray();
        using var helper = Helper(Grid(nx, ny, nz, 1, values), 0);
        var cloud = (PointCloud)Get(helper, "pointCloud");
        Check(cloud.Count == 300000, "volume obstacle sampling respects point budget");
        var seen = new HashSet<int>();
        foreach (var p in cloud)
        {
            int i = ((int)p.Location.X * ny + (int)p.Location.Y) * nz + (int)p.Location.Z;
            if (i % 2 != 0 || !seen.Add(i)) throw new InvalidOperationException("Sampling included walkable or duplicate cell.");
        }
        Check(seen.Contains(0) && seen.Max() > count - 10, "sampled barriers span full volume");
        cases.Add("3D obstacle-only sampling budget");
    }

    static void EmptyInputs()
    {
        foreach (double uniform in new[] { -1d, .5d })
        {
            object data = Grid(3, 4, 1, 1);
            Set(data, "MaximumDensity", Map(uniform));
            using var helper = Helper(data, 0);
            Check(Get(helper, "planarMesh") == null && Get(helper, "pointCloud") == null, "unblocked uniform map emits no geometry");
        }
        object zero = Grid(3, 4, 1, 1);
        Set(zero, "MaximumDensity", Map(0));
        using (var helper = Helper(zero, 0)) Check(((Mesh)Get(helper, "planarMesh")).Faces.Count > 0, "uniform zero map is visible");
        object empty = Invoke(zero, "WithActiveMask", new bool[12]);
        using (var helper = Helper(empty, 0)) Check(Get(helper, "planarMesh") == null && Get(helper, "pointCloud") == null, "empty active domain emits no geometry");
        using (var helper = Helper(null, 0)) Check(Get(helper, "planarMesh") == null && Get(helper, "pointCloud") == null, "null input emits no geometry");
        cases.Add("uniform sentinel, uniform zero, and empty inputs");
    }

    static void ComponentCache()
    {
        object component = Activator.CreateInstance(main.GetType("Nuclei4.Preview_Voxel", true));
        object data = Grid(4, 3, 1, 1, new float[12]);
        object dynamic = Activator.CreateInstance(main.GetType("Nuclei4.VoxelDynamicData", true), true);
        object field = Activator.CreateInstance(fieldType, All, null, new[] { data, dynamic, null }, null);
        Set(field, "DynamicStateSynchronizer", (Action)(() => throw new Exception("Obstacle overlay triggered a live field readback.")));
        Set(component, "voxel", field);
        Set(component, "valueIndex", 7);
        Invoke(component, "updateObstaclePreview", .01);
        object original = Get(component, "obstaclePreview");
        Check(original != null, "component builds obstacle overlay");
        foreach (int fieldIndex in new[] { 7, 8, 9, 10, 11, 12 })
        {
            Set(component, "valueIndex", fieldIndex);
            Invoke(dynamic, "IncrementVersion");
            Invoke(component, "updateObstaclePreview", .01);
            Check(ReferenceEquals(original, Get(component, "obstaclePreview")), "changing ant/slime field and live version reuses static obstacle geometry");
        }
        Set(component, "min", .9); Set(component, "max", 1d); Set(component, "colour", Color.Red);
        Invoke(component, "updateObstaclePreview", .01);
        Check(ReferenceEquals(original, Get(component, "obstaclePreview")), "threshold/color inputs preserve obstacle geometry");
        Invoke(component, "updateObstaclePreview", .02);
        Check(!ReferenceEquals(original, Get(component, "obstaclePreview")), "planar offset change refreshes geometry");
        Disposed(original);
        original = Get(component, "obstaclePreview");
        Set(data, "MaximumDensity", Map(-1));
        Invoke(component, "updateObstaclePreview", .02);
        Check(!ReferenceEquals(original, Get(component, "obstaclePreview")), "replacement maxDensity map invalidates geometry");
        Disposed(original);
        foreach (int fieldIndex in new[] { 0, 1, 2, 3, 4, 5, 6, 13 })
        {
            Set(component, "valueIndex", fieldIndex);
            Invoke(component, "updateObstaclePreview", .02);
            Check(Get(component, "obstaclePreview") == null, "unrelated preview type has no overlay");
        }
        Set(component, "valueIndex", 10); Set(data, "MaximumDensity", Map(0));
        Invoke(component, "updateObstaclePreview", .02);
        original = Get(component, "obstaclePreview");
        Invoke(component, "clearObstaclePreview");
        Check(Get(component, "obstaclePreview") == null, "clearing component preview removes overlay");
        Disposed(original);
        cases.Add("component field selection, readback independence, cache invalidation, disposal");
    }

    static object Grid(int x, int y, int z, double size, float[] values = null, bool[] active = null)
    {
        object data = gridType.GetMethod("CreateFullDomain").Invoke(null, new object[] { x, y, z, size });
        if (values != null) Set(data, "MaximumDensity", Map(-1, values));
        if (active != null) data = Invoke(data, "WithActiveMask", active);
        return data;
    }
    static object Map(double value, float[] values = null) => Activator.CreateInstance(mapType, new object[] { value, values });
    static IDisposable Helper(object data, double offset) => (IDisposable)Activator.CreateInstance(helperType, All, null, new[] { data, (object)offset }, null);
    static object Get(object target, string name) => target.GetType().GetField(name, All).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, All).SetValue(target, value);
    static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethods(All).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static void Disposed(object helper) => Check(Get(helper, "planarMesh") == null && Get(helper, "planarMaterial") == null && Get(helper, "pointCloud") == null, "disposal releases all obstacle geometry");
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("FAIL: " + message); assertions++; }
}
