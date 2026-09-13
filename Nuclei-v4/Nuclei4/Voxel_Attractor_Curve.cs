using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Threading.Tasks;
using GH_IO.Serialization;
using System.Windows.Forms;

namespace Nuclei4
{
    public class Voxel_Attractor_Curve : GH_Component
    {
        long selectedVoxelCount;

        public override void ClearData()
        {
            base.ClearData();
            selectedVoxelCount = 0;
            VoxelAttractorOutputs.WriteCount(this, ref selectedVoxelCount, 0);
        }

        /// <summary>
        /// Initializes a new instance of the Voxel_CurveAttractor class.
        /// </summary>
        public Voxel_Attractor_Curve()
          : base("Curve Attractor for Voxels", "Curve Attractor for Voxels",
              "Use Curves as Attractors for Voxel Centers",
              "Nuclei4", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        VoxelOutputDemand outputDemand;

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (outputDemand == null) outputDemand = new VoxelOutputDemand(this);
            outputDemand.Attach(document);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            outputDemand?.Detach();
            base.RemovedFromDocument(document);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
            //1
            pManager.AddCurveParameter("Attractor Curves", "attractorCurves", "Attractor Curves", GH_ParamAccess.list);
            pManager[1].DataMapping = GH_DataMapping.Flatten;
            //2
            pManager.AddNumberParameter("Minimum Range", "minRange", VoxelAttractorRange.MinimumDescription, GH_ParamAccess.item, 0.0);
            //3
            pManager.AddNumberParameter("Maximum Range", "maxRange", VoxelAttractorRange.MaximumDescription, GH_ParamAccess.item, 1.0);
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

            bool result = base.Read(reader);
            Params.Input[2].Description = VoxelAttractorRange.MinimumDescription;
            Params.Input[3].Description = VoxelAttractorRange.MaximumDescription;
            return result;
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
            //initialize input lists
            attractorCurves = new List<Curve>();

            //initialize output lists 
            voxelPositions = Params.Output[1].Recipients.Count > 0 ? new Grasshopper.DataTree<Point3d>() : null;
            voxelDistances = Params.Output[2].Recipients.Count > 0 ? new Grasshopper.DataTree<double>() : null;
            voxelIndices = Params.Output[3].Recipients.Count > 0 ? new Grasshopper.DataTree<int>() : null;

            if (!VoxelFieldAccess.TryGet(DA, "Voxels", Globals.voxelSize, out inputVoxelField))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "A valid voxel field is required.");
                return;
            }
            DA.GetDataList("Attractor Curves", attractorCurves);
            minR = 0; maxR = 1;
            DA.GetData("Minimum Range", ref minR);
            DA.GetData("Maximum Range", ref maxR);
            DA.GetData("Invert Voxel Selection", ref invert);

            if (!VoxelAttractorRange.TryNormalize(this, 3, 1, inputVoxelField.VoxelSize, ref minR, ref maxR)) return;

            if (trySolveWithSidecar(DA))
            {
                return;
            }

            //determine voxel settings
            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

            double voxelSize = Globals.voxelSize;

            //create list of empty voxels
            Voxel[,,] dummyVoxel = new Voxel[resX, resY, resZ];
            List<Voxel> dummyVoxels = new List<Voxel>();
            List<Point3d> attractorPoints = new List<Point3d>();

            //get curve division points
            for (int i = 0; i < attractorCurves.Count; i++)
            {
                Curve C = attractorCurves[i];
                Point3d[] divPt;

                double cLength = C.GetLength();
                if (cLength > voxelSize * 1.4)
                {
                    C.DivideByLength(voxelSize * 0.7, true, out divPt);
                }
                else
                {
                    divPt = new Point3d[2];
                    divPt[0] = C.PointAtStart;
                    divPt[1] = C.PointAtEnd;
                }

                if (divPt.Length > 0)
                {
                    for (int j = 0; j < divPt.Length; j++)
                    {
                        Point3d p = divPt[j];

                        if (p != null)
                        {
                            //convert division point coordinates to voxel center coordinates
                            int xID = System.Convert.ToInt32((p.X - Math.Abs(p.X % voxelSize)) / voxelSize);
                            int yID = System.Convert.ToInt32((p.Y - Math.Abs(p.Y % voxelSize)) / voxelSize);
                            int zID = System.Convert.ToInt32((p.Z - Math.Abs(p.Z % voxelSize)) / voxelSize);

                            if (xID >= 0 && xID < resX && yID >= 0 && yID < resY && zID >= 0 && zID < resZ)
                            {
                                //if the voxel doesn't already exist, then create it
                                if (dummyVoxel[xID, yID, zID] == null)
                                {
                                    dummyVoxel[xID, yID, zID] = new Voxel(voxelSize, xID, yID, zID);
                                    dummyVoxels.Add(new Voxel(voxelSize, xID, yID, zID));

                                    attractorPoints.Add(p);
                                }
                            }
                        }
                    }
                }
            }

