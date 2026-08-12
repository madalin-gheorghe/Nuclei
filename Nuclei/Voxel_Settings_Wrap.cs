using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Nuclei3
{
    public class Voxel_Settings_Wrap : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Solver_Settings class.
        /// </summary>
        public Voxel_Settings_Wrap()
          : base("Voxel Wrap Settings", "Voxel Wrap Settings",
              "Settings for the Boundary Condition",
              "Nuclei3", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Wrap", "wrap", "Boundary Conditions", GH_ParamAccess.item, false);
            pManager[0].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Wrap Settings", "wrapSettings", "Settings for the Boundary Condition", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData("Wrap", ref wrap);

            String voxelSettings = "WrapSettings" + " " + wrap.ToString();

            List<String> outputSettings = new List<String>();
            outputSettings.Add(voxelSettings);

            DA.SetDataList(0, outputSettings);
        }

        //-------------------------------------------------------------------
        //inputs
        bool wrap;

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
                return Nuclei3.Properties.Resources.EnvironmentSettings_Wrap;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("23ff0742-52ee-b81b-997d-56c038d20605"); }
        }
    }
}
