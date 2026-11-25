using Nuclei3;
using Rhino.Geometry;
using Particle = Nuclei3.Particle;
internal static partial class Probe
{
    static void DepartureTests()
    {
        int cases=0;
        foreach(var original in Shapes) foreach(bool wrap in new[]{false,true})
        {
            var shape=new Shape(original.Name,original.X==1?1:65,original.Y==1?1:65,original.Z==1?1:65);
            var solver=Create(shape,wrap,1,false);Set(solver,"iteration",2000);
            var flat=Get<Voxel[]>(solver,"voxelFlat");
            var home=new Point3d(shape.X==1?.5:32.5,shape.Y==1?.5:32.5,shape.Z==1?.5:32.5);
            var axis=shape.X==1?Vector3d.YAxis:Vector3d.XAxis;var side=shape.Z==1?Vector3d.YAxis:Vector3d.ZAxis;
            Voxel At(Point3d pos)=>flat[((int)pos.X*shape.Y+(int)pos.Y)*shape.Z+(int)pos.Z];
            var group=new ParticleGroup{ant=true,speed=3,sensorDistance=9,sensorAngle=45,rotationAngle=45,depositValue=10,baseWanderFrequency=10};
            var ant=new Particle(new Plane(home+axis, -axis,side)){home=new Plane(home,axis,side),parentParticleGroup=group,parentVoxel=At(home+axis),foundFood=true,age=2000,moveVector=-axis};
            var ants=new List<Particle>{ant};group.particles=ants;Set(solver,"particles",ants);Set(solver,"particleGroups",new List<ParticleGroup>{group});
            Call(solver,"particleCheckParentVoxel");Call(solver,"particleMoveAndDeposit",0L,0L);Call(solver,"particleCheckParentVoxel");
            Check(Get<bool>(ant,"antDepartingNest")&&!ant.foundFood,"delivery starts protected departure");
            Near(ant.pPlane.Origin.DistanceTo(home),0,"delivery at home");
            for(int step=1;step<=6;step++)
            {
                // Even a steering force aimed strongly into the nest cannot defeat departure.
                ant.moveVector=-axis*10000;Set(ant,"followingFood",true);
                Call(solver,"particleSense_Ant");Call(solver,"particleMoveAndDeposit",0L,0L);Call(solver,"particleCheckParentVoxel");
                Near(ant.pPlane.Origin.DistanceTo(home),step*3,"protected departure progress",1e-8);
                Check(ants.Count==1&&flat.Sum(v=>v.particleCount)==1,"departure preserves population");
            }
            ant.moveVector=side*10000;Call(solver,"particleMoveAndDeposit",0L,0L);
            Check(!Get<bool>(ant,"antDepartingNest"),"departure ends outside nest region");
            Check(Vector3d.Multiply(ant.pPlane.XAxis,side)>.9,"normal steering resumes");
            // Edible food still interrupts departure and is consumed once.
            ant.pPlane.Origin=home+axis*3;ant.parentVoxel=At(ant.pPlane.Origin);Set(ant,"antDepartingNest",true);ant.foundFood=false;ant.parentVoxel.antFood=.5;
            Call(solver,"particleCheckParentVoxel");Check(ant.foundFood&&!Get<bool>(ant,"antDepartingNest")&&ant.parentVoxel.antFood==0,"pickup interrupts departure");
            cases++;
        }
        Results.Add(new{Test="protected-nest-departure",Cases=cases,Passed=true});Console.WriteLine($"PASS {cases} protected departures: delivery, opposing scent force, exit, resumed steering and pickup.");
    }
}
