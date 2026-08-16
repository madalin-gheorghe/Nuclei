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
          : this("Voxel Preview", "Voxel Preview", "Displays voxel values in the Rhino viewport")
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
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ant Pheromones", "10"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ants and Slime", "11"));

                vallist.ListItems.AddRange(items);
                // Until now, the slider is a hypothetical object.
                // This command makes it 'real' and adds it to the canvas.
                GrasshopperDocument.AddObject(vallist, false);
                //Connect the new slider to this component
                Component.Params.Input[1].AddSource(vallist);
                Component.Params.Input[1].CollectData();
            }

            ensureCombinedPreviewChoices();

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

                if (tryReuseStaticPreviewCache(resX, resY, resZ))
                {
                    return;
                }

                if (!VoxelPreviewField.IsStatic(valueIndex))
                {
                    invalidateStaticPreviewCache();
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

                                if (valueIndex >= VoxelPreviewField.SlimeChemoattractants
                                    && valueIndex <= VoxelPreviewField.AntsAndSlime)
                                {
                                    double dynamicValue;
                                    if (tryGetDynamicPreviewValue(V, out dynamicValue))
                                    {
                                        addPreviewSample(voxelSamplesConcurrent, V, dynamicValue, previewPlanarOffset());
                                    }
                                }
                            }
                        }
                    }
                }
                );

                buildCachedPointCloud(voxelSamplesConcurrent);
                buildPlanarPreviewMesh();
                updateClippingBox();
                rememberStaticPreviewCache(resX, resY, resZ);
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

            if (!Globals.tridimensional && planarPreviewMesh != null && planarPreviewMesh.Faces.Count > 0)
            {
                args.Display.DrawMeshFalseColors(planarPreviewMesh);
                return;
            }

            if (!Globals.tridimensional && stablePointBuckets != null && stablePointBuckets.Count > 0)
            {
                drawStablePlanarPreview(args);
                return;
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
            ObjectChanged -= previewObjectChanged;
            NucleiGpuDisplayManager.DisableVoxelDensityPreview(InstanceGuid);
            base.RemovedFromDocument(document);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            ObjectChanged -= previewObjectChanged;
            ObjectChanged += previewObjectChanged;
        }

        void previewObjectChanged(IGH_DocumentObject sender, GH_ObjectChangedEventArgs e)
        {
            if (e == null || e.Type != GH_ObjectEventType.Preview) return;

            GH_Document document = OnPingDocument();
            if (document != null && !Hidden && !Locked)
            {
                document.ScheduleSolution(1, _ => ExpireSolution(false));
            }
        }

        internal bool WantsGpuDensityPreview
        {
            get { return false; }
        }

        internal bool WantsGpuDynamicDensityPreview
        {
            get { return false; }
        }

        internal int GpuDensityPreviewScale
        {
            get { return 1; }
        }

        internal bool WantsGpuVoxelPreview
        {
            get { return false; }
        }

        internal bool WantsSolverVoxelOutput
        {
            get { return !Hidden && !Locked; }
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
            get { return 0.8f; }
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

        void ensureCombinedPreviewChoices()
        {
            if (Params == null || Params.Input == null || Params.Input.Count < 2 || Params.Input[1].SourceCount != 1)
            {
                return;
            }

            Grasshopper.Kernel.Special.GH_ValueList valueList = Params.Input[1].Sources[0] as Grasshopper.Kernel.Special.GH_ValueList;
            if (valueList == null)
            {
                return;
            }

            bool nucleiDynamicFields = valueList.ListItems.Any(item => item.Expression == "7" && item.Name == "Slime Chemoattractants")
                && valueList.ListItems.Any(item => item.Expression == "8" && item.Name == "Ant Food Pheromones")
                && valueList.ListItems.Any(item => item.Expression == "9" && item.Name == "Ant Base Pheromones");
            if (!nucleiDynamicFields)
            {
                return;
            }

            if (!valueList.ListItems.Any(item => item.Expression == "10"))
            {
                valueList.ListItems.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ant Pheromones", "10"));
            }
            if (!valueList.ListItems.Any(item => item.Expression == "11"))
            {
                valueList.ListItems.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ants and Slime", "11"));
            }
        }

        void updateClippingBox()
        {
            clippingBox = BoundingBox.Empty;
            if (voxel != null)
            {
                double size = Globals.voxelSize > 0 ? Globals.voxelSize : voxelSize;
                clippingBox = new BoundingBox(
                    new Point3d(0, 0, 0),
                    new Point3d(voxel.GetLength(0) * size, voxel.GetLength(1) * size, voxel.GetLength(2) * size));
                clippingBox.Inflate(Math.Max(size * 2, 1.0));
                return;
            }

            if (voxelPoints == null || voxelPoints.Count == 0) return;

            clippingBox = new BoundingBox(voxelPoints);
            clippingBox.Inflate(Math.Max(Globals.voxelSize, 1.0));
        }

        void clearPreviewCache()
        {
            voxel = null;
            clearPointCloudPreview();
            disableGpuDensityPreview();
            invalidateStaticPreviewCache();
        }

        void clearPointCloudPreview()
        {
            voxelPoints = null;
            voxelValues = null;
            voxelPointCloud = null;
            stablePointBuckets = null;
            planarPreviewMesh = null;
            clippingBox = BoundingBox.Empty;
            invalidateStaticPreviewCache();
        }

        bool tryReuseStaticPreviewCache(int resX, int resY, int resZ)
        {
            if (!staticPreviewCacheValid || !VoxelPreviewField.IsStatic(valueIndex))
            {
                return false;
            }

            if (!ReferenceEquals(staticPreviewCacheVoxels, voxel))
            {
                return false;
            }

            VoxelGridData currentData;
            VoxelGridRegistry.TryGet(voxel, out currentData);
            if (!ReferenceEquals(staticPreviewCacheData, currentData))
            {
                return false;
            }

            if (staticPreviewCacheResX != resX
                || staticPreviewCacheResY != resY
                || staticPreviewCacheResZ != resZ
                || staticPreviewCacheValueIndex != valueIndex
                || staticPreviewCacheAutomaticDomain != automaticPreviewDomain
                || Math.Abs(staticPreviewCacheMin - min) > 1e-12
                || Math.Abs(staticPreviewCacheMax - max) > 1e-12
                || Math.Abs(staticPreviewCacheVoxelSize - voxelSize) > 1e-12
                || staticPreviewCacheColourArgb != colour.ToArgb())
            {
                return false;
            }

            if (staticPreviewCachePointCount > 0 && (voxelPointCloud == null || voxelPointCloud.Count != staticPreviewCachePointCount))
            {
                return false;
            }

            currentPreviewDomain = staticPreviewCachePreviewDomain;
            updatePreviewDomainMessage();
            return true;
        }

        void rememberStaticPreviewCache(int resX, int resY, int resZ)
        {
            if (!VoxelPreviewField.IsStatic(valueIndex))
            {
                invalidateStaticPreviewCache();
                return;
            }

            VoxelGridData currentData;
            VoxelGridRegistry.TryGet(voxel, out currentData);

            staticPreviewCacheValid = true;
            staticPreviewCacheVoxels = voxel;
            staticPreviewCacheData = currentData;
            staticPreviewCacheResX = resX;
            staticPreviewCacheResY = resY;
            staticPreviewCacheResZ = resZ;
            staticPreviewCacheValueIndex = valueIndex;
            staticPreviewCacheAutomaticDomain = automaticPreviewDomain;
            staticPreviewCacheMin = min;
            staticPreviewCacheMax = max;
            staticPreviewCacheVoxelSize = voxelSize;
            staticPreviewCacheColourArgb = colour.ToArgb();
            staticPreviewCachePreviewDomain = currentPreviewDomain;
            staticPreviewCachePointCount = voxelPointCloud != null ? voxelPointCloud.Count : 0;
        }

        void invalidateStaticPreviewCache()
        {
            staticPreviewCacheValid = false;
            staticPreviewCacheVoxels = null;
            staticPreviewCacheData = null;
            staticPreviewCachePointCount = 0;
        }

        void addPreviewSample(ConcurrentBag<VoxelPreviewSample> samples, Voxel V, double value, double planarOffset)
        {
            Point3d loc = V.loc;
            if (planarXY) loc.Z = planarOffset;
            if (planarXZ) loc.Y = planarOffset;
            if (planarYZ) loc.X = planarOffset;
            samples.Add(new VoxelPreviewSample(loc, value, V));
        }

        void buildCachedPointCloud(ConcurrentBag<VoxelPreviewSample> samples)
        {
            voxelPoints = new List<Point3d>(samples.Count);
            voxelValues = new List<double>(samples.Count);
            voxelPointCloud = new PointCloud();
            stablePointBuckets = !tridimensional ? new List<StablePointBucket>() : null;
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
                Color sampleColor = retrieveVoxelColor(sample);
                voxelPoints.Add(sample.Point);
                voxelValues.Add(sample.Value);
                voxelPointCloud.Add(sample.Point, sampleColor);

                if (stablePointBuckets != null)
                {
                    addStablePoint(sample.Point, sampleColor);
                }
            }
        }

        const int MaxPlanarPreviewMeshVertices = 1500000;

        void buildPlanarPreviewMesh()
        {
            planarPreviewMesh = null;
            if (voxel == null || tridimensional) return;

            int resX = voxel.GetLength(0);
            int resY = voxel.GetLength(1);
            int resZ = voxel.GetLength(2);

            int uCount;
            int vCount;
            if (planarXY)
            {
                uCount = resX;
                vCount = resY;
            }
            else if (planarXZ)
            {
                uCount = resX;
                vCount = resZ;
            }
            else if (planarYZ)
            {
                uCount = resY;
                vCount = resZ;
            }
            else
            {
                return;
            }

            if (uCount < 2 || vCount < 2) return;
            if ((long)uCount * vCount > MaxPlanarPreviewMeshVertices) return;

            Mesh mesh = new Mesh();
            mesh.Vertices.Capacity = uCount * vCount;
            mesh.Faces.Capacity = (uCount - 1) * (vCount - 1);

            for (int u = 0; u < uCount; u++)
            {
                for (int v = 0; v < vCount; v++)
                {
                    Voxel V = planarVoxelAt(u, v);
                    Point3d point = V != null ? planarPreviewPoint(V) : Point3d.Unset;
                    if (!point.IsValid)
                    {
                        point = fallbackPlanarPoint(u, v);
                    }

                    mesh.Vertices.Add(point);

                    double value;
                    Color color = tryGetPreviewValue(V, out value) ? retrieveVoxelColor(V, value) : Color.Black;
                    mesh.VertexColors.Add(color);
                }
            }

            for (int u = 0; u < uCount - 1; u++)
            {
                for (int v = 0; v < vCount - 1; v++)
                {
                    int a = u * vCount + v;
                    int b = (u + 1) * vCount + v;
                    int c = (u + 1) * vCount + v + 1;
                    int d = u * vCount + v + 1;
                    mesh.Faces.AddFace(a, b, c, d);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            planarPreviewMesh = mesh;
        }

        Voxel planarVoxelAt(int u, int v)
        {
            if (planarXY) return voxel[u, v, 0];
            if (planarXZ) return voxel[u, 0, v];
            return voxel[0, u, v];
        }

        Point3d planarPreviewPoint(Voxel V)
        {
            Point3d loc = V.loc;
            double offset = previewPlanarOffset();
            if (planarXY) loc.Z = offset;
            if (planarXZ) loc.Y = offset;
            if (planarYZ) loc.X = offset;
            return loc;
        }

        Point3d fallbackPlanarPoint(int u, int v)
        {
            double size = voxelSize > 0 ? voxelSize : 1.0;
            double offset = previewPlanarOffset();
            if (planarXY) return new Point3d(u * size, v * size, offset);
            if (planarXZ) return new Point3d(u * size, offset, v * size);
            return new Point3d(offset, u * size, v * size);
        }

        double previewPlanarOffset()
        {
            switch (valueIndex)
            {
                case 0: return voxelSize / 12;
                case 1: return voxelSize / 11;
                case 2: return voxelSize / 10;
                case 3: return voxelSize / 9;
                case 4: return voxelSize / 8;
                case 5: return voxelSize / 7;
                case 6: return voxelSize / 2.5;
                case 7: return voxelSize / 4;
                case 8: return voxelSize / 3;
                case 9: return voxelSize / 5;
                case 10: return voxelSize / 4;
                case 11: return voxelSize / 4;
                default: return voxelSize / 12;
            }
        }

        bool tryGetPreviewValue(Voxel V, out double value)
        {
            value = 0;
            if (V == null) return false;

            switch (valueIndex)
            {
                case 0:
                    value = V.minDensity;
                    if (value < 0) return false;
                    break;
                case 1:
                    value = V.maxDensity;
                    if (value < 0) return false;
                    break;
                case 2:
                    value = V.speedMultiplier;
                    if (value < 0) return false;
                    break;
                case 3:
                    value = V.sensorDistanceMultiplier;
                    if (value < 0) return false;
                    break;
                case 4:
                    value = V.sensorAngleMultiplier;
                    if (value < 0) return false;
                    break;
                case 5:
                    value = V.rotationAngleMultiplier;
                    if (value < 0) return false;
                    break;
                case 6:
                    value = V.food;
                    if (value < 0) return false;
                    break;
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                    return tryGetDynamicPreviewValue(V, out value);
                default:
                    return false;
            }

            return min <= value && value <= max;
        }

        void addStablePoint(Point3d point, Color color)
        {
            int argb = color.ToArgb();
            for (int i = 0; i < stablePointBuckets.Count; i++)
            {
                if (stablePointBuckets[i].Argb == argb)
                {
                    stablePointBuckets[i].Points.Add(point);
                    return;
                }
            }

            StablePointBucket bucket = new StablePointBucket(color);
            bucket.Points.Add(point);
            stablePointBuckets.Add(bucket);
        }

        void drawStablePlanarPreview(IGH_PreviewArgs args)
        {
            for (int i = 0; i < stablePointBuckets.Count; i++)
            {
                StablePointBucket bucket = stablePointBuckets[i];
                if (bucket.Points.Count == 0) continue;

                args.Display.DrawPoints(
                    bucket.Points,
                    Rhino.Display.PointStyle.Simple,
                    bucket.Color,
                    bucket.Color,
                    3.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    false,
                    false);
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
        List<StablePointBucket> stablePointBuckets;
        Mesh planarPreviewMesh;
        BoundingBox clippingBox = BoundingBox.Empty;
        bool gpuDensityPreviewActive = false;
        SolverGPU gpuDensitySolver;
        BoundingBox gpuDensityClippingBox = BoundingBox.Empty;
        bool staticPreviewCacheValid;
        Voxel[,,] staticPreviewCacheVoxels;
        VoxelGridData staticPreviewCacheData;
        int staticPreviewCacheResX;
        int staticPreviewCacheResY;
        int staticPreviewCacheResZ;
        int staticPreviewCacheValueIndex;
        bool staticPreviewCacheAutomaticDomain;
        double staticPreviewCacheMin;
        double staticPreviewCacheMax;
        double staticPreviewCacheVoxelSize;
        int staticPreviewCacheColourArgb;
        Interval staticPreviewCachePreviewDomain = new Interval(0, 1);
        int staticPreviewCachePointCount;

        struct VoxelPreviewSample
        {
            public VoxelPreviewSample(Point3d point, double value, Voxel voxel)
            {
                Point = point;
                Value = value;
                Food = voxel != null ? voxel.food : 0;
                Slime = voxel != null ? voxel.density : 0;
                FoodPheromone = voxel != null ? voxel.towardsFoodPheromone : 0;
                BasePheromone = voxel != null ? voxel.towardsBasePheromone : 0;
            }

            public Point3d Point;
            public double Value;
            public double Food;
            public double Slime;
            public double FoodPheromone;
            public double BasePheromone;
        }

        class StablePointBucket
        {
            public StablePointBucket(Color color)
            {
                Color = color;
                Argb = color.ToArgb();
                Points = new List<Point3d>();
            }

            public Color Color;
            public int Argb;
            public List<Point3d> Points;
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

            //in case max density is blocked
            if (valueIndex == 1)
            {
                if (d < 0.01)
                {
                    voxelColor = System.Drawing.Color.FromArgb(255, 29, 19, 53);
                }
            }

            return voxelColor;
        }

        Color retrieveVoxelColor(VoxelPreviewSample sample)
        {
            if (valueIndex < VoxelPreviewField.SlimeChemoattractants)
            {
                return retrieveVoxelColor(sample.Value);
            }

            return retrieveDynamicVoxelColor(
                sample.Food,
                sample.Slime,
                sample.FoodPheromone,
                sample.BasePheromone);
        }

        Color retrieveVoxelColor(Voxel V, double value)
        {
            if (valueIndex < VoxelPreviewField.SlimeChemoattractants || V == null)
            {
                return retrieveVoxelColor(value);
            }

            return retrieveDynamicVoxelColor(V.food, V.density, V.towardsFoodPheromone, V.towardsBasePheromone);
        }

        Color retrieveDynamicVoxelColor(double food, double slime, double foodPheromone, double basePheromone)
        {
            double foodVisual = previewValueToNormalized(food);
            double slimeVisual = previewValueToNormalized(slime);
            double foodPheromoneVisual = previewValueToNormalized(foodPheromone);
            double basePheromoneVisual = previewValueToNormalized(basePheromone);

            Color slimeColor = valueIndex == VoxelPreviewField.SlimeChemoattractants && hasCustomColour()
                ? colour
                : Color.FromArgb(255, 223, 255, 123);
            Color foodPheromoneColor = valueIndex == VoxelPreviewField.AntFoodPheromones && hasCustomColour()
                ? colour
                : Color.FromArgb(255, 57, 255, 170);
            Color basePheromoneColor = valueIndex == VoxelPreviewField.AntBasePheromones && hasCustomColour()
                ? colour
                : Color.FromArgb(255, 255, 0, 100);

            double red = 0;
            double green = 0;
            double blue = 0;
            double foregroundStrength = 0;

            if (valueIndex == VoxelPreviewField.SlimeChemoattractants || valueIndex == VoxelPreviewField.AntsAndSlime)
            {
                addColor(ref red, ref green, ref blue, slimeColor, slimeVisual);
                foregroundStrength = Math.Max(foregroundStrength, slimeVisual);
            }
            if (valueIndex == VoxelPreviewField.AntFoodPheromones
                || valueIndex == VoxelPreviewField.AntPheromones
                || valueIndex == VoxelPreviewField.AntsAndSlime)
            {
                addColor(ref red, ref green, ref blue, foodPheromoneColor, foodPheromoneVisual);
                foregroundStrength = Math.Max(foregroundStrength, foodPheromoneVisual);
            }
            if (valueIndex == VoxelPreviewField.AntBasePheromones
                || valueIndex == VoxelPreviewField.AntPheromones
                || valueIndex == VoxelPreviewField.AntsAndSlime)
            {
                addColor(ref red, ref green, ref blue, basePheromoneColor, basePheromoneVisual);
                foregroundStrength = Math.Max(foregroundStrength, basePheromoneVisual);
            }

            double foodBackground = 255.0 * foodVisual * (1.0 - foregroundStrength);
            return Color.FromArgb(
                255,
                clampColorChannel(red + foodBackground),
                clampColorChannel(green + foodBackground),
                clampColorChannel(blue + foodBackground));
        }

        bool tryGetDynamicPreviewValue(Voxel V, out double value)
        {
            double food = Math.Max(0, V.food);
            double slime = Math.Max(0, V.density);
            double foodPheromone = Math.Max(0, V.towardsFoodPheromone);
            double basePheromone = Math.Max(0, V.towardsBasePheromone);

            switch (valueIndex)
            {
                case VoxelPreviewField.SlimeChemoattractants:
                    value = Math.Max(food, slime);
                    break;
                case VoxelPreviewField.AntFoodPheromones:
                    value = Math.Max(food, foodPheromone);
                    break;
                case VoxelPreviewField.AntBasePheromones:
                    value = Math.Max(food, basePheromone);
                    break;
                case VoxelPreviewField.AntPheromones:
                    value = Math.Max(food, Math.Max(foodPheromone, basePheromone));
                    break;
                case VoxelPreviewField.AntsAndSlime:
                    value = Math.Max(food, Math.Max(slime, Math.Max(foodPheromone, basePheromone)));
                    break;
                default:
                    value = 0;
                    return false;
            }

            return value > 0.01 && min <= value && value <= max;
        }

        bool hasCustomColour()
        {
            return colour.R != 0 || colour.G != 0 || colour.B != 0;
        }

        static void addColor(ref double red, ref double green, ref double blue, Color color, double strength)
        {
            red += color.R * strength;
            green += color.G * strength;
            blue += color.B * strength;
        }

        static int clampColorChannel(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (int)Math.Round(value);
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
            double normalized = previewValueToNormalized(value);
            int index = (int)Math.Round(normalized * 255);
            if (index < 0) return 0;
            if (index > 255) return 255;
            return index;
        }

        double previewValueToNormalized(double value)
        {
            double range = currentPreviewDomain.T1 - currentPreviewDomain.T0;
            double normalized = range > 1e-12
                ? (value - currentPreviewDomain.T0) / range
                : value > 0 ? 1.0 : 0.0;
            if (normalized <= 0) return 0;
            if (normalized >= 1) return 1;
            return normalized;
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
            get { return new Guid("2f97ab1f-8665-65dc-e43f-7a91cc981668"); }
        }
    }

}

