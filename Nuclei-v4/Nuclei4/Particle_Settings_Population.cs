using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Nuclei4
{
    public class Particle_Settings_Population : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Settings class.
        /// </summary>
        public Particle_Settings_Population()
          : base("Particle Population Settings", "Particle Population Settings",
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
            //2
            pManager.AddNumberParameter("Random Division", "randomDiv", "Independent division probability per particle at each population update (0 to 1)", GH_ParamAccess.item, 0.0);
            pManager[2].Optional = true;
            //3
            pManager.AddNumberParameter("Random Death", "randomDie", "Independent removal probability per particle at each population update (0 to 1)", GH_ParamAccess.item, 0.0);
            pManager[3].Optional = true;
            //4
            pManager.AddIntegerParameter("Frequency", "frequency", "Apply random population changes once every X solver iterations", GH_ParamAccess.item, 1);
            pManager[4].Optional = true;
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
            DA.GetData("Random Division", ref randomDiv);
            DA.GetData("Random Death", ref randomDie);
            DA.GetData("Frequency", ref frequency);

            randomDiv = clampProbability(randomDiv);
            randomDie = clampProbability(randomDie);
            if (frequency < 1) frequency = 1;

            String particleSettings = "PopulationSettings" + " " + minPop + " " + maxPop + " " + randomDiv + " " + randomDie + " " + frequency;

            List<String> outputSettings = new List<String>();
            outputSettings.Add(particleSettings);

            DA.SetDataList(0, outputSettings);
        }


        //-------------------------------------------------------------------
        //inputs
        int minPop;
        int maxPop;
        double randomDiv;
        double randomDie;
        int frequency = 1;

        static double clampProbability(double value)
        {
            if (double.IsNaN(value) || value < 0) return 0;
            if (double.IsPositiveInfinity(value) || value > 1) return 1;
            return value;
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
                return Nuclei4.Properties.Resources.ParticlePopulationSettings;
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