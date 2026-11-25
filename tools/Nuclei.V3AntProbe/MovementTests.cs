using Nuclei3;
using Rhino.Geometry;
using Particle = Nuclei3.Particle;

internal static partial class Probe
{
    static void MovementTests()
    {
        int cases=0;
        foreach(Shape original in Shapes)foreach(bool wrap in new[]{false,true})
        foreach(int axis in Enumerable.Range(0,3).Where(a=>(a==0?original.X:a==1?original.Y:original.Z)>1))
        {
            Shape shape=new(original.Name,original.X==1?1:31,original.Y==1?1:31,original.Z==1?1:31);
            Solver solver=Create(shape,wrap,2,false);
            Voxel[] flat=Get<Voxel[]>(solver,"voxelFlat");
            foreach(Voxel v in flat){v.towardsFoodPheromone=0;v.towardsBasePheromone=0;}
            Set(solver,"iteration",3);
            Point3d origin=new(shape.X==1?.5:15.5,shape.Y==1?.5:15.5,shape.Z==1?.5:15.5);
            Vector3d direction=axis==0?Vector3d.XAxis:axis==1?Vector3d.YAxis:Vector3d.ZAxis;
            Vector3d side=axis==0?(shape.Y==1?Vector3d.ZAxis:Vector3d.YAxis):Vector3d.XAxis;
            if(shape.X==1)side=axis==1?Vector3d.ZAxis:Vector3d.YAxis;
            Voxel At(Point3d position)=>flat[((int)position.X*shape.Y+(int)position.Y)*shape.Z+(int)position.Z];
            var group=new ParticleGroup{ant=true,speed=1.1,sensorDistance=1,depositValue=.2};
            var p=new Particle(new Plane(origin,direction,side)){parentParticleGroup=group,parentVoxel=At(origin),home=new Plane(origin-direction*8,direction,side),age=20,moveVector=direction};
            var particles=new List<Particle>{p};Set(solver,"particles",particles);Set(solver,"particleGroups",new List<ParticleGroup>{group});
            Voxel target=At(origin+direction*1.1);target.antFood=.5;
            Call(solver,"particleMoveAndDeposit",0L,0L);
            Check(p.parentVoxel==target&&p.highDeposit,"new voxel movement failed "+shape.Name+axis);
            Near(target.towardsBasePheromone,.9*.2*(.02+.98*Math.Pow(1-Math.Min(1,20.0/(int)Call(solver,"antLaunchDuration",p,group)),2.5)),"searching base deposit");
            Near(target.towardsFoodPheromone,0,"searching emitted food trail");
            Call(solver,"particleCheckParentVoxel");
            Check(p.foundFood&&p.age==1&&target.antFood==0,"post-move pickup failed");
            // The return trip leaves food scent; age one follows the pickup.
            p.moveVector=direction;Voxel second=At(p.pPlane.Origin+direction*1.1);
            Call(solver,"particleMoveAndDeposit",0L,0L);
            Check(p.parentVoxel==second,"carrying move failed");
            Near(second.towardsFoodPheromone,.2*(.02+.98*Math.Pow(1-1.0/(int)Call(solver,"antLaunchDuration",p,group),2.5)),"carrying food deposit");
            Near(second.towardsBasePheromone,0,"carrying emitted base trail");
            double scent=second.towardsFoodPheromone;
            group.speed=.01;p.moveVector=direction;Call(solver,"particleMoveAndDeposit",0L,0L);
            Check(p.parentVoxel==second&&!p.highDeposit,"same-voxel move state");Near(second.towardsFoodPheromone,scent,"same-voxel move deposited");

            // A stationary occupant rejects movement and prevents deposition.
            group.speed=1.1;Point3d next=p.pPlane.Origin+direction*1.1;
            var stoppedGroup=new ParticleGroup{ant=true,speed=0,sensorDistance=1,depositValue=.2};
            var occupant=new Particle(new Plane(next,direction,side)){parentParticleGroup=stoppedGroup,parentVoxel=At(next),home=p.home,age=100,moveVector=direction};
            particles.Add(occupant);Voxel before=p.parentVoxel;p.moveVector=direction;
            Call(solver,"particleMoveAndDeposit",0L,0L);
            Check(p.parentVoxel==before&&!p.highDeposit,"occupied target accepted");Near(before.towardsFoodPheromone,scent,"blocked move deposited");
            Check(particles.Select(v=>v.parentVoxel).Distinct().Count()==particles.Count,"shared voxel occupancy");

            // Force a boundary crossing along every active axis, including Z.
            particles.Remove(occupant);group.speed=2;
            Point3d edge=origin;if(axis==0)edge.X=29.5;else if(axis==1)edge.Y=29.5;else edge.Z=29.5;
            p.pPlane=new Plane(edge,direction,side);p.parentVoxel=At(edge);p.moveVector=direction;p.antLaunchBoundaryHit=false;
            Call(solver,"particleMoveAndDeposit",0L,0L);
            Check(p.antLaunchBoundaryHit&&p.parentVoxel!=null,"boundary launch termination");
            Check(p.pPlane.Origin.X>=0&&p.pPlane.Origin.X<shape.X&&p.pPlane.Origin.Y>=0&&p.pPlane.Origin.Y<shape.Y&&p.pPlane.Origin.Z>=0&&p.pPlane.Origin.Z<shape.Z,"boundary escaped grid");
            if(wrap)Near(axis==0?p.pPlane.Origin.X:axis==1?p.pPlane.Origin.Y:p.pPlane.Origin.Z,.1,"wrapped coordinate");
            cases++;
        }
        Results.Add(new{Test="movement-deposition-boundaries",Cases=cases,Passed=true});
        Console.WriteLine($"PASS {cases} movement fixtures: all active axes, searching/returning deposits, pickup order, same-voxel/occupied no-deposit, wrapping and boundary launch termination.");
    }
}
