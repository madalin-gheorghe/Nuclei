using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Nuclei3
{
    public class Particle_Settings_Death : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DeathSettings class.
        /// </summary>
        public Particle_Settings_Death()
          : base("Particle Death Settings", "Death Settings",
              "Sets Up Dynamic Population Death Settings. Dies if Neighbour Count is OUTSIDE Range",
              "Nuclei3", " Particles")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Die", "die", "Die Boolean", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Minimum Age", "minAge", "Minimum Age Since The Particles Can Start Dying", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Die Range", "dieRange", "The Range To Check For Neighbour Particle Count", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Minimum Neighbours", "minN", "Minimum Number of Neighbour Particles", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Maximum Neighbours", "maxN", "Maximum Number of Neighbour Particles", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Frequency", "dieFrequency", "The Particles Die Once Every X Iterations", GH_ParamAccess.item, 5);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Death Settings", "dieSettings", "Settings Controlling Particle Death", GH_ParamAccess.list);
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
            DA.GetData("Die", ref die);
            DA.GetData("Minimum Age", ref minAge);
            DA.GetData("Die Range", ref dieRange);
            DA.GetData("Minimum Neighbours", ref dieMin);
            DA.GetData("Maximum Neighbours", ref dieMax);
            DA.GetData("Frequency", ref dieFreq);

            List<String> outputSettings = new List<String>();

            String deathSettings = "DeathSettings" + " " + die.ToString() + " " + minAge.ToString() + " " + dieRange + " " + dieMin + " " + dieMax + " " + dieFreq;
            outputSettings.Add(deathSettings);

            DA.SetDataList(0, outputSettings);
        }

        //-------------------------------------------------------------------

        //inputs
        bool die;
        int minAge;
        int dieRange;
        int dieMin;
        int dieMax;
        int dieFreq;

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
                return Nuclei3.Properties.Resources.ParticleDeath;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("9f969712-b5a5-485d-8345-bbb0a290912c"); }
        }
    }
}