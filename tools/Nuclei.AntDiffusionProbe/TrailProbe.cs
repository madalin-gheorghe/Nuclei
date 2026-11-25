using System.Text.Json;
internal static partial class Program
{
    static void RunTrailTests(string[] args)
    {
        var results=new List<object>();int shapeIndex=0;
        foreach(var shape in new[]{(65,65,17),(65,65,1),(65,1,65),(1,65,65)})
        {
            var f=new Fixture("trail",shape.Item1,shape.Item2,shape.Item3,0,true,0,0,0,0,Masks:false,Limits:false,Particles:false);
            int axis=f.X==1?1:0,side=f.Z==1?1:2,n=f.X*f.Y*f.Z;
            float speed=shapeIndex++%2==0?2:3;
            float[] home={f.X==1?.5f:5.5f,f.Y==1?.5f:5.5f,f.Z==1?.5f:5.5f};
            if(speed==3)home[axis]=12.5f;
            int[] dims={f.X,f.Y,f.Z};double distanceSquared=0;
            for(int i=0;i<3;i++)if(dims[i]>1){double edge=Math.Max(home[i]-1,dims[i]-1-home[i]);distanceSquared+=edge*edge;}
            int duration=(int)Math.Ceiling(.75*Math.Sqrt(distanceSquared)/speed);
            foreach(bool carrying in new[]{false,true})foreach(int age in new[]{1,duration/2,duration-1,duration,duration*2})
            {
                object input=CreateInput(f),settings=CreateSettings(f);
                float[] pos={f.X==1?.5f:32.5f,f.Y==1?.5f:32.5f,f.Z==1?.5f:8.5f};
                var dir=new float[3];dir[axis]=1;var y=new float[3];y[side]=1;
                Set(input,"ParticleCount",1);Set(input,"ParticlePositionsXyz",pos);Set(input,"ParticleDirectionsXyz",dir);Set(input,"ParticleYAxesXyz",y);Set(input,"ParticleHomesXyz",home);
                Set(input,"ParticleParentIndices",new[]{((int)pos[0]*f.Y+(int)pos[1])*f.Z+(int)pos[2]});Set(input,"ParticleGroupIndices",new[]{0});
                Set(input,"ParticleAges",new[]{age-1});Set(input,"ParticleAntStates",new[]{carrying?1u:0u});Set(input,"ParticleAntLaunchBoundaryStates",new[]{1u});
                Set(input,"GroupData0",new[]{speed,.1f,.7f,0f});Set(input,"GroupData1",new[]{.7f,1f,.2f,1f});
                foreach(var name in new[]{"VoxelDensity","AntFoodPheromone","AntBasePheromone","InitialAntFood"})Set(input,name,new float[n]);
                object dim=RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",Static)!.Invoke(null,new object[]{f.X,f.Y,f.Z})!;
                using var engine=(IDisposable)FoodEngine(input,settings);Invoke(engine,"Step",settings,dim,2,false,false,false);var state=Snapshot(engine);
                double total=0;var bytes=state[carrying?"antFoodReadback":"antBaseReadback"];for(int i=0;i<n;i++)total+=BitConverter.ToSingle(bytes,i*4);
                double expected=Math.Round(.2*(.02+.98*Math.Pow(Math.Max(0,1-age/(double)duration),2.5))*(carrying?1:.9)*1024)/1024;
                Assert(Math.Abs(total-expected)<.00001,$"trail {shape} carrying={carrying} age={age}/{duration}: {total} != {expected}");
                results.Add(new{shape,carrying,age,duration,total,expected,passed=true});
            }
        }
        string? json=Option(args,"--json");if(json!=null)File.WriteAllText(json,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS {results.Count} hardware map-scaled trail deposit checks, both phases, 2D/3D and 2% floor.");
    }
}
