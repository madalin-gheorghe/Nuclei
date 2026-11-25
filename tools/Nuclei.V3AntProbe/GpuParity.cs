using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Nuclei3;

internal static partial class Probe
{
    // Reuse the V4 probe's fixture builder and its real hardware engine loader.
    // Compare CPU doubles with GPU floats, without requiring identical random
    // particle ordering or replacing either implementation with a reference.
    static void GpuParity(string[] args)
    {
        Type gpuProbe=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath("tools/Nuclei.AntDiffusionProbe/bin/Release/net8.0-windows/Nuclei.AntDiffusionProbe.dll")).GetType("Program");
        object Invoke(string name,params object[] values)=>gpuProbe.GetMethod(name,Flags).Invoke(null,values);
        Invoke("Load",Path.GetFullPath(Program.Option(args,"--v4-output")??"Nuclei-v4/Nuclei4/bin/Release/net7.0-windows"));
        Type Type(string name)=>(Type)Invoke("RequiredType",name);
        Type fixtureType=gpuProbe.GetNestedType("Fixture",Flags);
        int cases=0;double maxError=0;
        foreach(Shape shape in Shapes)foreach(bool wrap in new[]{false,true})foreach(int range in new[]{0,2,17})
        foreach(var rates in new[]{(.17,.31),(.17,0.0),(0.0,.31),(0.0,0.0)})
        foreach(double falloff in new[]{0.0,.4,1.0})
        {
            object fixture=Activator.CreateInstance(fixtureType,new object[]{shape.Name,shape.X,shape.Y,shape.Z,range,wrap,rates.Item1,rates.Item2,.031,.073,true,true,false,false,false,0});
            object input=Invoke("CreateInput",fixture),settings=Invoke("CreateSettings",fixture);
            Solver solver=Create(shape,wrap,range,false,rates.Item1,rates.Item2);
            Set(solver,"antDiffusionGradual",1-falloff); Set(settings,"AntDiffusionGradual",1-falloff);
            Voxel[] flat=Get<Voxel[]>(solver,"voxelFlat");Voxel[,,] grid=Get<Voxel[,,]>(solver,"voxels");
            uint[] active=Get<uint[]>(input,"ActiveVoxelFlags");float[] food=Get<float[]>(input,"AntFoodPheromone"),home=Get<float[]>(input,"AntBasePheromone"),remaining=Get<float[]>(input,"InitialAntFood"),limits=Get<float[]>(input,"VoxelDensityLimits");
            for(int i=0;i<flat.Length;i++)
            {
                Voxel v=flat[i];
                if((active[i>>5]&(1u<<(i&31)))==0){flat[i]=grid[v.idX,v.idY,v.idZ]=null;continue;}
                v.towardsFoodPheromone=food[i];v.towardsBasePheromone=home[i];v.antFood=remaining[i];
                v.minDensity=limits[i];v.maxDensity=limits[flat.Length+i];
            }
            Set(solver,"activeVoxels",flat.Where(v=>v!=null).ToArray());Call(solver,"cacheFoodSourceVoxels");
            object sink=RuntimeHelpers.GetUninitializedObject(Type("Nuclei4.Gh1GpuSolverOutputSink"));
            using var engine=(IDisposable)Type("Nuclei4.GpuFullSlimeSolverEngine").GetConstructors(Flags).Single(c=>c.GetParameters().Length==8).Invoke(new[]{input,sink,settings,false,false,false,0,1});
            object capabilities=engine.GetType().GetProperty("Capabilities").GetValue(engine);
            Check(!Get<bool>(capabilities,"SoftwareFallback"),"GPU parity requires hardware D3D11");
            object dimension=Type("Nuclei4.SolverGpuDimensionMode").GetMethod("FromResolution",Flags).Invoke(null,new object[]{shape.X,shape.Y,shape.Z});
            for(int step=0;step<8;step++)
            {
                Set(solver,"iteration",step);Call(solver,"projectFoodSources");Call(solver,"diffuseVoxels");
                Call(engine,"Step",settings,dimension,step,false,false,false);Call(engine,"ReadBackAntFields");
                float[] actualFood=Get<float[]>(engine,"antFoodReadback"),actualBase=Get<float[]>(engine,"antBaseReadback");
                foreach(Voxel v in flat.Where(v=>v!=null))
                {
                    double error=Math.Max(Math.Abs(v.towardsFoodPheromone-actualFood[v.flatIndex]),Math.Abs(v.towardsBasePheromone-actualBase[v.flatIndex]));maxError=Math.Max(maxError,error);
                    Check(error<.00002,$"V3/V4 {shape.Name} wrap={wrap} range={range} rates={rates} step={step} voxel={v.flatIndex} food={v.towardsFoodPheromone:R}/{actualFood[v.flatIndex]:R} base={v.towardsBasePheromone:R}/{actualBase[v.flatIndex]:R}");
                }
            }
            cases++;
        }
        Results.Add(new{Test="hardware-v4-parity",Cases=cases,Steps=8,MaxError=maxError,Passed=true});
        Console.WriteLine($"PASS {cases} V3/V4 hardware field fixtures x8 steps; maximum double/float error={maxError:R}.");
    }
}
