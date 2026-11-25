using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Nuclei3;
using Rhino.Geometry;
using Particle = Nuclei3.Particle;

internal static partial class Probe
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<object> Results = new();
    static readonly int[][] Orders = { new[] {0,1,2}, new[] {1,2,0}, new[] {2,0,1}, new[] {2,1,0}, new[] {1,0,2}, new[] {0,2,1} };
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Flags).SetValue(obj, value);
    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Flags).GetValue(obj);
    static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethods(Flags).Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(obj, args);
    static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
    static void Near(double actual, double expected, string message, double tolerance = 1e-12) => Check(Math.Abs(actual - expected) <= tolerance, $"{message}: {actual:R} != {expected:R}");
    sealed record Shape(string Name, int X, int Y, int Z);
    static readonly Shape[] Shapes = { new("3d",19,13,9), new("xy",19,13,1), new("xz",19,1,13), new("yz",1,19,13) };

    public static int Run(string[] args)
    {
        string assemblyPath=typeof(Solver).Assembly.Location;
        Results.Add(new{Test="assembly",Path=assemblyPath,Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assemblyPath)))});
        if (args.Contains("--benchmark")) Benchmark(args);
        else if (args.Contains("--gpu-parity")) GpuParity(args);
        else if (args.Contains("--nest-only")) { ActualFoodTests(); NestTests(); TrailTests(); DepartureTests(); MovementTests(); ComponentTests(); }
        else
        {
            AntSimdProductionTests();
            FieldTests();
            BehaviorTests();
            ActualFoodTests();
            MovementTests();
            NestTests(); TrailTests(); DepartureTests();
            ComponentTests();
            Console.WriteLine("PASS all V3 ant regression checks.");
        }
        string json = Program.Option(args, "--json");
        if (json != null) File.WriteAllText(json, JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    static Solver Create(Shape shape, bool wrap, int range, bool sparse, double foodRate = .17, double baseRate = .31)
    {
        Solver solver = (Solver)RuntimeHelpers.GetUninitializedObject(typeof(Solver));
        int count = shape.X * shape.Y * shape.Z;
        var flat = new Voxel[count]; var grid = new Voxel[shape.X, shape.Y, shape.Z]; var active = new List<Voxel>();
        for (int x = 0, i = 0; x < shape.X; x++) for (int y = 0; y < shape.Y; y++) for (int z = 0; z < shape.Z; z++, i++)
        {
            if (sparse && i % 11 == 0) continue;
            var v = new Voxel(1, x, y, z) { flatIndex = i, density = .2 + (i % 7) * .01, towardsFoodPheromone = (i % 41) * .04, towardsBasePheromone = (i % 29) * .05 };
            v.boundary = !wrap && Boundary(v, shape);
            if (sparse)
            {
                if (i % 23 == 0) v.maxDensity = 0;
                else if (i % 17 == 0) v.maxDensity = .35;
                if (i % 19 == 0) v.minDensity = .17;
            }
            flat[i] = grid[x,y,z] = v; active.Add(v);
        }
        Set(solver,"resX",shape.X); Set(solver,"resY",shape.Y); Set(solver,"resZ",shape.Z);
        Set(solver,"dimX",(double)shape.X); Set(solver,"dimY",(double)shape.Y); Set(solver,"dimZ",(double)shape.Z);
        Set(solver,"voxelSize",1.0); Set(solver,"voxelSizeInverse",1.0);
        Set(solver,"voxelStrideX",shape.Y*shape.Z); Set(solver,"voxelStrideY",shape.Z);
        Set(solver,"voxels",grid); Set(solver,"voxelFlat",flat); Set(solver,"activeVoxels",active.ToArray());
        Set(solver,"planarXY",shape.Z==1); Set(solver,"planarXZ",shape.Y==1); Set(solver,"planarYZ",shape.X==1);
        Set(solver,"tridimensional",shape.X>1&&shape.Y>1&&shape.Z>1); Set(solver,"wrapBoundaries",wrap);
        Set(solver,"antParticles",true); Set(solver,"slimeParticles",false); Set(solver,"denseVoxelGrid",!sparse);
        Set(solver,"diffuseRange_Ant",range); Set(solver,"foodDiffuseRate",foodRate); Set(solver,"baseDiffuseRate",baseRate);
        Set(solver,"foodDecayRate",.031); Set(solver,"baseDecayRate",.073); Set(solver,"decay",.01);
        Set(solver,"diffuse",.15); Set(solver,"diffuseRange",1); Set(solver,"diffusionGradual",1.0); Set(solver,"antDiffusionGradual",1.0);
        Set(solver,"reusableDiffusionAxes",new int[3]); Set(solver,"iteration",1);
        Set(solver,"scalarVoxelDensity",new double[count]); Set(solver,"scalarVoxelScratch",new double[count]);
        Set(solver,"foodSourceValues",new double[count]); Set(solver,"random",new Random(89));
        Set(solver,"particles",new List<Particle>()); Set(solver,"wanderVectors",new List<Vector3d>{new(0,1,0)});
        Set(solver,"particleCountTouchedVoxels",Array.Empty<Voxel>());Set(solver,"particleOccupancyRejections",Array.Empty<Particle>());
        Call(solver,"precomputeAngles");
        return solver;
    }

    static bool Boundary(Voxel v, Shape s) => (s.X>1&&(v.idX==0||v.idX==s.X-1))||(s.Y>1&&(v.idY==0||v.idY==s.Y-1))||(s.Z>1&&(v.idZ==0||v.idZ==s.Z-1));
    static double Clamp(double value, Voxel v) { value=Math.Min(1,value); if(v.maxDensity>=0)value=Math.Min(value,v.maxDensity); if(v.minDensity>=0)value=Math.Max(value,v.minDensity); return value; }
    static double[] Weights(int range, double falloff = 0)
    {
        double[] w=Enumerable.Range(-range,range*2+1).Select(i=>(1+Math.Cos(Math.PI*i/(range+1)))*.5).ToArray();
        w=w.Select(v=>Math.Pow(v,1-falloff)).ToArray();
        double sum=w.Sum(); return w.Select(v=>v/sum).ToArray();
    }
    // Independent full-grid snapshots provide the expected Jacobi result. They
    // intentionally do not use the production line cache or paired traversal.
    static double[] Reference(double[] input, Voxel[] voxels, Shape s, int[] axes, int range, bool wrap, double rate, double decay, bool food, double falloff = 0)
    {
        double[] values=(double[])input.Clone(); double[] weights=Weights(range,falloff);
        double strength=falloff+(1-falloff)*rate, retention=1-rate*falloff;
        if(rate>0 || falloff>0) foreach(int axis in axes)
        {
            double[] next=new double[values.Length];
            for(int i=0;i<values.Length;i++)
            {
                Voxel v=voxels[i]; if(v==null)continue; double sum=0;
                for(int offset=-range;offset<=range;offset++)
                {
                    int x=v.idX+(axis==0?offset:0), y=v.idY+(axis==1?offset:0), z=v.idZ+(axis==2?offset:0);
                    if(wrap){x=(x%s.X+s.X)%s.X;y=(y%s.Y+s.Y)%s.Y;z=(z%s.Z+s.Z)%s.Z;}
                    if(x<0||x>=s.X||y<0||y>=s.Y||z<0||z>=s.Z)continue;
                    int j=(x*s.Y+y)*s.Z+z; Voxel n=voxels[j];
                    if(n!=null&&!(n.maxDensity>=0&&n.maxDensity<.01))sum+=values[j]*weights[offset+range];
                }
                double scale=axis==axes.Last()?retention:1;
                next[i]=!wrap&&Boundary(v,s)?0:Clamp(values[i]*((1-strength)*scale)+sum*(strength*scale),v);
            }
            values=next;
        }
        for(int i=0;i<values.Length;i++)if(voxels[i]!=null)values[i]=food&&!wrap&&Boundary(voxels[i],s)?0:Math.Max(0,values[i]-decay);
        return values;
    }

    static void FieldTests()
    {
        int cases=0;
        foreach(Shape shape in Shapes)foreach(bool wrap in new[]{false,true})foreach(int range in new[]{0,1,2,5,17})foreach(bool sparse in new[]{false,true})
        foreach(var rates in new[]{(.17,.31),(0.0,.31),(.17,0.0),(0.0,0.0)})
        foreach(double falloff in new[]{0.0,.4,1.0})
        {
            Solver solver=Create(shape,wrap,range,sparse,rates.Item1,rates.Item2);
            Set(solver,"antDiffusionGradual",1-falloff);
            Voxel[] flat=Get<Voxel[]>(solver,"voxelFlat");
            double[] food=flat.Select(v=>v?.towardsFoodPheromone??0).ToArray(), home=flat.Select(v=>v?.towardsBasePheromone??0).ToArray();
            double[] scalar=flat.Select(v=>v?.density??0).ToArray();
            for(int step=1;step<=6;step++)
            {
                Set(solver,"iteration",step);
                int[] axes=shape.Name=="3d"?Orders[step-1]:Enumerable.Range(0,3).Where(a=>(a==0?shape.X:a==1?shape.Y:shape.Z)>1).ToArray();
                if(shape.Name!="3d"&&(step&1)==0)Array.Reverse(axes);
                food=Reference(food,flat,shape,axes,range,wrap,rates.Item1,.031,true,falloff);
                home=Reference(home,flat,shape,axes,range,wrap,rates.Item2,.073,false,falloff);
                Call(solver,"diffuseVoxels");
                foreach(Voxel v in flat.Where(v=>v!=null))
                {
                    string label=$"{shape.Name} wrap={wrap} r={range} sparse={sparse} rates={rates} step={step} voxel={v.flatIndex}";
                    Near(v.towardsFoodPheromone,food[v.flatIndex],"food "+label);
                    Near(v.towardsBasePheromone,home[v.flatIndex],"base "+label);
                    Near(v.density,scalar[v.flatIndex],"ant-only scalar "+label,0);
                }
            }
            cases++;
        }
        Console.WriteLine($"PASS {cases} field fixtures x6 axis orders: 3D/XY/XZ/YZ, independent rates, radius0..17, repeated wrap, sparse holes, blocked voxels, limits, decay and static ant-only scalar.");
        Results.Add(new { Test="fields", Cases=cases, Steps=6, Passed=true });
    }

    static void BehaviorTests()
    {
        foreach(Shape oldShape in Shapes)
        {
            Shape shape=new(oldShape.Name,oldShape.X==1?1:31,oldShape.Y==1?1:31,oldShape.Z==1?1:31);
            Solver solver=Create(shape,true,2,false); Voxel[] flat=Get<Voxel[]>(solver,"voxelFlat");
            foreach(Voxel v in flat){v.towardsFoodPheromone=0;v.towardsBasePheromone=0;v.density=0;}
            Vector3d forward=shape.X==1?Vector3d.YAxis:Vector3d.XAxis, side=shape.Z==1?Vector3d.YAxis:Vector3d.ZAxis;
            if(shape.Name=="3d")side=Vector3d.YAxis;
            var origin=new Point3d(shape.X==1?.5:15.5,shape.Y==1?.5:15.5,shape.Z==1?.5:15.5);
            var group=new ParticleGroup{ant=true,speed=.5,sensorDistance=4,sensorAngle=45,rotationAngle=45,baseWanderFrequency=7};
            var p=new Particle(new Plane(origin,forward,side)){parentParticleGroup=group,home=new Plane(origin-forward*10,forward,side),age=10};
            p.parentVoxel=flat[((int)origin.X*shape.Y+(int)origin.Y)*shape.Z+(int)origin.Z];
            Set(solver,"particles",new List<Particle>{p}); Set(solver,"ant_slime",10.0);
            int sensorCount=shape.Name=="3d"?5:3;
            for(int target=0;target<sensorCount;target++)
            {
                foreach(Voxel v in flat){v.towardsFoodPheromone=0;v.towardsBasePheromone=0;v.density=0;}
                p.pPlane=new Plane(origin,forward,side); p.moveVector=Vector3d.Zero; p.foundFood=false;
                Vector3d direction=forward;
                if(target==0)direction=forward*Math.Sqrt(.5)-side*Math.Sqrt(.5);
                if(target==2)direction=forward*Math.Sqrt(.5)+side*Math.Sqrt(.5);
                if(target>=3)direction.Rotate((target==3?1:-1)*Math.PI/4,side);
                Point3d sensor=origin+direction*4;
                Voxel source=flat[((int)sensor.X*shape.Y+(int)sensor.Y)*shape.Z+(int)sensor.Z];
                source.towardsFoodPheromone=.8;
                // A conflicting slime signal cannot override the food gradient.
                p.parentVoxel.density=100;
                Call(solver,"particleSenseValuesAndVectors",0L,0L);
                Check(Get<bool>(p,"followingFood"),shape.Name+" failed food acquisition sensor"+target);
                Near((p.moveVector-direction).Length,0,shape.Name+" sensor direction"+target,1e-10);
                p.moveVector=Vector3d.Zero; Call(solver,"particleSense_Ant"); Vector3d reduced=p.moveVector;
                Set(p,"followingFood",false);p.moveVector=Vector3d.Zero;Call(solver,"particleSense_Ant");
                Near((p.moveVector*.02-reduced).Length,0,shape.Name+" exploration scale",1e-10);
                source.towardsFoodPheromone=0;p.moveVector=Vector3d.Zero;
                Call(solver,"particleSenseValuesAndVectors",0L,0L);
                Check(!Get<bool>(p,"followingFood"),shape.Name+" failed scent loss");
                // Carrying ants retain return behavior even with a food gradient.
                source.towardsFoodPheromone=.8;p.foundFood=true;p.moveVector=Vector3d.Zero;
                Call(solver,"particleSenseValuesAndVectors",0L,0L);
                Check(!Get<bool>(p,"followingFood"),shape.Name+" carrying follows food");p.foundFood=false;
            }
            foreach(double flatValue in new[]{0.0,1e-8,.8})
            {
                foreach(Voxel v in flat)v.towardsFoodPheromone=flatValue;
                Call(solver,"particleSenseValuesAndVectors",0L,0L);
                Check(!Get<bool>(p,"followingFood"),shape.Name+" flat/trace scent manufactured gradient");
            }
            foreach(Voxel v in flat)v.towardsFoodPheromone=0;
            Voxel foodVoxel=p.parentVoxel;foodVoxel.antFood=.25;foodVoxel.food=.4;
            Get<double[]>(solver,"foodSourceValues")[foodVoxel.flatIndex]=.4;
            Call(solver,"cacheFoodSourceVoxels");double previousDensity=foodVoxel.density;
            Call(solver,"projectFoodSources");Near(foodVoxel.antFood,.25,"food moved");Near(foodVoxel.towardsFoodPheromone,.25,"food scent emission");Near(foodVoxel.density,previousDensity,"scalar emitted without slime");
            Set(solver,"slimeParticles",true);Call(solver,"projectFoodSources");Near(foodVoxel.density,previousDensity+.4,"mixed slime source");
            Set(solver,"slimeParticles",false);Set(solver,"iteration",3);p.foundFood=false;
            Call(solver,"particleCheckParentVoxel");Check(p.foundFood&&p.age==1,"pickup state");Near(foodVoxel.antFood,0,"fractional pickup clamp");
            double scent=foodVoxel.towardsFoodPheromone;Call(solver,"projectFoodSources");Near(foodVoxel.towardsFoodPheromone,scent,"depleted source still emits");
            p.home=p.pPlane; p.antLaunchBoundaryHit=true;Call(solver,"particleCheckParentVoxel");
            Check(!p.foundFood&&p.age==1&&!p.antLaunchBoundaryHit,"nest reset");
            Results.Add(new{Test="behavior",Shape=shape.Name,SensorDirections=sensorCount,Passed=true});
            Console.WriteLine($"PASS {shape.Name}: {sensorCount} sensor directions, scent acquisition/loss, exploration, carrying, food emission/depletion, mixed scalar source and nest reset.");
        }
        MethodInfo gradient=typeof(Solver).GetMethod("hasFoodGradient",Flags);
        Check(!(bool)gradient.Invoke(null,new object[]{.8,.8,-1.0,-1.0,-1.0}),"invalid sensors manufactured gradient");
        Check(!(bool)gradient.Invoke(null,new object[]{1e-8,0.0,-1.0,-1.0,-1.0}),"trace manufactured gradient");
    }

    static void Benchmark(string[] args)
    {
        int size=int.Parse(Program.Option(args,"--size")??"100");
        string mode=Program.Option(args,"--shape")??"3d";
        Shape shape=mode=="3d"?new("3d",size,size,size):new("xy",size,size,1);
        Solver solver=Create(shape,false,2,false,.1,.1);
        var diffuse=(Action)typeof(Solver).GetMethod("diffuseVoxels",Flags).CreateDelegate(typeof(Action),solver);
        var project=(Action)typeof(Solver).GetMethod("projectFoodSources",Flags).CreateDelegate(typeof(Action),solver);
        Voxel[] flat=Get<Voxel[]>(solver,"voxelFlat");
        for(int i=0;i<flat.Length;i+=997)flat[i].antFood=10;
        for(int i=0;i<12;i++){Set(solver,"iteration",i+1);project();diffuse();}
        var times=new List<double>(); long allocations=GC.GetTotalAllocatedBytes(true);
        for(int i=0;i<36;i++){Set(solver,"iteration",i+1);var watch=Stopwatch.StartNew();project();diffuse();watch.Stop();times.Add(watch.Elapsed.TotalMilliseconds);}
        allocations=GC.GetTotalAllocatedBytes(true)-allocations;
        times.Sort();double median=(times[17]+times[18])*.5;
        var result=new{Test="benchmark",Shape=mode,Size=size,Voxels=flat.Length,MedianMilliseconds=median,P90Milliseconds=times[32],AllocatedBytesPerStep=allocations/36,Steps=36};
        Results.Add(result);Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
