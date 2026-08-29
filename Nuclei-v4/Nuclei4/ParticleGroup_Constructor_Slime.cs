using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using GH_IO.Serialization;
using Rhino.Geometry;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using static Nuclei4.ParticleGroup;

namespace Nuclei4
{
    public class ParticleGroup_Constructor_Slime : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ParticleConstructor class.
        /// </summary>
        public ParticleGroup_Constructor_Slime()
          : base("Construct Slime Particles", "Construct Slime Particles",
              "Construct and Define Slime Particle Properties",
              "Nuclei4", " Particles")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Voxel Field", "voxels", "Voxel field used for internal particle generation", GH_ParamAccess.item);
            //1
            pManager.AddPointParameter("Initial Particle Positions", "particlePos", "Initial Particle Positions", GH_ParamAccess.list);
            pManager[1].Optional = true;
            //2
            pManager.AddIntegerParameter("Particle Count", "count", "Number of particles to generate at random voxel centers when no initial positions are supplied", GH_ParamAccess.item, 0);
            //3
            pManager.AddNumberParameter("Speed", "speed", "Speed of particle movement", GH_ParamAccess.item, 1.3);
            //4
            pManager.AddNumberParameter("Sensor Distance", "sensorDistance", "Maximum distance for sensing surrounding voxel values", GH_ParamAccess.item, 6);
            //5
            pManager.AddNumberParameter("Sensor Angle", "sensorAngle", "Angle of sensing surrounding voxel values", GH_ParamAccess.item, 45);
            //6
            pManager.AddNumberParameter("Rotation Angle", "rotationAngle", "Angle of rotation for the particles", GH_ParamAccess.item, 45);
            //7
            pManager.AddNumberParameter("Deposit", "deposit", "The Amount of Chemoattractants Each Particle Deposits in the Environment", GH_ParamAccess.item, 1);
            //8
            pManager.AddNumberParameter("Exploration", "exploration", "Classic: frequency of random directions. Probabilistic: 0 chooses the strongest direction, while 1 samples all positive directions equally", GH_ParamAccess.item, 0);
            //9
            pManager.AddColourParameter("Colour", "colour", "The Display Color of The Particles", GH_ParamAccess.item, Color.FromArgb(125, 220, 255, 0));
            pManager[9].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //0
            pManager.RegisterParam(new ParticleGroupParameter(), "Output Particle Group", "particles", "OutputParticles");
            pManager[0].DataMapping = GH_DataMapping.Flatten;
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        public bool ProbabilisticSteering
        {
            get { return probabilisticSteering; }
        }

        public override void CreateAttributes()
        {
            m_attributes = new SlimeSteeringAttributes(this);
        }

        public void SetProbabilisticSteering(bool enabled)
        {
            if (probabilisticSteering == enabled) return;

            RecordUndoEvent("Change slime steering mode");
            probabilisticSteering = enabled;
            ExpireSolution(true);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("ProbabilisticSteering", probabilisticSteering);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            probabilisticSteering = false;
            reader.TryGetBoolean("ProbabilisticSteering", ref probabilisticSteering);
            bool result = base.Read(reader);
            normalizeExplorationParameterMetadata();
            return result;
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //get values
            inputVoxelField = null;
            VoxelFieldAccess.TryGet(DA, "Voxel Field", 1.0, out inputVoxelField);

            initialPtList = new List<Point3d>();
            DA.GetDataList(1, initialPtList);

            DA.GetData("Particle Count", ref generatedParticleCount);
            if (generatedParticleCount < 0) generatedParticleCount = 0;

            DA.GetData("Speed", ref particleSpeed);
            DA.GetData("Sensor Distance", ref particleSensorDistance);
            DA.GetData("Sensor Angle", ref particleSensorAngle);
            DA.GetData("Rotation Angle", ref particleRotationAngle);
            DA.GetData("Deposit", ref particleDepositValue);

            // Fixed Grasshopper inputs are serialized by ordinal. Older archives
            // restore this parameter's former "Wander" metadata during base.Read,
            // so name-based lookup can fail even though its wire and value survived.
            DA.GetData(8, ref particleWander);

            DA.GetData("Colour", ref colour);

            ParticleGroup PG = new ParticleGroup(particleSpeed, particleSensorDistance, (int) Math.Floor(particleSensorAngle), (int) Math.Floor(particleRotationAngle), particleDepositValue,
                particleWander, -1, colour);
            PG.ant = false;
            PG.connectedSteering = probabilisticSteering;
            createParticles(PG);

            DA.SetData(0, PG);

            this.Message = (probabilisticSteering ? "Probabilistic" : "Classic") + " | Particles: " + outputParticles.Count;
        }

