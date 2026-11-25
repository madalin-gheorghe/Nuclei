using System.Drawing;
using Grasshopper.Kernel;
using Nuclei3;

internal static partial class Probe
{
    static void ComponentTests()
    {
        var ants = new ParticleGroup_Constructor_Ant();
        Check(ants.Params.Input.Count == 9 && ants.Params.Input.All(p => p.Name != "Particle Count"), "ant constructor must not expose scattered count");
        Check(!ants.Params.Input[1].Optional, "nest points must be required");
        var settings = new EnivronmentSettings_Ant();
        Check(settings.Params.Input.Count == 6 && settings.Params.Input[4].Name == "Falloff" && settings.Params.Input[5].Name == "Diffuse Range", "ant Falloff input order");
        var solver = Create(Shapes[0], true, 1, false);
        foreach (var item in new[] { ("", 1.0), (" 0.4", .6), (" 1", 0.0), (" -1", 1.0), (" 2", 0.0), (" NaN", 1.0) })
        {
            Set(solver, "settings", new List<string> { "VoxelSettingsAnt 0.05 0.005 0.1 0.01 3" + item.Item1 });
            Call(solver, "readSolverSettings");
            Near(Get<double>(solver, "antDiffusionGradual"), item.Item2, "legacy/default/clamped ant Falloff");
            Check(Get<int>(solver, "diffuseRange_Ant") == 3, "Falloff parsing must preserve Range");
        }
        int count=0;
        var ids=new HashSet<Guid>();
        foreach(Type type in typeof(Solver).Assembly.GetTypes().Where(t=>!t.IsAbstract&&typeof(GH_Component).IsAssignableFrom(t)))
        {
            var component=(GH_Component)Activator.CreateInstance(type);
            component.CreateAttributes();
            Check(ids.Add(component.ComponentGuid),"duplicate component GUID: "+type.Name);
            var icon=(Bitmap)type.GetProperty("Icon",Flags).GetValue(component);
            Check(icon!=null&&icon.Width>0&&icon.Height>0,"missing component icon: "+type.Name);
            count++;
        }
        Check(count==37,"component count changed: "+count);
        Results.Add(new{Test="component-instantiation-and-icons",Components=count,Passed=true});
        Console.WriteLine($"PASS {count} component constructors, attributes, unique GUIDs and icons.");
    }
}
