using System;
using GH_IO.Serialization;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Nuclei4
{
    public class EnivronmentSettings_Ant : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Solver_Settings class.
        /// </summary>
        public EnivronmentSettings_Ant()
          : base("Voxel Settings Ant", "Voxel Settings Ant",
              "Sets Up How The Environment Data Is Interpreted for Ant Particles",
              "Nuclei4", " Environment")
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
            pManager.AddNumberParameter("Falloff", "falloff", "Shared food and base pheromone falloff, from 0 (local weighted diffusion) to 1 (uniform averaging across the range).", GH_ParamAccess.item, 0.0);
            pManager[4].Optional = true;

            //5
            pManager.AddIntegerParameter("Diffuse Range", "range", "The Range of Diffusion of the Deposited Values", GH_ParamAccess.item, 1);
            pManager[5].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Voxel Settings", "voxelSettings", "Settings For How The Environment and Data Is Interpreted", GH_ParamAccess.list);
        }

        public override bool Read(GH_IReader reader)
        {
            if (reader.ChunkExists("param_input", 5)) return base.Read(reader);
            // Keep the old Range parameter in slot four while reading, then
            // insert Falloff without replacing Range or its existing wires.
            IGH_Param falloffParameter = Params.Input[4];
            Params.UnregisterInputParameter(falloffParameter, false);
            try { return base.Read(reader); }
            finally
            {
                Params.RegisterInputParam(falloffParameter, 4);
                Params.OnParametersChanged();
            }
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
            DA.GetData("Falloff", ref falloff);
            if (double.IsNaN(falloff) || falloff < 0) falloff = 0;
            if (falloff > 1) falloff = 1;

            String voxelSettings = "VoxelSettingsAnt" + " " + foodDiffuseRate + " " + foodDecayRate + " " + baseDiffuseRate + " " + baseDecayRate + " " + diffuseRange + " " + falloff;

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
        double falloff;

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
                return Nuclei4.Properties.Resources.EnvironmentSettings_Ant;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("3486cda4-b3f3-47a1-886b-f047d6d7a13a"); }
        }
    }
}