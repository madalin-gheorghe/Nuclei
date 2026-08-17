using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Nuclei3
{
    public class Particle_Extractor_Point : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ParticleExtract class.
        /// </summary>
        public Particle_Extractor_Point()
          : base("Extract Particle Positions", "Extract Particle Positions",
              "Extract Particle Positions",
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
            pManager.AddPointParameter("Particle Positions", "particlePos", "Particle Positions", GH_ParamAccess.list);
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

            //outputParticlesPos = new List<Point3d>();
            outputParticlesPos = new Grasshopper.DataTree<Point3d>();

            for (int i = 0; i < particles.Count; i++)
            {
                Particle P = particles[i];
                outputParticlesPos.Add(P.pPlane.Origin, new Grasshopper.Kernel.Data.GH_Path(retrieveIndex(P)));
            }

            DA.SetDataTree(0, outputParticlesPos);
        }

        //-------------------------------------------------------------------

        //inputs
        List<Particle> particles;

        //-------------------------------------------------------------------

        //outputs
        //List<Point3d> outputParticlesPos;
        Grasshopper.DataTree<Point3d> outputParticlesPos;

        //-------------------------------------------------------------------

        int retrieveIndex(Particle P)
        {
            int index = Globals.particleGroups.IndexOf(P.parentParticleGroup);
            if (index >= 0)
            {
                return index;
            }
            else
            {
                return 0;
            }
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
                return Nuclei3.Properties.Resources.ParticlePositions;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("311bd82d-56cc-592b-aae4-69f8824a0a3b"); }
        }
    }
}