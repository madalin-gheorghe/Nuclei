using Nuclei3;
using Rhino.Geometry;
using Particle = Nuclei3.Particle;

internal static partial class Probe
{
    static void ActualFoodTests()
    {
        int cases=0;
        foreach(var original in Shapes)
        {
            var shape=new Shape(original.Name,original.X==1?1:31,original.Y==1?1:31,original.Z==1?1:31);
            var solver=Create(shape,true,0,false);var flat=Get<Voxel[]>(solver,"voxelFlat");
            var forward=shape.X==1?Vector3d.YAxis:Vector3d.XAxis;var side=shape.Z==1?Vector3d.YAxis:Vector3d.ZAxis;
            var origin=new Point3d(shape.X==1?.5:15.5,shape.Y==1?.5:15.5,shape.Z==1?.5:15.5);
            var group=new ParticleGroup{ant=true,speed=1,sensorDistance=4,sensorAngle=45,rotationAngle=45};
            Voxel At(Point3d pt)=>flat[((int)pt.X*shape.Y+(int)pt.Y)*shape.Z+(int)pt.Z];
            var p=new Particle(new Plane(origin,forward,side)){parentParticleGroup=group,parentVoxel=At(origin),age=40};
            Set(solver,"particles",new List<Particle>{p});
            Vector3d Dir(int target){var d=forward;if(target==0)d=forward*Math.Sqrt(.5)-side*Math.Sqrt(.5);if(target==2)d=forward*Math.Sqrt(.5)+side*Math.Sqrt(.5);if(target>=3)d.Rotate((target==3?1:-1)*Math.PI/4,side);return d;}
            for(int target=0;target<(shape.Name=="3d"?5:3);target++)
            foreach(string scenario in new[]{"food","depleted","blocked","carrying"})
            {
                foreach(var v in flat){v.antFood=0;v.towardsFoodPheromone=0;v.towardsBasePheromone=0;v.density=0;v.maxDensity=-1;}
                p.pPlane=new Plane(origin,forward,side);p.moveVector=Vector3d.Zero;p.foundFood=scenario=="carrying";
                var source=At(origin+Dir(target)*4);int rival=target==1?0:1;
                var trail=At(origin+Dir(rival)*4);trail.towardsFoodPheromone=10000;trail.towardsBasePheromone=10000;
                source.antFood=scenario=="depleted"?0:.25;
                if(scenario=="blocked")source.maxDensity=0;
                Call(solver,"particleSenseValuesAndVectors",0L,0L);
                Near((p.moveVector-Dir(scenario=="food"?target:rival)).Length,0,$"actual food {shape.Name}/{target}/{scenario}",1e-10);
                cases++;
            }
        }
        Results.Add(new{Test="actual-food-priority",Cases=cases,Passed=true});
        Console.WriteLine($"PASS {cases} actual-food priorities: all 2D/3D sensors, stronger competing scent, depletion, blocked food and carrying ants.");
    }
}
