using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Voxel_Vectors_BlendAll : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Vectors_BlendAll class.
        /// </summary>
        public Voxel_Vectors_BlendAll()
          : base("Voxel Vectors Blend", "Blend Vectorfield",
              "Blend All Vectors By Averaging Their Neighbours",
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
            //1
            pManager.AddNumberParameter("Blend Strength", "blendStrength", "Strength of Blend", GH_ParamAccess.item, 0.25);
            pManager[1].Optional = true;
            //2
            pManager.AddIntegerParameter("Blend Range", "range", "The Range of Blend", GH_ParamAccess.item, 1);
            pManager[2].Optional = true;
            //3
            pManager.AddIntegerParameter("Blend Iterations", "iterations", "Blend Number of Iterations", GH_ParamAccess.item, 1);
            pManager[3].Optional = true;
            //4
            pManager.AddBooleanParameter("Wrap Blend", "wrap", "Boundary conditions", GH_ParamAccess.item, false);
            pManager[4].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //0
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
            blendDiffuse = 0.25;
            blendRange = 1;
            blendIterations = 1;
            wrapBlend = false;

            DA.GetData(0, ref inputVoxels);
            DA.GetData(1, ref blendDiffuse);
            DA.GetData(2, ref blendRange);
            DA.GetData(3, ref blendIterations);
            DA.GetData(4, ref wrapBlend);

            //determine voxel settings
            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

            //determine whether 3D or 2D 
            bool planarXY = false;
            bool planarXZ = false;
            bool planarYZ = false;

            if (resX == 1)
            {
                planarXY = false;
                planarXZ = false;
                planarYZ = true;
            }
            if (resY == 1)
            {
                planarXY = false;
                planarXZ = true;
                planarYZ = false;
            }
            if (resZ == 1)
            {
                planarXY = true;
                planarXZ = false;
                planarYZ = false;
            }

            double voxelSize = Globals.voxelSize;

            //check to see if input voxel comes from construct voxels and is null
            int activeVoxelsCounter = 0;

            //count active voxels
            Parallel.For(0, resX, i =>
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (inputVoxels[i, j, k] != null)
                        {
                            int parallelCounter = System.Threading.Interlocked.Increment(ref activeVoxelsCounter);
                        }
                    }
                }
            }
            );

            voxels = new Voxel[resX, resY, resZ];
        
            if (activeVoxelsCounter == 0)
            {
                //if there are 0 active voxels in then instantiate new voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            voxels[i, j, k] = new Voxel(voxelSize, i, j, k);
                        }
                    }
                }
                );
            } else
            {
                //inherit values from input voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if(inputVoxels[i,j,k] != null)
                            {
                                Voxel inV = inputVoxels[i, j, k];
                                if(voxels[i,j,k] == null) voxels[i, j, k] = new Voxel(voxelSize, i, j, k);
                                Voxel outV = voxels[i, j, k];

                                outV.minDensity = inV.minDensity;
                                outV.maxDensity = inV.maxDensity;
                                outV.density = inV.density;

                                outV.speedMultiplier = inV.speedMultiplier;
                                outV.sensorAngleMultiplier = inV.sensorAngleMultiplier;
                                outV.sensorDistanceMultiplier = inV.sensorDistanceMultiplier;
                                outV.rotationAngleMultiplier = inV.rotationAngleMultiplier;

                                outV.food = inV.food;

                                outV.voxelVector = inV.voxelVector;
                                outV.frequency = inV.frequency;
                            }
                        }
                    }
                }
                );
            }

            //create zero vector newVoxelVector array
            Vector3d[,,] newVoxelVector = new Vector3d[resX, resY, resZ];

            //blend
            for (int it = 0; it < blendIterations; it++)
            {
                Parallel.For(0, resZ, k =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int i = 0; i < resX; i++)
                        {
                            if (voxels[i, j, k] != null)
                            {
                                Voxel outV = voxels[i, j, k];

                                Vector3d neighbourSum = new Vector3d();

                                for (int u = -blendRange; u <= blendRange; u++)
                                {
                                    for (int v = -blendRange; v <= blendRange; v++)
                                    {
                                        for (int w = -blendRange; w <= blendRange; w++)
                                        {
                                            int b_xID = i + u;
                                            int b_yID = j + v;
                                            int b_zID = k + w;

                                            if (wrapBlend)
                                            {
                                                if (b_xID < 0) b_xID += resX;
                                                if (b_xID > resX - 1) b_xID -= resX;

                                                if (b_yID < 0) b_yID += resY;
                                                if (b_yID > resY - 1) b_yID -= resY;

                                                if (b_zID < 0) b_zID += resZ;
                                                if (b_zID > resZ - 1) b_zID -= resZ;
                                            }

                                            if (b_xID >= 0 && b_xID < resX && b_yID >= 0 && b_yID < resY && b_zID >= 0 && b_zID < resZ)
                                            {
                                                if (voxels[b_xID, b_yID, b_zID] != null)
                                                {
                                                    Voxel neighbour = voxels[b_xID, b_yID, b_zID];

                                                    neighbour.voxelVector.Unitize();
                                                    neighbourSum += neighbour.voxelVector;
                                                }
                                            }
                                        }
                                    }
                                }

                                neighbourSum.Unitize();
                                outV.voxelVector.Unitize();
                                newVoxelVector[i, j, k] = (1.0 - blendDiffuse) * outV.voxelVector + blendDiffuse * neighbourSum;
                                newVoxelVector[i, j, k].Unitize();
                            }
                        }
                    }
                }
                );

                //assign values
                Parallel.For(0, resZ, k =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int i = 0; i < resX; i++)
                        {
                            if (voxels[i, j, k] != null)
                            {
                                Voxel V = voxels[i, j, k];
                                V.voxelVector = newVoxelVector[i, j, k];

                                if (planarXY)
                                {
                                    V.voxelVector = new Vector3d(V.voxelVector.X, V.voxelVector.Y, 0);
                                    V.voxelVector.Unitize();
                                }
                                else if (planarXZ)
                                {
                                    V.voxelVector = new Vector3d(V.voxelVector.X, 0, V.voxelVector.Z);
                                    V.voxelVector.Unitize();
                                }
                                else if (planarYZ)
                                {
                                    V.voxelVector = new Vector3d(0, V.voxelVector.Y, V.voxelVector.Z);
                                    V.voxelVector.Unitize();
                                }
                            }
                        }
                    }
                }
                );
            }


            DA.SetData(0, voxels);
        }

        //-------------------------------------------------------------------

        //inputs
        double blendDiffuse;
        int blendRange;
        int blendIterations;
        bool wrapBlend;
        Voxel[,,] inputVoxels;

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
                return Nuclei3.Properties.Resources.VoxelBlendVectors2;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("9cecbe0d-5b14-4e01-a7ee-7987e75faf71"); }
        }
    }
}