            //create ranges depending on min & max tresholds
            double theRealMin = Math.Min(minR, maxR);
            double theRealMax = Math.Max(minR, maxR);
            int maxRange = Convert.ToInt32(Math.Ceiling(theRealMax / voxelSize));

            //create voxels around dummy voxels
            voxels = new Voxel[resX, resY, resZ];

            Parallel.For(0, dummyVoxels.Count, i =>
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

                                    double dist = attractorPoints[i].DistanceTo(outV.loc);

                                    if (theRealMin <= dist && dist <= theRealMax)
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
                                double maxDist = -99999;
                                double outputDist = -1;
                                int outputDistCounter = 0;

                                for (int c = 0; c < attractorCurves.Count; c++)
                                {
                                    Curve attractorC = attractorCurves[c];

                                    double t = -1;
                                    attractorC.ClosestPoint(V.loc, out t);

                                    if (t != -1)
                                    {
                                        Point3d closestP = attractorC.PointAt(t);

                                        double dist = closestP.DistanceTo(V.loc);

                                        if (theRealMin<= dist && dist <= theRealMax)
                                        {
                                            if (dist < minDist)
                                            {
                                                minDist = dist;
                                                index = c;
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
                                    voxelPositions?.Add(V.loc, new Grasshopper.Kernel.Data.GH_Path(index));
                                    voxelIndices?.Add(indexCounter, new Grasshopper.Kernel.Data.GH_Path(index));

                                    if (min) voxelDistances?.Add(minDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                    if (max) voxelDistances?.Add(maxDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                    if (average) voxelDistances?.Add(outputDist / outputDistCounter * 1f, new Grasshopper.Kernel.Data.GH_Path(index));

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

                                    for (int c = 0; c < attractorCurves.Count; c++)
                                    {
                                        Curve attractorC = attractorCurves[c];

                                        double t = -1;
                                        attractorC.ClosestPoint(V.loc, out t);

                                        if (t != -1)
                                        {
                                            Point3d closestP = attractorC.PointAt(t);
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
                                double maxDist = -99999;
                                double outputDist = -1;
                                int outputDistCounter = 0;

                                for (int c = 0; c < attractorCurves.Count; c++)
                                {
                                    Curve attractorC = attractorCurves[c];

                                    double t = -1;
                                    attractorC.ClosestPoint(V.loc, out t);

                                    if (t != -1)
                                    {
                                        Point3d closestP = attractorC.PointAt(t);

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
                                    voxelPositions?.Add(V.loc, new Grasshopper.Kernel.Data.GH_Path(index));
                                    voxelIndices?.Add(indexCounter, new Grasshopper.Kernel.Data.GH_Path(index));

                                    if (min) voxelDistances?.Add(minDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                    if (max) voxelDistances?.Add(maxDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                    if (average) voxelDistances?.Add(outputDist / outputDistCounter * 1f, new Grasshopper.Kernel.Data.GH_Path(index));

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
            if (voxelPositions != null) DA.SetDataTree(1, voxelPositions);
            if (voxelDistances != null) DA.SetDataTree(2, voxelDistances);
            if (voxelIndices != null) DA.SetDataTree(3, voxelIndices);

            VoxelAttractorOutputs.WriteCount(this, ref selectedVoxelCount, indexCounter);

        }

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
            bool pairBand = VoxelAttractorBand.NeedsPairs(theRealMin, theRealMax, inputData.VoxelSize);
            double searchMax = VoxelAttractorBand.SearchMaximum(theRealMin, theRealMax, inputData.VoxelSize);
            double minSquared = pairBand ? 0 : theRealMin * theRealMin;
            double maxSquared = searchMax * searchMax;
            int maxRange = Convert.ToInt32(Math.Ceiling(searchMax / inputData.VoxelSize));

            List<Point3d> samplePoints = collectCurveSamplePoints(inputData);
            VoxelSelectionBuilder selected = new VoxelSelectionBuilder(inputData.Count);

            Parallel.For(0, samplePoints.Count, i =>
            {
                Point3d point = samplePoints[i];
                int centerX = voxelIndex(point.X, inputData.VoxelSize);
                int centerY = voxelIndex(point.Y, inputData.VoxelSize);
                int centerZ = voxelIndex(point.Z, inputData.VoxelSize);

                for (int x = centerX - maxRange; x <= centerX + maxRange; x++)
                {
                    if (x < 0 || x >= inputData.ResX) continue;
                    double dx = (x * inputData.VoxelSize + inputData.VoxelSize / 2) - point.X;
                    double dx2 = dx * dx;

                    for (int y = centerY - maxRange; y <= centerY + maxRange; y++)
                    {
                        if (y < 0 || y >= inputData.ResY) continue;
                        double dy = (y * inputData.VoxelSize + inputData.VoxelSize / 2) - point.Y;
                        double dxy2 = dx2 + dy * dy;
                        if (dxy2 > maxSquared) continue;

                        for (int z = centerZ - maxRange; z <= centerZ + maxRange; z++)
                        {
                            if (z < 0 || z >= inputData.ResZ) continue;
                            double dz = (z * inputData.VoxelSize + inputData.VoxelSize / 2) - point.Z;
                            double distanceSquared = dxy2 + dz * dz;
                            if (distanceSquared >= minSquared && distanceSquared <= maxSquared)
                            {
                                selected.SetThreadSafe(inputData.FlatIndex(x, y, z));
                            }
                        }
                    }
                }
            });

            if (!invert || theRealMin != 0)
            {
                refineCurveSelection(inputData, selected, theRealMin, theRealMax);
            }

            if (invert) selected.Invert();
            selected.IntersectWith(inputData);
            VoxelGridData outputData = selected.ApplyTo(inputData);
            VoxelField outputField = inputVoxelField.WithData(outputData);

            VoxelAttractorOutputs.Write(DA, outputField, attractorCurves.Count, (a, center) => { double d; return tryCurveDistance(attractorCurves[a], center, out d) ? d : double.NaN; },
                theRealMin, theRealMax, invert, min, max, average, voxelPositions, voxelDistances, voxelIndices);

            VoxelAttractorOutputs.WriteCount(this, ref selectedVoxelCount, outputData.ActiveCount);
            return true;
        }

        List<Point3d> collectCurveSamplePoints(VoxelGridData inputData)
        {
            HashSet<int> sampleCells = new HashSet<int>();
            List<Point3d> samplePoints = new List<Point3d>();

            for (int i = 0; i < attractorCurves.Count; i++)
            {
                Curve curve = attractorCurves[i];
                if (curve == null)
                {
                    continue;
                }

                Point3d[] divPt = null;
                double curveLength = curve.GetLength();
                if (curveLength > inputData.VoxelSize * 1.4)
                {
                    curve.DivideByLength(inputData.VoxelSize * 0.7, true, out divPt);
                }
                else
                {
                    divPt = new Point3d[2];
                    divPt[0] = curve.PointAtStart;
                    divPt[1] = curve.PointAtEnd;
                }

                if (divPt == null)
                {
                    continue;
                }

                for (int j = 0; j < divPt.Length; j++)
                {
                    Point3d point = divPt[j];
                    int xID = voxelIndex(point.X, inputData.VoxelSize);
                    int yID = voxelIndex(point.Y, inputData.VoxelSize);
                    int zID = voxelIndex(point.Z, inputData.VoxelSize);

                    if (xID >= 0 && xID < inputData.ResX && yID >= 0 && yID < inputData.ResY && zID >= 0 && zID < inputData.ResZ)
                    {
                        int flatIndex = inputData.FlatIndex(xID, yID, zID);
                        if (sampleCells.Add(flatIndex))
                        {
                            samplePoints.Add(point);
                        }
                    }
                }
            }

            return samplePoints;
        }

        void refineCurveSelection(VoxelGridData inputData, VoxelSelectionBuilder selected, double minDistance, double maxDistance)
        {
            selected.Filter(flatIndex =>
            {
                Point3d center = inputData.CenterPoint(flatIndex);
                for (int c = 0; c < attractorCurves.Count; c++)
                {
                    double dist;
                    if (tryCurveDistance(attractorCurves[c], center, out dist) && VoxelAttractorBand.Contains(inputData, flatIndex, dist, p => { double d; return tryCurveDistance(attractorCurves[c], p, out d) ? d : double.NaN; }, minDistance, maxDistance))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        bool tryCurveDistance(Curve curve, Point3d point, out double distance)
        {
            distance = 0;
            if (curve == null)
            {
                return false;
            }

            double t;
            if (!curve.ClosestPoint(point, out t))
            {
                return false;
            }

            distance = curve.PointAt(t).DistanceTo(point);
            return true;
        }

        int voxelIndex(double coordinate, double size)
        {
            return System.Convert.ToInt32((coordinate - Math.Abs(coordinate % size)) / size);
        }

        //-------------------------------------------------------------------

        //inputs
        public bool min = true;
        public bool max = false;
        public bool average = false;

        Voxel[,,] inputVoxels;
        VoxelField inputVoxelField;

        List<Curve> attractorCurves;
        double minR, maxR;

        bool invert;

        //-------------------------------------------------------------------

        //outputs
        Voxel[,,] voxels;
        Grasshopper.DataTree<Point3d> voxelPositions;
        Grasshopper.DataTree<double> voxelDistances;
        Grasshopper.DataTree<int> voxelIndices;

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
                return Nuclei4.Properties.Resources.VoxelCurveAttractor;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("59741edd-30fb-42a7-93cb-26f7dee3e03b"); }
        }
    }
}
