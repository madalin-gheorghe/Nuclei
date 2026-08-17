using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuclei4
{
    public class Iterations : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Settings_Trail class.
        /// </summary>
        public Iterations()
          : base("Nuclei4 Solver Iterations", "Nuclei4 Solver Iterations",
              "Defines the maximum number of iterations",
              "Nuclei4", " Solver")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddIntegerParameter("Iterations", "iterations", "Maximum Number of Iterations", GH_ParamAccess.item, 1);
            pManager[0].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Solver Settings", "solverSettings", "Settings For Solver", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.secondary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData("Iterations", ref iterations);

            String particleSettings = "SolverSettings" + " " + iterations;

            List<String> outputSettings = new List<String>();
            outputSettings.Add(particleSettings);

            DA.SetDataList(0, outputSettings);
        }

        //-------------------------------------------------------------------
        //inputs
        int iterations;

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
                return Nuclei4.Properties.Resources.Counter;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("117cfcd7-ca25-4b01-8d76-688e2661ebb6"); }
        }
    }
}
