using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Nuclei3
{
    public class Particle_Extractor_NeighbourCount : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Extractor_Vector class.
        /// </summary>
        public Particle_Extractor_NeighbourCount()
          : base("Extract Particle Neighbour Count", "Particle Neighbour Count",
              "Extract Particle Neighbour Count",
              "Nuclei3", " Particles")
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
            pManager.AddNumberParameter("Particle Neighbour Count", "particleNC", "Particle Neighbour Count", GH_ParamAccess.list);
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

            outputParticlesNeighbours = new List<int>();

            for (int i = 0; i < particles.Count; i++)
            {
                Particle p = particles[i];

                outputParticlesNeighbours.Add(p.neighbourCount_Div);
            }

            DA.SetDataList(0, outputParticlesNeighbours);
        }

        //-------------------------------------------------------------------

        //inputs
        List<Particle> particles;

        //-------------------------------------------------------------------

        //outputs
        List<int> outputParticlesNeighbours;

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
                return Nuclei3.Properties.Resources.ParticleVectors;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("ae909d38-0792-5e06-b003-3b28b1b88f3e"); }
        }
    }
}