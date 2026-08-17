using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Particle_Settings_Ant_Slime : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Settings class.
        /// </summary>
        public Particle_Settings_Ant_Slime()
          : base("Particle Settings Slime Ant Interaction", "Particle Settings Slime Ant Interaction",
              "Sets Up Species Interaction Settings",
              "Nuclei4", " Particles")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddNumberParameter("Slime -> Ant Food", "slime ~ antFood", "Interaction between SLIME Particles and ANT FOOD Pheromones. VALUES FROM 0 TO 1", GH_ParamAccess.item, 0.5);
            //1
            pManager.AddNumberParameter("Slime -> Ant Base", "slime ~ antBase", "Interaction between SLIME Particles and ANT BASE Pheromones. VALUES FROM 0 TO 1", GH_ParamAccess.item, 0.5);
            //2
            pManager.AddNumberParameter("Ant -> Slime", "ant ~ slime", "Interaction between ANT Particles and SLIME Chemoattractants. VALUES FROM 0 TO 1", GH_ParamAccess.item, 0.5);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Interaction Settings", "interactionSettings", "Settings Controlling Species Interactions", GH_ParamAccess.list);
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
            //get values
            DA.GetData("Slime -> Ant Food", ref slime_antFoodPheromones);
            DA.GetData("Slime -> Ant Base", ref slime_antBasePheromones);
            DA.GetData("Ant -> Slime", ref ant_slimeChemoattractants);


            List<String> outputSettings = new List<String>();

            String particleSettings = "SpeciesInteractionSettings" + " " + slime_antFoodPheromones.ToString() + " " + slime_antBasePheromones.ToString() + " " + ant_slimeChemoattractants.ToString();
            outputSettings.Add(particleSettings);

            DA.SetDataList(0, outputSettings);
        }

        //-------------------------------------------------------------------

        //inputs
        double ant_slimeChemoattractants = 0.5;
        double slime_antFoodPheromones = 0.5;
        double slime_antBasePheromones = 0.5;

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
                return Nuclei3.Properties.Resources.ParticleSettings_Interaction;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("9c28782b-f3db-40e3-8bd7-f099d8b62ae3"); }
        }
    }
}