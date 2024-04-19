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

            //determine voxel settings
            int resX = voxel.GetLength(0);
            int resY = voxel.GetLength(1);
            int resZ = voxel.GetLength(2);

            outputVoxelVectors = new List<Vector3d>();

            for (int k = 0; k < resZ; k++)
            {
                for (int i = 0; i < resX; i++)
                {
                    for (int j = 0; j < resY; j++)
                    {
                        if (voxel[i, j, k] != null)
                        {
                            Voxel V = voxel[i, j, k];
                            outputVoxelVectors.Add(V.voxelVector);
                        }
                    }
                }
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