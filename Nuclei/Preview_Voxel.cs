using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Linq;
using System.Drawing;
using static Rhino.UI.Fonts;

namespace Nuclei3
{
    public class Preview_Voxel : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Preview class.
        /// </summary>
        public Preview_Voxel()
          : base("Preview Voxel Density", "Preview Voxel Density",
              "Faster Preview For Voxel Density",
              "Nuclei3", "Preview")
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
            pManager.AddIntegerParameter("Type", "type", "Type of Voxel Value", GH_ParamAccess.item, 0);
            //2
            pManager.AddNumberParameter("Minimum Treshold", "min", "Minimum Voxel Value for Preview", GH_ParamAccess.item, 0);
            pManager[2].Optional = true;
            //3
            pManager.AddNumberParameter("Maximum Treshold", "max", "Maximum Voxel Value for Preview", GH_ParamAccess.item, 100);
            pManager[3].Optional = true;
            //4
            pManager.AddColourParameter("Colour", "colour", "The Display Colour of Voxel Values", GH_ParamAccess.item, Color.FromArgb(0,0,0,0));
            pManager[4].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        /// 
        
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.AddTextParameter("Voxel Preview", "voxelPreview", "Settings Controlling Voxel Preview. Connects to Solver's Display Input", GH_ParamAccess.list);
        }
       
        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (Params.Input[0].SourceCount != 0)
            {
                DA.GetData("Voxels", ref voxel);
            } else
            {
                voxel = null;
                voxelPoints = null;
                voxelValues = null;
                clippingBox = BoundingBox.Empty;
            }

            //add value list
            if (Params.Input[1].SourceCount == 0)
            {
                //instantiate new value list
                var vallist = new Grasshopper.Kernel.Special.GH_ValueList();
                vallist.ListMode = Grasshopper.Kernel.Special.GH_ValueListMode.DropDown;
                vallist.CreateAttributes();

                //customise value list position
                GH_Component Component = this;
                GH_Document GrasshopperDocument = this.OnPingDocument();
                float xCoord = (float)Component.Attributes.Pivot.X - 250;
                float yCoord = (float)Component.Attributes.Pivot.Y - 31;
                PointF cornerPt = new PointF(xCoord, yCoord);
                vallist.Attributes.Pivot = cornerPt;

                //populate value list with our own data
                vallist.ListItems.Clear();
                var items = new List<Grasshopper.Kernel.Special.GH_ValueListItem>();
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Minimum Density", "0"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Maximum Density", "1"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Speed", "2"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Sensor Distance", "3"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Sensor Angle", "4"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Rotation Angle", "5"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Food", "6"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Slime Chemoattractants", "7"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ant Food Pheromones", "8"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ant Base Pheromones", "9"));

                vallist.ListItems.AddRange(items);
                // Until now, the slider is a hypothetical object.
                // This command makes it 'real' and adds it to the canvas.
                GrasshopperDocument.AddObject(vallist, false);
                //Connect the new slider to this component
                Component.Params.Input[1].AddSource(vallist);
                Component.Params.Input[1].CollectData();
            }

            //DA.GetData("Display", ref display);
            DA.GetData("Type", ref valueIndex);
            DA.GetData("Minimum Treshold", ref min);
            DA.GetData("Maximum Treshold", ref max);

            colour = Color.Black;

            DA.GetData("Colour", ref colour);

            initializeGlobalVoxelColors();
            initializeCustomColour();

            if (voxel != null && !this.Hidden)
            {
                //determine voxel settings
                int resX = voxel.GetLength(0);
                int resY = voxel.GetLength(1);
                int resZ = voxel.GetLength(2);

                //determine voxelSize
                voxelSize = Globals.voxelSize;

                //determine whether 3D or 2D 
                planarXY = false;
                planarXZ = false;
                planarYZ = false;
                tridimensional = true;

                if (resX == 1)
                {
                    planarXY = false;
                    planarXZ = false;
                    planarYZ = true;
                    tridimensional = false;
                }
                if (resY == 1)
                {
                    planarXY = false;
                    planarXZ = true;
                    planarYZ = false;
                    tridimensional = false;
                }
                if (resZ == 1)
                {
                    planarXY = true;
                    planarXZ = false;
                    planarYZ = false;
                    tridimensional = false;
                }

                if (resX > 1 && resY > 1 && resZ > 1)
                {
                    tridimensional = true;
                    planarXY = false;
                    planarXZ = false;
                    planarYZ = false;
                }
                else
                {
                    tridimensional = false;
                }

                ConcurrentBag<Point3d> voxePointConcurrent = new ConcurrentBag<Point3d>();
                ConcurrentBag<double> voxelValuesConcurrent = new ConcurrentBag<double>();

                maxExistingVoxelValue = 1;

                //get positions and values
                Parallel.For(0, resZ, k =>
                {
                    for (int i = 0; i < resX; i++)
                    {
                        for (int j = 0; j < resY; j++)
                        {
                            if (voxel[i, j, k] != null)
                            {
                                Voxel V = voxel[i, j, k];

                                if (valueIndex == 0)
                                {
                                    if (V.minDensity >= 0.01)
                                    {
                                        if (min <= V.minDensity && V.minDensity <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.minDensity);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 12;
                                            if (planarXZ) loc.Y = voxelSize / 12;
                                            if (planarYZ) loc.X = voxelSize / 12;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 1)
                                {
                                    if (V.maxDensity ==0 || V.maxDensity>=0.01)
                                    {
                                        if (min <= V.maxDensity && V.maxDensity <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.maxDensity);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 11;
                                            if (planarXZ) loc.Y = voxelSize / 11;
                                            if (planarYZ) loc.X = voxelSize / 11;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 2)
                                {
                                    if (V.speedMultiplier > 0.01)
                                    {
                                        if (maxExistingVoxelValue < V.speedMultiplier) maxExistingVoxelValue = V.speedMultiplier;

                                        if (min <= V.speedMultiplier && V.speedMultiplier <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.speedMultiplier);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 10;
                                            if (planarXZ) loc.Y = voxelSize / 10;
                                            if (planarYZ) loc.X = voxelSize / 10;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 3)
                                {
                                    if (V.sensorDistanceMultiplier > 0.01)
                                    {
                                        if (maxExistingVoxelValue < V.sensorDistanceMultiplier) maxExistingVoxelValue = V.sensorDistanceMultiplier;

                                        if (min <= V.sensorDistanceMultiplier && V.sensorDistanceMultiplier <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.sensorDistanceMultiplier);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 9;
                                            if (planarXZ) loc.Y = voxelSize / 9;
                                            if (planarYZ) loc.X = voxelSize / 9;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 4)
                                {
                                    if (V.sensorAngleMultiplier > 0.01)
                                    {
                                        if (maxExistingVoxelValue < V.sensorAngleMultiplier) maxExistingVoxelValue = V.sensorAngleMultiplier;

                                        if (min <= V.sensorAngleMultiplier && V.sensorAngleMultiplier <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.sensorAngleMultiplier);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 8;
                                            if (planarXZ) loc.Y = voxelSize / 8;
                                            if (planarYZ) loc.X = voxelSize / 8;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 5)
                                {
                                    if (V.rotationAngleMultiplier > 0.01)
                                    {
                                        if (maxExistingVoxelValue < V.rotationAngleMultiplier) maxExistingVoxelValue = V.rotationAngleMultiplier;

                                        if (min <= V.rotationAngleMultiplier && V.rotationAngleMultiplier <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.rotationAngleMultiplier);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 7;
                                            if (planarXZ) loc.Y = voxelSize / 7;
                                            if (planarYZ) loc.X = voxelSize / 7;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 6)
                                {
                                    if (V.food > 0.01)
                                    {
                                        if(maxExistingVoxelValue < V.food) maxExistingVoxelValue = V.food;

                                        if (min <= V.food && V.food <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.food);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 2.5;
                                            if (planarXZ) loc.Y = voxelSize / 2.5;
                                            if (planarYZ) loc.X = voxelSize / 2.5;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 7)
                                {
                                    if (V.density > 0.01)
                                    {
                                        if (min <= V.density && V.density <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.density);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 4;
                                            if (planarXZ) loc.Y = voxelSize / 4;
                                            if (planarYZ) loc.X = voxelSize / 4;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 8)
                                {
                                    if (V.towardsFoodPheromone > 0.01)
                                    {
                                        if (min <= V.towardsFoodPheromone && V.towardsFoodPheromone <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.towardsFoodPheromone);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 3;
                                            if (planarXZ) loc.Y = voxelSize / 3;
                                            if (planarYZ) loc.X = voxelSize / 3;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }

                                if (valueIndex == 9)
                                {
                                    if (V.towardsBasePheromone > 0.01)
                                    {
                                        if (min <= V.towardsBasePheromone && V.towardsBasePheromone <= max)
                                        {
                                            voxelValuesConcurrent.Add(V.towardsBasePheromone);

                                            Point3d loc = V.loc;
                                            if (planarXY) loc.Z = voxelSize / 5;
                                            if (planarXZ) loc.Y = voxelSize / 5;
                                            if (planarYZ) loc.X = voxelSize / 5;
                                            voxePointConcurrent.Add(loc);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                );

                voxelPoints = voxePointConcurrent.ToList();
                voxelValues = voxelValuesConcurrent.ToList();
                updateClippingBox();
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            //draw background polygon
            if (!Globals.tridimensional)
            {
                args.Display.DrawPolygon(Globals.bgPolygon, Color.Black, true);
            }

            //voxels
            voxelPointCloud = new PointCloud();

            if (voxelPoints != null)
            {

                for (int i = 0; i < voxelPoints.Count; i++)
                {
                    Point3d p = voxelPoints[i];
                    double value = voxelValues[i];

                    voxelPointCloud.Add(p, retrieveVoxelColor(value));
                }

                if (voxelPointCloud.Count > 0) args.Display.DrawPointCloud(voxelPointCloud, (int) 3);
            }
        }

        public override BoundingBox ClippingBox
        {
            get { return clippingBox; }
        }

        void updateClippingBox()
        {
            clippingBox = BoundingBox.Empty;
            if (voxelPoints == null || voxelPoints.Count == 0) return;

            clippingBox = new BoundingBox(voxelPoints);
            clippingBox.Inflate(Math.Max(Globals.voxelSize, 1.0));
        }


        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] voxel;

        double voxelSize = 1;

        bool planarXY = false;
        bool planarXZ = false;
        bool planarYZ = false;
        bool tridimensional = true;

        int valueIndex;
        //bool display = false;
        double min, max;

        Color colour;

        double maxExistingVoxelValue = 1;

        List<Color> voxelColorList;

        List<Point3d> voxelPoints;
        List<double> voxelValues;

        PointCloud voxelPointCloud;
        BoundingBox clippingBox = BoundingBox.Empty;

        //-------------------------------------------------------------------

        void initializeCustomColour()
        {
            if (colour.R != 0 || colour.G != 0 || colour.B != 0)
            {
                voxelColorList = new List<Color>();

                for (int i = 0; i <= 255; i++)
                {
                    Color customColour = Color.FromArgb((int)Math.Floor(i * 0.5), colour.R, colour.G, colour.B);
                    voxelColorList.Add(customColour);
                }
            }
        }

        //-------------------------------------------------------------------

        Color retrieveVoxelColor(double d)
        {
            Color voxelColor = Color.Black;

            int index = (int)Math.Floor(d / maxExistingVoxelValue * 255);

            if (colour.R == 0 && colour.G == 0 && colour.B == 0)
            {
                if (valueIndex < 7)
                {
                    voxelColor = Globals.voxelColorList_White[index];
                }

                if (valueIndex == 7)
                {
                    voxelColor = Globals.voxelColorList_chemoAttractants[index];
                }

                if (valueIndex == 8)
                {
                    voxelColor = Globals.voxelColorList_antFoodPheromones[index];
                }

                if (valueIndex == 9)
                {
                    voxelColor = Globals.voxelColorList_antBasePheromones[index];
                }
            } else
            {
                voxelColor = voxelColorList[index];
            }

            //in case max density == 0 
            if (valueIndex == 1)
            {
                if (d < 0.01)
                {
                    voxelColor = System.Drawing.Color.FromArgb(255, 18, 12, 33);
                }
            }

            return voxelColor;
        }

        //-------------------------------------------------------------------

        void initializeGlobalVoxelColors()
        {
            if (Globals.voxelColorList_White == null)
            {
                Color valuesWhiteColor = System.Drawing.Color.FromArgb(255, 255, 255, 255);
                Color chemoAttractantsColor = System.Drawing.Color.FromArgb(255, 223, 255, 123);
                Color antFoodPheromonesColor = System.Drawing.Color.FromArgb(255, 57, 255, 170);
                Color antBasePheromonesColor = System.Drawing.Color.FromArgb(255, 255, 0, 100);

                Globals.voxelColorList_White = new List<Color>();
                Globals.voxelColorList_chemoAttractants = new List<Color>();
                Globals.voxelColorList_antFoodPheromones = new List<Color>();
                Globals.voxelColorList_antBasePheromones = new List<Color>();

                for (int i = 0; i <= 255; i++)
                {
                    Color color_white = System.Drawing.Color.FromArgb((int)Math.Floor(i * 0.5), valuesWhiteColor.R, valuesWhiteColor.G, valuesWhiteColor.B);
                    Globals.voxelColorList_White.Add(color_white);

                    Color color_chemoAttractants = System.Drawing.Color.FromArgb((int)Math.Floor(i * 0.5), chemoAttractantsColor.R, chemoAttractantsColor.G, chemoAttractantsColor.B);
                    Globals.voxelColorList_chemoAttractants.Add(color_chemoAttractants);

                    Color color_antFoodPheromones = System.Drawing.Color.FromArgb((int)Math.Floor(i * 0.5), antFoodPheromonesColor.R, antFoodPheromonesColor.G, antFoodPheromonesColor.B);
                    Globals.voxelColorList_antFoodPheromones.Add(color_antFoodPheromones);

                    Color color_antBasePheromones = System.Drawing.Color.FromArgb((int)Math.Floor(i * 0.5), antBasePheromonesColor.R, antBasePheromonesColor.G, antBasePheromonesColor.B);
                    Globals.voxelColorList_antBasePheromones.Add(color_antBasePheromones);
                }
            }
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
                return Nuclei3.Properties.Resources.PreviewVoxelDensities;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("a95d5adc-0c7c-40c2-893f-6a9732ffdfa5"); }
        }
    }
}
