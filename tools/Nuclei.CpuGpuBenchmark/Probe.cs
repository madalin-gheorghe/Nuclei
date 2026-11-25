using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Nuclei3;
using Rhino.Geometry;
using Particle = Nuclei3.Particle;

internal static class Probe
{
    const BindingFlags F = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static T Get<T>(object o,string n)=>(T)o.GetType().GetField(n,F).GetValue(o);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(o,a);
    static uint Hash(uint x){x^=x>>16;x*=0x7feb352d;x^=x>>15;x*=0x846ca68b;return x^(x>>16);}
    public static int Run(string[] args)
    {
        int Int(string n,int d)=>int.Parse(Program.Option(args,n)??d.ToString());
        int x=Int("--grid",64),y=x,z=args.Contains("--2d")?1:x,count=Int("--particles",1500),warm=Int("--warmup",300),steps=Int("--steps",30),repeats=Int("--repeats",5);
        bool ant=args.Contains("--ants"),gpu=args.Contains("--gpu"); double scale=double.Parse(Program.Option(args,"--scale")??"1",System.Globalization.CultureInfo.InvariantCulture);
        int cells=checked(x*y*z); if(count>cells)throw new ArgumentException("Too many particles");
        var density=new float[cells];var food=new float[cells];var pos=new float[count*3];var dir=new float[count*3];var axes=new float[count*3];var homes=new float[count*3];var parents=new int[count];var ages=Enumerable.Repeat(20,count).ToArray();
        for(int i=0;i<cells;i++)density[i]=.1f+(Hash((uint)i)&255)*.001f;
        for(int i=0;i<8;i++)food[(int)((long)(i+1)*cells/9)]=ant?100f:.04f;
        int multiplier=7919;int Gcd(int a,int b){while(b!=0){int t=a%b;a=b;b=t;}return a;}while(Gcd(multiplier,cells)!=1)multiplier++;
        for(int i=0;i<count;i++){
            int cell=(int)(((long)i*multiplier+104729)%cells),p=i*3;parents[i]=cell;
            pos[p]=cell/(y*z)+.5f;pos[p+1]=cell/z%y+.5f;pos[p+2]=cell%z+.5f;
            double t=(Hash((uint)i^0x12345678)&0xffffff)*(2*Math.PI/16777216),dz=z==1?0:(Hash((uint)i^0x9e3779b9)&0xffffff)*(2.0/16777216)-1,r=Math.Sqrt(1-dz*dz);
            dir[p]=(float)(Math.Cos(t)*r);dir[p+1]=(float)(Math.Sin(t)*r);dir[p+2]=(float)dz;axes[p]=(float)-Math.Sin(t);axes[p+1]=(float)Math.Cos(t);
            homes[p]=x*.5f;homes[p+1]=y*.5f;homes[p+2]=z*.5f;
        }
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var a in new[]{density,food,pos,dir,axes,homes})digest.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(a.AsSpan()));
        string fixtureHash=Convert.ToHexString(digest.GetHashAndReset());
        Action<int> advance;Action fence=()=>{};Func<int> population;Func<double[]> field; Func<float[]> positions; IDisposable engine=null;string binary;object solverObject=null;
        if(!gpu){
            var solver=new Solver();solverObject=solver;Set(solver,"settings",new List<string>());Call(solver,"readSolverSettings");var asm=typeof(Solver).Assembly;
            object data=asm.GetType("Nuclei3.VoxelGridData").GetMethod("CreateFullDomain",F).Invoke(null,new object[]{x,y,z,1d});
            object Map(float[] values)=>Activator.CreateInstance(asm.GetType("Nuclei3.VoxelScalarMap"),new object[]{0d,values.Select(v=>(double)v).ToArray()});
            Set(data,"Density",Map(ant?new float[cells]:density));Set(data,ant?"AntFood":"Food",Map(food));
            var shell=new Voxel[x,y,z];asm.GetType("Nuclei3.VoxelGridRegistry").GetMethod("Set",F).Invoke(null,new[]{shell,data});
            Set(solver,"inputVoxels",shell);Set(solver,"wrapBoundaries",true);Call(solver,"inheritVoxels");Call(solver,"precomputeAngles");Call(solver,"createWanderVectors");
            Set(solver,"slimeParticles",!ant);Set(solver,"antParticles",ant);Set(solver,"diffuse",.1d);Set(solver,"decay",.01d);Set(solver,"diffuseRange",1);Set(solver,"diffusionGradual",1d);
            Set(solver,"diffuseRange_Ant",1);Set(solver,"foodDiffuseRate",.1d);Set(solver,"baseDiffuseRate",.1d);Set(solver,"foodDecayRate",.0001d);Set(solver,"baseDecayRate",.0005d);Set(solver,"antDiffusionGradual",1d);
            var vox=Get<Voxel[]>(solver,"voxelFlat");if(ant)for(int i=0;i<cells;i++){vox[i].towardsFoodPheromone=density[i];vox[i].towardsBasePheromone=density[i];}
            var group=new ParticleGroup{ant=ant,speed=.75*scale,sensorDistance=3*scale,sensorAngle=45,rotationAngle=45,depositValue=1,baseWanderFrequency=0,wanderFrequency=0};
            var particles=new List<Particle>();for(int i=0;i<count;i++){int p=i*3;var plane=new Plane(new Point3d(pos[p],pos[p+1],pos[p+2]),new Vector3d(dir[p],dir[p+1],dir[p+2]),new Vector3d(axes[p],axes[p+1],axes[p+2]));particles.Add(new Particle(plane){parentParticleGroup=group,parentVoxel=vox[parents[i]],home=new Plane(new Point3d(homes[p],homes[p+1],homes[p+2]),plane.XAxis,plane.YAxis),age=20,moveVector=plane.XAxis});}
            group.particles=particles;Set(solver,"particles",particles);Set(solver,"particleGroups",new List<ParticleGroup>{group});Set(solver,"random",new Random(89));Call(solver,"particleCheckParentVoxel");
            advance=i=>{Set(solver,"iteration",i);Call(solver,"particleSenseValuesAndVectors",0L,0L);if(ant)Call(solver,"particleSense_Ant");Call(solver,"particleMoveAndDeposit",0L,0L);Call(solver,"particleCheckParentVoxel");Call(solver,"projectFoodSources");Call(solver,"diffuseVoxels");};
            positions=()=>particles.SelectMany(p=>new[]{(float)p.pPlane.Origin.X,(float)p.pPlane.Origin.Y,(float)p.pPlane.Origin.Z}).ToArray(); population=()=>Get<List<Particle>>(solver,"particles").Count;field=()=>vox.Select(v=>ant?v.towardsFoodPheromone:v.density).ToArray();binary=asm.Location;
        }else{
            Type helper=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath("tools/Nuclei.GpuResearchProbe/bin/Release/net8.0-windows/Nuclei.GpuResearchProbe.dll")).GetType("Program");
            object Invoke(string n,params object[] a)=>helper.GetMethod(n,F).Invoke(null,a);
            string root=Path.GetFullPath("Nuclei-v4/Nuclei4/bin/Release/net7.0-windows");Invoke("Load",root);
            Type T(string n)=>(Type)Invoke("RequiredType",n);object Make(string n)=>Activator.CreateInstance(T(n),true);
            object input=Make("Nuclei4.GpuSolverInput"),settings=Make("Nuclei4.SolverGpuSettings");
            Set(input,"ResX",x);Set(input,"ResY",y);Set(input,"ResZ",z);Set(input,"VoxelSize",1f);Set(input,"ParticleCount",count);Set(input,"GroupCount",1);Set(input,"HasAntParticles",ant);Set(input,"HasSlimeParticles",!ant);Set(input,"ProcessDensity",!ant);
            Set(input,"VoxelDensity",ant?new float[cells]:density);Set(input,ant?"InitialAntFood":"InitialFood",food);if(ant){Set(input,"AntFoodPheromone",density);Set(input,"AntBasePheromone",(float[])density.Clone());}
            Set(input,"ParticlePositionsXyz",pos);Set(input,"ParticleDirectionsXyz",dir);Set(input,"ParticleYAxesXyz",axes);Set(input,"ParticleHomesXyz",homes);Set(input,"ParticleParentIndices",parents);Set(input,"ParticleAges",ages);Set(input,"ParticleGroupIndices",new int[count]);Set(input,"ParticleAntStates",new uint[count]);Set(input,"ParticleAntLaunchBoundaryStates",new uint[count]);
            Set(input,"GroupData0",new[]{(float)(.75*scale),(float)(3*scale),MathF.PI/4,0f});Set(input,"GroupData1",new[]{MathF.PI/4,ant?1f:0f,1f,0f});Set(input,"GroupColorData",new[]{1f,.5f,.1f,1f});
            Set(settings,"WrapBoundaries",true);Set(settings,"Diffuse",.1d);Set(settings,"Decay",.01d);Set(settings,"DiffuseRange",1);Set(settings,"DiffusionGradual",1d);Set(settings,"AntDiffuseRange",1);Set(settings,"AntFoodDiffuse",.1d);Set(settings,"AntBaseDiffuse",.1d);Set(settings,"AntFoodDecay",.0001d);Set(settings,"AntBaseDecay",.0005d);Set(settings,"AntDiffusionGradual",1d);
            object sink=RuntimeHelpers.GetUninitializedObject(T("Nuclei4.Gh1GpuSolverOutputSink"));engine=(IDisposable)T("Nuclei4.GpuFullSlimeSolverEngine").GetConstructors(F).Single(c=>c.GetParameters().Length==8).Invoke(new[]{input,sink,settings,false,false,false,0,1});
            object capabilities=engine.GetType().GetProperty("Capabilities").GetValue(engine);if(Get<bool>(capabilities,"SoftwareFallback"))throw new Exception("Software GPU fallback");
            object dim=T("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",F).Invoke(null,new object[]{x,y,z});
            advance=i=>Call(engine,"Step",settings,dim,i,false,false,false);population=()=>(int)Call(engine,"SynchronizeActiveParticleCount");fence=()=>population();
            positions=()=>{Call(engine,"ReadBackParticles");return Get<float[]>(engine,"particlePositionReadback");};field=()=>{Call(engine,ant?"ReadBackAntFields":"ReadBackDensity");return Get<float[]>(engine,ant?"antFoodReadback":"densityReadback").Select(v=>(double)v).ToArray();};binary=Path.Combine(root,"Nuclei4.Gpu.D3D11.dll");
        }
        try{
            if(population()!=count)throw new Exception("Initial population mismatch");
            for(int i=1;i<=warm;i++){advance(i);if(i%100==0)Console.WriteLine($"WARM {i}/{warm}");}fence();
            var samples=new List<double>();for(int r=0;r<repeats;r++){var watch=Stopwatch.StartNew();for(int j=1;j<=steps;j++)advance(warm+r*steps+j);fence();watch.Stop();samples.Add(watch.Elapsed.TotalMilliseconds/steps);Console.WriteLine($"SAMPLE {r+1}: {samples[^1]:F4} ms/step; population={population()}");}
            int finalCount=population();if(finalCount!=count)throw new Exception($"Population changed {count}->{finalCount}");var finalPositions=positions();int moved=Enumerable.Range(0,count).Count(i=>Math.Abs(finalPositions[i*3]-pos[i*3])+Math.Abs(finalPositions[i*3+1]-pos[i*3+1])+Math.Abs(finalPositions[i*3+2]-pos[i*3+2])>.001);if(moved<count/2)throw new Exception("Most particles did not move");var values=field();if(values.Any(v=>!double.IsFinite(v))||values.Max()<=0)throw new Exception("Invalid/empty field");
            var result=new{Backend=gpu?"GPU":"CPU",Species=ant?"ants":"slime",X=x,Y=y,Z=z,Particles=count,FinalParticles=finalCount,MovedParticles=moved,Scale=scale,Speed=.75*scale,SensorDistance=3*scale,Warmup=warm,Steps=steps,Repeats=repeats,FixtureSha256=fixtureHash,Binary=binary,BinarySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary))),MillisecondsPerStep=samples,MedianMs=samples.Order().ElementAt(samples.Count/2),FieldMean=values.Average(),FieldMaximum=values.Max(),Scope="completed simulation batches; no previews, outputs, dynamic population; CPU default parallelism",Timestamp=DateTimeOffset.Now};
            File.WriteAllText(Program.Option(args,"--json")??"benchmark.json",JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        }finally{engine?.Dispose();}
        return 0;
    }
}


