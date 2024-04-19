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

            //determine voxel settings
            int resX = v1.GetLength(0);
            int resY = v1.GetLength(1);
            int resZ = v1.GetLength(2);

            //if voxels from construct voxels, then create empty voxels
            double voxelSize = Globals.voxelSize;

            //check to see if input voxel comes from construct voxels and is null

            //v1
            int activeVoxelsCounter1 = 0;

            //count active voxels
            Parallel.For(0, resX, i =>
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (v1[i, j, k] != null)
                        {
                            int parallelCounter = System.Threading.Interlocked.Increment(ref activeVoxelsCounter1);
                        }
                    }
                }
            }
            );

            //if there are 0 active voxels in v1 then instantiate empty v1 voxels
            if (activeVoxelsCounter1 == 0)
            {
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            v1[i, j, k] = new Voxel(voxelSize, i, j, k);
                        }
                    }
                }
                );
            }

            //v2
            int activeVoxelsCounter2 = 0;

            //count active voxels
            Parallel.For(0, resX, i =>
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (v2[i, j, k] != null)
                        {
                            int parallelCounter = System.Threading.Interlocked.Increment(ref activeVoxelsCounter2);
                        }
                    }
                }
            }
            );

            //if there are 0 active voxels in v1 then instantiate empty v2 voxels
            if (activeVoxelsCounter2 == 0)
            {
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            v2[i, j, k] = new Voxel(voxelSize, i, j, k);
                        }
                    }
                }
                );
            }

            //create empty voxels
            voxels = new Voxel[resX, resY, resZ];

            Parallel.For(0, resX, i =>
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        //GATE AND NOT
                        if (v1[i, j, k] != null || v2[i, j, k] != null)
                        {

                            if (v1[i, j, k] != null && v2[i, j, k] == null)
                            {
                                Voxel initialV1 = v1[i, j, k];

                                Voxel V = new Voxel(voxelSize, i, j, k);
                                voxels[i, j, k] = V;

                                //assign the voxel values from v1
                                V.minDensity = initialV1.minDensity;
                                V.maxDensity = initialV1.maxDensity;

                                V.speedMultiplier = initialV1.speedMultiplier;
                                V.sensorAngleMultiplier = initialV1.sensorAngleMultiplier;
                                V.sensorDistanceMultiplier = initialV1.sensorDistanceMultiplier;
                                V.rotationAngleMultiplier = initialV1.rotationAngleMultiplier;

                                V.food = initialV1.food;

                                V.voxelVector = initialV1.voxelVector;

                                V.frequency = initialV1.frequency;
                            }
                        }
                    }
                }
            }
            );

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