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
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static Nuclei3.ParticleGroup;
using System.Drawing;

namespace Nuclei3
{
    public class ParticleGroup
    {
        #region fields

        public List<Particle> particles = new List<Particle>();

        public double speed, sensorDistance, depositValue = 0;
        public double wanderFrequency, foodWanderFrequency, baseWanderFrequency = 0;
        public int sensorAngle, rotationAngle = 0;
        public Color color;

        public bool ant = false;
        #endregion

        //-------------------------------------------------------------------

        #region constructors

        public ParticleGroup(double _speed, double _sensorDistance, int _sensorAngle, int _rotationAngle, double _depositValue,
            double _wanderFrequency, double _foodWanderFrequency, double _baseWanderFrequency, Color _color)
        {
            speed = _speed;
            sensorDistance = _sensorDistance;
            sensorAngle = _sensorAngle;
            rotationAngle = _rotationAngle;
            depositValue = _depositValue;
            wanderFrequency = _wanderFrequency;
            foodWanderFrequency = _foodWanderFrequency;
            baseWanderFrequency = _baseWanderFrequency;
            color = _color;
        }

        #region Goo Addition
        public ParticleGroup ()
        {
            speed = 1.3;
            sensorDistance = 6;
            sensorAngle = 45;
            rotationAngle = 45;
            depositValue = 1;
            wanderFrequency = 0;
            foodWanderFrequency = 0;
            baseWanderFrequency = 0;
        }

        public ParticleGroup Duplicate()
        {
            ParticleGroup dup = new ParticleGroup(speed,sensorDistance,sensorAngle,rotationAngle,depositValue,wanderFrequency,foodWanderFrequency,baseWanderFrequency,color);
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

 

        #endregion

        //-------------------------------------------------------------------

        #region methods

        public void updateWanderFrequency()
        {
            if (wanderFrequency < 0) wanderFrequency = 0;
            if (wanderFrequency > 1) wanderFrequency = 1;
            wanderFrequency = 1 - wanderFrequency;

            wanderFrequency = Math.Floor(Math.Pow(wanderFrequency, 3) * particles.Count / 40);
            if (wanderFrequency < 1) wanderFrequency = 1;
        }


        public void updateFoodWanderFrequency()
        {
            if (foodWanderFrequency < 0) foodWanderFrequency = 0;
            if (foodWanderFrequency > 1) foodWanderFrequency = 1;
            foodWanderFrequency = 1 - foodWanderFrequency;

            foodWanderFrequency = (int)Math.Floor(Math.Pow(foodWanderFrequency, 3) * particles.Count / 40);
            if (foodWanderFrequency < 1) foodWanderFrequency = 1;
        }

        public void updateBaseWanderFrequency()
        {
            if (baseWanderFrequency < 0) baseWanderFrequency = 0;
            if (baseWanderFrequency > 1) baseWanderFrequency = 1;

            baseWanderFrequency = (int)Math.Floor(baseWanderFrequency * particles.Count / 40);
            if (baseWanderFrequency < 1) baseWanderFrequency = 1;
        }

        #endregion


        /// <summary>
        /// Particle Goo wrapper class, makes sure Particle can be used in Grasshopper.
        /// </summary>

        public class ParticleGroupGoo : GH_Goo<ParticleGroup>
        {
            #region constructors

            public ParticleGroupGoo()
            {
                this.Value = new ParticleGroup();
            }

            public ParticleGroupGoo(ParticleGroup PG)
            {
                if (PG == null)
                {
                    PG = new ParticleGroup();
                }
                this.Value = PG;
            }

            public override IGH_Goo Duplicate()
            {
                return DuplicateParticleGroup();
            }
            public ParticleGroupGoo DuplicateParticleGroup()
            {
                return new ParticleGroupGoo(Value == null ? new ParticleGroup() : Value.Duplicate());
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
                    return "Null Particle Group";
                }
                else
                {
                    return "Particle Group";
                }
            }
            public override string TypeName
            {
                get
                {
                    return "Particle Group";
                }
            }
            public override string TypeDescription
            {
                get
                {
                    return "Particle Group";
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
                Value = (ParticleGroup)source;
                return true;
            }

            #endregion
        }

        public class ParticleGroupParameter : GH_PersistentParam<ParticleGroupGoo>
        {
            public ParticleGroupParameter()
                : base(new GH_InstanceDescription("Particle Group List", "Particle Group List", "Particle Group List", "Nuclei3", "Parameters"))
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
                    return new Guid("5cf001f6-7143-4ada-8d98-a9f53efbed6a");
                }
            }

            protected override GH_GetterResult Prompt_Singular(ref ParticleGroupGoo value)
            {
                return GH_GetterResult.cancel;

            }
            protected override GH_GetterResult Prompt_Plural(ref List<ParticleGroupGoo> values)
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
}
