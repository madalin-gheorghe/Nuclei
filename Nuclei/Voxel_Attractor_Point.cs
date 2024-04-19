using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Runtime.Remoting.Messaging;
using GH_IO.Serialization;
using System.Windows.Forms;

namespace Nuclei3
{
    public class Voxel_Attractor_Point : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_PointAttractor class.
        /// </summary>
        public Voxel_Attractor_Point()
          : base("Point Attractor for Voxels", "Point Attractor",
              "Use Points as Attractors for Voxel Centers",
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
            pManager.AddPointParameter("Attractor Points", "attractorPoints", "Attractor Points", GH_ParamAccess.list);
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

            //initialize input lists
            attractorPoints = new List<Point3d>();

            //initialize output lists 
            voxelPositions = new Grasshopper.DataTree<Point3d>();
            voxelDistances = new Grasshopper.DataTree<double>();
            voxelIndices = new Grasshopper.DataTree<int>();

            DA.GetData("Voxels", ref inputVoxels);
            DA.GetDataList("Attractor Points", attractorPoints);
            DA.GetData("Minimum Range", ref minR);
            DA.GetData("Maximum Range", ref maxR);
            DA.GetData("Invert Voxel Selection", ref invert);

            //determine voxel settings
            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

            double voxelSize = Globals.voxelSize;

            //create list of empty voxels
            Voxel[,,] dummyVoxel = new Voxel[resX, resY, resZ];
            List<Voxel> dummyVoxels = new List<Voxel>();

            for (int i = 0; i < attractorPoints.Count; i++)
            {
                Point3d p = attractorPoints[i];

                //convert point coordinates to voxel center coordinates
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

            if (invert)
            {
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
            }

            int indexCounter = 0;

            //output voxel positions based on each attractor point
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

                            for (int p = 0; p < attractorPoints.Count; p++)
                            {
                                Point3d attractorP = attractorPoints[p];

                                double dist = attractorP.DistanceTo(V.loc);

                                if (!invert)
                                {
                                    if (theRealMin <= dist && dist <= theRealMax)
                                    {
                                        if (dist < minDist)
                                        {
                                            minDist = dist;
                                            index = p;
                                        }

                                        if (dist > maxDist)
                                        {
                                            maxDist = dist;
                                        }

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

                            if (index != -1)
                            {
                                voxelPositions.Add(V.loc, new Grasshopper.Kernel.Data.GH_Path(index));
                                voxelIndices.Add(indexCounter, new Grasshopper.Kernel.Data.GH_Path(index));

                                if (min) voxelDistances.Add(minDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                if(max) voxelDistances.Add(maxDist, new Grasshopper.Kernel.Data.GH_Path(index));
                                if (average) voxelDistances.Add(outputDist / outputDistCounter * 1f, new Grasshopper.Kernel.Data.GH_Path(index));

                                indexCounter++;
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

        List<Point3d> attractorPoints;
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
                return Nuclei3.Properties.Resources.VoxelPointAttractor;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("848aeb01-90d9-453f-9165-4c7968ecd962"); }
        }
    }
}