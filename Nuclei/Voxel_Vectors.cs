using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Voxel_Vectors : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Vectors class.
        /// </summary>
        public Voxel_Vectors()
          : base("Define Voxel Vectors", "Voxel Vectors",
              "Define Voxel Vector Field",
              "Nuclei3", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
            //pManager[0].DataMapping = GH_DataMapping.Flatten;
            //1
            pManager.AddVectorParameter("Voxel Vector", "vector", "Vector Assigned to Voxel", GH_ParamAccess.list);
            pManager[1].DataMapping = GH_DataMapping.Flatten;
            //2
            pManager.AddIntegerParameter("Frequency", "frequency", "The Smaller the Frequency, the Bigger the Vectorfield Impact on Simulation", GH_ParamAccess.list,1);
            pManager[2].DataMapping = GH_DataMapping.Flatten;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
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
            //set inputs
            voxelVectors = new List<Vector3d>();
            frequency = new List<int>();

            DA.GetData("Voxels", ref inputVoxels);
            DA.GetDataList(1, voxelVectors);
            DA.GetDataList(2, frequency);

            VoxelGridData inputData = VoxelGridRegistry.GetOrCapture(inputVoxels, Globals.voxelSize);
            VoxelGridData outputData = inputData.WithVectorValues(voxelVectors, frequency);
            voxels = outputData.ToVoxelArray(true);
            VoxelGridRegistry.Set(voxels, outputData);

            DA.SetData(0, voxels);
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] inputVoxels;
        List<Vector3d> voxelVectors;
        List<int> frequency;

        //outputs
        Voxel[,,] voxels;

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
                return Nuclei3.Properties.Resources.EnvironmentVectors3;
                //return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("69302de9-b10e-49e4-8562-f01d829c0033"); }
        }
    }
}
