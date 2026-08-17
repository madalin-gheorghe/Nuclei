using Grasshopper.Kernel;
using Rhino.Geometry;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;

namespace Nuclei4
{
    public class Particle_Extractor_TrailPoints : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Extractor_TrailPoints class.
        /// </summary>
        public Particle_Extractor_TrailPoints()
          : base("Extract Particle Trails", "Extract Particle Trails",
              "Extract Particle Trail Points",
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
            pManager.AddPointParameter("Particle Positions", "trailPos", "Particle Positions", GH_ParamAccess.list);
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

            outputParticleTrails = new Grasshopper.DataTree<Point3d>();

            for (int i = 0; i < particles.Count; i++)
            {
                Particle P = particles[i];
                if (P == null || P.trails == null || P.trails.Count == 0)
                {
                    continue;
                }

                Grasshopper.Kernel.Data.GH_Path path = new Grasshopper.Kernel.Data.GH_Path(retrieveIndex(P), i);
                for (int j = 0; j < P.trails.Count; j++)
                {
                    outputParticleTrails.Add(P.trails[j], path);
                }
            }

            DA.SetDataTree(0, outputParticleTrails);
        }

        //-------------------------------------------------------------------

        //inputs
        List<Particle> particles;

        //-------------------------------------------------------------------

        //outputs
        Grasshopper.DataTree<Point3d> outputParticleTrails;

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
                return Nuclei4.Properties.Resources.ParticleTrails;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("0a97c625-4da3-4143-89c6-d88249de8741"); }
        }
    }
}
