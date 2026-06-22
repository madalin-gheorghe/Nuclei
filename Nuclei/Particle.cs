using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Grasshopper;
using Rhino;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;

namespace Nuclei3
{
    public class ParticlePreviewCache
    {
        public PointCloud SlimePointCloud = new PointCloud();
        public PointCloud AntPointCloud1 = new PointCloud();
        public PointCloud AntPointCloud2 = new PointCloud();
        public BoundingBox ClippingBox = BoundingBox.Empty;
        public int ParticleCount = 0;
        public bool HasPoint = false;
        public bool IsValid = false;

        internal readonly object SyncRoot = new object();

        public void BeginBuild(int particleCount)
        {
            SlimePointCloud = new PointCloud();
            AntPointCloud1 = new PointCloud();
            AntPointCloud2 = new PointCloud();
            ClippingBox = BoundingBox.Empty;
            ParticleCount = particleCount;
            HasPoint = false;
            IsValid = false;
        }

        public void Invalidate(int particleCount)
        {
            SlimePointCloud = new PointCloud();
            AntPointCloud1 = new PointCloud();
            AntPointCloud2 = new PointCloud();
            ClippingBox = BoundingBox.Empty;
            ParticleCount = particleCount;
            HasPoint = false;
            IsValid = false;
        }

        internal void Merge(ParticlePreviewBuildCache local)
        {
            if (local == null || !local.HasPoint) return;

            local.AppendTo(SlimePointCloud, AntPointCloud1, AntPointCloud2);

            if (!HasPoint)
            {
                ClippingBox = local.ClippingBox;
                HasPoint = true;
            }
            else
            {
                ClippingBox.Union(local.ClippingBox);
            }
        }

        public void CompleteBuild()
        {
            if (HasPoint)
            {
                ClippingBox.Inflate(Math.Max(Globals.voxelSize, 1.0));
            }

            IsValid = true;
        }
    }

    internal sealed class ParticlePreviewBuildCache
    {
        readonly List<Point3d> slimePoints;
        readonly List<Color> slimeColors;
        readonly List<Point3d> antPoints1;
        readonly List<Color> antColors1;
        readonly List<Point3d> antPoints2;
        readonly List<Color> antColors2;
        Dictionary<ParticleGroup, Color> foundFoodColors;

        public BoundingBox ClippingBox = BoundingBox.Empty;
        public bool HasPoint = false;

        public ParticlePreviewBuildCache(int initialCapacity)
        {
            slimePoints = new List<Point3d>(initialCapacity);
            slimeColors = new List<Color>(initialCapacity);
            antPoints1 = new List<Point3d>();
            antColors1 = new List<Color>();
            antPoints2 = new List<Point3d>();
            antColors2 = new List<Color>();
        }

        public void AddParticle(Particle particle)
        {
            ParticleGroup group = particle.parentParticleGroup;
            Point3d point = particle.pPlane.Origin;
            IncludePoint(point);

            if (group == null || !group.ant)
            {
                slimePoints.Add(point);
                slimeColors.Add(group != null ? group.color : Color.White);
                return;
            }

            if (!particle.foundFood)
            {
                antPoints1.Add(point);
                antColors1.Add(group.color);
                return;
            }

            antPoints2.Add(point);
            antColors2.Add(GetFoundFoodColor(group));
        }

        public void AppendTo(PointCloud slimePointCloud, PointCloud antPointCloud1, PointCloud antPointCloud2)
        {
            AppendPointsWithColors(slimePointCloud, slimePoints, slimeColors);
            AppendPointsWithColors(antPointCloud1, antPoints1, antColors1);
            AppendPointsWithColors(antPointCloud2, antPoints2, antColors2);
        }

        static void AppendPointsWithColors(PointCloud pointCloud, List<Point3d> points, List<Color> colors)
        {
            if (points.Count == 0) return;

            int startIndex = pointCloud.Count;
            pointCloud.AddRange(points);

            for (int i = 0; i < colors.Count; i++)
            {
                pointCloud[startIndex + i].Color = colors[i];
            }
        }

