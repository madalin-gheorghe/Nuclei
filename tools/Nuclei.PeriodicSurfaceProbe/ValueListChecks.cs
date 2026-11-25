using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class ValueListChecks
{
    public static void Run(Assembly assembly)
    {
        foreach(string name in new[]{"Voxel_Values","Voxel_Extractor_Values","Voxel_Values_BlendAll","Voxel_PeriodicSurface","Preview_Voxel","GpuVolumeToMesh"})
        foreach(string scenario in new[]{"fresh","empty voxels","missing multiplier","existing lists","existing integers","stored choices","partial lists"})
        {
            if(scenario=="missing multiplier" && name!="Voxel_Values")continue;
            bool dendro=name=="GpuVolumeToMesh";
            bool function=name=="Voxel_PeriodicSurface";
            int savedType=function?18:13;
            GH_Document.EnableSolutions=false;
            using var document=new GH_Document(); document.Enabled=false;
            var component=(GH_Component)Activator.CreateInstance(assembly.GetType("Nuclei4."+name,true));
            component.CreateAttributes(); component.Attributes.Pivot=new PointF(550,250); document.AddObject(component,false);
            string label=name+" / "+scenario;
            int[] inputs=dendro ? new[]{1,3} : new[]{1};
            var existing=new Dictionary<int,IGH_Param>();
            Check(!document.Objects.OfType<GH_ValueList>().Any(),label+": waits for saved/pasted wires before creating lists");
            if(scenario=="empty voxels") {
                var empty=new Param_GenericObject(); empty.CreateAttributes(); document.AddObject(empty,false); component.Params.Input[0].AddSource(empty);
            }
            if(scenario=="missing multiplier") {
                var data=assembly.GetType("Nuclei4.VoxelGridData",true).GetMethod("CreateFullDomain").Invoke(null,new object[]{2,2,2,1.0});
                var field=Activator.CreateInstance(assembly.GetType("Nuclei4.VoxelField",true),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{data,null,null},null);
                var source=new Param_GenericObject(); source.PersistentData.Append(new GH_ObjectWrapper(field)); source.CreateAttributes(); document.AddObject(source,false); component.Params.Input[0].AddSource(source);
            }
            if(scenario=="stored choices") {
                foreach(int index in inputs) {
                    var input=(Param_Integer)component.Params.Input[index]; input.PersistentData.Clear(); input.PersistentData.Append(new GH_Integer(index==1?savedType:1));
                }
            }
            if(scenario=="existing lists" || scenario=="partial lists" || scenario=="existing integers") {
                foreach(int index in inputs.Where(i=>scenario!="partial lists" || i==1)) {
                    int value=index==1?savedType:1;
                    IGH_Param source;
                    if(scenario=="existing integers") {
                        var integer=new Param_Integer(); integer.PersistentData.Append(new GH_Integer(value)); source=integer;
                    } else {
                        var list=new GH_ValueList(); list.ListItems.Clear(); list.ListItems.Add(new GH_ValueListItem("Saved choice",value.ToString())); list.SelectItem(0); source=list;
                    }
                    source.CreateAttributes(); document.AddObject(source,false); component.Params.Input[index].AddSource(source); existing[index]=source;
                }
            }
            GH_Document.EnableSolutions=true; document.Enabled=true; document.NewSolution(false);
            foreach(int index in inputs) {
                Check(component.Params.Input[index].SourceCount==1,label+": input "+index+" has exactly one source without voxel data");
                var source=component.Params.Input[index].Sources.Single();
                if(existing.TryGetValue(index,out var prior)) {
                    Check(ReferenceEquals(prior,source),label+": existing source "+index+" preserved");
                } else {
                    var list=source as GH_ValueList;
                    Check(list!=null && document.Objects.Contains(list),label+": list "+index+" placed on canvas");
                    Check(list.ListMode==GH_ValueListMode.DropDown && list.Attributes.Pivot.X<component.Attributes.Pivot.X,label+": dropdown "+index+" positioned beside component");
                    int expected=scenario=="stored choices" ? (index==1?savedType:1) : (dendro && index==1?7:function?1:0);
                    Check(list.ListItems.Single(i=>i.Selected).Expression==expected.ToString(),label+": default/stored selection "+index+" retained");
                    if(index==1 && function) {
                        Check(list.ListItems.Select(i=>int.Parse(i.Expression)).SequenceEqual(new[]{1,2,3,4,5,6,7,9,10,11,12,14,15,16,18}),label+": function preset IDs preserved");
                    }
                    else if(index==1) {
                        Check(list.ListItems.Any(i=>i.Name=="Slime Food" && i.Expression=="6") && list.ListItems.Any(i=>i.Name=="Ant Food" && i.Expression=="13"),label+": type list includes separate food choices");
                        int max=name=="Voxel_Values" || name=="Voxel_Values_BlendAll"?6:name=="Voxel_Extractor_Values"?9:11;
                        Check(list.ListItems.Select(i=>int.Parse(i.Expression)).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,max+1).Append(13)),label+": component-specific type choices preserved");
                    }
                    else Check(list.ListItems.Select(i=>i.Name).SequenceEqual(new[]{"Continuous","Discrete"}),label+": method list choices");
                }
            }
            var ids=document.Objects.Select(o=>o.InstanceGuid).ToArray();
            component.ExpireSolution(false); document.NewSolution(false); document.NewSolution(false);
            Check(document.Objects.Select(o=>o.InstanceGuid).SequenceEqual(ids),label+": repeated solutions do not create duplicates");
            // Removing a list should restore only that missing source on the next normal solution.
            if(scenario=="fresh") {
                var first=component.Params.Input[1].Sources.Single();
                var method=dendro?component.Params.Input[3].Sources.Single():null;
                document.RemoveObject(first,false); document.NewSolution(false);
                Check(component.Params.Input[1].SourceCount==1 && component.Params.Input[1].Sources[0].InstanceGuid!=first.InstanceGuid,label+": deleted list restored without voxel data");
                if(dendro) Check(ReferenceEquals(method,component.Params.Input[3].Sources.Single()),label+": other dropdown not duplicated");
            }
            document.RemoveObject(component,false);
            foreach(int index in inputs) component.Params.Input[index].RemoveAllSources();
            int objectCount=document.ObjectCount; document.NewSolution(false);
            Check(document.ObjectCount==objectCount && inputs.All(i=>component.Params.Input[i].SourceCount==0),label+": removed component no longer creates lists");
            GH_Document.EnableSolutions=false;
        }
    }
    static void Check(bool condition,string label)
    {
        if(!condition)throw new InvalidOperationException(label);
        Console.WriteLine("PASS: "+label);
    }
}
