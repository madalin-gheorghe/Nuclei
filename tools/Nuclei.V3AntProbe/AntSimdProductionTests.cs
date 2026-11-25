using Nuclei3;

internal static partial class Probe
{
    static void AntSimdProductionTests()
    {
        int cases=0;double maxError=0;
        foreach(var shape in new[]{new Shape("3d",17,19,23),new Shape("xy",79,67,1),new Shape("xz",79,1,67),new Shape("yz",1,79,67)})
        foreach(int range in new[]{0,1,3,20})
        foreach(var rates in new[]{new[]{.17,.31},new[]{0.0,.31},new[]{.17,0.0}})
        foreach(double falloff in new[]{0.0,.4,1.0})
        {
            var solver=Create(shape,true,range,false,rates[0],rates[1]);Set(solver,"densityLimitsDisabled",true); Set(solver,"antDiffusionGradual",1-falloff);
            var flat=Get<Voxel[]>(solver,"voxelFlat");
            var food=flat.Select(v=>v.towardsFoodPheromone).ToArray();var home=flat.Select(v=>v.towardsBasePheromone).ToArray();
            for(int step=1;step<=6;step++)
            {
                Set(solver,"iteration",step);
                int[] axes=shape.Name=="3d"?Orders[step-1]:Enumerable.Range(0,3).Where(axis=>(axis==0?shape.X:axis==1?shape.Y:shape.Z)>1).ToArray();
                if(shape.Name!="3d"&&step%2==0)Array.Reverse(axes);
                food=Reference(food,flat,shape,axes,range,true,rates[0],.031,true,falloff);
                home=Reference(home,flat,shape,axes,range,true,rates[1],.073,false,falloff);
                Call(solver,"diffuseVoxels");
                for(int i=0;i<flat.Length;i++)
                {
                    maxError=Math.Max(maxError,Math.Abs(flat[i].towardsFoodPheromone-food[i]));
                    maxError=Math.Max(maxError,Math.Abs(flat[i].towardsBasePheromone-home[i]));
                }
            }
            Check(Get<double[]>(solver,"antSimdFood")!=null,"Production SIMD did not execute");cases++;
        }
        Check(maxError<1e-12,"Production ant SIMD differs from independent reference");
        Results.Add(new{Test="production-ant-simd",Cases=cases,Steps=6,MaxError=maxError,Passed=true});
        Console.WriteLine($"PASS production ant SIMD: {cases} cases, independent reference error {maxError:E3}.");
    }
}