        //-------------------------------------------------------------------

        //inputs
        List<Point3d> initialPtList;
        int generatedParticleCount;
        VoxelField inputVoxelField;

        double particleSpeed;
        double particleSensorAngle;
        double particleSensorDistance;
        double particleRotationAngle;
        double particleDepositValue;

        double particleWander;
        bool probabilisticSteering = false;

        Color colour;

        //outputs
        List<Particle> outputParticles;

        void normalizeExplorationParameterMetadata()
        {
            if (Params.Input.Count <= 8) return;

            IGH_Param parameter = Params.Input[8];
            parameter.Name = "Exploration";
            parameter.NickName = "exploration";
            parameter.Description = "Classic: frequency of random directions. Probabilistic: 0 chooses the strongest direction, while 1 samples all positive directions equally";
        }

        sealed class SlimeSteeringAttributes : GH_ComponentAttributes
        {
            const float ToggleHeight = 18f;
            const float ToggleMargin = 2f;
            RectangleF toggleBounds;

            public SlimeSteeringAttributes(ParticleGroup_Constructor_Slime owner)
                : base(owner)
            {
            }

            ParticleGroup_Constructor_Slime SlimeOwner
            {
                get { return (ParticleGroup_Constructor_Slime)Owner; }
            }

            protected override void Layout()
            {
                base.Layout();

                RectangleF bounds = Bounds;
                bounds.Height += ToggleHeight + ToggleMargin * 2;
                Bounds = bounds;
                toggleBounds = new RectangleF(
                    bounds.X + ToggleMargin,
                    bounds.Bottom - ToggleHeight - ToggleMargin,
                    bounds.Width - ToggleMargin * 2,
                    ToggleHeight);
            }

            protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
            {
                base.Render(canvas, graphics, channel);
                if (channel != GH_CanvasChannel.Objects) return;

                RectangleF classicBounds = toggleBounds;
                classicBounds.Width *= 0.5f;
                RectangleF probabilisticBounds = toggleBounds;
                probabilisticBounds.X = classicBounds.Right;
                probabilisticBounds.Width -= classicBounds.Width;

                bool probabilistic = SlimeOwner.ProbabilisticSteering;
                Color inactiveColor = Color.FromArgb(255, 146, 146, 161);
                Color selectedColor = Color.FromArgb(255, 226, 161, 62);
                using (Brush classicBrush = new SolidBrush(probabilistic ? inactiveColor : selectedColor))
                using (Brush probabilisticBrush = new SolidBrush(probabilistic ? selectedColor : inactiveColor))
                using (Pen borderPen = new Pen(Color.FromArgb(95, 95, 95)))
                using (StringFormat textFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.FillRectangle(classicBrush, classicBounds);
                    graphics.FillRectangle(probabilisticBrush, probabilisticBounds);
                    graphics.DrawRectangle(borderPen, toggleBounds.X, toggleBounds.Y, toggleBounds.Width, toggleBounds.Height);
                    graphics.DrawLine(borderPen, classicBounds.Right, toggleBounds.Top, classicBounds.Right, toggleBounds.Bottom);
                    graphics.DrawString("Classic", SystemFonts.MessageBoxFont, Brushes.Black, classicBounds, textFormat);
                    graphics.DrawString("Probabilistic", SystemFonts.MessageBoxFont, Brushes.Black, probabilisticBounds, textFormat);
                }
            }

            public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                if (e.Button == MouseButtons.Left && toggleBounds.Contains(e.CanvasLocation))
                {
                    SlimeOwner.SetProbabilisticSteering(e.CanvasLocation.X >= toggleBounds.X + toggleBounds.Width * 0.5f);
                    return GH_ObjectResponse.Handled;
                }

                return base.RespondToMouseDown(sender, e);
            }
        }

        //-------------------------------------------------------------------

        void createParticles(ParticleGroup _PG)
        {
            if (initialPtList != null && initialPtList.Count > 0)
            {
                outputParticles = ParticleGenerator.CreateFromPoints(initialPtList, _PG);
            }
            else
            {
                VoxelGridData voxelData = inputVoxelField != null ? inputVoxelField.Data : null;
                outputParticles = voxelData != null
                    ? ParticleGenerator.CreateScatteredParticles(generatedParticleCount, _PG, voxelData)
                    : new List<Particle>();
            }

            _PG.particles = outputParticles;
        }

        //-------------------------------------------------------------------

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Nuclei4.Properties.Resources.Particle_Slime;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("24ede5e7-2957-4f98-8f83-80c6f5dfd31f"); }
        }
    }
}
