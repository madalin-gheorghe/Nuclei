using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Nuclei3
{
    public class Voxel_Extractor_Point : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the VoxelPosition class.
        /// </summary>
        public Voxel_Extractor_Point()
          : base("Extract Voxel Positions", "Extract Voxel Positions",
              "Extract Voxel Positions",
              "Nuclei4", " Environment")
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
            pManager.AddPointParameter("Voxel Positions", "voxelPosition", "Centers of Voxels", GH_ParamAccess.list);
            //pManager.HideParameter(0);
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
            VoxelField field;
            if (!VoxelFieldAccess.TryGet(DA, 0, Globals.voxelSize, out field)) return;
            VoxelGridData voxelData = field.Data;

            //add voxel centers to output list
            if (outputVoxelPositions == null)
            {
                outputVoxelPositions = new List<Point3d>(voxelData.ActiveCount);
            }
            else
            {
                outputVoxelPositions.Clear();
                if (outputVoxelPositions.Capacity < voxelData.ActiveCount)
                {
                    outputVoxelPositions.Capacity = voxelData.ActiveCount;
                }
            }

            for (int i = 0; i < voxelData.ActiveCount; i++)
            {
                outputVoxelPositions.Add(voxelData.CenterPoint(voxelData.ActiveFlatIndexAt(i)));
            }
            

            DA.SetDataList(0, outputVoxelPositions);
        }

        //-------------------------------------------------------------------

        //outputs
        //List<Point3d> outputVoxelPositions;
        List <Point3d> outputVoxelPositions;

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
                return Nuclei3.Properties.Resources.VoxelPosition;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("deb55383-1cb3-4b17-8d01-cc05d1b9c635"); }
        }
    }
}
