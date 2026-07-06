using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Voxels_AND_NOT : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxels_AND_NOT class.
        /// </summary>
        public Voxels_AND_NOT()
           : base("Voxel Selection Difference", "Voxel Selection Difference",
              "Perform Difference on Voxel Selection (AND NOT): V1 - V2",
              "Nuclei3", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Voxel", "V1", "Connects to Voxels", GH_ParamAccess.item);

            //1
            pManager.AddGenericParameter("Voxel", "V2", "Connects to Voxels", GH_ParamAccess.item);
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
            get { return GH_Exposure.senary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref v1);
            DA.GetData(1, ref v2);

            VoxelGridData data1 = VoxelGridRegistry.GetOrCapture(v1, Globals.voxelSize);
            VoxelGridData data2 = VoxelGridRegistry.GetOrCapture(v2, data1.VoxelSize);
            bool[] activeMask = new bool[data1.Count];

            for (int i = 0; i < data1.ActiveCount; i++)
            {
                int flatIndex = data1.ActiveFlatIndexAt(i);
                if (!data2.IsActive(flatIndex))
                {
                    activeMask[flatIndex] = true;
                }
            }

            VoxelGridData outputData = data1.WithActiveMask(activeMask);
            voxels = outputData.ToVoxelArray(true);
            VoxelGridRegistry.Set(voxels, outputData);

            DA.SetData(0, voxels);
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] v1;
        Voxel[,,] v2;

        //-------------------------------------------------------------------

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
                return Nuclei3.Properties.Resources.VoxelsDifference;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("df63568f-cb5c-43e4-9b2f-1e7f0553404c"); }
        }
    }
}
