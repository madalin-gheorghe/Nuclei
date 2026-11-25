using System.Text.Json;
internal static partial class Program
{
    static void RunDepartureTests(string[] args)
    {
        var results=new List<object>();
        foreach(var shape in new[]{(65,65,65),(65,65,1),(65,1,65),(1,65,65)})
        foreach(bool mixed in new[]{false,true}) foreach(string scenario in new[]{"gradient","pickup","obstacle"})
        {
            var f=new Fixture("departure",shape.Item1,shape.Item2,shape.Item3,0,true,0,0,0,0,Masks:false,Limits:false,Particles:false);
            int n=f.X*f.Y*f.Z,axis=f.X==1?1:0,side=f.Z==1?1:2;
            float[] home={f.X==1?.5f:32.5f,f.Y==1?.5f:32.5f,f.Z==1?.5f:32.5f};var pos=(float[])home.Clone();pos[axis]+=1;
            int At(float[] p)=>((int)p[0]*f.Y+(int)p[1])*f.Z+(int)p[2];
            object input=CreateInput(f),settings=CreateSettings(f);var dir=new float[3];dir[axis]=-1;var y=new float[3];y[side]=1;
            Set(input,"HasSlimeParticles",mixed);Set(input,"ParticleCount",1);Set(input,"ParticlePositionsXyz",pos);Set(input,"ParticleDirectionsXyz",dir);Set(input,"ParticleYAxesXyz",y);Set(input,"ParticleHomesXyz",home);
            Set(input,"ParticleParentIndices",new[]{At(pos)});Set(input,"ParticleGroupIndices",new[]{0});Set(input,"ParticleAges",new[]{1999});Set(input,"ParticleAntStates",new[]{1u});Set(input,"ParticleAntLaunchBoundaryStates",new[]{1u});
            Set(input,"GroupData0",new[]{3f,9f,MathF.PI/4,0f});Set(input,"GroupData1",new[]{MathF.PI/4,1f,10f,10f});
            Set(input,"VoxelDensity",new float[n]);Set(input,"AntBasePheromone",new float[n]);Set(input,"InitialAntFood",new float[n]);
            var scent=new float[n];for(int x=0;x<f.X;x++)for(int yy=0;yy<f.Y;yy++)for(int z=0;z<f.Z;z++)scent[(x*f.Y+yy)*f.Z+z]=1000-10*MathF.Sqrt(MathF.Pow(x+.5f-home[0],2)+MathF.Pow(yy+.5f-home[1],2)+MathF.Pow(z+.5f-home[2],2));
            Set(input,"AntFoodPheromone",scent);
            var ahead=(float[])home.Clone();ahead[axis]+=3;
            if(scenario=="pickup"){var food=new float[n];food[At(ahead)]=.5f;Set(input,"InitialAntFood",food);}
            if(scenario=="obstacle"){var flags=Enumerable.Repeat(uint.MaxValue,(n+31)/32).ToArray();flags[At(ahead)>>5]&=~(1u<<(At(ahead)&31));Set(input,"VoxelFlags",flags);}
            object dim=RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",Static)!.Invoke(null,new object[]{f.X,f.Y,f.Z})!;
            using var engine=(IDisposable)FoodEngine(input,settings);
            Invoke(engine,"Step",settings,dim,2000,false,false,false);var state=Snapshot(engine);
            uint Flag(string field)=>BitConverter.ToUInt32(state["Deposits"],Get<int>(engine,field)*4);
            float Coord(int c)=>BitConverter.ToSingle(state["particlePositionReadback"],c*4);
            Assert(Flag("particleAntStateOffset")==0&&(Flag("particleAntLaunchBoundaryOffset")&2)!=0,"delivery must arm departure");
            for(int c=0;c<3;c++)Assert(Math.Abs(Coord(c)-home[c])<1e-5,"exact home delivery");
            int steps=scenario=="gradient"?6:1;
            for(int step=1;step<=steps;step++)
            {
                Invoke(engine,"Step",settings,dim,2000+step,false,false,false);state=Snapshot(engine);
                if(scenario!="obstacle")Assert(Math.Abs(Coord(axis)-home[axis]-3*step)<1e-4,$"departure blocked by scent: {shape}/{mixed}/{scenario}/step={step}");
            }
            if(scenario=="pickup")Assert(Flag("particleAntStateOffset")==1&&(Flag("particleAntLaunchBoundaryOffset")&2)==0,"pickup must end departure");
            if(scenario=="obstacle")Assert(BitConverter.ToUInt32(state["ParticleCounts"],At(ahead)*4)==0,"departure bypassed obstacle");
            if(scenario=="gradient")
            {
                for(int x=0;x<f.X;x++)for(int yy=0;yy<f.Y;yy++)for(int z=0;z<f.Z;z++)scent[(x*f.Y+yy)*f.Z+z]=(side==1?yy:z)*100;
                Invoke(engine,"ResetFloatFieldPair",scent,Get<object>(engine,"antFoodA"),Get<object>(engine,"antFoodAView"),Get<object>(engine,"antFoodB"),Get<object>(engine,"antFoodBView"));
                Invoke(engine,"Step",settings,dim,2007,false,false,false);state=Snapshot(engine);
                Assert((Flag("particleAntLaunchBoundaryOffset")&2)==0,"departure did not end at exit");
                Assert(BitConverter.ToSingle(state["particleDirectionReadback"],side*4)>.5,"food sensing did not resume: "+shape+" mixed="+mixed+" direction="+string.Join(",",Enumerable.Range(0,3).Select(c=>BitConverter.ToSingle(state["particleDirectionReadback"],c*4))));
            }
            results.Add(new{shape,mixed,scenario,passed=true});
        }
        string? json=Option(args,"--json");if(json!=null)File.WriteAllText(json,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS {results.Count} hardware protected-departure fixtures: strong nest scent, delivery, exit, resumed sensing, pickup and obstacles.");
    }
}
