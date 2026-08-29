using System;
using System.Collections.Generic;

using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Nuclei4
{
    public class EnivronmentSettings : GH_Component
    {
        const int CurrentInputSchema = 2;

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
            pManager.AddNumberParameter("Decay Rate", "decay", "The rate of decay of the deposited values", GH_ParamAccess.item, 0.03);
            pManager[1].Optional = true;
            //2
            pManager.AddNumberParameter("Falloff", "falloff", "The rate at which the diffusion is spread around the nearby voxels. VALUES FROM 0 TO 1", GH_ParamAccess.item, 0.0);
            pManager[2].Optional = true;
            //3
            pManager.AddIntegerParameter("Diffuse Range", "range", "The range of diffusion of the deposited values", GH_ParamAccess.item, 1);
            pManager[3].Optional = true;
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

        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32("VoxelSettingsSlimeSchema", CurrentInputSchema);
            writer.SetBoolean("Input2StoresLegacyGradual", input2StoresLegacyGradual);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            int schema = 0;
            reader.TryGetInt32("VoxelSettingsSlimeSchema", ref schema);

            if (schema >= CurrentInputSchema)
            {
                bool result = base.Read(reader);
                input2StoresLegacyGradual = false;
                reader.TryGetBoolean("Input2StoresLegacyGradual", ref input2StoresLegacyGradual);
                normalizeInputMetadata();
                return result;
            }

            ArchivedInputSchema archivedSchema = detectArchivedInputSchema(reader);
            if (archivedSchema == ArchivedInputSchema.LegacyFourInputs)
            {
                // Registered modern order is [Diffuse, Decay, Falloff, Range].
                // Temporarily restore the pushed legacy order and matching types so
                // GH_ComponentParamServer.Read deserializes each archived chunk into
                // its original parameter object. Sorting those same objects back
                // afterwards preserves InstanceGuids, sources and persistent data.
                Params.SortInput(new[] { 0, 2, 3, 1 });
                bool result;
                try
                {
                    result = base.Read(reader);
                }
                finally
                {
                    Params.SortInput(new[] { 0, 3, 1, 2 });
                }

                input2StoresLegacyGradual = true;
                normalizeInputMetadata();
                return result;
            }

            if (archivedSchema == ArchivedInputSchema.LegacyThreeInputs)
            {
                // Early V4 archives predate Gradual. Remove the new Falloff input
                // only while the three legacy chunks are read, then reinsert that
                // untouched default parameter in the modern slot.
                IGH_Param falloffParameter = Params.Input[2];
                Params.UnregisterInputParameter(falloffParameter, false);
                Params.SortInput(new[] { 0, 2, 1 });
                bool result;
                try
                {
                    result = base.Read(reader);
                }
                finally
                {
                    Params.SortInput(new[] { 0, 2, 1 });
                    Params.RegisterInputParam(falloffParameter, 2);
                    Params.OnParametersChanged();
                }

                input2StoresLegacyGradual = false;
                normalizeInputMetadata();
                return result;
            }

            bool modernResult = base.Read(reader);
            input2StoresLegacyGradual = false;
            normalizeInputMetadata();
            return modernResult;
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref diffuseRate);
            DA.GetData(1, ref decayRate);
            DA.GetData(2, ref diffusionFalloff);
            DA.GetData(3, ref diffuseRange);

            if (double.IsNaN(diffusionFalloff) || diffusionFalloff < 0) diffusionFalloff = 0;
            if (double.IsPositiveInfinity(diffusionFalloff) || diffusionFalloff > 1) diffusionFalloff = 1;

            double diffusionGradual = input2StoresLegacyGradual
                ? diffusionFalloff
                : 1.0 - diffusionFalloff;

            String voxelSettings = "VoxelSettingsSlime" + " " + diffuseRate + " " + diffuseRange + " " + decayRate + " " + diffusionGradual;

            List<String> outputSettings = new List<String>();
            outputSettings.Add(voxelSettings);

            DA.SetDataList(0, outputSettings);
         }

        //-------------------------------------------------------------------
        //inputs
        double diffuseRate;
        int diffuseRange;
        double decayRate;
        double diffusionFalloff = 0.0;
        bool input2StoresLegacyGradual;

        enum ArchivedInputSchema
        {
            Unknown,
            Modern,
            LegacyThreeInputs,
            LegacyFourInputs
        }

        static ArchivedInputSchema detectArchivedInputSchema(GH_IReader reader)
        {
            int count = 0;
            while (reader.ChunkExists("param_input", count)) count++;

            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                GH_IReader parameterReader = reader.FindChunk("param_input", i);
                string name = string.Empty;
                if (parameterReader != null) parameterReader.TryGetString("Name", ref name);
                names[i] = name;
            }

            if (matches(names, "Diffuse Rate", "Decay Rate", "Falloff", "Diffuse Range"))
            {
                return ArchivedInputSchema.Modern;
            }

            if (matches(names, "Diffuse Rate", "Diffuse Range", "Decay Rate", "Gradual"))
            {
                return ArchivedInputSchema.LegacyFourInputs;
            }

            if (matches(names, "Diffuse Rate", "Diffuse Range", "Decay Rate"))
            {
                return ArchivedInputSchema.LegacyThreeInputs;
            }

            return ArchivedInputSchema.Unknown;
        }

        static bool matches(string[] values, params string[] expected)
        {
            if (values == null || values.Length != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(values[i], expected[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        void normalizeInputMetadata()
        {
            if (Params.Input.Count != 4) return;

            setInputMetadata(0, "Diffuse Rate", "diffuse", "The rate of diffusion of the deposited values");
            setInputMetadata(1, "Decay Rate", "decay", "The rate of decay of the deposited values");
            if (input2StoresLegacyGradual)
            {
                setInputMetadata(2, "Gradual (legacy)", "gradual", "Legacy diffusion control preserved from this definition. Effective Falloff is 1 minus this value.");
            }
            else
            {
                setInputMetadata(2, "Falloff", "falloff", "The rate at which the diffusion is spread around the nearby voxels. VALUES FROM 0 TO 1");
            }
            setInputMetadata(3, "Diffuse Range", "range", "The range of diffusion of the deposited values");
        }

        void setInputMetadata(int index, string name, string nickname, string description)
        {
            IGH_Param parameter = Params.Input[index];
            parameter.Name = name;
            parameter.NickName = nickname;
            parameter.Description = description;
            parameter.Optional = true;
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
