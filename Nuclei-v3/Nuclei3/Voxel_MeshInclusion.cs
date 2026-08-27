using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Nuclei3
{
    public class Voxel_MeshInclusion : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_BrepInclusion class.
        /// </summary>
        public Voxel_MeshInclusion()
          : base("Voxel Inclusion in Mesh", "Voxel Inclusion in Mesh",
              "Test if a Voxel Center is Inside a Mesh",
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
            pManager.AddMeshParameter("Inclusion Meshes", "inclusionMeshes", "Inclusion Meshes", GH_ParamAccess.list);
            pManager[1].DataMapping = GH_DataMapping.Flatten;
            //2
            pManager.AddBooleanParameter("Invert Voxel Selection", "invertSelection", "Inverts the Voxel Selection", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
            pManager.AddPointParameter("Output Voxel Positions", "voxelPosition", "Output Voxel Positions", GH_ParamAccess.list);
            pManager.HideParameter(1);
        }


        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.quinary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //set inputs
            meshes = new List<Mesh>();

            //set outputs
            voxelPositions = new Grasshopper.DataTree<Point3d>();

            DA.GetData("Voxels", ref inputVoxels);
            DA.GetDataList("Inclusion Meshes", meshes);
            DA.GetData("Invert Voxel Selection", ref invert);

            if (trySolveWithSidecar(DA))
            {
                return;
            }

            //determine voxel settings
            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

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
                            inputVoxels[i, j, k] = new Voxel(voxelSize, i, j, k);
                        }
                    }
                }
                );
            }

            //test mesh inclusion
            for (int i = 0; i < resX; i++)
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (inputVoxels[i, j, k] != null)
                        {
                            Voxel inV = inputVoxels[i, j, k];
                            Point3d pt = inV.loc;

                            for (int m = 0; m < meshes.Count; m++)
                            {
                                Mesh M = meshes[m];
                                bool isInside = M.IsPointInside(pt, voxelSize / 2, true);

                                if (isInside)
                                {
                                    if (voxels[i, j, k] == null)
                                    {
                                        Voxel outV = new Voxel(voxelSize, i, j, k);

                                        //inherit values from input voxels
                                        outV.minDensity = inV.minDensity;
                                        outV.maxDensity = inV.maxDensity;
                                        outV.density = inV.density;

                                        outV.speedMultiplier = inV.speedMultiplier;
                                        outV.sensorAngleMultiplier = inV.sensorAngleMultiplier;
                                        outV.sensorDistanceMultiplier = inV.sensorDistanceMultiplier;
                                        outV.rotationAngleMultiplier = inV.rotationAngleMultiplier;

                                        outV.food = inV.food;
                                        outV.antFood = inV.antFood;

                                        outV.voxelVector = inV.voxelVector;
                                        outV.frequency = inV.frequency;

                                        voxels[i, j, k] = outV;

                                        voxelPositions.Add(outV.loc, new Grasshopper.Kernel.Data.GH_Path(m));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        
            
            if (invert == true)
            {
                voxelPositions.Clear();

                for (int i = 0; i < resX; i++)
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if (voxels[i, j, k] == null)
                            {
                                Voxel outV = new Voxel(voxelSize, i, j, k);

                                //inherit values from input voxels
                                if (inputVoxels[i, j, k] != null)
                                {
                                    Voxel inV = inputVoxels[i, j, k];

                                    outV.minDensity = inV.minDensity;
                                    outV.maxDensity = inV.maxDensity;
                                    outV.density = inV.density;

                                    outV.speedMultiplier = inV.speedMultiplier;
                                    outV.sensorAngleMultiplier = inV.sensorAngleMultiplier;
                                    outV.sensorDistanceMultiplier = inV.sensorDistanceMultiplier;
                                    outV.rotationAngleMultiplier = inV.rotationAngleMultiplier;

                                    outV.food = inV.food;
                                    outV.antFood = inV.antFood;

                                    outV.voxelVector = inV.voxelVector;
                                    outV.frequency = inV.frequency;
                                }

                                voxels[i, j, k] = outV;
                                voxelPositions.Add(outV.loc, new Grasshopper.Kernel.Data.GH_Path(0));
                            }
                            else
                            {
                                voxels[i, j, k] = null;
                            }
                        }
                    }
                }

            }

            /*
            //output voxel positions based on each attractor curve
            for (int i = 0; i < resX; i++)
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (voxels[i, j, k] != null)
                        {
                            Voxel V = voxels[i, j, k];

                            int index = -1;
                            double minDist = 999999;

                            for (int m = 0; m < meshes.Count; m++)
                            {
                                Mesh attractorM = meshes[m];

                                double t = -1;
                                Point3d closestP = attractorM.ClosestPoint(V.loc);

                                double dist = closestP.DistanceTo(V.loc);

                                if (dist < minDist)
                                {
                                    minDist = dist;
                                    index = m;
                                }

                            }

                            if (index != -1)
                            {
                                voxelPositions.Add(V.loc, new Grasshopper.Kernel.Data.GH_Path(index));
                            }
                        }
                    }
                }
            }
            */

            DA.SetData(0, voxels);
            DA.SetDataTree(1, voxelPositions);
        }

        //-------------------------------------------------------------------

        bool trySolveWithSidecar(IGH_DataAccess DA)
        {
            VoxelGridData inputData = VoxelGridRegistry.GetOrCapture(inputVoxels, Globals.voxelSize);
            if (inputData.Count == 0)
            {
                return false;
            }

            BoundingBox[] meshBounds = new BoundingBox[meshes.Count];
            for (int i = 0; i < meshes.Count; i++)
            {
                meshBounds[i] = meshes[i] != null ? meshes[i].GetBoundingBox(true) : BoundingBox.Unset;
                if (meshBounds[i].IsValid)
                {
                    meshBounds[i].Inflate(inputData.VoxelSize / 2);
                }
            }

            bool[] insideMask = new bool[inputData.Count];
            int[] firstMeshIndex = new int[inputData.Count];
            for (int i = 0; i < firstMeshIndex.Length; i++)
            {
                firstMeshIndex[i] = -1;
            }

            Parallel.For(0, inputData.ActiveCount, ordinal =>
            {
                int flatIndex = inputData.ActiveFlatIndexAt(ordinal);
                Point3d point = inputData.CenterPoint(flatIndex);

                for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
                {
                    Mesh mesh = meshes[meshIndex];
                    if (mesh == null)
                    {
                        continue;
                    }

                    BoundingBox bounds = meshBounds[meshIndex];
                    if (bounds.IsValid && !bounds.Contains(point))
                    {
                        continue;
                    }

                    if (mesh.IsPointInside(point, inputData.VoxelSize / 2, true))
                    {
                        insideMask[flatIndex] = true;
                        firstMeshIndex[flatIndex] = meshIndex;
                        break;
                    }
                }
            });

            bool[] outputMask = new bool[inputData.Count];
            if (invert)
            {
                for (int flatIndex = 0; flatIndex < outputMask.Length; flatIndex++)
                {
                    outputMask[flatIndex] = !insideMask[flatIndex];
                }
            }
            else
            {
                Array.Copy(insideMask, outputMask, insideMask.Length);
            }

            VoxelGridData outputData = inputData.WithActiveMask(outputMask);
            voxels = outputData.ToVoxelArray(true);
            VoxelGridRegistry.Set(voxels, outputData);

            voxelPositions.Clear();
            for (int ordinal = 0; ordinal < outputData.ActiveCount; ordinal++)
            {
                int flatIndex = outputData.ActiveFlatIndexAt(ordinal);
                int pathIndex = invert ? 0 : Math.Max(0, firstMeshIndex[flatIndex]);
                voxelPositions.Add(outputData.CenterPoint(flatIndex), new Grasshopper.Kernel.Data.GH_Path(pathIndex));
            }

            DA.SetData(0, voxels);
            DA.SetDataTree(1, voxelPositions);
            return true;
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] inputVoxels;

        List<Mesh> meshes;

        bool invert;

        //-------------------------------------------------------------------

        //outputs

        Voxel[,,] voxels;
        Grasshopper.DataTree<Point3d> voxelPositions;

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
                return Nuclei3.Properties.Resources.VoxelMeshInclusion;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("75c987f0-57a0-56c9-bf9f-f4ef4f3ebfed"); }
        }
    }
}
