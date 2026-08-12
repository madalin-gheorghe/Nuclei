using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Nuclei3
{
    public class EnivronmentSettings_Ant : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Solver_Settings class.
        /// </summary>
        public EnivronmentSettings_Ant()
          : base("Voxel Settings Ant", "Voxel Settings Ant",
              "Sets Up How The Environment Data Is Interpreted for Ant Particles",
              "Nuclei3", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddNumberParameter("Food Pheromones Diffuse Rate", "foodDiffuse", "The Rate of Diffusion of the Pheromones that Guide Particles Towards Food", GH_ParamAccess.item, 0.05);
            pManager[0].Optional = true;

            //1
            pManager.AddNumberParameter("Food Decay Rate", "foodDecay", "The Rate of Decay of the Pheromones that Guide Particles Towards Food", GH_ParamAccess.item, 0.005);
            pManager[1].Optional = true;

            //2
            pManager.AddNumberParameter("Base Pheromones Diffuse Rate", "baseDiffuse", "The Rate of Diffusion of the Pheromones that Guide Particles Back To Base", GH_ParamAccess.item, 0.1);
            pManager[2].Optional = true;

            //3
            pManager.AddNumberParameter("Base Decay Rate", "baseDecay", "The Rate of Decay of the Pheromones that Guide Particles Back To Base", GH_ParamAccess.item, 0.01);
            pManager[3].Optional = true;

            //4
            pManager.AddIntegerParameter("Diffuse Range", "range", "The Range of Diffusion of the Deposited Values", GH_ParamAccess.item, 1);
            pManager[4].Optional = true;
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
            DA.GetData("Food Pheromones Diffuse Rate", ref foodDiffuseRate);
            DA.GetData("Food Decay Rate", ref foodDecayRate);

            DA.GetData("Base Pheromones Diffuse Rate", ref baseDiffuseRate);
            DA.GetData("Base Decay Rate", ref baseDecayRate);

            DA.GetData("Diffuse Range", ref diffuseRange);

            String voxelSettings = "VoxelSettingsAnt" + " " + foodDiffuseRate + " " + foodDecayRate + " " + baseDiffuseRate + " " + baseDecayRate + " " + diffuseRange;

            List<String> outputSettings = new List<String>();
            outputSettings.Add(voxelSettings);

            DA.SetDataList(0, outputSettings);
        }

        //-------------------------------------------------------------------
        //inputs
        double foodDiffuseRate;
        double foodDecayRate;

        double baseDiffuseRate;
        double baseDecayRate;

        int diffuseRange;

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
                return Nuclei3.Properties.Resources.EnvironmentSettings_Ant;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("a0acc69f-9759-57ab-9f35-ca254d4fbaa8"); }
        }
    }
}