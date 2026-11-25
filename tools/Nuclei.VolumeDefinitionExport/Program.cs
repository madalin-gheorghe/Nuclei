using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using GH_IO.Serialization;
using Rhino.Runtime.InProcess;
using Vortice.Direct3D11;
using Vortice.DXGI;

internal static class Program
{
    internal const string RhinoSystem = @"C:\Program Files\Rhino 9 WIP\System";
    internal const string GhRoot = @"C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper";
    internal const string Gha = @"C:\Users\madal\AppData\Roaming\Grasshopper\Libraries\Nuclei4\Nuclei4.gha";
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
    [STAThread] static int Main(string[] args)
    {
        int status = 1;
        try
        {
            var roots = new[] { Path.Combine(RhinoSystem,"netcore"), GhRoot, Path.GetDirectoryName(Gha), RhinoSystem };
            SetDllDirectory(RhinoSystem);
            Environment.SetEnvironmentVariable("PATH", string.Join(';', roots)+";"+Environment.GetEnvironmentVariable("PATH"));
            AssemblyLoadContext.Default.Resolving += (c,n) => roots.SelectMany(r=>new[]{Path.Combine(r,n.Name+".dll"),Path.Combine(r,n.Name+".gha")}).Where(File.Exists).Select(c.LoadFromAssemblyPath).FirstOrDefault();
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(RhinoSystem,"netcore","RhinoCommon.dll"));
            new Thread(()=> { Thread.Sleep(600000); Console.Error.WriteLine("Export watchdog expired"); TerminateProcess(new IntPtr(-1),3); }) { IsBackground=true }.Start();
            typeof(Program).Assembly.GetType("RhinoStage").GetMethod("Run").Invoke(null,new object[]{args});
            status=0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); }
        Console.Out.Flush(); Console.Error.Flush();
        TerminateProcess(new IntPtr(-1),(uint)status);
        return status;
    }
}

internal static class RhinoStage
{
    public static void Run(string[] args)
    {
        Console.WriteLine("Starting isolated RhinoCore");
        var core = new RhinoCore(new[]{"/safemode","/nosplash","/notemplate"},WindowStyle.NoWindow);
        Console.WriteLine("RhinoCore ready: "+Rhino.RhinoApp.Version);
        typeof(Program).Assembly.GetType("ExportStage").GetMethod("Run").Invoke(null,new object[]{args});
        GC.KeepAlive(core);
    }
}

internal static class ExportStage
{
    const BindingFlags All = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Field(object value,string name) => value.GetType().GetField(name,All).GetValue(value);
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void Json(string path,object value) => File.WriteAllText(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    static string Item(XElement e,string name) => e.Element("items")?.Elements("item").SingleOrDefault(x=>(string)x.Attribute("name")==name)?.Value;
    public static void Run(string[] args)
    {
        string source=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]);
        int steps=args.Length>2?int.Parse(args[2]):200;
        if(Directory.Exists(output)) throw new IOException("Output exists: "+output);
        Directory.CreateDirectory(output);
        string originalHash=Hash(source);
        File.Copy(source,Path.Combine(output,Path.GetFileName(source)));
        var profile=Path.Combine(output,"isolated-profile"); Directory.CreateDirectory(Path.Combine(profile,"Libraries"));
        typeof(Grasshopper.Folders).GetField("m_appdataFolder",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,profile+Path.DirectorySeparatorChar);
        Console.WriteLine("Loading isolated component server");
        var server=Grasshopper.Instances.ComponentServer;
        var load=server.GetType().GetMethods(All).Single(m=>m.Name=="LoadGHA" && m.GetParameters().Length==2 && m.GetParameters()[0].ParameterType==typeof(GH_ExternalFile));
        load.Invoke(server,new object[]{new GH_ExternalFile(Program.Gha),false});
        Console.WriteLine("Loaded Nuclei: "+Hash(Program.Gha));

