using System.Text.Json;

internal static partial class Program
{
    static void RunNestTests(string[] args)
    {
        if (!args.Contains("--arrivals-only")) RunNestSteeringTests();
        RunNestCrowdingTests();
        var results=new List<object>();
        foreach(var shape in new[]{(33,33,9),(33,33,1),(33,1,33),(1,33,33)})
        foreach(bool mixed in new[]{false,true}) foreach(bool dynamic in new[]{false,true})
        foreach(bool particleDeposits in new[]{false,true})
        {
            var f=new Fixture("nest",shape.Item1,shape.Item2,shape.Item3,0,true,0,0,0,0,Masks:false,Limits:false,Particles:false);
            object input=CreateInput(f),settings=CreateSettings(f);int n=f.X*f.Y*f.Z;
            int axis=f.X==1?1:0,side=f.Z==1?1:2;
            float[] home={f.X==1?.5f:16.5f,f.Y==1?.5f:16.5f,f.Z==1?.5f:4.5f};
            var positions=home.Concat(home).ToArray();positions[axis]-=1.1f;positions[3+axis]+=1.1f;
            var directions=new float[6];directions[side]=directions[3+side]=1;
            var yAxes=new float[6];yAxes[axis]=yAxes[3+axis]=1;
            int At(float[] p,int offset)=>( (int)p[offset]*f.Y+(int)p[offset+1])*f.Z+(int)p[offset+2];
            Set(input,"HasSlimeParticles",mixed);Set(input,"ParticleCount",2);
            Set(input,"ParticlePositionsXyz",positions);Set(input,"ParticleDirectionsXyz",directions);Set(input,"ParticleYAxesXyz",yAxes);
            Set(input,"ParticleHomesXyz",home.Select((v,i)=>(i==0?f.X:i==1?f.Y:f.Z)==1?0f:v).Concat(home.Select((v,i)=>(i==0?f.X:i==1?f.Y:f.Z)==1?0f:v)).ToArray());Set(input,"ParticleParentIndices",new[]{At(positions,0),At(positions,3)});
            Set(input,"ParticleGroupIndices",new[]{0,0});Set(input,"ParticleAges",new[]{40,40});Set(input,"ParticleAntStates",new[]{1u,1u});Set(input,"ParticleAntLaunchBoundaryStates",new[]{1u,1u});
            Set(input,"GroupData0",new[]{2f,2f,.7f,0f});Set(input,"GroupData1",new[]{.7f,1f,.2f,1f});
            foreach(var name in new[]{"VoxelDensity","AntFoodPheromone","AntBasePheromone","InitialAntFood"})Set(input,name,new float[n]);
            Set(settings,"DynamicPopulation",dynamic);Set(settings,"MinimumPopulation",0);Set(settings,"MaximumPopulation",8);Set(settings,"Decay",0d);
            object dimension=RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",Static)!.Invoke(null,new object[]{f.X,f.Y,f.Z})!;
            using var engine=(IDisposable)FoodEngine(input,settings);
            Set(engine,"forceParticleDrivenDepositForValidation",particleDeposits);
            Set(engine,"disableAdaptiveAntDepositsForValidation",!particleDeposits);
            string label=$"{shape} mixed={mixed} dynamic={dynamic} sparseDeposits={particleDeposits}";
            for(int step=2;step<14;step++)
            {
                Invoke(engine,"Step",settings,dimension,step,false,false,false);var state=Snapshot(engine);
                float F(string key,int i)=>BitConverter.ToSingle(state[key],i*4);
                Assert(F("particlePositionReadback",3)>=0&&F("particlePositionReadback",7)>=0,"nest culling "+label);
                uint sum=0;for(int i=0;i<n;i++){uint count=BitConverter.ToUInt32(state["ParticleCounts"],i*4);Assert(count<=2,"corrupt nest occupancy "+label);sum+=count;}
                Assert(sum==2,"nest count conservation "+label+" step="+step+" count="+sum);
                if(step==2)
                {
                    for(int i=0;i<2;i++)for(int c=0;c<3;c++)Assert(Math.Abs(F("particlePositionReadback",i*4+c)-home[c])<1e-5,"exact nest arrival "+label);
                    int nest=At(home,0);Assert(BitConverter.ToUInt32(state["ParticleCounts"],nest*4)==2,"two ants share nest "+label);
                    Assert(Math.Abs(F("antFoodReadback",nest)-2*Math.Round(.2*.02*1024)/1024)<.0001,"shared deposits lost/duplicated actual="+F("antFoodReadback",nest)+" expected="+(2*Math.Round(.2*.02*1024)/1024)+" "+label);
                    int offset=Get<int>(engine,"particleAntStateOffset");
                    Assert(BitConverter.ToUInt32(state["Deposits"],offset*4)==0&&BitConverter.ToUInt32(state["Deposits"],(offset+1)*4)==0,"deliveries did not clear food "+label);
                }
            }
            results.Add(new{shape=shape.ToString(),mixed,dynamic,particleDeposits,passed=true});
        }
        string? json=Option(args,"--json");if(json!=null)File.WriteAllText(json,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS {results.Count} hardware nest fixtures: exact overlapping arrivals, food delivery, deposits, departures and persistent/dynamic counts.");
    }

    static void RunNestSteeringTests()
    {
        int cases=0;
        foreach(var shape in new[]{(33,33,9),(33,33,1),(33,1,33),(1,33,33)})
        foreach(float distance in new[]{4f,3f,2f,1f,.01f})
        {
            var f=new Fixture("nest-steering",shape.Item1,shape.Item2,shape.Item3,0,true,0,0,0,0,Masks:false,Limits:false,Particles:false);
            int axis=f.X==1?1:0,side=f.Z==1?1:2,n=f.X*f.Y*f.Z;
            float[] home={f.X==1?.5f:16.5f,f.Y==1?.5f:16.5f,f.Z==1?.5f:4.5f};
            float[] Direction(float sensor)
            {
                object input=CreateInput(f),settings=CreateSettings(f);var pos=(float[])home.Clone();pos[axis]-=distance;
                var direction=new float[3];direction[side]=1;var y=new float[3];y[axis]=1;
                Set(input,"ParticleCount",1);Set(input,"ParticlePositionsXyz",pos);Set(input,"ParticleDirectionsXyz",direction);Set(input,"ParticleYAxesXyz",y);Set(input,"ParticleHomesXyz",home.Select((v,i)=>(i==0?f.X:i==1?f.Y:f.Z)==1?0f:v).ToArray());
                Set(input,"ParticleParentIndices",new[]{((int)pos[0]*f.Y+(int)pos[1])*f.Z+(int)pos[2]});Set(input,"ParticleGroupIndices",new[]{0});
                Set(input,"ParticleAges",new[]{40});Set(input,"ParticleAntStates",new[]{1u});Set(input,"ParticleAntLaunchBoundaryStates",new[]{1u});
                Set(input,"GroupData0",new[]{.001f,sensor,.7f,0f});Set(input,"GroupData1",new[]{.7f,1f,0f,1f});
                foreach(var name in new[]{"VoxelDensity","AntFoodPheromone","AntBasePheromone","InitialAntFood"})Set(input,name,new float[n]);
                object dim=RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",Static)!.Invoke(null,new object[]{f.X,f.Y,f.Z})!;
                using var engine=(IDisposable)FoodEngine(input,settings);Invoke(engine,"Step",settings,dim,2,false,false,false);var state=Snapshot(engine);
                return Enumerable.Range(0,3).Select(i=>BitConverter.ToSingle(state["particleDirectionReadback"],i*4)).ToArray();
            }
            var baseline=Direction(.001f);var actual=Direction(2);float t=Math.Clamp(1-distance/4,0,1);
            var expected=baseline.Select((v,i)=>v*(1-t)+(i==axis?t:0)).ToArray();float length=MathF.Sqrt(expected.Sum(v=>v*v));
            for(int i=0;i<3;i++)Assert(Math.Abs(actual[i]-expected[i]/length)<.0001,$"GPU home takeover {shape} distance={distance}");
            cases++;
        }
        Console.WriteLine($"PASS {cases} hardware linear home-takeover comparisons against unchanged steering.");
    }

    static void RunNestCrowdingTests()
    {
        int cases=0;
        foreach(var shape in new[]{(33,33,9),(33,33,1),(33,1,33),(1,33,33)})
        foreach(string scenario in new[]{"pickup","death","obstacle"})
        {
            var f=new Fixture("nest-crowding",shape.Item1,shape.Item2,shape.Item3,0,true,0,0,0,0,Masks:false,Limits:false,Particles:false);
            int n=f.X*f.Y*f.Z,axis=f.X==1?1:0,side=f.Z==1?1:2;
            float[] home={f.X==1?.5f:16.5f,f.Y==1?.5f:16.5f,f.Z==1?.5f:4.5f};var pos=(float[])home.Clone();pos[axis]+=1;
            int At(float[] p)=>((int)p[0]*f.Y+(int)p[1])*f.Z+(int)p[2];
            object input=CreateInput(f),settings=CreateSettings(f);
            var forward=new float[3];forward[axis]=-1;var y=new float[3];y[side]=1;
            Set(input,"ParticleCount",2);Set(input,"ParticlePositionsXyz",pos.Concat(pos).ToArray());Set(input,"ParticleDirectionsXyz",forward.Concat(forward).ToArray());Set(input,"ParticleYAxesXyz",y.Concat(y).ToArray());
            Set(input,"ParticleHomesXyz",home.Concat(home).ToArray());Set(input,"ParticleParentIndices",new[]{At(pos),At(pos)});Set(input,"ParticleGroupIndices",new[]{0,0});
            Set(input,"ParticleAges",new[]{40,40});Set(input,"ParticleAntStates",new[]{scenario=="obstacle"?1u:0u,scenario=="obstacle"?1u:0u});Set(input,"ParticleAntLaunchBoundaryStates",new[]{1u,1u});
            Set(input,"GroupData0",new[]{scenario=="obstacle"?2f:0f,2f,.7f,0f});Set(input,"GroupData1",new[]{.7f,1f,.2f,1f});
            foreach(var name in new[]{"VoxelDensity","AntFoodPheromone","AntBasePheromone","InitialAntFood"})Set(input,name,new float[n]);
            if(scenario=="pickup"){var food=new float[n];food[At(pos)]=.5f;Set(input,"InitialAntFood",food);}
            if(scenario=="obstacle"){var flags=Enumerable.Repeat(uint.MaxValue,(n+31)/32).ToArray();flags[At(home)>>5]&=~(1u<<(At(home)&31));Set(input,"VoxelFlags",flags);}
            if(scenario=="death"){Set(settings,"DynamicPopulation",true);Set(settings,"MinimumPopulation",0);Set(settings,"MaximumPopulation",8);Set(settings,"RandomDeathProbability",1d);}
            object dim=RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",Static)!.Invoke(null,new object[]{f.X,f.Y,f.Z})!;
            using var engine=(IDisposable)FoodEngine(input,settings);Invoke(engine,"Step",settings,dim,2,false,false,false);var state=Snapshot(engine);
            int expected=scenario=="death"?0:2;uint total=0;for(int i=0;i<n;i++){uint count=BitConverter.ToUInt32(state["ParticleCounts"],i*4);Assert(count<=2,"crowding counter overflow");total+=count;}
            Assert(total==expected,$"{shape} {scenario} total={total}");
            if(scenario=="pickup")
            {
                int offset=Get<int>(engine,"particleAntStateOffset");uint carried=BitConverter.ToUInt32(state["Deposits"],offset*4)+BitConverter.ToUInt32(state["Deposits"],(offset+1)*4);
                Assert(carried==1,"shared food picked up twice");Assert(BitConverter.ToUInt32(state["antFoodRemainingReadback"],At(pos)*4)==0,"fractional food remains");
            }
            if(scenario=="obstacle")Assert(BitConverter.ToUInt32(state["ParticleCounts"],At(home)*4)==0,"overlap bypassed obstacle");
            cases++;
        }
        Console.WriteLine($"PASS {cases} hardware crowding fixtures: shared pickup, death releases and blocked nests.");
    }
}
