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
          : base("Extract Voxel Positions", "Voxel Centers",
              "Extract Voxel Positions",
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
            DA.GetData(0, ref inputVoxels);

            //determine voxel settings
            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

            //determine voxel size
            double voxelSize = Globals.voxelSize;

            int totalVoxelCount = resX * resY * resZ;

            //add voxel centers to output list
            if (outputVoxelPositions == null)
            {
                outputVoxelPositions = new List<Point3d>(totalVoxelCount);
            }
            else
            {
                outputVoxelPositions.Clear();
                if (outputVoxelPositions.Capacity < totalVoxelCount)
                {
                    outputVoxelPositions.Capacity = totalVoxelCount;
                }
            }

            for (int i = 0; i < resX; i++)
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        Voxel inputV = inputVoxels[i, j, k];

                        if (inputV != null)
                        {
                            outputVoxelPositions.Add(inputV.loc);
                        }
                    }
                }
            }

            //if all voxels are NULL, then instantiate new blank voxel positions
            if (outputVoxelPositions.Count == 0)
            {
                for (int i = 0; i < resX; i++)
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            outputVoxelPositions.Add(new Point3d(i * voxelSize + voxelSize / 2, j * voxelSize + voxelSize / 2, k * voxelSize + voxelSize / 2));
                        }
                    }
                }
            }
            

            DA.SetDataList(0, outputVoxelPositions);
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] inputVoxels;

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
            get { return new Guid("a1f09327-216f-4782-8137-4c112528c7da"); }
        }
    }
}
