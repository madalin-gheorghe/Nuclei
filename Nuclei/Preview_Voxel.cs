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
          : base(name, nickname, description, "Nuclei4", "Preview")
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
            highResolutionDisplay = false;
            reader.TryGetBoolean("HighResolutionDisplay", ref highResolutionDisplay);
            return base.Read(reader);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            var highResToggle = Menu_AppendItem(menu, "High Res Display", highResolutionDisplayHandler, true, highResolutionDisplay);
            highResToggle.ToolTipText = "Display small 2D Slime Chemoattractants fields with a 10x interpolated GPU texture. Large fields stay at native resolution.";
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

                if (tryReuseStaticPreviewCache(resX, resY, resZ))
                {
                    return;
                }

                if (!VoxelPreviewField.IsStatic(valueIndex))
                {
                    invalidateStaticPreviewCache();
                }

                VoxelGridData previewData = VoxelGridRegistry.GetOrCapture(voxel, voxelSize);
                if (!tridimensional && buildPlanarPreviewMesh(previewData))
                {
                    clearPointCloudOnly();
                    updateClippingBox();
                    rememberStaticPreviewCache(resX, resY, resZ);
                    return;
                }

                buildSampledPointCloud(previewData, resX, resY, resZ);
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

            if (Hidden || Locked) return;

            bool hasPlanarMesh = planarPreviewMesh != null && planarPreviewMesh.Faces.Count > 0;
            bool hasPointCloud = voxelPointCloud != null && voxelPointCloud.Count > 0;
            if (!hasPlanarMesh && !hasPointCloud) return;

            //draw background polygon
            if (!Globals.tridimensional)
            {
                args.Display.DrawPolygon(Globals.bgPolygon, Color.Black, true);
            }

            if (!Globals.tridimensional && hasPlanarMesh)
            {
                args.Display.DrawMeshFalseColors(planarPreviewMesh);
                return;
            }

            if (!Globals.tridimensional && stablePointBuckets != null && stablePointBuckets.Count > 0)
            {
                drawStablePlanarPreview(args);
                return;
            }

            if (hasPointCloud)
            {
                args.Display.DrawPointCloud(voxelPointCloud, 3);
            }
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
            get { return safeGpuDensityPreviewScale(); }
        }

        internal bool WantsGpuVoxelPreview
        {
            get { return WantsGpuDynamicDensityPreview; }
        }

        internal bool WantsSolverVoxelOutput
        {
            get
            {
                int currentValueIndex = CurrentValueIndex();
                return !Hidden
                    && !Locked
                    && !WantsGpuVoxelPreview
                    && VoxelPreviewField.IsDynamicDensity(currentValueIndex);
            }
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

            bool useCustomColor = !VoxelPreviewField.IsCombinedDynamicDensity(currentValueIndex)
                && (colour.R != 0 || colour.G != 0 || colour.B != 0);
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

        const int HighResolutionDensityPreviewScale = 10;
        const int MaxSharedPreviewTextureDimension = 16384;
        const long MaxHighResolutionDensityPreviewPixels = 33554432;

        int safeGpuDensityPreviewScale()
        {
            if (!highResolutionDisplay || voxel == null)
            {
                return 1;
            }

            int width;
            int height;
            if (!tryGetPlanarDensityPreviewBaseSize(out width, out height))
            {
                return 1;
            }

            long basePixels = (long)width * height;
            if (basePixels <= 0)
            {
                return 1;
            }

            long scaledWidth = (long)width * HighResolutionDensityPreviewScale;
            long scaledHeight = (long)height * HighResolutionDensityPreviewScale;
            long scaledPixels = scaledWidth * scaledHeight;
            if (scaledWidth > MaxSharedPreviewTextureDimension
                || scaledHeight > MaxSharedPreviewTextureDimension
                || scaledPixels > MaxHighResolutionDensityPreviewPixels)
            {
                return 1;
            }

            return HighResolutionDensityPreviewScale;
        }

        bool tryGetPlanarDensityPreviewBaseSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            if (voxel == null)
            {
                return false;
            }

            int x = voxel.GetLength(0);
            int y = voxel.GetLength(1);
            int z = voxel.GetLength(2);
            if (x <= 0 || y <= 0 || z <= 0)
            {
                return false;
            }

            if (x > 1 && y > 1 && z == 1)
            {
                width = x;
                height = y;
                return true;
            }

            if (x > 1 && y == 1 && z > 1)
            {
                width = x;
                height = z;
                return true;
            }

            if (x == 1 && y > 1 && z > 1)
            {
                width = y;
                height = z;
                return true;
            }

            return false;
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

        void clearPointCloudOnly()
        {
            voxelPoints = null;
            voxelValues = null;
            voxelPointCloud = null;
            stablePointBuckets = null;
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

        const int MaxPlanarPreviewMeshVertices = 400000;
        const int MaxPointCloudPreviewSamples = 300000;

        bool buildPlanarPreviewMesh(VoxelGridData previewData)
        {
            planarPreviewMesh = null;
            stablePointBuckets = null;
            if (voxel == null || tridimensional) return false;

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
                return false;
            }

            if (uCount < 2 || vCount < 2) return false;

            int sampleUCount;
            int sampleVCount;
            resolvePlanarSampleCounts(uCount, vCount, out sampleUCount, out sampleVCount);
            if (sampleUCount < 2 || sampleVCount < 2) return false;

            int vertexCount = sampleUCount * sampleVCount;
            double[] values = new double[vertexCount];
            bool[] hasValues = new bool[vertexCount];
            minExistingVoxelValue = double.PositiveInfinity;
            maxExistingVoxelValue = double.NegativeInfinity;

            Mesh mesh = new Mesh();
            mesh.Vertices.Capacity = vertexCount;
            mesh.Faces.Capacity = (sampleUCount - 1) * (sampleVCount - 1);

            for (int su = 0; su < sampleUCount; su++)
            {
                int u = sampleIndexToSourceIndex(su, sampleUCount, uCount);
                for (int sv = 0; sv < sampleVCount; sv++)
                {
                    int v = sampleIndexToSourceIndex(sv, sampleVCount, vCount);
                    int vertexIndex = su * sampleVCount + sv;

                    int x;
                    int y;
                    int z;
                    planarCoordinates(u, v, out x, out y, out z);
                    mesh.Vertices.Add(previewPointAt(previewData, x, y, z));

                    double value;
                    if (tryGetPreviewValueAt(previewData, x, y, z, out value))
                    {
                        values[vertexIndex] = value;
                        hasValues[vertexIndex] = true;
                        if (value < minExistingVoxelValue) minExistingVoxelValue = value;
                        if (value > maxExistingVoxelValue) maxExistingVoxelValue = value;
                    }
                }
            }

            applyPreviewDomainFromSamples();

            for (int i = 0; i < vertexCount; i++)
            {
                mesh.VertexColors.Add(hasValues[i] ? retrieveVoxelColor(values[i]) : Color.Black);
            }

            for (int su = 0; su < sampleUCount - 1; su++)
            {
                for (int sv = 0; sv < sampleVCount - 1; sv++)
                {
                    int a = su * sampleVCount + sv;
                    int b = (su + 1) * sampleVCount + sv;
                    int c = (su + 1) * sampleVCount + sv + 1;
                    int d = su * sampleVCount + sv + 1;
                    mesh.Faces.AddFace(a, b, c, d);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            planarPreviewMesh = mesh;
            return true;
        }

        void buildSampledPointCloud(VoxelGridData previewData, int resX, int resY, int resZ)
        {
            planarPreviewMesh = null;
            stablePointBuckets = null;
            List<VoxelPreviewSample> samples = new List<VoxelPreviewSample>();
            minExistingVoxelValue = double.PositiveInfinity;
            maxExistingVoxelValue = double.NegativeInfinity;

            int step = previewSampleStep(resX, resY, resZ, MaxPointCloudPreviewSamples);
            for (int x = 0; x < resX; x += step)
            {
                for (int y = 0; y < resY; y += step)
                {
                    for (int z = 0; z < resZ; z += step)
                    {
                        double value;
                        if (!tryGetPreviewValueAt(previewData, x, y, z, out value))
                        {
                            continue;
                        }

                        Point3d point = previewPointAt(previewData, x, y, z);
                        samples.Add(new VoxelPreviewSample(point, value));
                        if (value < minExistingVoxelValue) minExistingVoxelValue = value;
                        if (value > maxExistingVoxelValue) maxExistingVoxelValue = value;
                    }
                }
            }

            applyPreviewDomainFromSamples();

            voxelPoints = new List<Point3d>(samples.Count);
            voxelValues = new List<double>(samples.Count);
            voxelPointCloud = new PointCloud();
            for (int i = 0; i < samples.Count; i++)
            {
                VoxelPreviewSample sample = samples[i];
                Color sampleColor = retrieveVoxelColor(sample.Value);
                voxelPoints.Add(sample.Point);
                voxelValues.Add(sample.Value);
                voxelPointCloud.Add(sample.Point, sampleColor);
            }
        }

        void applyPreviewDomainFromSamples()
        {
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
        }

        void resolvePlanarSampleCounts(int uCount, int vCount, out int sampleUCount, out int sampleVCount)
        {
            sampleUCount = uCount;
            sampleVCount = vCount;
            long fullCount = (long)uCount * vCount;
            if (fullCount <= MaxPlanarPreviewMeshVertices)
            {
                return;
            }

            double scale = Math.Sqrt(MaxPlanarPreviewMeshVertices / (double)fullCount);
            sampleUCount = Math.Max(2, (int)Math.Floor(uCount * scale));
            sampleVCount = Math.Max(2, (int)Math.Floor(vCount * scale));

            while ((long)sampleUCount * sampleVCount > MaxPlanarPreviewMeshVertices)
            {
                if (sampleUCount >= sampleVCount && sampleUCount > 2)
                {
                    sampleUCount--;
                }
                else if (sampleVCount > 2)
                {
                    sampleVCount--;
                }
                else
                {
                    break;
                }
            }
        }

        static int sampleIndexToSourceIndex(int sampleIndex, int sampleCount, int sourceCount)
        {
            if (sourceCount <= 1) return 0;
            if (sampleCount <= 1) return 0;
            int value = (int)Math.Round(sampleIndex * (sourceCount - 1) / (double)(sampleCount - 1));
            if (value < 0) return 0;
            if (value >= sourceCount) return sourceCount - 1;
            return value;
        }

        static int previewSampleStep(int resX, int resY, int resZ, int maxSamples)
        {
            long count = (long)Math.Max(1, resX) * Math.Max(1, resY) * Math.Max(1, resZ);
            if (count <= maxSamples) return 1;

            int step = 1;
            while (sampledCount(resX, resY, resZ, step) > maxSamples)
            {
                step++;
            }

            return step;
        }

        static long sampledCount(int resX, int resY, int resZ, int step)
        {
            return ((resX + step - 1L) / step)
                * ((resY + step - 1L) / step)
                * ((resZ + step - 1L) / step);
        }

        void planarCoordinates(int u, int v, out int x, out int y, out int z)
        {
            if (planarXY)
            {
                x = u;
                y = v;
                z = 0;
                return;
            }

            if (planarXZ)
            {
                x = u;
                y = 0;
                z = v;
                return;
            }

            x = 0;
            y = u;
            z = v;
        }

        Point3d previewPointAt(VoxelGridData previewData, int x, int y, int z)
        {
            Voxel V = voxelAt(x, y, z);
            Point3d loc = V != null
                ? V.loc
                : previewData != null
                    ? previewData.CenterPoint(previewData.FlatIndex(x, y, z))
                    : fallbackPoint(x, y, z);

            double offset = previewPlanarOffset();
            if (planarXY) loc.Z = offset;
            if (planarXZ) loc.Y = offset;
            if (planarYZ) loc.X = offset;
            return loc;
        }

        Point3d fallbackPoint(int x, int y, int z)
        {
            double size = voxelSize > 0 ? voxelSize : 1.0;
            return new Point3d(x * size + size / 2, y * size + size / 2, z * size + size / 2);
        }

        Voxel voxelAt(int x, int y, int z)
        {
            if (voxel == null
                || x < 0 || y < 0 || z < 0
                || x >= voxel.GetLength(0)
                || y >= voxel.GetLength(1)
                || z >= voxel.GetLength(2))
            {
                return null;
            }

            return voxel[x, y, z];
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
                default: return voxelSize / 12;
            }
        }

        bool tryGetPreviewValueAt(VoxelGridData previewData, int x, int y, int z, out double value)
        {
            value = 0;
            int flatIndex = -1;
            if (previewData != null)
            {
                flatIndex = previewData.FlatIndex(x, y, z);
                if (!previewData.IsActive(flatIndex)) return false;
            }

            Voxel V = voxelAt(x, y, z);

            switch (valueIndex)
            {
                case 0:
                    value = previewData != null ? previewData.MinimumDensity.Get(flatIndex) : V != null ? V.minDensity : -1;
                    if (value < 0) return false;
                    break;
                case 1:
                    value = previewData != null ? previewData.MaximumDensity.Get(flatIndex) : V != null ? V.maxDensity : -1;
                    if (value < 0) return false;
                    break;
                case 2:
                    value = previewData != null ? previewData.Speed.Get(flatIndex) : V != null ? V.speedMultiplier : -1;
                    if (value < 0) return false;
                    break;
                case 3:
                    value = previewData != null ? previewData.SensorDistance.Get(flatIndex) : V != null ? V.sensorDistanceMultiplier : -1;
                    if (value < 0) return false;
                    break;
                case 4:
                    value = previewData != null ? previewData.SensorAngle.Get(flatIndex) : V != null ? V.sensorAngleMultiplier : -1;
                    if (value < 0) return false;
                    break;
                case 5:
                    value = previewData != null ? previewData.RotationAngle.Get(flatIndex) : V != null ? V.rotationAngleMultiplier : -1;
                    if (value < 0) return false;
                    break;
                case 6:
                    value = previewData != null ? previewData.Food.Get(flatIndex) : V != null ? V.food : -1;
                    if (value < 0) return false;
                    break;
                case 7:
                    value = V != null ? V.density : previewData != null ? previewData.Density.Get(flatIndex) : 0;
                    if (value <= 0.01) return false;
                    break;
                case 8:
                    if (V == null) return false;
                    value = V.towardsFoodPheromone;
                    if (value <= 0.01) return false;
                    break;
                case 9:
                    if (V == null) return false;
                    value = V.towardsBasePheromone;
                    if (value <= 0.01) return false;
                    break;
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
        bool highResolutionDisplay = true;
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
            public VoxelPreviewSample(Point3d point, double value)
            {
                Point = point;
                Value = value;
            }

            public Point3d Point;
            public double Value;
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
                if (d < VoxelOccupancy.BlockedMaxDensityThreshold)
                {
                    voxelColor = System.Drawing.Color.FromArgb(255, 29, 19, 53);
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
            get { return new Guid("fb2ea9fc-5963-4587-b09b-0422f61174db"); }
        }
    }

}
