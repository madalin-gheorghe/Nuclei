using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Special;
using GH_IO.Serialization;
using System.Xml.Linq;

internal static class ChecksStage
{
    public static void Run(string gha, string output)
    {
        Directory.CreateDirectory(output);
        string profile = Path.Combine(output, "profile");
        Directory.CreateDirectory(Path.Combine(profile, "Libraries"));
        typeof(Grasshopper.Folders).GetField("m_appdataFolder", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, profile + Path.DirectorySeparatorChar);
        GH_Document.EnableSolutions = false;
        var server = Grasshopper.Instances.ComponentServer;
        server.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name == "LoadGHA" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(GH_ExternalFile))
            .Invoke(server, new object[] { new GH_ExternalFile(gha), false });
        var main = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Nuclei4");
        if (Environment.GetCommandLineArgs().Contains("--value-lists-only"))
        {
            ValueListChecks.Run(main);
            File.WriteAllText(Path.Combine(output,"passed.txt"),"Automatic value lists passed for Define, Extract, Blend, Function Attractor, Preview and Dendro; empty inputs, choices, existing wires, duplicates and removal checked.");
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--demand-only"))
        {
            AttractorDemandChecks(main);
            FunctionAndInclusionDemandChecks(main);
            File.WriteAllText(Path.Combine(output,"passed.txt"),"Connection-only lazy output refresh passed.");
            return;
        }
        if (!Environment.GetCommandLineArgs().Contains("--skip-archives")) InputOrderChecks(main, output);
        FunctionPresetChecks(main, output);
        AttractorDemandChecks(main);
        FunctionAndInclusionDemandChecks(main);
        File.WriteAllText(Path.Combine(output,"passed.txt"),
            "Function presets, Custom override, list migration, attractor ranges and demand-driven outputs passed. " +
            (Environment.GetCommandLineArgs().Contains("--skip-archives") ? "Archive checks skipped." : "Schema and archive roundtrip passed."));
    }
    static void InputOrderChecks(Assembly main,string output)
    {
        var type=main.GetType("Nuclei4.Voxel_PeriodicSurface",true);
        string[] order={"Voxels","Surface","Custom","Scale","Iso Value","Minimum Range","Maximum Range"};
        foreach(string fixture in new[]{"old5","old7","new7"}) {
            using var document=new GH_Document(); document.Enabled=false;
            var c=(GH_Component)Activator.CreateInstance(type); c.CreateAttributes(); document.AddObject(c,false);
            Check(c.Params.Input.Select(p=>p.Name).SequenceEqual(order),"fresh input order");
            Check(((IGH_PreviewObject)c.Params.Output[1]).Hidden,"hidden positions");
            ((Param_String)c.Params.Input[2]).PersistentData.Clear(); ((Param_String)c.Params.Input[2]).PersistentData.Append(new GH_String("Math.Sin(x) + y"));
            SetNumber(c,3,11); SetNumber(c,4,0.35);
            if(fixture!="old5") { SetNumber(c,5,1); SetNumber(c,6,4); }
            if(fixture.StartsWith("old")) {
                var custom=c.Params.Input[2]; c.Params.Input.RemoveAt(2); c.Params.Input.Insert(4,custom);
                c.Name="Periodic Surface for Voxels"; c.NickName="Periodic Surface";
            }
            if(fixture=="old5") { c.Params.UnregisterInputParameter(c.Params.Input[6],true); c.Params.UnregisterInputParameter(c.Params.Input[5],true); }
            c.Params.OnParametersChanged();
            foreach(var parameter in c.Params.Input) {
                var source=new Param_GenericObject(); source.CreateAttributes(); document.AddObject(source,false); parameter.AddSource(source);
            }
            var ids=c.Params.Input.ToDictionary(p=>p.Name,p=>p.InstanceGuid);
            var sourceIds=c.Params.Input.ToDictionary(p=>p.Name,p=>p.Sources[0].InstanceGuid);
            string path=Path.Combine(output,fixture+".gh");
            Check(new GH_DocumentIO(document).SaveQuiet(path),fixture+": save fixture");
            if(fixture.StartsWith("old")) {
                var archive=new GH_Archive(); Check(archive.ReadFromFile(path),"read legacy fixture");
                var xml=XDocument.Parse(archive.Serialize_Xml());
                foreach(var item in xml.Descendants("item").Where(e=>(string)e.Attribute("name")=="FunctionAttractorInputOrder").ToArray()) item.Remove();
                foreach(var items in xml.Descendants("items")) items.SetAttributeValue("count",items.Elements("item").Count());
                var legacy=new GH_Archive(); Check(legacy.Deserialize_Xml(xml.ToString()),"remove new order marker");
                path=Path.Combine(output,fixture+"-legacy.gh"); Check(legacy.WriteToFile(path,false,false),"write legacy fixture");
            }
            for(int pass=0;pass<2;pass++) {
                var io=new GH_DocumentIO(); Check(io.Open(path),fixture+": open"); using var opened=io.Document;
                var restored=(GH_Component)opened.Objects.Single(o=>o.InstanceGuid==c.InstanceGuid);
                Check(restored.Name=="Function Attractor",fixture+": component name");
                Check(restored.Params.Input.Select(p=>p.Name).SequenceEqual(order),fixture+": custom follows surface");
                foreach(var input in restored.Params.Input.Where(p=>ids.ContainsKey(p.Name))) {
                    Check(input.InstanceGuid==ids[input.Name],fixture+": parameter identity "+input.Name);
                    Check(input.Sources.Single().InstanceGuid==sourceIds[input.Name],fixture+": resolved wire "+input.Name);
                }
                Check(((Param_String)restored.Params.Input[2]).PersistentData.AllData(true).Cast<GH_String>().Single().Value=="Math.Sin(x) + y",fixture+": custom formula retained");
                Check(((Param_Number)restored.Params.Input[3]).PersistentData.AllData(true).Cast<GH_Number>().Single().Value==11,fixture+": scale retained");
                Check(((Param_Number)restored.Params.Input[4]).PersistentData.AllData(true).Cast<GH_Number>().Single().Value==0.35,fixture+": iso retained");
                Check(((Param_Number)restored.Params.Input[5]).PersistentData.AllData(true).Cast<GH_Number>().Single().Value==(fixture=="old5"?0:1),fixture+": minimum retained/defaulted");
                Check(((Param_Number)restored.Params.Input[6]).PersistentData.AllData(true).Cast<GH_Number>().Single().Value==(fixture=="old5"?2:4),fixture+": maximum retained/defaulted");
                path=Path.Combine(output,fixture+"-resaved.gh"); Check(io.SaveQuiet(path),fixture+": resave");
            }
        }
    }
    static void AttractorDemandChecks(Assembly main)
    {
        var gridType=main.GetType("Nuclei4.VoxelGridData",true);
        var data=gridType.GetMethod("CreateFullDomain").Invoke(null,new object[]{10,10,10,1.0});
        var field=Activator.CreateInstance(main.GetType("Nuclei4.VoxelField",true),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{data,null,null},null);
        foreach(string kind in new[]{"Point","Curve","Mesh"})
        {
            using var doc=new GH_Document();
            var c=(GH_Component)Activator.CreateInstance(main.GetType("Nuclei4.Voxel_Attractor_"+kind,true));
            c.CreateAttributes(); doc.AddObject(c,false);
            ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(field));
            var center=new Rhino.Geometry.Point3d(5.5,5.5,5.5);
            if(kind=="Point") ((Param_Point)c.Params.Input[1]).PersistentData.Append(new GH_Point(center));
            if(kind=="Curve") ((Param_Curve)c.Params.Input[1]).PersistentData.Append(new GH_Curve(new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(2.5,5.5,5.5),new Rhino.Geometry.Point3d(7.5,5.5,5.5))));
            if(kind=="Mesh") {
                var mesh=new Rhino.Geometry.Mesh();
                mesh.Vertices.Add(5.5,0,0); mesh.Vertices.Add(5.5,10,0); mesh.Vertices.Add(5.5,10,10); mesh.Vertices.Add(5.5,0,10); mesh.Faces.AddFace(0,1,2,3);
                ((Param_Mesh)c.Params.Input[1]).PersistentData.Append(new GH_Mesh(mesh));
            }
            SetNumber(c,2,2); SetNumber(c,3,5);
            GH_Document.EnableSolutions=true; doc.Enabled=true;
            Solve(doc,c);
            string expected=Mask(c,gridType);
            Check(expected.Length>0,kind+": nonempty selection");
            Check(c.Params.Output.Skip(1).All(p=>p.VolatileDataCount==0),kind+": disconnected outputs empty");
            foreach(string name in new[]{"voxelPositions","voxelDistances","voxelIndices"})
                Check(c.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c)==null,kind+": no "+name+" allocation");
            for(int index=1;index<4;index++) {
                var sink=new Param_GenericObject(); sink.CreateAttributes(); doc.AddObject(sink,false); sink.AddSource(c.Params.Output[index]);
                doc.NewSolution(false);
                Check(Mask(c,gridType)==expected,kind+": output connection preserves selection "+index);
                Check(c.Params.Output[index].VolatileDataCount>0,kind+": connected output supplied "+index);
                Check(sink.VolatileDataCount==c.Params.Output[index].VolatileDataCount,kind+": consumer receives data in same solution");
                Check(c.Params.Output.Skip(1).Where((p,n)=>n+1!=index).All(p=>p.VolatileDataCount==0),kind+": other outputs remain empty");
                sink.RemoveAllSources(); doc.RemoveObject(sink,false); doc.NewSolution(false);
                Check(c.Params.Output[index].VolatileDataCount==0,kind+": disconnected output clears "+index);
            }
            SetNumber(c,2,5); SetNumber(c,3,2); Solve(doc,c);
            Check(Mask(c,gridType)==expected,kind+": reversed explicit ranges sorted");
            SetNumber(c,2,5); SetNumber(c,3,1); Solve(doc,c);
            double effectiveMin=(double)c.GetType().GetField("minR",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
            double effectiveMax=(double)c.GetType().GetField("maxR",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
            Check(effectiveMin==5 && effectiveMax>5,kind+": omitted maximum grows from minimum");
            var explicitMaximum=new Param_Number(); explicitMaximum.CreateAttributes(); doc.AddObject(explicitMaximum,false);
            explicitMaximum.PersistentData.Append(new GH_Number(1)); c.Params.Input[3].AddSource(explicitMaximum); c.Params.Input[3].ExpireSolution(false); Solve(doc,c);
            effectiveMin=(double)c.GetType().GetField("minR",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
            Check(effectiveMin==1,kind+": connected maximum equal to default is still explicit");
            c.Params.Input[3].RemoveAllSources(); doc.RemoveObject(explicitMaximum,false);
            SetNumber(c,2,2.2); SetNumber(c,3,2.25); Solve(doc,c);
            string thin=Mask(c,gridType);
            var sinks=new List<Param_GenericObject>();
            for(int index=1;index<4;index++) { var sink=new Param_GenericObject(); sink.CreateAttributes(); doc.AddObject(sink,false); sink.AddSource(c.Params.Output[index]); sinks.Add(sink); }
            Solve(doc,c);
            Check(Mask(c,gridType)==thin,kind+": thin-wall selection independent of auxiliary outputs");
            int thinCount=thin.Length==0?0:thin.Split(',').Length;
            Check(thinCount>0 && c.Params.Output.Skip(1).All(p=>p.VolatileDataCount==thinCount),kind+": expanded wall points/distances/indices align");
            foreach(var sink in sinks) { sink.RemoveAllSources(); doc.RemoveObject(sink,false); }
            var inverted=(Param_Boolean)c.Params.Input[4]; inverted.PersistentData.Clear(); inverted.PersistentData.Append(new GH_Boolean(true)); inverted.ExpireSolution(false); Solve(doc,c);
            string inverse=Mask(c,gridType);
            var indexOnly=new Param_GenericObject(); indexOnly.CreateAttributes(); doc.AddObject(indexOnly,false); indexOnly.AddSource(c.Params.Output[3]); Solve(doc,c);
            Check(Mask(c,gridType)==inverse && c.Params.Output[3].VolatileDataCount==inverse.Split(',').Length,kind+": inverted index-only output aligns");
            var voxelInput=(Param_GenericObject)c.Params.Input[0];
            voxelInput.PersistentData.Append(new GH_ObjectWrapper(field)); voxelInput.ExpireSolution(false); Solve(doc,c);
            Check(c.Params.Output[0].VolatileDataCount==2 && c.Message==VoxelCountMessage(c),kind+": message totals multiple output fields");
            inverted.PersistentData.Clear(); inverted.PersistentData.Append(new GH_Boolean(false)); inverted.ExpireSolution(false);
            SetNumber(c,2,1000); SetNumber(c,3,1001); Solve(doc,c);
            Check(c.Message=="Voxels: 0" && VoxelCountMessage(c)=="Voxels: 0",kind+": empty selection reports zero");
            voxelInput.PersistentData.Clear(); voxelInput.ExpireSolution(false); doc.NewSolution(false);
            Check(c.Message=="Voxels: 0",kind+": missing input clears old count");
            GH_Document.EnableSolutions=false;
        }
    }
    static void FunctionAndInclusionDemandChecks(Assembly main)
    {
        var gridType=main.GetType("Nuclei4.VoxelGridData",true);
        var data=gridType.GetMethod("CreateFullDomain").Invoke(null,new object[]{10,10,10,1.0});
        var field=Activator.CreateInstance(main.GetType("Nuclei4.VoxelField",true),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{data,null,null},null);
        foreach(string kind in new[]{"Voxel_PeriodicSurface","Voxel_MeshInclusion"})
        {
            using var doc=new GH_Document();
            var c=(GH_Component)Activator.CreateInstance(main.GetType("Nuclei4."+kind,true));
            c.CreateAttributes(); doc.AddObject(c,false);
            ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(field));
            if(kind=="Voxel_PeriodicSurface") {
                var choice=new Param_Integer(); choice.CreateAttributes(); doc.AddObject(choice,false); choice.PersistentData.Append(new GH_Integer(1)); c.Params.Input[1].AddSource(choice);
                var formula=new Param_String(); formula.CreateAttributes(); doc.AddObject(formula,false); formula.PersistentData.Append(new GH_String("x")); c.Params.Input[2].AddSource(formula);
                SetNumber(c,3,10); SetNumber(c,4,4.5);
            }
            else {
                var mesh=Rhino.Geometry.Mesh.CreateFromBox(new Rhino.Geometry.BoundingBox(1,1,1,8,8,8),1,1,1);
                ((Param_Mesh)c.Params.Input[1]).PersistentData.Append(new GH_Mesh(mesh));
            }
            GH_Document.EnableSolutions=true; doc.Enabled=true; Solve(doc,c);
            string expected=Mask(c,gridType); Check(expected.Length>0,kind+": nonempty initial voxels");
            for(int index=1;index<c.Params.Output.Count;index++) {
                var sink=new Param_GenericObject(); sink.CreateAttributes(); doc.AddObject(sink,false); sink.AddSource(c.Params.Output[index]);
                doc.NewSolution(false);
                Check(Mask(c,gridType)==expected,kind+": wiring alone preserves selection");
                Check(c.Params.Output[index].VolatileDataCount>0 && sink.VolatileDataCount==c.Params.Output[index].VolatileDataCount,kind+": wiring alone delivers output "+index);
                object cached=OutputField(c);
                var second=new Param_GenericObject(); second.CreateAttributes(); doc.AddObject(second,false); second.AddSource(c.Params.Output[index]); doc.NewSolution(false);
                Check(ReferenceEquals(cached,OutputField(c)) && second.VolatileDataCount==sink.VolatileDataCount,kind+": second consumer reuses cached output");
                sink.RemoveAllSources(); doc.RemoveObject(sink,false); doc.NewSolution(false);
                Check(ReferenceEquals(cached,OutputField(c)) && c.Params.Output[index].VolatileDataCount>0,kind+": remaining consumer keeps cached output");
                second.RemoveAllSources(); doc.RemoveObject(second,false); doc.NewSolution(false);
                Check(c.Params.Output[index].VolatileDataCount==0,kind+": last disconnect clears output without forced recompute");
            }
            if(kind=="Voxel_PeriodicSurface") {
                var voxelInput=(Param_GenericObject)c.Params.Input[0];
                voxelInput.PersistentData.Append(new GH_ObjectWrapper(field)); voxelInput.ExpireSolution(false); Solve(doc,c);
                Check(c.Params.Output[0].VolatileDataCount==2 && c.Message==VoxelCountMessage(c),"Function: message totals multiple output fields");
                SetNumber(c,4,1000000); Solve(doc,c);
                Check(c.Message=="Voxels: 0" && VoxelCountMessage(c)=="Voxels: 0","Function: empty selection reports zero");
                voxelInput.PersistentData.Clear(); voxelInput.ExpireSolution(false); doc.NewSolution(false);
                Check(c.Message=="Voxels: 0","Function: missing input clears old count");
            }
            // Removed components must not keep receiving document solution events.
            doc.RemoveObject(c,false);
            var watcher=c.GetType().GetField("outputDemand",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
            Check(watcher.GetType().GetField("document",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(watcher)==null,kind+": demand watcher detached on removal");
            GH_Document.EnableSolutions=false;
        }
    }
    static void FunctionPresetChecks(Assembly main,string output)
    {
        var type=main.GetType("Nuclei4.Voxel_PeriodicSurface",true);
        var names=(string[])type.GetField("Names",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        var values=(int[])type.GetField("Values",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        var gridType=main.GetType("Nuclei4.VoxelGridData",true);
        var data=gridType.GetMethod("CreateFullDomain").Invoke(null,new object[]{37,29,23,1.0});
        var field=Activator.CreateInstance(main.GetType("Nuclei4.VoxelField",true),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{data,null,null},null);
        using var doc=new GH_Document();
        var c=(GH_Component)Activator.CreateInstance(type); c.CreateAttributes(); doc.AddObject(c,false);
        ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(field));
        GH_Document.EnableSolutions=true; doc.Enabled=true; Solve(doc,c);
        var list=(GH_ValueList)c.Params.Input[1].Sources.Single();
        Check(list.ListItems.Select(i=>i.Name).SequenceEqual(names) && !names.Contains("Custom"),"generated list has every preset and no Custom");
        var masks=new List<string>();
        for(int i=0;i<names.Length;i++) {
            list.SelectItem(i); list.ExpireSolution(false); doc.NewSolution(false);
            string mask=Mask(c,gridType); masks.Add(mask);
            Check(c.Message==VoxelCountMessage(c) && mask.Length>0 && mask.Split(',').Length<24679,"dropdown evaluates "+names[i]);
        }
        // Connect/disconnect only: Grasshopper must collect the overriding formula in that solution.
        var formula=new Param_String(); formula.CreateAttributes(); doc.AddObject(formula,false); formula.PersistentData.Append(new GH_String("x"));
        c.Params.Input[2].AddSource(formula); doc.NewSolution(false);
        string customMask=Mask(c,gridType);
        Check(c.Message==VoxelCountMessage(c) && customMask!=masks.Last(),"connected Custom overrides a centered preset using original coordinates");
        list.SelectItem(0); list.ExpireSolution(false); doc.NewSolution(false);
        Check(c.Message==VoxelCountMessage(c) && Mask(c,gridType)==customMask,"preset changes do not override Custom");
        c.Params.Input[1].RemoveAllSources();
        var invalid=new Param_Integer(); invalid.CreateAttributes(); doc.AddObject(invalid,false); invalid.PersistentData.Append(new GH_Integer(999)); c.Params.Input[1].AddSource(invalid); doc.NewSolution(false);
        Check(!c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any() && Mask(c,gridType)==customMask,"valid Custom ignores invalid preset ID");
        invalid.PersistentData.Clear(); invalid.ExpireSolution(false); doc.NewSolution(false);
        Check(!c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any() && Mask(c,gridType)==customMask,"valid Custom ignores empty Surface");
        c.Params.Input[1].RemoveAllSources(); c.Params.Input[1].AddSource(list);
        foreach(string bad in new[]{"", "unknown(x)"}) {
            formula.PersistentData.Clear(); formula.PersistentData.Append(new GH_String(bad)); formula.ExpireSolution(false); doc.NewSolution(false);
            // Grasshopper can insert a null placeholder when an item output is unset.
            Check(c.Message=="Voxels: 0" && c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any() && c.Params.Output[0].VolatileData.AllData(true).All(v=>v==null || v.ScriptVariable()==null),"invalid connected Custom reports error instead of using preset");
        }
        c.Params.Input[2].RemoveAllSources(); doc.NewSolution(false);
        Check(c.Message==VoxelCountMessage(c) && Mask(c,gridType)==masks[0],"disconnecting Custom restores preset immediately");
        // Upgrade precisely the generated legacy list, retaining selected preset values.
        foreach(int oldSelection in new[]{4,7}) {
            list.ListItems.Clear();
            for(int i=0;i<7;i++)list.ListItems.Add(new GH_ValueListItem(names[i],(i+1).ToString()));
            list.ListItems.Add(new GH_ValueListItem("Custom","8")); list.SelectItem(oldSelection); list.ExpireSolution(false); doc.NewSolution(false);
            int expected=oldSelection==7?0:oldSelection;
            Check(list.ListItems.Select(i=>i.Expression).SequenceEqual(values.Select(v=>v.ToString())) && Mask(c,gridType)==masks[expected],"legacy dropdown upgrades and preserves choice "+oldSelection);
        }
        // The previous full catalog drops only IDs 13/17, retaining later IDs and choices.
        foreach(int oldValue in new[]{13,14,16,17,18}) {
            list.ListItems.Clear();
            foreach(int value in Enumerable.Range(1,18).Where(v=>v!=8)) {
                string name=value==13?"Chirped Labyrinth":value==17?"Blended Field":names[Array.IndexOf(values,value)];
                list.ListItems.Add(new GH_ValueListItem(name,value.ToString()));
            }
            list.SelectItem(list.ListItems.FindIndex(i=>i.Expression==oldValue.ToString())); list.ExpireSolution(false); doc.NewSolution(false);
            int expected=Array.IndexOf(values,oldValue); if(expected<0)expected=0;
            Check(list.ListItems.Select(i=>i.Expression).SequenceEqual(values.Select(v=>v.ToString())) && Mask(c,gridType)==masks[expected],"removed presets upgraded with stable selection for ID "+oldValue);
        }
        // Authored lists may intentionally contain arbitrary entries: don't rewrite them.
        list.ListItems.Clear(); list.ListItems.Add(new GH_ValueListItem("My gyroid","1")); list.SelectItem(0); list.ExpireSolution(false); doc.NewSolution(false);
        Check(list.ListItems.Count==1 && Mask(c,gridType)==masks[0],"user-authored value list preserved");
        GH_Document.EnableSolutions=false;
    }
    static object OutputField(GH_Component c) => ((GH_ObjectWrapper)c.Params.Output[0].VolatileData.AllData(true).Single()).Value;
    static string VoxelCountMessage(GH_Component c) => "Voxels: " + c.Params.Output[0].VolatileData.AllData(true)
        .OfType<GH_ObjectWrapper>().Where(w=>w.Value!=null)
        .Sum(w=>Convert.ToInt64(w.Value.GetType().GetProperty("ActiveCount").GetValue(w.Value))).ToString(System.Globalization.CultureInfo.InvariantCulture);
    static void SetNumber(GH_Component c,int index,double value) { var p=(Param_Number)c.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(value)); p.ExpireSolution(false); }
    static void Solve(GH_Document doc,GH_Component c) { c.ExpireSolution(false); doc.NewSolution(false); Check(!c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(),c.Name+": solve without errors"); }
    static string Mask(GH_Component c,Type gridType) {
        var wrapper=(GH_ObjectWrapper)c.Params.Output[0].VolatileData.AllData(true).Single();
        var field=wrapper.Value; var data=field.GetType().GetProperty("Data",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);
        int count=(int)gridType.GetField("ActiveCount").GetValue(data);
        if(c.GetType().Name.StartsWith("Voxel_Attractor_") || c.GetType().Name=="Voxel_PeriodicSurface")
            Check(c.Message==VoxelCountMessage(c),c.Name+": message matches active output count");
        return string.Join(",",Enumerable.Range(0,count).Select(i=>gridType.GetMethod("ActiveFlatIndexAt").Invoke(data,new object[]{i})));
    }
    static void Check(bool condition,string label) { if(!condition)throw new Exception(label); Console.WriteLine("PASS: "+label); }
}
