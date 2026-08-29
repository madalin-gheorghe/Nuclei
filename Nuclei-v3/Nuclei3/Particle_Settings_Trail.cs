using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Parameters;
using GH_IO.Serialization;
using System;
using System.Collections.Generic;

namespace Nuclei3
{
    public class Particle_Settings_Trail : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Settings_Trail class.
        /// </summary>
        public Particle_Settings_Trail()
          : base("Particle Trail Settings", "Particle Trail Settings",
              "Sets Up Dynamic Trail Settings",
              "Nuclei3", " Particles")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddIntegerParameter("Trail Size", "trailSize", "Size Of Particle Trail", GH_ParamAccess.item, 5);
            pManager[0].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Trail Settings", "trailSettings", "Settings For Particle Trail", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.tertiary; }
        }

        public override bool Read(GH_IReader reader)
        {
            if (hasLegacyFrequencyInput(reader))
            {
                // Temporarily recreate the retired input so Grasshopper can read old
                // archives, then discard that parameter together with its old wire/value.
                Params.RegisterInputParam(new Param_Integer(), 1);
                bool result;
                try
                {
                    result = base.Read(reader);
                }
                finally
                {
                    if (Params.Input.Count > 1)
                        Params.UnregisterInputParameter(Params.Input[1], true);
                    Params.OnParametersChanged();
                }
                return result;
            }

            return base.Read(reader);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData("Trail Size", ref trailSize);
            String particleSettings = "TrailSettings" + " " + trailSize + " " + TrailFrequency;

            List<String> outputSettings = new List<String>();
            outputSettings.Add(particleSettings);

            DA.SetDataList(0, outputSettings);
        }

        //-------------------------------------------------------------------
        //inputs
        const int TrailFrequency = 1;
        int trailSize;

        static bool hasLegacyFrequencyInput(GH_IReader reader)
        {
            if (!reader.ChunkExists("param_input", 1)) return false;

            GH_IReader frequency = reader.FindChunk("param_input", 1);
            string name = string.Empty;
            return frequency != null
                && frequency.TryGetString("Name", ref name)
                && string.Equals(name, "Trail Frequency", StringComparison.Ordinal);
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
                return Nuclei3.Properties.Resources.ParticleTrailSettings;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("7869ca3b-e735-980e-0fe7-523cc3425e62"); }
        }
    }
}
