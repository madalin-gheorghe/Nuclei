using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Nuclei4
{
    public class Particle_Settings_Division : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DynamicPopulation class.
        /// </summary>
        public Particle_Settings_Division()
          : base("Particle Division Settings", "Particle Division Settings",
              "Sets Up Dynamic Population Division Settings. Divides if Neighbour Count is INSIDE Range",
              "Nuclei4", " Particles")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Divide", "divide", "Divide Boolean", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Minimum Age", "minAge", "Minimum Age Since The Particles Can Start Dividing", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Division Range", "divRange", "The Range To Check For Neighbour Particle Count", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Minimum Neighbours", "minN", "Minimum Number of Neighbour Particles", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Maximum Neighbours", "maxN", "Maximum Number of Neighbour Particles", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Frequency", "divFrequency", "The Particles Divide Once Every X Iterations", GH_ParamAccess.item, 5);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Division Settings", "divSettings", "Settings Controlling Particle Division", GH_ParamAccess.list);
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
            DA.GetData("Divide", ref division);
            DA.GetData("Minimum Age", ref minAge);
            DA.GetData("Division Range", ref divRange);
            DA.GetData("Minimum Neighbours", ref divMin);
            DA.GetData("Maximum Neighbours", ref divMax);
            DA.GetData("Frequency", ref divFreq);

            List<String> outputSettings = new List<String>();

            String divisionSettings = "DivisionSettings" + " " + division.ToString() + " " + minAge.ToString() + " " + divRange + " " + divMin + " " + divMax + " " + divFreq;
            outputSettings.Add(divisionSettings);

            DA.SetDataList(0, outputSettings);
        }

        //-------------------------------------------------------------------

        //inputs
        bool division;
        int minAge;
        int divRange;
        int divMin;
        int divMax;
        int divFreq;

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
                return Nuclei4.Properties.Resources.ParticleDivision;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("7e505abb-dc4f-4226-a922-f92e25ab70da"); }
        }
    }
}