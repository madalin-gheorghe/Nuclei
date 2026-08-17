using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Nuclei4
{
    public class Particle_Extractor_Vector : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Extractor_Vector class.
        /// </summary>
        public Particle_Extractor_Vector()
          : base("Extract Particle Vectors", "Extract Particle Vectors",
              "Extract Particle Directions",
              "Nuclei4", " Particles")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Particles", "particles", "Input Particles", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddVectorParameter("Particle Vectors", "particleVec", "Particle Vectors", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.quinary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            particles = new List<Particle>();
            DA.GetData(0, ref particles);
            ParticleList.EnsureCpuStateCurrent(particles);

            outputParticlesAngle = new List<Vector3d>();

            for (int i = 0; i < particles.Count; i++)
            {
                Particle p = particles[i];

                outputParticlesAngle.Add(p.pPlane.XAxis);
            }

            DA.SetDataList(0, outputParticlesAngle);
        }

        //-------------------------------------------------------------------

        //inputs
        List<Particle> particles;

        //-------------------------------------------------------------------

        //outputs
        List<Vector3d> outputParticlesAngle;

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
                return Nuclei4.Properties.Resources.ParticleVectors;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("59e6dba6-2cec-4873-8b54-9f099d3599c2"); }
        }
    }
}
