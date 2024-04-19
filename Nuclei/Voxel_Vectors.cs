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

            //count active voxels
            int activeVoxelsCounter = 0;
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

            int counter = 0;

            voxels = new Voxel[resX, resY, resZ];

            if (activeVoxelsCounter == 0)
            {
                //if there are 0 active voxels then instantiate new voxels
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

                counter = resX * resY * resZ;

            } else
            {
                //inherit values from input voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if (inputVoxels[i, j, k] != null)
                            {
                                Voxel inV = inputVoxels[i, j, k];
                                if (voxels[i, j, k] == null) voxels[i, j, k] = new Voxel(voxelSize, i, j, k);
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

                counter = activeVoxelsCounter;
            }

            //assign values
            int listIndex = 0;

            for (int i = 0; i < resX; i++)
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (voxels[i, j, k] != null)
                        {
                            Voxel V = voxels[i, j, k];

                            if (counter == voxelVectors.Count && counter == frequency.Count)
                            {
                                Vector3d unitV = voxelVectors[listIndex];
                                V.voxelVector = unitV;
                                V.frequency = frequency[listIndex];
                                listIndex++;
                            }

                            if (counter == voxelVectors.Count && counter != frequency.Count)
                            {
                                Vector3d unitV = voxelVectors[listIndex];
                                V.voxelVector = unitV;
                                V.frequency = frequency[0];
                                listIndex++;
                            }

                            if (counter != voxelVectors.Count && counter == frequency.Count)
                            {
                                Vector3d unitV = voxelVectors[0];
                                V.voxelVector = unitV;
                                V.frequency = frequency[listIndex];
                                listIndex++;
                            }

                            if (counter != voxelVectors.Count && counter != frequency.Count)
                            {
                                Vector3d unitV = voxelVectors[0];
                                V.voxelVector = unitV;
                                V.frequency = frequency[0];
                            }

                            if (planarXY)
                            {
                                V.voxelVector = new Vector3d(V.voxelVector.X, V.voxelVector.Y, 0);
                            }
                            else if (planarXZ)
                            {
                                V.voxelVector = new Vector3d(V.voxelVector.X, 0, V.voxelVector.Z);
                            }
                            else if (planarYZ)
                            {
                                V.voxelVector = new Vector3d(0, V.voxelVector.Y, V.voxelVector.Z);
                            }
                        }
                    }
                }
            }

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