        var archive=new GH_Archive(); if(!archive.ReadFromFile(source)) throw new IOException("Archive read failed");
        var xml=XDocument.Parse(archive.Serialize_Xml());
        var objects=xml.Descendants("chunk").Where(e=>(string)e.Attribute("name")=="Object").ToArray();
        XElement Container(XElement e)=>e.Element("chunks").Elements("chunk").Single(x=>(string)x.Attribute("name")=="Container");
        var owner=new Dictionary<string,XElement>();
        foreach(var o in objects) foreach(var id in Container(o).Descendants("item").Where(e=>(string)e.Attribute("name")=="InstanceGuid")) owner[id.Value]=o;
        var retained=new HashSet<XElement>();
        void Keep(XElement o) { if(!retained.Add(o)) return; foreach(var wire in Container(o).Descendants("item").Where(e=>(string)e.Attribute("name")=="Source")) { if(!owner.TryGetValue(wire.Value,out var upstream)) throw new Exception("Unresolved original wire "+wire.Value); Keep(upstream); } }
        Keep(objects.Single(o=>Item(Container(o),"InstanceGuid")=="775fe0d2-5ebe-4b85-82b0-1d7f38732e81"));
        Keep(objects.Single(o=>Item(Container(o),"InstanceGuid")=="8556110e-65cb-45d8-8f8e-df992fd08398"));
        var removed=objects.Where(o=>!retained.Contains(o)).Select(o=>new{Index=(string)o.Attribute("index"),Name=Item(o,"Name"),InstanceId=Item(Container(o),"InstanceGuid")}).ToArray();
        var list=objects[0].Parent; foreach(var o in objects.Where(o=>!retained.Contains(o))) o.Remove();
        int index=0; foreach(var o in list.Elements("chunk")) o.SetAttributeValue("index",index++);
        list.SetAttributeValue("count",index);
        list.Parent.Element("items").Elements("item").Single(e=>(string)e.Attribute("name")=="ObjectCount").Value=index.ToString();
        // Only omit downstream mesh/trail consumers, annotations and scheduler; every solver/voxel-preview ancestor remains byte-identical XML.
        var filtered=new GH_Archive(); if(!filtered.Deserialize_Xml(xml.ToString())) throw new Exception("Filtered archive invalid");
        string filteredPath=Path.Combine(output,"isolated-render-path.gh"); if(!filtered.WriteToFile(filteredPath,false,false)) throw new Exception("Filtered archive write failed");
        Console.WriteLine("Opening exact upstream graph: "+retained.Count+" objects");
        GH_Document.EnableSolutions=false;
        var io=new GH_DocumentIO(); if(!io.Open(filteredPath)) throw new Exception("GH open failed");
        var doc=io.Document;
        var solver=(GH_Component)doc.Objects.Single(o=>o.InstanceGuid.ToString()=="775fe0d2-5ebe-4b85-82b0-1d7f38732e81");
        var preview=(GH_Component)doc.Objects.Single(o=>o.InstanceGuid.ToString()=="8556110e-65cb-45d8-8f8e-df992fd08398");
        bool savedPreviewHidden=preview.Hidden;
        // Rendering is the requested test: enable this preview only in the unsaved test document.
        preview.Hidden=false;
        if(Path.GetFullPath(solver.GetType().Assembly.Location)!=Path.GetFullPath(Program.Gha)) throw new Exception("Wrong GHA resolved");
        var reset=(GH_BooleanToggle)solver.Params.Input[0].Sources.Single();
        foreach(var o in doc.Objects.Where(o=>o.GetType().Name.Contains("Timer")||o.GetType().Name.Contains("Trigger"))) if(o is IGH_ActiveObject a) a.Locked=true;
        GH_Document.EnableSolutions=true; doc.Enabled=true;
        reset.Value=true; reset.ExpireSolution(false);
        Console.WriteLine("Resetting saved 250-cubed graph"); doc.NewSolution(false);
        Check(solver); Check(preview);
        reset.Value=false; reset.ExpireSolution(false);
        for(int i=1;i<=steps;i++) { solver.ExpireSolution(false); doc.NewSolution(false); Check(solver); if(i%20==0) {Console.WriteLine("Step "+i+": "+solver.Message); Console.Out.Flush();} }
        object frame=preview.GetType().GetMethod("GetGpuDensityFieldPreviewFrame",All).Invoke(preview,null) ?? throw new Exception("No volume preview frame");
        object engine=Field(solver,"fullSolverEngine");
        int iteration=(int)Field(solver,"iteration"); if(iteration!=steps) throw new Exception("Unexpected iteration "+iteration);
        var frameFields=frame.GetType().GetFields(All).Where(f=>f.FieldType.IsPrimitive && f.FieldType!=typeof(IntPtr)).ToDictionary(f=>f.Name,f=>f.GetValue(frame));
        Json(Path.Combine(output,"style.json"),frameFields);
        byte[] density=Read(engine,frame,false), gradient=Read(engine,frame,true);
        File.WriteAllBytes(Path.Combine(output,"density.raw"),density); File.WriteAllBytes(Path.Combine(output,"gradient.raw"),gradient);
        frameFields["TestName"]="v4-15_3D-Intro-step-"+iteration; frameFields["Pattern"]="Actual Grasshopper solver snapshot"; frameFields["Format"]="R32G32B32A32_Float"; frameFields["Channels"]=4;
        Json(Path.Combine(output,"volume-metadata.json"),frameFields);
        var parameters=doc.Objects.OfType<GH_Component>().Select(c=>new{Type=c.GetType().FullName,c.InstanceGuid,c.Message,Inputs=c.Params.Input.Select(p=>new {p.Name,Values=p.VolatileData.AllData(true).Select(v=>v.ToString()).Take(30).ToArray(),p.SourceCount}).ToArray()}).ToArray();
        if(Hash(source)!=originalHash) throw new Exception("Source changed on disk");
        Json(Path.Combine(output,"provenance.json"),new{Passed=true,Source=source,SourceSha256=originalHash,Gha=Program.Gha,GhaSha256=Hash(Program.Gha),RhinoVersion=Rhino.RhinoApp.Version.ToString(),Iteration=iteration,SavedPreviewHidden=savedPreviewHidden,TestPreviewHidden=preview.Hidden,ExcludedObjects=removed,RetainedObjectCount=retained.Count,Parameters=parameters,DensitySha256=Hash(Path.Combine(output,"density.raw")),GradientSha256=Hash(Path.Combine(output,"gradient.raw")),Notes="Original file is unchanged. In-memory test pathway retains every original upstream node/wire/setting of SolverGPU and Voxel Preview, omitting downstream mesh/trail consumers and scheduling. Reset and explicit steps replace timer; preview visibility enabled only in test; frozen GPU snapshot exported before renderer benchmark."});
        Console.WriteLine("EXPORT PASSED: "+output);
    }
    static void Check(GH_Component c) { var errors=c.RuntimeMessages(GH_RuntimeMessageLevel.Error); if(errors.Count>0) throw new Exception(c.Name+": "+string.Join(" | ",errors)); }
    static byte[] Read(object engine,object frame,bool gradient)
    {
        var device=(ID3D11Device)Field(engine,"device"); var context=(ID3D11DeviceContext)Field(engine,"context");
        using var source=device.OpenSharedResource<ID3D11Texture2D>((IntPtr)Field(frame,gradient?"GradientSharedHandle":"SharedHandle"));
        var d=source.Description; if(d.Format!=(gradient?Format.R16G16B16A16_Float:Format.R32G32B32A32_Float)) throw new Exception("Unexpected format "+d.Format);
        using var staging=device.CreateTexture2D(new Texture2DDescription(d.Format,d.Width,d.Height,1,1,BindFlags.None,ResourceUsage.Staging,CpuAccessFlags.Read,1,0,ResourceOptionFlags.None));
        context.CopyResource(staging,source); var mapped=context.Map(staging,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
        try { int rowBytes=d.Width*(gradient?8:16); var bytes=new byte[rowBytes*d.Height]; for(int row=0;row<d.Height;row++) Marshal.Copy(mapped.DataPointer+row*mapped.RowPitch,bytes,row*rowBytes,rowBytes); return bytes; }
        finally { context.Unmap(staging,0); }
    }
}
