using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Nuclei4
{
    public class EnivronmentSettings : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Solver_Settings class.
        /// </summary>
        public EnivronmentSettings()
          : base("Voxel Settings Slime", "Voxel Settings Slime",
              "Sets Up How The Environment Data Is Interpreted for Slime Particles",
              "Nuclei4", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddNumberParameter("Diffuse Rate", "diffuse", "The rate of diffusion of the deposited values", GH_ParamAccess.item, 0.1);
            pManager[0].Optional = true;
            //1
            pManager.AddIntegerParameter("Diffuse Range", "range", "The range of diffusion of the deposited values", GH_ParamAccess.item, 1);
            pManager[1].Optional = true;
            //2
            pManager.AddNumberParameter("Decay Rate", "decay", "The rate of decay of the deposited values", GH_ParamAccess.item, 0.03);
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Voxel Settings", "voxelSettings", "Settings For How The Environment and Data Is Interpreted", GH_ParamAccess.list);
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
            DA.GetData("Diffuse Rate", ref diffuseRate);
            DA.GetData("Diffuse Range", ref diffuseRange);
            DA.GetData("Decay Rate", ref decayRate);
            
            String voxelSettings = "VoxelSettingsSlime" + " " + diffuseRate + " " + diffuseRange + " " + decayRate;

            List<String> outputSettings = new List<String>();
            outputSettings.Add(voxelSettings);

            DA.SetDataList(0, outputSettings);
         }

        //-------------------------------------------------------------------
        //inputs
        double diffuseRate;
        int diffuseRange;
        double decayRate;

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
                return Nuclei4.Properties.Resources.EnvironmentSettings_Slime;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("dc1f1c7b-2376-487d-a4ac-d14d9cad856d"); }
        }
    }
}