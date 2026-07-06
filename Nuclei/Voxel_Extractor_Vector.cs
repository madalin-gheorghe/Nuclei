using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Nuclei3
{
    public class Voxel_Extractor_Vector : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Extractor_Vector class.
        /// </summary>
        public Voxel_Extractor_Vector()
          : base("Extract Voxel Vector", "Voxel Vectors",
              "Extract Voxel Vectorfield",
              "Nuclei3", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddVectorParameter("Voxel Vectors", "voxelVector", "Voxel Vectors", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.septenary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref voxel);

            VoxelGridData voxelData = VoxelGridRegistry.GetOrCapture(voxel, Globals.voxelSize);

            if (outputVoxelVectors == null)
            {
                outputVoxelVectors = new List<Vector3d>(voxelData.ActiveCount);
            }
            else
            {
                outputVoxelVectors.Clear();
                if (outputVoxelVectors.Capacity < voxelData.ActiveCount)
                {
                    outputVoxelVectors.Capacity = voxelData.ActiveCount;
                }
            }

            for (int i = 0; i < voxelData.ActiveCount; i++)
            {
                outputVoxelVectors.Add(voxelData.GetVectorValue(voxelData.ActiveFlatIndexAt(i)));
            }

            DA.SetDataList(0, outputVoxelVectors);
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] voxel;

        //-------------------------------------------------------------------

        //outputs
        List<Vector3d> outputVoxelVectors;

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
                return Nuclei3.Properties.Resources.VoxelVector;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("be2396bf-49dd-43ca-85a2-8cf72d708cfe"); }
        }
    }
}