        void IncludePoint(Point3d point)
        {
            if (!HasPoint)
            {
                ClippingBox = new BoundingBox(point, point);
                HasPoint = true;
                return;
            }

            ClippingBox.Union(point);
        }

        Color GetFoundFoodColor(ParticleGroup group)
        {
            if (foundFoodColors == null)
            {
                foundFoodColors = new Dictionary<ParticleGroup, Color>();
            }

            Color color;
            if (!foundFoodColors.TryGetValue(group, out color))
            {
                color = BrightFoundFoodColor(group.color);
                foundFoodColors[group] = color;
            }

            return color;
        }

        static Color BrightFoundFoodColor(Color color)
        {
            int r = (int)(color.R * 1.75);
            if (r > 255) r = 255;

            int g = (int)(color.G * 1.75);
            if (g > 255) g = 255;

            int b = (int)(color.B * 1.75);
            if (b > 255) b = 255;

            return Color.FromArgb(175, r, g, b);
        }
    }

    public class ParticleList : List<Particle>
    {
        public ParticlePreviewCache PreviewCache = new ParticlePreviewCache();

        public ParticleList()
        {
        }

        public ParticleList(IEnumerable<Particle> particles)
            : base(particles)
        {
        }
    }

    public class Particle
    {
        #region fields

        //parent
        public ParticleGroup parentParticleGroup;

        //particle plane
        public Plane pPlane;

        //voxel
        public Voxel parentVoxel;

        //deposit
        public bool highDeposit;

        //neighbours
        public int neighbourCount_Die = 0;
        public int neighbourCount_Div = 0;

        //death settings
        public bool divide = false;
        public bool die = false;

        //trails
        public List<Point3d> trails = new List<Point3d>();

        //ant
        public Plane home;
        public int age = 0;

        public Vector3d moveVector;

        public bool foundFood = false;
        #endregion

        //-------------------------------------------------------------------

        #region constructors

        public Particle(Plane _pPlane)
        {
            pPlane = _pPlane;
            age = 0;

            moveVector = new Vector3d();
        }

        #region Goo Addition
        public Particle()
        {
            pPlane = new Plane(new Point3d(0, 0, 0), new Vector3d(0, 0, 0));
        }
        public Particle Duplicate()
        {
            Particle dup = new Particle(PPlane);
            return dup;
        }
        #endregion

        #endregion

        //-------------------------------------------------------------------

        #region Goo Properties
        public bool IsValid
        {
            get
            {
                return true;
            }
        }
        public Plane PPlane 
        { 
            get 
            {
                return pPlane; 
            }

            set
            {
                pPlane = value;
            }
        }

        #endregion

        //-------------------------------------------------------------------

        #region methods

        public void alignToVectorField()
        {
            Vector3d projectedX = new Vector3d(pPlane.XAxis.X, pPlane.XAxis.Y, 0);
            Vector3d projectedV = new Vector3d(parentVoxel.voxelVector.X, parentVoxel.voxelVector.Y, 0);
            double angleXY = Vector3d.VectorAngle(projectedX, projectedV, Plane.WorldXY);
            pPlane.Rotate(angleXY, Plane.WorldXY.ZAxis, pPlane.Origin);

            Plane vPlane = new Plane(pPlane.Origin, pPlane.XAxis, parentVoxel.voxelVector);
            double angle = Vector3d.VectorAngle(pPlane.XAxis, parentVoxel.voxelVector, vPlane);

            pPlane.Rotate(angle, vPlane.ZAxis, pPlane.Origin);
        }

