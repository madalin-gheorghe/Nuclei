using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Nuclei3
{
    public class Preview_Particle : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DeathSettings class.
        /// </summary>
        public Preview_Particle()
          : base("Particle Preview Settings", "Particle Preview",
              "Sets Up Dynamic Particle Preview Settings",
              "Nuclei3", "Preview")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Particles", "particles", "Input Particles", GH_ParamAccess.item);

            pManager.AddNumberParameter("Point Size", "size", "Point Display Size", GH_ParamAccess.item, 2);
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.AddTextParameter("Particle Preview", "particlePreview", "Settings Controlling Particle Preview. Connects to Solver's Display Input", GH_ParamAccess.list);
        }
        

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.secondary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            long solveStart = Stopwatch.GetTimestamp();

            colour = System.Drawing.Color.FromArgb(175, 220, 255, 0);

            DA.GetData(0, ref particles);
            DA.GetData("Point Size", ref size);

            if (Hidden)
            {
                clearPointClouds();
                ParticlePreviewDisplayConduit.Unregister(InstanceGuid);
                recordPreviewTiming(Stopwatch.GetTimestamp() - solveStart, 0);
                return;
            }

            GpuParticlePreviewFrame gpuFrame = tryGetGpuParticlePreviewFrame();
            if (gpuFrame != null && gpuFrame.IsValid)
            {
                clearPointClouds();
                clippingBox = gpuFrame.ClippingBox;
                ParticlePreviewDisplayConduit.Register(this);
                recordPreviewTiming(Stopwatch.GetTimestamp() - solveStart, 0);
                return;
            }

            long rebuildTicks = 0;
            if (!tryUseCachedPointClouds(false))
            {
                long rebuildStart = Stopwatch.GetTimestamp();
                rebuildPointClouds();
                rebuildTicks = Stopwatch.GetTimestamp() - rebuildStart;
            }

            if (particles == null)
            {
                ParticlePreviewDisplayConduit.Unregister(InstanceGuid);
            }
            else
            {
                ParticlePreviewDisplayConduit.Register(this);
            }

            recordPreviewTiming(Stopwatch.GetTimestamp() - solveStart, rebuildTicks);
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
        }

        public override BoundingBox ClippingBox
        {
            get { return clippingBox; }
        }

        internal bool WantsSolverPreviewCache
        {
            get { return !Hidden && !Locked; }
        }

        internal ParticlePreviewDisplayFrame GetDisplayFrame(bool refreshAsync)
        {
            if (Hidden || Locked || particles == null) return null;

            GpuParticlePreviewFrame gpuFrame = tryGetGpuParticlePreviewFrame();
            if (gpuFrame != null && gpuFrame.IsValid)
            {
                clippingBox = gpuFrame.ClippingBox;
                return new ParticlePreviewDisplayFrame
                {
                    GpuFrame = gpuFrame,
                    ClippingBox = clippingBox,
                    PointSize = size,
                    HasPoint = clippingBox.IsValid
                };
            }

            tryUseCachedPointClouds(refreshAsync);
            if (slimePointCloud.Count == 0 && antPointCloud1.Count == 0 && antPointCloud2.Count == 0)
            {
                return null;
            }

            return new ParticlePreviewDisplayFrame
            {
                SlimePointCloud = slimePointCloud,
                AntPointCloud1 = antPointCloud1,
                AntPointCloud2 = antPointCloud2,
                ClippingBox = clippingBox,
                PointSize = size,
                HasPoint = clippingBox.IsValid
            };
        }

        internal void RecordConduitDrawTiming(long drawTicks)
        {
            recordPreviewDrawTiming(drawTicks);
        }

        void clearPointClouds()
        {
            slimePointCloud = new PointCloud();
            antPointCloud1 = new PointCloud();
            antPointCloud2 = new PointCloud();
            clippingBox = BoundingBox.Empty;
        }

        void rebuildPointClouds()
        {
            clearPointClouds();

            if (particles == null) return;

            Dictionary<ParticleGroup, Color> foundFoodColors = new Dictionary<ParticleGroup, Color>();
            bool hasPoint = false;

            for (int i = 0; i < particles.Count; i++)
            {
                Particle P = particles[i];
                ParticleGroup group = P.parentParticleGroup;
                Point3d P_loc = P.pPlane.Origin;
                includePointInClippingBox(P_loc, ref hasPoint);

                if (!group.ant)
                {
                    slimePointCloud.Add(P_loc, group.color);
                }
                else if (!P.foundFood)
                {
                    slimePointCloud.Add(P_loc, group.color);
                }
                else
                {
                    if (!foundFoodColors.TryGetValue(group, out Color particleColor))
                    {
                        int R = (int)(group.color.R * 1.75);
                        if (R > 255) R = 255;

                        int G = (int)(group.color.G * 1.75);
                        if (G > 255) G = 255;

                        int B = (int)(group.color.B * 1.75);
                        if (B > 255) B = 255;

                        particleColor = Color.FromArgb(175, R, G, B);
                        foundFoodColors[group] = particleColor;
                    }

                    antPointCloud2.Add(P_loc, particleColor);
                }
            }

            if (hasPoint)
            {
                clippingBox.Inflate(Math.Max(Globals.voxelSize, 1.0));
            }
        }

        bool tryUseCachedPointClouds(bool refreshAsync)
        {
            ParticleList particleList = particles as ParticleList;
            ParticlePreviewCache cache = particleList != null ? particleList.PreviewCache : null;
            if (cache == null) return false;
            if (refreshAsync)
            {
                cache.TryRefreshAsync();
            }
            if (!cache.IsValid) return false;

            slimePointCloud = cache.SlimePointCloud ?? new PointCloud();
            antPointCloud1 = cache.AntPointCloud1 ?? new PointCloud();
            antPointCloud2 = cache.AntPointCloud2 ?? new PointCloud();
            clippingBox = cache.HasPoint ? cache.ClippingBox : BoundingBox.Empty;
            return true;
        }

        void includePointInClippingBox(Point3d point, ref bool hasPoint)
        {
            if (!hasPoint)
            {
                clippingBox = new BoundingBox(point, point);
                hasPoint = true;
                return;
            }

            clippingBox.Union(point);
        }

        GpuParticlePreviewFrame tryGetGpuParticlePreviewFrame()
        {
            ParticleList particleList = particles as ParticleList;
            return particleList != null ? particleList.GetGpuPreviewFrame() : null;
        }

        void recordPreviewTiming(long totalTicks, long rebuildTicks)
        {
            timingCallCount++;
            timingSampleCount++;
            timingTotalTicks += totalTicks;
            timingRebuildTicks += rebuildTicks;

            if (timingSampleCount < TimingReporter.ReportFrequency) return;

            double totalMs = TimingReporter.TicksToMilliseconds(timingTotalTicks, timingSampleCount);
            double rebuildMs = TimingReporter.TicksToMilliseconds(timingRebuildTicks, timingSampleCount);
            double drawMs = TimingReporter.TicksToMilliseconds(timingDrawTicks, timingDrawSampleCount);
            int particleCount = particles != null ? particles.Count : 0;

            TimingReporter.WritePreviewAverages(timingCallCount, timingSampleCount, particleCount, 1, totalMs, rebuildMs, drawMs);

            timingSampleCount = 0;
            timingTotalTicks = 0;
            timingRebuildTicks = 0;
            timingDrawSampleCount = 0;
            timingDrawTicks = 0;
        }

        void recordPreviewDrawTiming(long drawTicks)
        {
            timingDrawSampleCount++;
            timingDrawTicks += drawTicks;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            ParticlePreviewDisplayConduit.Unregister(InstanceGuid);
            base.RemovedFromDocument(document);
        }

        //-------------------------------------------------------------------

        //inputs
        List<Particle> particles;
        PointCloud slimePointCloud = new PointCloud();
        PointCloud antPointCloud1 = new PointCloud();
        PointCloud antPointCloud2 = new PointCloud();
        BoundingBox clippingBox = BoundingBox.Empty;
        //bool display;
        Color colour;
        double size;

        //timing
        int timingCallCount = 0;
        int timingSampleCount = 0;
        int timingDrawSampleCount = 0;
        long timingTotalTicks = 0;
        long timingRebuildTicks = 0;
        long timingDrawTicks = 0;

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
                return Nuclei3.Properties.Resources.PreviewParticles;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("a4a9e7cb-f899-461a-9137-207d4601ae14"); }
        }
    }
}
