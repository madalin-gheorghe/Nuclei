using Nuclei3;
using Rhino.Geometry;
using Particle = Nuclei3.Particle;

internal static partial class Probe
{
    static void TrailTests()
    {
        int cases=0;
        foreach(var original in Shapes)
        {
            var shape=new Shape(original.Name,original.X==1?1:33,original.Y==1?1:33,original.Z==1?1:33);
            var solver=Create(shape,false,1,false);var voxel=Get<Voxel[]>(solver,"voxelFlat")[0];
            foreach(double coordinate in new[]{5.5,16.5}) foreach(double speed in new[]{1.0,3.0})
            {
                var home=new Point3d(shape.X==1?.5:coordinate,shape.Y==1?.5:coordinate,shape.Z==1?.5:coordinate);
                int axes=(shape.X>1?1:0)+(shape.Y>1?1:0)+(shape.Z>1?1:0);
                double edge=Math.Max(coordinate-1,32-coordinate);
                int duration=(int)Math.Ceiling(.75*Math.Sqrt(axes)*edge/speed);
                var group=new ParticleGroup{ant=true,speed=speed};
                var ant=new Particle(Plane.WorldXY){home=new Plane(home,Vector3d.XAxis,Vector3d.YAxis),parentParticleGroup=group,parentVoxel=voxel};
                Check((int)Call(solver,"antLaunchDuration",ant,group)==duration,"individual launch distance/speed duration");
                foreach(bool carrying in new[]{false,true}) foreach(int age in new[]{0,duration/2,duration-1,duration,duration*2})
                {
                    ant.age=age;ant.foundFood=carrying;voxel.towardsFoodPheromone=0;voxel.towardsBasePheromone=0;
                    Call(solver,"depositAtVoxel",ant,voxel,group,.2);
                    double expected=.2*(.02+.98*Math.Pow(Math.Max(0,1-age/(double)duration),2.5))*(carrying?1:.9);
                    Near(carrying?voxel.towardsFoodPheromone:voxel.towardsBasePheromone,expected,"map-scaled trail age and 2% floor",1e-12);
                    cases++;
                }
            }
        }
        Results.Add(new{Test="map-scaled-trail-deposits",Cases=cases,Passed=true});
        Console.WriteLine($"PASS {cases} trail deposits across homes, speeds, phases and 2D/3D layouts.");
    }
}
