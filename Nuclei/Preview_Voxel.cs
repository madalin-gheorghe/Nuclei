using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using GH_IO.Serialization;
using static Rhino.UI.Fonts;

namespace Nuclei3
{
    public class Preview_Voxel : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Preview class.
        /// </summary>
        public Preview_Voxel()
          : this("Preview Voxel Density", "Preview Voxel Density", "Faster Preview For Voxel Density")
        {
        }

        protected Preview_Voxel(string name, string nickname, string description)
          : base(name, nickname, description, "Nuclei3", "Preview")
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
            pManager.AddNumberParameter("Maximum Treshold", "max", "Maximum Voxel Value for Preview", GH_ParamAccess.item, 1);
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
        }

        #region menu items

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("HighResolutionDisplay", highResolutionDisplay);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            highResolutionDisplay = true;
            reader.TryGetBoolean("HighResolutionDisplay", ref highResolutionDisplay);
            return base.Read(reader);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            var highResToggle = Menu_AppendItem(menu, "High Res Display", highResolutionDisplayHandler, true, highResolutionDisplay);
            highResToggle.ToolTipText = "Display Slime Chemoattractants with a 10x interpolated GPU texture.";
        }

        void highResolutionDisplayHandler(object sender, EventArgs e)
        {
            highResolutionDisplay = !highResolutionDisplay;
            disableGpuDensityPreview();
            ExpireSolution(true);
        }

        #endregion
       
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
                clearPreviewCache();
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
            min = 0;
            max = 1;
            DA.GetData("Type", ref valueIndex);
            DA.GetData("Minimum Treshold", ref min);
            DA.GetData("Maximum Treshold", ref max);
            bool hasMinimumInput = Params.Input[2].SourceCount != 0;
            bool hasMaximumInput = Params.Input[3].SourceCount != 0;
            automaticPreviewDomain = !hasMinimumInput && !hasMaximumInput;
            if (!hasMinimumInput) min = 0;
            if (!hasMaximumInput) max = 1;
            normalizeInputDomain();

            colour = Color.Black;

            DA.GetData("Colour", ref colour);

            initializeGlobalVoxelColors();
            initializeCustomColour();

            if (tryUseGpuDensityPreview())
            {
                return;
            }

            disableGpuDensityPreview();

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

                ConcurrentBag<VoxelPreviewSample> voxelSamplesConcurrent = new ConcurrentBag<VoxelPreviewSample>();

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
                                    if (V.minDensity >= 0)
                                    {
                                        if (min <= V.minDensity && V.minDensity <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.minDensity, voxelSize / 12);
                                        }
                                    }
                                }

                                if (valueIndex == 1)
                                {
                                    if (V.maxDensity >= 0)
                                    {
                                        if (min <= V.maxDensity && V.maxDensity <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.maxDensity, voxelSize / 11);
                                        }
                                    }
                                }

                                if (valueIndex == 2)
                                {
                                    if (V.speedMultiplier >= 0)
                                    {
                                        if (min <= V.speedMultiplier && V.speedMultiplier <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.speedMultiplier, voxelSize / 10);
                                        }
                                    }
                                }

                                if (valueIndex == 3)
                                {
                                    if (V.sensorDistanceMultiplier >= 0)
                                    {
                                        if (min <= V.sensorDistanceMultiplier && V.sensorDistanceMultiplier <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.sensorDistanceMultiplier, voxelSize / 9);
                                        }
                                    }
                                }

                                if (valueIndex == 4)
                                {
                                    if (V.sensorAngleMultiplier >= 0)
                                    {
                                        if (min <= V.sensorAngleMultiplier && V.sensorAngleMultiplier <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.sensorAngleMultiplier, voxelSize / 8);
                                        }
                                    }
                                }

                                if (valueIndex == 5)
                                {
                                    if (V.rotationAngleMultiplier >= 0)
                                    {
                                        if (min <= V.rotationAngleMultiplier && V.rotationAngleMultiplier <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.rotationAngleMultiplier, voxelSize / 7);
                                        }
                                    }
                                }

                                if (valueIndex == 6)
                                {
                                    if (V.food >= 0)
                                    {
                                        if (min <= V.food && V.food <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.food, voxelSize / 2.5);
                                        }
                                    }
                                }

                                if (valueIndex == 7)
                                {
                                    if (V.density > 0.01)
                                    {
                                        if (min <= V.density && V.density <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.density, voxelSize / 4);
                                        }
                                    }
                                }

                                if (valueIndex == 8)
                                {
                                    if (V.towardsFoodPheromone > 0.01)
                                    {
                                        if (min <= V.towardsFoodPheromone && V.towardsFoodPheromone <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.towardsFoodPheromone, voxelSize / 3);
                                        }
                                    }
                                }

                                if (valueIndex == 9)
                                {
                                    if (V.towardsBasePheromone > 0.01)
                                    {
                                        if (min <= V.towardsBasePheromone && V.towardsBasePheromone <= max)
                                        {
                                            addPreviewSample(voxelSamplesConcurrent, V, V.towardsBasePheromone, voxelSize / 5);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                );

                buildCachedPointCloud(voxelSamplesConcurrent);
                updateClippingBox();
            }
            else
            {
                clearPreviewCache();
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (gpuDensityPreviewActive) return;

            if (Hidden || Locked || voxelPointCloud == null || voxelPointCloud.Count == 0) return;

            //draw background polygon
            if (!Globals.tridimensional)
            {
                args.Display.DrawPolygon(Globals.bgPolygon, Color.Black, true);
            }

            args.Display.DrawPointCloud(voxelPointCloud, 3);
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                if (gpuDensityPreviewActive && gpuDensityClippingBox.IsValid)
                {
                    return gpuDensityClippingBox;
                }

                return clippingBox;
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            NucleiGpuDisplayManager.DisableVoxelDensityPreview(InstanceGuid);
            base.RemovedFromDocument(document);
        }

        internal bool WantsGpuDensityPreview
        {
            get { return WantsGpuDynamicDensityPreview; }
        }

        internal bool WantsGpuDynamicDensityPreview
        {
            get { return Rhino.RhinoApp.ExeVersion >= 9 && !Hidden && !Locked && VoxelPreviewField.IsDynamicDensity(CurrentValueIndex()); }
        }

        internal int GpuDensityPreviewScale
        {
            get { return highResolutionDisplay ? 10 : 1; }
        }

        internal bool WantsGpuVoxelPreview
        {
            get { return Rhino.RhinoApp.ExeVersion >= 9 && !Hidden && !Locked && VoxelPreviewField.IsGpuSupported(CurrentValueIndex()); }
        }

        internal bool WantsSolverVoxelOutput
        {
            get { return !Hidden && !Locked && !WantsGpuVoxelPreview; }
        }

        internal GpuDensityFieldPreviewFrame GetGpuDensityFieldPreviewFrame()
        {
            if (!gpuDensityPreviewActive || !WantsGpuVoxelPreview) return null;

            SolverGPU solver = gpuDensitySolver;
            if (solver == null && !NucleiGpuDisplayManager.TryGetSolverForVoxels(voxel, out solver))
            {
                return null;
            }

            int currentValueIndex = CurrentValueIndex();
            GpuDensityFieldPreviewFrame frame = solver.GetDensityFieldPreviewFrame(currentValueIndex, ValidFloat(min, 0), ValidFloat(max, float.MaxValue), GpuDensityPreviewScale);
            if (frame == null || !frame.IsValid)
            {
                return null;
            }

            applyGpuPreviewStyle(frame, currentValueIndex);
            gpuDensitySolver = solver;
            gpuDensityClippingBox = frame.ClippingBox;
            return frame;
        }

        internal void RecordGpuDensityFieldPreviewDrawTiming(long drawTicks)
        {
            if (gpuDensitySolver != null)
            {
                gpuDensitySolver.RecordDensityFieldPreviewDrawTiming(drawTicks);
            }
        }

        bool tryUseGpuDensityPreview()
        {
            if (voxel == null || !WantsGpuVoxelPreview) return false;

            SolverGPU solver;
            if (!NucleiGpuDisplayManager.TryGetSolverForVoxels(voxel, out solver))
            {
                return false;
            }

            int currentValueIndex = CurrentValueIndex();
            GpuDensityFieldPreviewFrame frame = solver.GetDensityFieldPreviewFrame(currentValueIndex, ValidFloat(min, 0), ValidFloat(max, float.MaxValue), GpuDensityPreviewScale);
            if (frame == null || !frame.IsValid)
            {
                return false;
            }

            applyGpuPreviewStyle(frame, currentValueIndex);
            clearPointCloudPreview();
            gpuDensitySolver = solver;
            gpuDensityPreviewActive = true;
            gpuDensityClippingBox = frame.ClippingBox;
            NucleiGpuDisplayManager.SetVoxelDensityPreview(this);
            return true;
        }

        void applyGpuPreviewStyle(GpuDensityFieldPreviewFrame frame, int currentValueIndex)
        {
            if (frame == null) return;

            frame.ValueIndex = currentValueIndex;
            frame.MinimumThreshold = ValidFloat(min, 0);
            frame.MaximumThreshold = ValidFloat(max, float.MaxValue);
            if (frame.MaximumThreshold < frame.MinimumThreshold)
            {
                float temp = frame.MinimumThreshold;
                frame.MinimumThreshold = frame.MaximumThreshold;
                frame.MaximumThreshold = temp;
            }

            bool useCustomColor = colour.R != 0 || colour.G != 0 || colour.B != 0;
            frame.UseCustomColor = useCustomColor;
            frame.ColorR = colour.R / 255.0f;
            frame.ColorG = colour.G / 255.0f;
            frame.ColorB = colour.B / 255.0f;
            frame.ColorA = colour.A > 0 ? colour.A / 255.0f : 1.0f;
            frame.VolumeOpacity = PreviewVolumeOpacity;
            frame.VolumeContrast = PreviewVolumeContrast;
            frame.VolumeSampleCount = PreviewVolumeSampleCount;
            frame.VolumeRenderMode = PreviewVolumeRenderMode;
            frame.PreviewScale = frame.VolumeContrast;
        }

        protected virtual float PreviewVolumeOpacity
        {
            get { return 1.5f; }
        }

        protected virtual float PreviewVolumeContrast
        {
            get { return 1.5f; }
        }

        protected virtual int PreviewVolumeSampleCount
        {
            get { return 0; }
        }

        protected virtual int PreviewVolumeRenderMode
        {
            get { return 0; }
        }

        static float ValidFloat(double value, float fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            if (value > float.MaxValue) return float.MaxValue;
            if (value < -float.MaxValue) return -float.MaxValue;
            return (float)value;
        }

        void disableGpuDensityPreview()
        {
            if (gpuDensityPreviewActive)
            {
                NucleiGpuDisplayManager.DisableVoxelDensityPreview(InstanceGuid);
            }

            gpuDensityPreviewActive = false;
            gpuDensitySolver = null;
            gpuDensityClippingBox = BoundingBox.Empty;
        }

        int CurrentValueIndex()
        {
            try
            {
                if (Params != null && Params.Input != null && Params.Input.Count > 1 && Params.Input[1].VolatileData != null)
                {
                    foreach (object item in Params.Input[1].VolatileData.AllData(true))
                    {
                        GH_Integer integer = item as GH_Integer;
                        if (integer != null)
                        {
                            return integer.Value;
                        }

                        int parsed;
                        if (item != null && int.TryParse(item.ToString(), out parsed))
                        {
                            return parsed;
                        }

                        break;
                    }
                }
            }
            catch
            {
            }

            return valueIndex;
        }

        void updateClippingBox()
        {
            clippingBox = BoundingBox.Empty;
            if (voxelPoints == null || voxelPoints.Count == 0) return;

            clippingBox = new BoundingBox(voxelPoints);
            clippingBox.Inflate(Math.Max(Globals.voxelSize, 1.0));
        }

        void clearPreviewCache()
        {
            voxel = null;
            clearPointCloudPreview();
            disableGpuDensityPreview();
        }

        void clearPointCloudPreview()
        {
            voxelPoints = null;
            voxelValues = null;
            voxelPointCloud = null;
            clippingBox = BoundingBox.Empty;
        }

        void addPreviewSample(ConcurrentBag<VoxelPreviewSample> samples, Voxel V, double value, double planarOffset)
        {
            Point3d loc = V.loc;
            if (planarXY) loc.Z = planarOffset;
            if (planarXZ) loc.Y = planarOffset;
            if (planarYZ) loc.X = planarOffset;
            samples.Add(new VoxelPreviewSample(loc, value));
        }

        void buildCachedPointCloud(ConcurrentBag<VoxelPreviewSample> samples)
        {
            voxelPoints = new List<Point3d>(samples.Count);
            voxelValues = new List<double>(samples.Count);
            voxelPointCloud = new PointCloud();
            minExistingVoxelValue = double.PositiveInfinity;
            maxExistingVoxelValue = double.NegativeInfinity;

            foreach (VoxelPreviewSample sample in samples)
            {
                if (double.IsNaN(sample.Value) || double.IsInfinity(sample.Value)) continue;
                if (sample.Value < minExistingVoxelValue) minExistingVoxelValue = sample.Value;
                if (sample.Value > maxExistingVoxelValue) maxExistingVoxelValue = sample.Value;
            }

            if (double.IsPositiveInfinity(minExistingVoxelValue) || double.IsNegativeInfinity(maxExistingVoxelValue))
            {
                if (automaticPreviewDomain)
                {
                    minExistingVoxelValue = 0;
                    maxExistingVoxelValue = 1;
                }
                else
                {
                    minExistingVoxelValue = min;
                    maxExistingVoxelValue = max;
                }
            }

            if (automaticPreviewDomain)
            {
                currentPreviewDomain = new Interval(minExistingVoxelValue, maxExistingVoxelValue);
            }

            updatePreviewDomainMessage();

            foreach (VoxelPreviewSample sample in samples)
            {
                voxelPoints.Add(sample.Point);
                voxelValues.Add(sample.Value);
                voxelPointCloud.Add(sample.Point, retrieveVoxelColor(sample.Value));
            }
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

        bool automaticPreviewDomain = true;
        Interval currentPreviewDomain = new Interval(0, 1);
        double minExistingVoxelValue = 0;
        double maxExistingVoxelValue = 1;

        List<Color> voxelColorList;

        List<Point3d> voxelPoints;
        List<double> voxelValues;

        PointCloud voxelPointCloud;
        BoundingBox clippingBox = BoundingBox.Empty;
        bool gpuDensityPreviewActive = false;
        SolverGPU gpuDensitySolver;
        BoundingBox gpuDensityClippingBox = BoundingBox.Empty;
        bool highResolutionDisplay = true;

        struct VoxelPreviewSample
        {
            public VoxelPreviewSample(Point3d point, double value)
            {
                Point = point;
                Value = value;
            }

            public Point3d Point;
            public double Value;
        }

        //-------------------------------------------------------------------

        void initializeCustomColour()
        {
            if (colour.R != 0 || colour.G != 0 || colour.B != 0)
            {
                voxelColorList = VoxelPreviewPalette.CreateValuePalette(colour);
            }
        }

        //-------------------------------------------------------------------

        Color retrieveVoxelColor(double d)
        {
            Color voxelColor = Color.Black;

            int index = previewValueToColorIndex(d);

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
            VoxelPreviewPalette.EnsureInitialized();
        }

        void normalizeInputDomain()
        {
            if (automaticPreviewDomain)
            {
                min = double.NegativeInfinity;
                max = double.PositiveInfinity;
                currentPreviewDomain = new Interval(0, 1);
                updatePreviewDomainMessage();
                return;
            }

            if (double.IsNaN(min) || double.IsInfinity(min)) min = 0;
            if (double.IsNaN(max) || double.IsInfinity(max)) max = 1;

            if (min > max)
            {
                double temp = min;
                min = max;
                max = temp;
            }

            currentPreviewDomain = new Interval(min, max);
            updatePreviewDomainMessage();
        }

        void updatePreviewDomainMessage()
        {
            Message = formatDomainValue(currentPreviewDomain.T0) + " to " + formatDomainValue(currentPreviewDomain.T1);
        }

        static string formatDomainValue(double value)
        {
            if (Math.Abs(value) < 1e-12) value = 0;
            return value.ToString("0.###");
        }

        int previewValueToColorIndex(double value)
        {
            double range = currentPreviewDomain.T1 - currentPreviewDomain.T0;
            double normalized;

            if (range > 1e-12)
            {
                normalized = (value - currentPreviewDomain.T0) / range;
            }
            else
            {
                normalized = value > 0 ? 1.0 : 0.0;
            }

            int index = (int)Math.Round(normalized * 255);
            if (index < 0) return 0;
            if (index > 255) return 255;
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

    public class Preview_Voxel_Fancy : Preview_Voxel
    {
        public Preview_Voxel_Fancy()
          : base("Fancy Preview", "Fancy Preview", "Experimental GPU voxel preview using maximum intensity projection and interpolated sampling")
        {
        }

        protected override int PreviewVolumeRenderMode
        {
            get { return 1; }
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.secondary; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("9a7dfb59-5092-4196-93be-a3d8fa1054ef"); }
        }
    }
}
