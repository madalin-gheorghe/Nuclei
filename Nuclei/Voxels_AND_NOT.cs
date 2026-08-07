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
              "Nuclei4", " Environment")
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

            if (!sameGrid(data1, data2))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Difference inputs must have matching voxel grid dimensions.");
                DA.SetData(0, v1);
                return;
            }

            long data1Signature = data1.ContentSignature();
            long data2Signature = data2.ContentSignature();
            if (canReuseCachedSidecarOutput(data1, data2, data1Signature, data2Signature))
            {
                voxels = cachedSidecarOutputVoxels;
                VoxelGridRegistry.Set(voxels, cachedSidecarOutputData);
                DA.SetData(0, voxels);
                return;
            }

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
            cacheSidecarOutput(data1, data2, data1Signature, data2Signature, voxels, outputData);

            DA.SetData(0, voxels);
        }

        bool canReuseCachedSidecarOutput(VoxelGridData data1, VoxelGridData data2, long data1Signature, long data2Signature)
        {
            return cachedSidecarOutputVoxels != null &&
                   cachedSidecarOutputData != null &&
                   (object.ReferenceEquals(cachedSidecarInputData1, data1) || cachedSidecarInputSignature1 == data1Signature) &&
                   (object.ReferenceEquals(cachedSidecarInputData2, data2) || cachedSidecarInputSignature2 == data2Signature);
        }

        void cacheSidecarOutput(VoxelGridData data1, VoxelGridData data2, long data1Signature, long data2Signature, Voxel[,,] outputVoxels, VoxelGridData outputData)
        {
            cachedSidecarInputData1 = data1;
            cachedSidecarInputData2 = data2;
            cachedSidecarInputSignature1 = data1Signature;
            cachedSidecarInputSignature2 = data2Signature;
            cachedSidecarOutputVoxels = outputVoxels;
            cachedSidecarOutputData = outputData;
        }

        static bool sameGrid(VoxelGridData data1, VoxelGridData data2)
        {
            return data1 != null &&
                   data2 != null &&
                   data1.ResX == data2.ResX &&
                   data1.ResY == data2.ResY &&
                   data1.ResZ == data2.ResZ &&
                   data1.Count == data2.Count;
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] v1;
        Voxel[,,] v2;

        //-------------------------------------------------------------------

        //outputs
        Voxel[,,] voxels;
        VoxelGridData cachedSidecarInputData1;
        VoxelGridData cachedSidecarInputData2;
        long cachedSidecarInputSignature1;
        long cachedSidecarInputSignature2;
        Voxel[,,] cachedSidecarOutputVoxels;
        VoxelGridData cachedSidecarOutputData;

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
            get { return new Guid("9fb92daa-e99b-4ac3-985a-b985ad7bcf62"); }
        }
    }
}