        //-------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void alignToVector(Vector3d V)
        {
            if (!V.Unitize())
            {
                return;
            }

            alignToUnitVector(V);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void alignToUnitVector(Vector3d V)
        {
            Vector3d normal = pPlane.ZAxis;
            if (normal.Unitize())
            {
                double normalDot = V.X * normal.X + V.Y * normal.Y + V.Z * normal.Z;
                if (Math.Abs(normalDot) < 1e-9)
                {
                    Vector3d yAxis = Vector3d.CrossProduct(normal, V);
                    if (yAxis.Unitize())
                    {
                        pPlane = new Plane(pPlane.Origin, V, yAxis);
                        return;
                    }
                }
            }

            Vector3d projectedX = new Vector3d(pPlane.XAxis.X, pPlane.XAxis.Y, 0);
            Vector3d projectedV = new Vector3d(V.X, V.Y, 0);
            double angleXY = Vector3d.VectorAngle(projectedX, projectedV, Plane.WorldXY);
            pPlane.Rotate(angleXY, Plane.WorldXY.ZAxis, pPlane.Origin);

            Plane vPlane = new Plane(pPlane.Origin, pPlane.XAxis, V);
            double angle = Vector3d.VectorAngle(pPlane.XAxis, V, vPlane);

            pPlane.Rotate(angle, vPlane.ZAxis, pPlane.Origin);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void alignToUnitVectorPlanarXY(Vector3d V)
        {
            Vector3d yAxis = new Vector3d(-V.Y, V.X, 0);
            pPlane = new Plane(pPlane.Origin, V, yAxis);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void alignToUnitVectorPlanarXZ(Vector3d V)
        {
            Vector3d yAxis = new Vector3d(V.Z, 0, -V.X);
            pPlane = new Plane(pPlane.Origin, V, yAxis);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void alignToUnitVectorPlanarYZ(Vector3d V)
        {
            Vector3d yAxis = new Vector3d(0, -V.Z, V.Y);
            pPlane = new Plane(pPlane.Origin, V, yAxis);
        }

        #endregion
    }

    /// <summary>
    /// Particle Goo wrapper class, makes sure Particle can be used in Grasshopper.
    /// </summary>

    public class ParticleGoo : GH_Goo<Particle>
    {

        #region constructors

        public ParticleGoo()
        {
            this.Value = new Particle();
        }

        public ParticleGoo(Particle P)
        {
            if (P == null)
            {
                P = new Particle();
            }
            this.Value = P;
        }

        public override IGH_Goo Duplicate()
        {
            return DuplicateParticle();
        }
        public ParticleGoo DuplicateParticle()
        {
            return new ParticleGoo(Value == null ? new Particle() : Value.Duplicate());
        }

        #endregion

        //-------------------------------------------------------------------

        #region properties

        public override bool IsValid 
        {
            get
            {
                return true;
            }
        }

        public override string ToString()
        {
            if (Value == null)
            {
                return "Null Particles";
            }
            else
            {
                return "Particle";
            }
        }
        public override string TypeName
        {
            get 
            {
                return "Particle";
            }
        }
        public override string TypeDescription
        {
            get
            {
                return "Particle";
            }
        }

        #endregion

        #region casting methods

        public override bool CastTo<Q>(ref Q target)
        {
            target = (Q)(object)Value;
            return true;
        }

        public override bool CastFrom(object source)
        {
            Value = (Particle)source;
            return true;
        }

        #endregion
    }

    public class ParticleParameter : GH_PersistentParam <ParticleGoo>
    {
        public ParticleParameter()
            : base(new GH_InstanceDescription("Particle List", "Particle List", "Particle List", "Nuclei3", "Parameters"))
        {
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        public override GH_Exposure Exposure
        { 
            get
            {
                return GH_Exposure.hidden;
            }
        }

        public override Guid ComponentGuid
        {
            get 
            { 
                return new Guid("63caf026-d9be-43fb-801d-a786c8a3d65d"); 
            }
        }

        protected override GH_GetterResult Prompt_Singular(ref ParticleGoo value)
        {
            return GH_GetterResult.cancel;

        }
        protected override GH_GetterResult Prompt_Plural(ref List<ParticleGoo> values)
        {
            return GH_GetterResult.cancel;
        }

        /*
        protected override System.Windows.Forms.ToolStripMenuItem Menu_CustomSingleValueItem()
        {
            System.Windows.Forms.ToolStripMenuItem item = new System.Windows.Forms.ToolStripMenuItem();
            item.Text = "Not available";
            item.Visible = false;
            return item;
        }
        protected override System.Windows.Forms.ToolStripMenuItem Menu_CustomMultiValueItem()
        {
            System.Windows.Forms.ToolStripMenuItem item = new System.Windows.Forms.ToolStripMenuItem();
            item.Text = "Not available";
            item.Visible = false;
            return item;
        }
        */

    }




}
