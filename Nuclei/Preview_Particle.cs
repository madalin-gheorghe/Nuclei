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

            pManager.AddIntegerParameter("Preview Step", "step", "Draws every nth particle. 1 draws all particles.", GH_ParamAccess.item, 1);
            pManager[2].Optional = true;
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
            DA.GetData("Preview Step", ref previewStep);

            if (previewStep < 1) previewStep = 1;

            if (Hidden)
            {
                clearPointClouds();
                recordPreviewTiming(Stopwatch.GetTimestamp() - solveStart, 0);
                return;
            }

            long rebuildTicks = 0;
            if (!tryUseCachedPointClouds())
            {
                long rebuildStart = Stopwatch.GetTimestamp();
                rebuildPointClouds();
                rebuildTicks = Stopwatch.GetTimestamp() - rebuildStart;
            }

            recordPreviewTiming(Stopwatch.GetTimestamp() - solveStart, rebuildTicks);
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            long drawStart = Stopwatch.GetTimestamp();

            try
            {
                if (!this.Hidden && particles != null)
                {
                    base.DrawViewportWires(args);

                    //draw background polygon
                    if (!Globals.tridimensional)
                    {
                        args.Display.DrawPolygon(Globals.bgPolygon, Color.Black, true);
                    }

                    if (slimePointCloud.Count > 0) args.Display.DrawPointCloud(slimePointCloud, (float)size);
                    if (antPointCloud1.Count > 0) args.Display.DrawPointCloud(antPointCloud1, (float)size);
                    if (antPointCloud2.Count > 0) args.Display.DrawPointCloud(antPointCloud2, (float)(size * 1.5));
                }
            }
            finally
            {
                recordPreviewDrawTiming(Stopwatch.GetTimestamp() - drawStart);
            }

        }

        public override BoundingBox ClippingBox
        {
            get { return clippingBox; }
        }

        internal bool WantsSolverPreviewCache
        {
            get { return !Hidden && !Locked && previewStep == 1; }
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

            for (int i = 0; i < particles.Count; i += previewStep)
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
                    antPointCloud1.Add(P_loc, group.color);
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

        bool tryUseCachedPointClouds()
        {
            if (previewStep != 1) return false;

            ParticleList particleList = particles as ParticleList;
            ParticlePreviewCache cache = particleList != null ? particleList.PreviewCache : null;
            if (cache == null || !cache.IsValid) return false;

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

            TimingReporter.WritePreviewAverages(timingCallCount, timingSampleCount, particleCount, previewStep, totalMs, rebuildMs, drawMs);

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
        int previewStep = 1;

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
