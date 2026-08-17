using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Nuclei4
{
    public class Voxel_Attractor_Mesh : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Attractor_Mesh2 class.
        /// </summary>
        public Voxel_Attractor_Mesh()
          : base("Mesh Attractor for Voxel", "Mesh Attractor for Voxel",
              "Use Meshes as Attractors for Voxel Centers",
              "Nuclei4", " Environment")
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
            pManager.AddMeshParameter("Attractor Meshes", "attractorMeshes", "Attractor Meshes", GH_ParamAccess.list);
            pManager[1].DataMapping = GH_DataMapping.Flatten;
            //2
            pManager.AddNumberParameter("Minimum Range", "minRange", "Minimum Range for Attractor", GH_ParamAccess.item, 0.0);
            //3
            pManager.AddNumberParameter("Maximum Range", "maxRange", "Maximum Range for Attractor", GH_ParamAccess.item, 1.0);
            //4
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
            pManager.AddNumberParameter("Output Distances to Voxels", "voxelDistance", "Output Distances from Attractor to Voxel", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Output Voxel Indices", "voxelIndex", "Output Voxel Indices for Sorting", GH_ParamAccess.list);
        }

        #region menu items

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("Minimum", this.min);
            writer.SetBoolean("Maximum", this.max);
            writer.SetBoolean("Average", this.average);

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            this.min = true;
            reader.TryGetBoolean("Minimum", ref this.min);

            this.max = false;
            reader.TryGetBoolean("Maximum", ref this.max);

            this.average = false;
            reader.TryGetBoolean("Average", ref this.average);

            return base.Read(reader);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            var minToggle = Menu_AppendItem(menu, "Minimum", minHandler, true, this.min);
            minToggle.ToolTipText = "Minimum";

            var maxToggle = Menu_AppendItem(menu, "Maximum", maxHandler, true, this.max);
            maxToggle.ToolTipText = "Maximum";

            var averageToggle = Menu_AppendItem(menu, "Average", averageHandler, true, this.average);
            averageToggle.ToolTipText = "Average";
        }

        protected void handler(object sender, EventArgs e)
        {
            this.min = !this.min;

            this.max = !this.max;

            this.average = !this.average;

            this.ExpireSolution(true);
        }

        protected void minHandler(object sender, EventArgs e)
        {
            this.min = true;
            this.max = false;
            this.average = false;
            this.ExpireSolution(true);
        }

        protected void maxHandler(object sender, EventArgs e)
        {
            this.min = false;
            this.max = true;
            this.average = false;
            this.ExpireSolution(true);
        }

        protected void averageHandler(object sender, EventArgs e)
        {
            this.min = false;
            this.max = false;
            this.average = true;
            this.ExpireSolution(true);
        }



        #endregion

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
            //initialize inputs
            attractorMeshes = new List<Mesh>();


            //initialize output lists 
            voxelPositions = new Grasshopper.DataTree<Point3d>();
            voxelDistances = new Grasshopper.DataTree<double>();
            voxelIndices = new Grasshopper.DataTree<int>();

            if (!VoxelFieldAccess.TryGet(DA, "Voxels", Globals.voxelSize, out inputVoxelField))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "A valid voxel field is required.");
                return;
            }
            DA.GetDataList("Attractor Meshes", attractorMeshes);
            DA.GetData("Minimum Range", ref minR);
            DA.GetData("Maximum Range", ref maxR);
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

            //remesh
            QuadRemeshParameters quadRemeshParameters = new QuadRemeshParameters();
            quadRemeshParameters.TargetEdgeLength = voxelSize / 2;

            for (int i = 0; i < attractorMeshes.Count; i++)
            {
                attractorMeshes[i] = attractorMeshes[i].QuadRemesh(quadRemeshParameters);
            }

            //create list of empty voxels
            Voxel[,,] dummyVoxel = new Voxel[resX, resY, resZ];
            ConcurrentBag<Voxel> dummyVoxelsConcurrent = new ConcurrentBag<Voxel>();

            //read mesh vertices and create dummy voxels
            Parallel.For(0, attractorMeshes.Count, i =>
            {
                Mesh M = attractorMeshes[i];

                for(int j=0; j<M.Vertices.Count; j++)
                {
                    //convert vertex coordinates to voxel center coordinates
                    int xID = System.Convert.ToInt32((M.Vertices[j].X - Math.Abs(M.Vertices[j].X % voxelSize)) / voxelSize);
                    int yID = System.Convert.ToInt32((M.Vertices[j].Y - Math.Abs(M.Vertices[j].Y % voxelSize)) / voxelSize);
                    int zID = System.Convert.ToInt32((M.Vertices[j].Z - Math.Abs(M.Vertices[j].Z % voxelSize)) / voxelSize);

                    if (xID >= 0 && xID < resX && yID >= 0 && yID < resY && zID >= 0 && zID < resZ)
                    {
                        //if the voxel doesn't already exist, then create it
                        if (dummyVoxel[xID, yID, zID] == null)
                        {
                            dummyVoxel[xID, yID, zID] = new Voxel(voxelSize, xID, yID, zID);
                            dummyVoxelsConcurrent.Add(new Voxel(voxelSize, xID, yID, zID));
                        }
                    }
                }
            }
            );

            //create ranges depending on min & max tresholds
            double theRealMin = Math.Min(minR, maxR);
            double theRealMax = Math.Max(minR, maxR);
            int maxRange = Convert.ToInt32(Math.Ceiling(theRealMax / voxelSize));

            //create voxels around dummy voxels
            Voxel[] dummyVoxels = dummyVoxelsConcurrent.ToArray();
            voxels = new Voxel[resX, resY, resZ];

            Parallel.For(0, dummyVoxels.Length, i =>
            {
                //search around dummyV
                for (int u = dummyVoxels[i].idX - maxRange; u <= dummyVoxels[i].idX + maxRange; u++)
                {
                    for (int v = dummyVoxels[i].idY - maxRange; v <= dummyVoxels[i].idY + maxRange; v++)
                    {
                        for (int w = dummyVoxels[i].idZ - maxRange; w <= dummyVoxels[i].idZ + maxRange; w++)
                        {
                            if (u >= 0 && u < resX && v >= 0 && v < resY && w >= 0 && w < resZ)
                            {
                                //create new Voxel, if it doesn't exist already
                                if (voxels[u, v, w] == null)
                                {
                                    Voxel outV = new Voxel(voxelSize, u, v, w);

                                    double dist = dummyVoxels[i].loc.DistanceTo(outV.loc);

                                    if (dist <= theRealMax)
                                    {

                                        //inherit values from input voxels
                                        if (inputVoxels[u, v, w] != null)
                                        {
                                            Voxel inV = inputVoxels[u, v, w];

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

                                        voxels[u, v, w] = outV;
                                    }
                                }
                            }
                        }
                    }
                }
            }
           );


            int indexCounter = 0;

            if (!invert)
            {
                //output voxel positions based on each attractor mesh
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
                                double maxDist = -99999;
                                double outputDist = -1;
                                int outputDistCounter = 0;

                                for (int m = 0; m < attractorMeshes.Count; m++)
                                {
                                    Mesh attractorM = attractorMeshes[m];

                                    Point3d closestP = attractorM.ClosestPoint(V.loc);

                                    if (closestP != Point3d.Unset)
                                    {
                                        double dist = closestP.DistanceTo(V.loc);

                                        if (theRealMin <= dist && dist <= theRealMax)
                                        {
                                            if (dist < minDist)
                                            {
                                                minDist = dist;
                                                index = m;
                                            }

                                            if (dist > maxDist)
                                            {
                                                maxDist = dist;
                                            }

                                            outputDist += dist;
                                            outputDistCounter++;
                                        }
                                    }
                                }

                                if (index != -1)
                                {
                                    voxelPositions.Add(V.loc, new Grasshopper.Kernel.Data.GH_Path(index));
                                    voxelIndices.Add(indexCounter, new Grasshopper.Kernel.Data.GH_Path(index));

                                    if (min) voxelDistances.Add(minDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                    if (max) voxelDistances.Add(maxDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                    if (average) voxelDistances.Add(outputDist / outputDistCounter * 1f, new Grasshopper.Kernel.Data.GH_Path(index));

                                    indexCounter++;
                                }
                                else
                                {
                                    voxels[i, j, k] = null;
                                }
                            }
                        }
                    }
                }
            }

            if (invert)
            {
                //remove voxels that are closer to the curves than the minimum distance

                if (theRealMin != 0)
                {
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

                                    for (int m = 0; m < attractorMeshes.Count; m++)
                                    {
                                        Mesh attractorM = attractorMeshes[m];

                                        Point3d closestP = attractorM.ClosestPoint(V.loc);

                                        if (closestP != Point3d.Unset)
                                        {
                                            double dist = closestP.DistanceTo(V.loc);

                                            if (theRealMin <= dist && dist <= theRealMax)
                                            {
                                                index = 0;
                                            }
                                        }
                                    }

                                    if (index == -1)
                                    {
                                        voxels[i, j, k] = null;
                                    }
                                }
                            }
                        }
                    }
                }

                //reverse null and non-null voxels
                Parallel.For(0, resX, i =>
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

                                    outV.voxelVector = inV.voxelVector;
                                    outV.frequency = inV.frequency;
                                }

                                voxels[i, j, k] = outV;
                            }
                            else
                            {
                                voxels[i, j, k] = null;
                            }
                        }
                    }
                }
                );

                //output voxel positions based on each attractor mesh
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
                                double maxDist = -99999;
                                double outputDist = -1;
                                int outputDistCounter = 0;

                                for (int m = 0; m < attractorMeshes.Count; m++)
                                {
                                    Mesh attractorM = attractorMeshes[m];

                                    Point3d closestP = attractorM.ClosestPoint(V.loc);

                                    if (closestP != Point3d.Unset)
                                    {
                                        double dist = closestP.DistanceTo(V.loc);

                                        index = 0;
                                        outputDist += dist;
                                        outputDistCounter++;

                                        if (dist < minDist) minDist = dist;
                                        if (dist > maxDist) maxDist = dist;

                                    }
                                }

                                if (index != -1)
                                {
                                    voxelPositions.Add(V.loc, new Grasshopper.Kernel.Data.GH_Path(index));
                                    voxelIndices.Add(indexCounter, new Grasshopper.Kernel.Data.GH_Path(index));

                                    if (min) voxelDistances.Add(minDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                    if (max) voxelDistances.Add(maxDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                    if (average) voxelDistances.Add(outputDist / outputDistCounter * 1f, new Grasshopper.Kernel.Data.GH_Path(index));

                                    indexCounter++;
                                }
                                else
                                {
                                    voxels[i, j, k] = null;
                                }
                            }
                        }
                    }
                }
            }

            DA.SetData(0, voxels);
            DA.SetDataTree(1, voxelPositions);
            DA.SetDataTree(2, voxelDistances);
            DA.SetDataTree(3, voxelIndices);

            if (min) this.Message = "Minimum";
            if (max) this.Message = "Maximum";
            if (average) this.Message = "Average";
        }

        //-------------------------------------------------------------------

        //inputs
        public bool min = true;
        public bool max = false;
        public bool average = false;

        Voxel[,,] inputVoxels;
        VoxelField inputVoxelField;

        List<Mesh> attractorMeshes;
        double minR, maxR;

        bool invert;

        //-------------------------------------------------------------------

        //outputs
        Voxel[,,] voxels;
        Grasshopper.DataTree<Point3d> voxelPositions;
        Grasshopper.DataTree<double> voxelDistances;
        Grasshopper.DataTree<int> voxelIndices;

        //-------------------------------------------------------------------

        bool trySolveWithSidecar(IGH_DataAccess DA)
        {
            VoxelGridData inputData = inputVoxelField.Data;
            if (inputData.Count == 0 || inputData.VoxelSize <= 0)
            {
                return false;
            }

            double theRealMin = Math.Min(minR, maxR);
            double theRealMax = Math.Max(minR, maxR);
            VoxelSelectionBuilder selected = new VoxelSelectionBuilder(inputData.Count);

            for (int meshIndex = 0; meshIndex < attractorMeshes.Count; meshIndex++)
            {
                Mesh mesh = attractorMeshes[meshIndex];
                if (mesh == null)
                {
                    continue;
                }

                BoundingBox bounds = mesh.GetBoundingBox(true);
                if (!bounds.IsValid)
                {
                    continue;
                }

                bounds.Inflate(theRealMax);
                int minX = clampVoxelIndex(voxelIndex(bounds.Min.X, inputData.VoxelSize), inputData.ResX);
                int minY = clampVoxelIndex(voxelIndex(bounds.Min.Y, inputData.VoxelSize), inputData.ResY);
                int minZ = clampVoxelIndex(voxelIndex(bounds.Min.Z, inputData.VoxelSize), inputData.ResZ);
                int maxX = clampVoxelIndex(voxelIndex(bounds.Max.X, inputData.VoxelSize), inputData.ResX);
                int maxY = clampVoxelIndex(voxelIndex(bounds.Max.Y, inputData.VoxelSize), inputData.ResY);
                int maxZ = clampVoxelIndex(voxelIndex(bounds.Max.Z, inputData.VoxelSize), inputData.ResZ);

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            int flatIndex = inputData.FlatIndex(x, y, z);
                            Point3d center = inputData.CenterPoint(flatIndex);
                            Point3d closest = mesh.ClosestPoint(center);
                            if (closest == Point3d.Unset)
                            {
                                continue;
                            }

                            double dist = closest.DistanceTo(center);
                            if (theRealMin <= dist && dist <= theRealMax)
                            {
                                selected.Set(flatIndex);
                            }
                        }
                    }
                }
            }

            if (invert) selected.Invert();
            selected.IntersectWith(inputData);
            VoxelGridData outputData = selected.ApplyTo(inputData);
            VoxelField outputField = inputVoxelField.WithData(outputData);

            int indexCounter = 0;
            for (int ordinal = 0; ordinal < outputData.ActiveCount; ordinal++)
            {
                int flatIndex = outputData.ActiveFlatIndexAt(ordinal);
                Point3d center = outputData.CenterPoint(flatIndex);

                int index = -1;
                double minDist = 999999;
                double maxDist = -99999;
                double outputDist = -1;
                int outputDistCounter = 0;

                for (int meshIndex = 0; meshIndex < attractorMeshes.Count; meshIndex++)
                {
                    Mesh mesh = attractorMeshes[meshIndex];
                    if (mesh == null)
                    {
                        continue;
                    }

                    Point3d closest = mesh.ClosestPoint(center);
                    if (closest == Point3d.Unset)
                    {
                        continue;
                    }

                    double dist = closest.DistanceTo(center);
                    if (!invert)
                    {
                        if (theRealMin <= dist && dist <= theRealMax)
                        {
                            if (dist < minDist)
                            {
                                minDist = dist;
                                index = meshIndex;
                            }

                            if (dist > maxDist) maxDist = dist;
                            outputDist += dist;
                            outputDistCounter++;
                        }
                    }
                    else
                    {
                        index = 0;
                        outputDist += dist;
                        outputDistCounter++;
                        if (dist < minDist) minDist = dist;
                        if (dist > maxDist) maxDist = dist;
                    }
                }

                if (index != -1 && outputDistCounter > 0)
                {
                    Grasshopper.Kernel.Data.GH_Path path = new Grasshopper.Kernel.Data.GH_Path(index);
                    voxelPositions.Add(center, path);
                    voxelIndices.Add(indexCounter, path);

                    if (min) voxelDistances.Add(minDist, path);
                    if (max) voxelDistances.Add(maxDist, path);
                    if (average) voxelDistances.Add(outputDist / outputDistCounter * 1f, path);

                    indexCounter++;
                }
            }

            DA.SetData(0, outputField);
            DA.SetDataTree(1, voxelPositions);
            DA.SetDataTree(2, voxelDistances);
            DA.SetDataTree(3, voxelIndices);

            if (min) this.Message = "Minimum";
            if (max) this.Message = "Maximum";
            if (average) this.Message = "Average";
            return true;
        }

        int voxelIndex(double coordinate, double size)
        {
            return System.Convert.ToInt32((coordinate - Math.Abs(coordinate % size)) / size);
        }

        int clampVoxelIndex(int index, int count)
        {
            if (index < 0) return 0;
            if (index >= count) return count - 1;
            return index;
        }

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
                return Nuclei4.Properties.Resources.VoxelMeshAttractor;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("6bb5c231-45a2-4fd8-8698-93da2b8631aa"); }
        }
    }
}
