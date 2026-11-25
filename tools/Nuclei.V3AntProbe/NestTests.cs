using Nuclei3;
using Rhino.Geometry;
using Particle = Nuclei3.Particle;

internal static partial class Probe
{
    static void NestTests()
    {
        int cases=0;
        foreach(var original in Shapes) foreach(bool wrap in new[]{false,true})
        {
            var shape=new Shape(original.Name,original.X==1?1:33,original.Y==1?1:33,original.Z==1?1:33);
            var solver=Create(shape,wrap,1,false);Set(solver,"iteration",3);
            var flat=Get<Voxel[]>(solver,"voxelFlat");foreach(var v in flat){v.towardsFoodPheromone=0;v.towardsBasePheromone=0;}
            var home=new Point3d(shape.X==1?.5:16.5,shape.Y==1?.5:16.5,shape.Z==1?.5:16.5);
            var axis=shape.X==1?Vector3d.YAxis:Vector3d.XAxis;var side=shape.Z==1?Vector3d.YAxis:Vector3d.ZAxis;
            Voxel At(Point3d pos)=>flat[((int)pos.X*shape.Y+(int)pos.Y)*shape.Z+(int)pos.Z];
            var group=new ParticleGroup{ant=true,speed=2,sensorDistance=2,depositValue=.2};
            var ants=new List<Particle>();
            foreach(int sign in new[]{-1,1})
            {
                var pos=home+axis*(sign*1.1);
                ants.Add(new Particle(new Plane(pos,axis*sign,side)){parentParticleGroup=group,home=new Plane(home,axis,side),parentVoxel=At(pos),foundFood=true,age=40,moveVector=side});
            }
            foreach(var ant in ants){var remembered=home;if(shape.X==1)remembered.X=0;if(shape.Y==1)remembered.Y=0;if(shape.Z==1)remembered.Z=0;ant.home.Origin=remembered;}
            group.particles=ants;Set(solver,"particles",ants);Set(solver,"particleGroups",new List<ParticleGroup>{group});
            // Blend endpoints and intermediate values against the specified linear weights.
            var probe=ants[0];
            foreach(double distance in new[]{4.0,3.0,2.0,1.0,.01})
            {
                probe.pPlane.Origin=home-axis*distance;double t=Math.Max(0,1-distance/4);
                var expected=side*(1-t)+axis*t;expected.Unitize();
                var actual=(Vector3d)Call(solver,"blendAntNestApproach",probe,side);
                Near((expected-actual).Length,0,"linear nest blend "+shape.Name,1e-12);
            }
            probe.pPlane.Origin=home-axis*1.1;
            Call(solver,"particleCheckParentVoxel");
            Check(ants.Count==2,"arrival preparation culled ants");
            foreach(var ant in ants)ant.moveVector=side;
            Call(solver,"particleMoveAndDeposit",0L,0L);
            Check(ants.All(a=>a.pPlane.Origin.DistanceTo(home)<1e-10),"final step must land exactly at nest");
            Check(At(home).particleCount==2,"two arrivals must share a counted voxel");
            Near(At(home).towardsFoodPheromone,2*.2*(.02+.98*Math.Pow(1-Math.Min(1,41.0/(int)Call(solver,"antLaunchDuration",ants[0],group)),2.5)),"shared food pheromone deposits",1e-12);
            Call(solver,"particleCheckParentVoxel");
            Check(ants.Count==2&&ants.All(a=>!a.foundFood&&a.age==1),"both deliveries reset search state");
            Check(At(home).particleCount==2,"shared ants survived recount");
            // Departures keep sharing until they leave their nest region.
            group.speed=1;
            var departure=ants.Select((a,i)=>(a,direction:axis*(i==0?-1:1))).ToDictionary(v=>v.a,v=>v.direction);
            for(int step=0;step<6;step++)
            {
                for(int i=0;i<ants.Count;i++){var direction=departure[ants[i]];ants[i].pPlane=new Plane(ants[i].pPlane.Origin,direction,side);ants[i].moveVector=direction;}
                Call(solver,"particleMoveAndDeposit",0L,0L);Call(solver,"particleCheckParentVoxel");
                Check(ants.Count==2&&flat.Sum(v=>v.particleCount)==2&&flat.All(v=>v.particleCount>=0),"departure occupancy conservation");
            }
            Check(ants.All(a=>!(bool)Call(solver,"antSharesNestVoxel",a,a.pPlane.Origin)),"ants leave shared region");
            // Shared pickup consumes a fractional food source only once.
            foreach(var ant in ants){ant.pPlane.Origin=home+axis;ant.parentVoxel=At(ant.pPlane.Origin);ant.foundFood=false;}
            At(home+axis).antFood=.5;Call(solver,"particleCheckParentVoxel");
            Check(ants.Count(a=>a.foundFood)==1&&At(home+axis).antFood==0,"shared food consumption is atomic");
            At(home).maxDensity=0;group.speed=2;
            foreach(var ant in ants){ant.foundFood=true;ant.moveVector=-axis;}
            Call(solver,"particleMoveAndDeposit",0L,0L);
            Check(ants.All(a=>a.parentVoxel!=At(home)&&a.parentVoxel.maxDensity!=0),"overlap must not bypass a blocked nest voxel");
            cases++;
        }
        Results.Add(new{Test="nest-blend-shared-arrival-departure",Cases=cases,Passed=true});
        Console.WriteLine($"PASS {cases} nest fixtures: linear takeover, exact arrival, overlap, deposits, delivery, departure counts and atomic food pickup.");
    }
}
