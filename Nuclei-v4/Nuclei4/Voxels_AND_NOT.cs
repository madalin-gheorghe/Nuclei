using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nuclei4
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
            VoxelField field1;
            VoxelField field2;
            if (!VoxelFieldAccess.TryGet(DA, 0, Globals.voxelSize, out field1) ||
                !VoxelFieldAccess.TryGet(DA, 1, Globals.voxelSize, out field2))
            {
                return;
            }

            VoxelGridData data1 = field1.Data;
            VoxelGridData data2 = field2.Data;

            if (!sameGrid(data1, data2))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Difference inputs must have matching voxel grid dimensions.");
                DA.SetData(0, field1);
                return;
            }

            if (canReuseCachedSidecarOutput(data1, data2))
            {
                DA.SetData(0, cachedSidecarOutputField);
                return;
            }

            VoxelSelectionBuilder activeSelection = new VoxelSelectionBuilder(data1.Count);
            activeSelection.UnionWith(data1);
            activeSelection.ExceptWith(data2);

            VoxelGridData outputData = activeSelection.ApplyTo(data1);
            VoxelField outputField = field1.WithData(outputData);
            cacheSidecarOutput(data1, data2, outputField, outputData);

            DA.SetData(0, outputField);
        }

        bool canReuseCachedSidecarOutput(VoxelGridData data1, VoxelGridData data2)
        {
            return cachedSidecarOutputField != null &&
                   cachedSidecarOutputData != null &&
                   object.ReferenceEquals(cachedSidecarInputData1, data1) &&
                   object.ReferenceEquals(cachedSidecarInputData2, data2);
        }

        void cacheSidecarOutput(VoxelGridData data1, VoxelGridData data2, VoxelField outputField, VoxelGridData outputData)
        {
            cachedSidecarInputData1 = data1;
            cachedSidecarInputData2 = data2;
            cachedSidecarOutputField = outputField;
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
        VoxelGridData cachedSidecarInputData1;
        VoxelGridData cachedSidecarInputData2;
        VoxelField cachedSidecarOutputField;
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
                return Nuclei4.Properties.Resources.VoxelsDifference;
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
