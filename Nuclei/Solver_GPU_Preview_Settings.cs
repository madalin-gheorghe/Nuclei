using System;
using System.Collections.Generic;

using Grasshopper.Kernel;

namespace Nuclei3
{
    public class Solver_GPU_Preview_Settings : GH_Component
    {
        public Solver_GPU_Preview_Settings()
          : base("Solver GPU Preview Settings", "GPU Preview Settings",
              "Settings for Solver GPU display output",
              "Nuclei3", " Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Density Field", "density", "Enable the shared GPU density field preview", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Mode", "mode", "GPU preview mode. Supported: SharedDensity", GH_ParamAccess.item, "SharedDensity");
            pManager[0].Optional = true;
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("GPU Preview Settings", "gpuPreviewSettings", "Settings for Solver GPU preview output", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.secondary; }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref densityField);
            DA.GetData(1, ref mode);

            List<string> outputSettings = new List<string>();
            outputSettings.Add("GpuPreviewSettings " + densityField + " " + NormalizeModeToken(mode));
            DA.SetDataList(0, outputSettings);
        }

        static string NormalizeModeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "SharedDensity";
            }

            return value.Trim().Replace(" ", "_");
        }

        bool densityField = false;
        string mode = "SharedDensity";

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Nuclei3.Properties.Resources.PreviewVoxelDensities;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("2a961462-280f-4d1f-878f-18107a72ab64"); }
        }
    }
}
