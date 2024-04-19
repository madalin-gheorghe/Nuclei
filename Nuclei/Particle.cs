using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Grasshopper;
using Rhino;
using System.Diagnostics.Eventing.Reader;

namespace Nuclei3
{
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

        public void alignToVector(Vector3d V)
        {
            Vector3d projectedX = new Vector3d(pPlane.XAxis.X, pPlane.XAxis.Y, 0);
            Vector3d projectedV = new Vector3d(V.X, V.Y, 0);
            double angleXY = Vector3d.VectorAngle(projectedX, projectedV, Plane.WorldXY);
            pPlane.Rotate(angleXY, Plane.WorldXY.ZAxis, pPlane.Origin);

            Plane vPlane = new Plane(pPlane.Origin, pPlane.XAxis, V);
            double angle = Vector3d.VectorAngle(pPlane.XAxis, V, vPlane);

            pPlane.Rotate(angle, vPlane.ZAxis, pPlane.Origin);
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
