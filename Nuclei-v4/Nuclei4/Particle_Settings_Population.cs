using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Nuclei3
{
    public class Particle_Settings_Population : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Settings class.
        /// </summary>
        public Particle_Settings_Population()
          : base("Particle Population Settings", "Population Settings",
              "Sets Up Population Settings",
              "Nuclei4", " Particles")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddIntegerParameter("Minimum Population", "minPop", "Minimum Population of Particles", GH_ParamAccess.item, 100);
            pManager[0].Optional = true;
            //1
            pManager.AddIntegerParameter("Maximum Population", "maxPop", "Maxiumum Population of Particles", GH_ParamAccess.item, 20000);
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Population Settings", "populationSettings", "Settings For Particle Population", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.quarternary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData("Minimum Population", ref minPop);
            DA.GetData("Maximum Population", ref maxPop);

            String particleSettings = "PopulationSettings" + " " + minPop + " " + maxPop;

            List<String> outputSettings = new List<String>();
            outputSettings.Add(particleSettings);

            DA.SetDataList(0, outputSettings);
        }


        //-------------------------------------------------------------------
        //inputs
        int minPop;
        int maxPop;

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
                return Nuclei3.Properties.Resources.ParticlePopulationSettings;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("0224814f-9543-481b-9ec0-5f206c39b408"); }
        }
    }
}