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
          : base("Construct Slime Particles", "Slime Particles",
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
            pManager.AddPointParameter("Initial Particle Positions", "particlePos", "Initial Particle Positions", GH_ParamAccess.list);
            //1
            pManager.AddNumberParameter("Speed", "speed", "Speed of particle movement", GH_ParamAccess.item, 1.3);
            //2
            pManager.AddNumberParameter("Sensor Distance", "sensorDistance", "Maximum distance for sensing surrounding voxel values", GH_ParamAccess.item, 6);
            //3
            pManager.AddNumberParameter("Sensor Angle", "sensorAngle", "Angle of sensing surrounding voxel values", GH_ParamAccess.item, 45);
            //4
            pManager.AddNumberParameter("Rotation Angle", "rotationAngle", "Angle of rotation for the particles", GH_ParamAccess.item, 45);
            //5
            pManager.AddNumberParameter("Deposit", "deposit", "The Amount of Chemoattractants Each Particle Deposits in the Environment", GH_ParamAccess.item, 1);
            //6
            pManager.AddNumberParameter("Wander", "wander", "The Frequency of Random Directions. VALUES FROM 0 TO 1. The Larger the Value the More Chaotic", GH_ParamAccess.item, 0);
            //7
            pManager.AddColourParameter("Colour", "colour", "The Display Color of The Particles", GH_ParamAccess.item, Color.FromArgb(125, 220, 255, 0));
            pManager[7].Optional = true;
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
            initialPtList = new List<Point3d>();
            DA.GetDataList(0, initialPtList);

            DA.GetData("Speed", ref particleSpeed);
            DA.GetData("Sensor Distance", ref particleSensorDistance);
            DA.GetData("Sensor Angle", ref particleSensorAngle);
            DA.GetData("Rotation Angle", ref particleRotationAngle);
            DA.GetData("Deposit", ref particleDepositValue);

            DA.GetData("Wander", ref particleWander);

            DA.GetData("Colour", ref colour);

            ParticleGroup PG = new ParticleGroup(particleSpeed, particleSensorDistance, (int) Math.Floor(particleSensorAngle), (int) Math.Floor(particleRotationAngle), particleDepositValue,
                particleWander, -1, -1, colour);
            PG.ant = false;
            createParticles(PG);

            DA.SetData(0, PG);

            this.Message = "Particles: " + outputParticles.Count;
        }

        //-------------------------------------------------------------------

        //inputs
        List<Point3d> initialPtList;

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
            ConcurrentBag<Particle> particleGroupConcurrent = new ConcurrentBag<Particle>();

            Parallel.For(0, initialPtList.Count, i =>
            {
                Point3d pt = initialPtList[i];
                Vector3d xVector = new Vector3d(1, 0, 0);
                Vector3d yVector = new Vector3d(0, 1, 0);
                Vector3d zVector = new Vector3d(0, 0, 1);
                Plane pPlane = new Plane(new Point3d(pt.X, pt.Y, pt.Z), xVector, yVector);

                //random vector for particle
                System.Random random = new System.Random(11 * i);
                double particleAngleX = random.NextDouble() * 4 * Math.PI;
                double particleAngleY = random.NextDouble() * 4 * Math.PI;
                double particleAngleZ = random.NextDouble() * 4 * Math.PI;
                pPlane.Rotate(particleAngleZ, zVector);
                pPlane.Rotate(particleAngleX, xVector);
                pPlane.Rotate(particleAngleY, yVector);

                Particle P = new Particle(pPlane);
                P.parentParticleGroup = _PG;
                particleGroupConcurrent.Add(P);
            }
            );

            outputParticles = new List<Particle>();
            outputParticles = particleGroupConcurrent.ToList();

            _PG.particles = new List<Particle>(outputParticles);
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
            get { return new Guid("d64158f3-9ccd-4aa6-954f-8b2e12113bc3"); }
        }
    }
}