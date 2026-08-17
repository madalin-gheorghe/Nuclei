using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Nuclei3
{
    public class Particle_Settings_Trail : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Settings_Trail class.
        /// </summary>
        public Particle_Settings_Trail()
          : base("Particle Trail Settings", "Particle Trail Settings",
              "Sets Up Dynamic Trail Settings",
              "Nuclei4", " Particles")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddIntegerParameter("Trail Size", "trailSize", "Size Of Particle Trail", GH_ParamAccess.item, 5);
            pManager[0].Optional = true;
            //1
            pManager.AddIntegerParameter("Trail Frequency", "trailFrequency", "Frequency Of Particle Trail Sampling", GH_ParamAccess.item, 1);
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Trail Settings", "trailSettings", "Settings For Particle Trail", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.tertiary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData("Trail Size", ref trailSize);
            DA.GetData("Trail Frequency", ref trailFreq);

            String particleSettings = "TrailSettings" + " " + trailSize + " " + trailFreq;

            List<String> outputSettings = new List<String>();
            outputSettings.Add(particleSettings);

            DA.SetDataList(0, outputSettings);
        }

        //-------------------------------------------------------------------
        //inputs
        int trailSize;
        int trailFreq;

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
                return Nuclei3.Properties.Resources.ParticleTrailSettings;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("cd0bb03c-2b66-4dbb-864e-02015f0255e7"); }
        }
    }
}