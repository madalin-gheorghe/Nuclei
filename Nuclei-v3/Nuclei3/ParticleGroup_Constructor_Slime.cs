using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Threading.Tasks;
using System.Drawing;
using static Nuclei3.ParticleGroup;

namespace Nuclei3
{
    public class ParticleGroup_Constructor_Slime : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ParticleConstructor class.
        /// </summary>
        public ParticleGroup_Constructor_Slime()
          : base("Construct Slime Particles", "Construct Slime Particles",
              "Construct and Define Slime Particle Properties",
              "Nuclei3", " Particles")
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
            pManager.AddNumberParameter("Wander", "wander", "The Frequency of Random Directions. VALUES FROM 0 TO 1. The Larger the Value the More Chaotic", GH_ParamAccess.item, 0);
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

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //get values
            inputVoxels = null;
            DA.GetData("Voxel Field", ref inputVoxels);

            initialPtList = new List<Point3d>();
            DA.GetDataList(1, initialPtList);

            DA.GetData("Particle Count", ref generatedParticleCount);
            if (generatedParticleCount < 0) generatedParticleCount = 0;

            DA.GetData("Speed", ref particleSpeed);
            DA.GetData("Sensor Distance", ref particleSensorDistance);
            DA.GetData("Sensor Angle", ref particleSensorAngle);
            DA.GetData("Rotation Angle", ref particleRotationAngle);
            DA.GetData("Deposit", ref particleDepositValue);

            DA.GetData("Wander", ref particleWander);

            DA.GetData("Colour", ref colour);

            ParticleGroup PG = new ParticleGroup(particleSpeed, particleSensorDistance, (int) Math.Floor(particleSensorAngle), (int) Math.Floor(particleRotationAngle), particleDepositValue,
                particleWander, -1, colour);
            PG.ant = false;
            createParticles(PG);

            DA.SetData(0, PG);

            this.Message = "Particles: " + outputParticles.Count;
        }

        //-------------------------------------------------------------------

        //inputs
        List<Point3d> initialPtList;
        int generatedParticleCount;
        Voxel[,,] inputVoxels;

        double particleSpeed;
        double particleSensorAngle;
        double particleSensorDistance;
        double particleRotationAngle;
        double particleDepositValue;

        double particleWander;

        Color colour;

        //outputs
        List<Particle> outputParticles;

        //-------------------------------------------------------------------

        void createParticles(ParticleGroup _PG)
        {
            if (initialPtList != null && initialPtList.Count > 0)
            {
                outputParticles = ParticleGenerator.CreateFromPoints(initialPtList, _PG);
            }
            else
            {
                VoxelGridData voxelData = inputVoxels != null ? VoxelGridRegistry.GetOrCapture(inputVoxels, 1.0) : null;
                outputParticles = voxelData != null
                    ? ParticleGenerator.CreateRandomVoxelCenterParticles(generatedParticleCount, _PG, voxelData)
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
                return Nuclei3.Properties.Resources.Particle_Slime;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("5a2dfaa4-b7fb-4b04-a86c-e939f01886dd"); }
        }
    }
}

