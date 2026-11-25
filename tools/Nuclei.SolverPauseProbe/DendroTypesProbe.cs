using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using Rhino.Geometry;

internal static class DendroTypesProbe
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    internal static void Run(Assembly assembly)
    {
        var componentType = assembly.GetType("Nuclei4.GpuVolumeToMesh", true);
        var component = (GH_Component)Activator.CreateInstance(componentType);
        Require(component.Params.Input.Count == 7 && component.Params.Input[1].Name == "Type", "Type must be second.");
        var old = (GH_Component)Activator.CreateInstance(componentType);
        old.Params.UnregisterInputParameter(old.Params.Input[1], false);
        ((Param_Number)old.Params.Input[1]).PersistentData.Clear();
        ((Param_Number)old.Params.Input[1]).PersistentData.Append(new GH_Number(2.75));
        ((Param_Boolean)old.Params.Input[4]).PersistentData.Clear();
        ((Param_Boolean)old.Params.Input[4]).PersistentData.Append(new GH_Boolean(true));
        var chunk = new GH_LooseChunk("component");
        Require(old.Write(chunk) && component.Read(chunk), "Legacy archive failed.");
        Require(component.Params.Input.Count == 7 && component.Params.Input[1].Name == "Type", "Legacy input migration failed.");
        Require(((GH_Number)((Param_Number)component.Params.Input[2]).PersistentData.AllData(true).First()).Value == 2.75, "Iso value moved/lost.");
        Require(((GH_Boolean)((Param_Boolean)component.Params.Input[5]).PersistentData.AllData(true).First()).Value, "Update moved/lost.");
        Require(((GH_Integer)((Param_Integer)component.Params.Input[1]).PersistentData.AllData(true).First()).Value == 7, "Legacy slime default lost.");

        var voxelType = assembly.GetType("Nuclei4.Voxel", true);
        var voxels = Array.CreateInstance(voxelType, 3, 3, 3);
        string[] properties = { "minDensity", "maxDensity", "speedMultiplier", "sensorDistanceMultiplier", "sensorAngleMultiplier", "rotationAngleMultiplier", "food", "density", "towardsFoodPheromone", "towardsBasePheromone", "antFood" };
        for (int z = 0; z < 3; z++) for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
        {
            object v = Activator.CreateInstance(voxelType, new object[] { 1.0, x, y, z });
            for (int i = 0; i < properties.Length; i++) voxelType.GetProperty(properties[i]).SetValue(v, (double)(i + 2));
            voxels.SetValue(v, x, y, z);
        }
        object[] resolve = { voxels, 1.0, null };
        Require((bool)assembly.GetType("Nuclei4.VoxelFieldAccess").GetMethod("TryResolve", Static).Invoke(null, resolve), "Fixture unresolved.");
        object field = resolve[2];
        var mesher = assembly.GetType("Nuclei4.VoxelScalarMesher", true);
        var value = mesher.GetMethod("Value", Static);
        var create = mesher.GetMethod("Create", Static);
        int[] types = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 13, 10, 11 };
        double[] expected = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 11, 11 };
        for (int i = 0; i < types.Length; i++)
        {
            Require((double)value.Invoke(null, new object[] { field, types[i], 13 }) == expected[i], "Wrong scalar for type " + types[i]);
            object result = create.Invoke(null, new object[] { field, types[i], expected[i] / 2, 100000, 0 });
            Require((bool)result.GetType().GetField("Success").GetValue(result), "No mesh for type " + types[i]);
            using var mesh = (Mesh)result.GetType().GetField("Mesh").GetValue(result);
            Require(mesh.IsValid && mesh.IsClosed && mesh.Faces.Count > 0, "Invalid/open mesh for type " + types[i]);
        }
        foreach (double iso in new[] { 0.0, -1.0 })
        {
            object result = create.Invoke(null, new object[] { field, 0, iso, 100000, 1 });
            Require((bool)result.GetType().GetField("Success").GetValue(result), "Zero/negative threshold failed.");
            ((Mesh)result.GetType().GetField("Mesh").GetValue(result)).Dispose();
        }
        object limited = create.Invoke(null, new object[] { field, 0, 1.0, 1, 0 });
        Require(!(bool)limited.GetType().GetField("Success").GetValue(limited), "Triangle limit ignored.");
        object empty = create.Invoke(null, new object[] { field, 0, 100.0, 100000, 0 });
        Require(!(bool)empty.GetType().GetField("Success").GetValue(empty), "Empty selection accepted.");
        Console.WriteLine("PASS V4: all 13 scalar types, closed meshes, smoothing, zero/negative iso, limits, empty selection, legacy input migration.");
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
