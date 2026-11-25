using System.Text.Json;

internal static partial class Program
{
    static void RunActualFoodTests(string[] args)
    {
        var results = new List<object>();
        uint HashKey(uint v) { unchecked { v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; return v ^ (v >> 16); } }
        int iteration = 2;
        while (HashKey(unchecked((uint)iteration * 747796405u + 0x9e3779b9u)) % 7 == 0) iteration++;
        foreach (var shape in new[] { (65,65,65), (65,65,1), (65,1,65), (1,65,65) })
        foreach (bool mixed in new[] { false, true })
        foreach (string scenario in new[] { "food", "depleted", "invalid-food", "carrying", "vertical" })
        {
            if (scenario == "vertical" && (shape.Item1 == 1 || shape.Item2 == 1 || shape.Item3 == 1)) continue;
            var f = new Fixture("exploration",shape.Item1,shape.Item2,shape.Item3,0,true,0,0,0,0,Masks:false,Limits:false,Particles:false);
            int n=f.X*f.Y*f.Z, axis=f.X==1?1:0, side=f.Z==1?1:2;
            float[] pos={f.X==1?.5f:32.5f,f.Y==1?.5f:32.5f,f.Z==1?.5f:32.5f};
            var home=(float[])pos.Clone(); home[axis]-=20;
            var dir=new float[3];dir[axis]=1;var y=new float[3];y[side]=1;
            int At(float[] p)=>((int)p[0]*f.Y+(int)p[1])*f.Z+(int)p[2];
            var sensor=new float[5][];
            for(int i=0;i<5;i++)sensor[i]=(float[])pos.Clone();
            sensor[0][axis]+=MathF.Cos(MathF.PI/4)*4;sensor[0][side]-=MathF.Sin(MathF.PI/4)*4;
            sensor[1][axis]+=4;
            sensor[2][axis]+=MathF.Cos(MathF.PI/4)*4;sensor[2][side]+=MathF.Sin(MathF.PI/4)*4;
            sensor[3][axis]+=MathF.Cos(MathF.PI/4)*4;sensor[3][1]+=MathF.Sin(MathF.PI/4)*4;
            sensor[4][axis]+=MathF.Cos(MathF.PI/4)*4;sensor[4][1]-=MathF.Sin(MathF.PI/4)*4;
            var food=new float[n];var bases=Enumerable.Repeat(5f,n).ToArray();
            bases[At(sensor[0])]=1;bases[At(sensor[1])]=3;bases[At(sensor[2])]=4;
            food[At(sensor[0])]=10000;
            var edible=new float[n];edible[At(sensor[2])]=scenario=="depleted"?0:.25f;
            int expectedSide=scenario=="depleted"||scenario=="invalid-food"?-1:1;
            if(scenario=="carrying")bases[At(sensor[2])]=9;
            if(scenario=="vertical") { edible[At(sensor[2])]=0;edible[At(sensor[3])]=.25f; }
            object input=CreateInput(f),settings=CreateSettings(f);
            Set(input,"HasSlimeParticles",mixed);Set(input,"ParticleCount",1);Set(input,"ParticlePositionsXyz",pos);Set(input,"ParticleDirectionsXyz",dir);Set(input,"ParticleYAxesXyz",y);Set(input,"ParticleHomesXyz",home);
            Set(input,"ParticleParentIndices",new[]{At(pos)});Set(input,"ParticleGroupIndices",new[]{0});Set(input,"ParticleAges",new[]{40});Set(input,"ParticleAntStates",new[]{scenario=="carrying"?1u:0u});Set(input,"ParticleAntLaunchBoundaryStates",new[]{1u});
            Set(input,"GroupData0",new[]{1f,4f,MathF.PI/4,0f});Set(input,"GroupData1",new[]{MathF.PI/2,1f,.2f,100000f});
            if(scenario=="invalid-food")
            {
                var flags=Enumerable.Repeat(uint.MaxValue,(n+31)/32).ToArray();int invalid=At(sensor[2]);flags[invalid>>5]&=~(1u<<(invalid&31));Set(input,"VoxelFlags",flags);
            }
            Set(input,"VoxelDensity",new float[n]);Set(input,"AntFoodPheromone",food);Set(input,"AntBasePheromone",bases);Set(input,"InitialAntFood",edible);
            Set(settings,"AntSlime",0d);Set(settings,"Decay",0d);
            object dim=RequiredType("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",Static)!.Invoke(null,new object[]{f.X,f.Y,f.Z})!;
            using var engine=(IDisposable)FoodEngine(input,settings);Invoke(engine,"Step",settings,dim,iteration,false,false,false);var state=Snapshot(engine);
            var actual=Enumerable.Range(0,3).Select(c=>BitConverter.ToSingle(state["particleDirectionReadback"],c*4)).ToArray();
            bool passed=scenario=="vertical"?actual[1]>.8:expectedSide==0?actual[axis]>.99:actual[side]*expectedSide>.8;
            Assert(passed,$"Exploration {shape}/{mixed}/{scenario}: {string.Join(",",actual)}");
            results.Add(new{shape,mixed,scenario,actual,passed});
        }
        string? json=Option(args,"--json");if(json!=null)File.WriteAllText(json,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS {results.Count} hardware actual-food priorities: competing scent, depleted/blocked food, returning ants and 3D vertical sensing.");
    }
}
