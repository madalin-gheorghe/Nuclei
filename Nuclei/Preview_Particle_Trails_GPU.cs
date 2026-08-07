using Grasshopper.Kernel;
using Rhino.Geometry;

using System;
using System.Drawing;

namespace Nuclei3
{
    public class Preview_Particle_Trails_GPU : GH_Component
    {
        public Preview_Particle_Trails_GPU()
          : base("Particle Trail Preview GPU", "Trail Preview GPU",
              "Displays GPU particle trails with Direct3D",
              "Nuclei4", "Preview")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Particles", "particles", "Input Particles", GH_ParamAccess.item);

            pManager.AddNumberParameter("Alpha", "alpha", "Trail opacity multiplier", GH_ParamAccess.item, 0.35);
            pManager[1].Optional = true;

            pManager.AddNumberParameter("Depth Focus", "depth", "Camera-depth fading for 3D trail readability. 0 disables it, 1 is strongest.", GH_ParamAccess.item, 0.55);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.secondary; }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref particles);
            DA.GetData("Alpha", ref alpha);
            DA.GetData("Depth Focus", ref depthFocus);

            alpha = Clamp(alpha, 0, 1);
            depthFocus = Clamp(depthFocus, 0, 1);

            if (Hidden || Locked || particles == null)
            {
                ParticleTrailPreviewDisplayConduit.Unregister(InstanceGuid);
                clippingBox = BoundingBox.Empty;
                Message = "";
                return;
            }

            GpuParticleTrailPreviewFrame frame = tryGetGpuTrailPreviewFrame();
            if (frame != null && frame.IsValid)
            {
                updatePalettes(frame);
                clippingBox = frame.ClippingBox;
                ParticleTrailPreviewDisplayConduit.Register(this);
                Message = frame.ValidTrailCount + " samples";
                return;
            }

            ParticleTrailPreviewDisplayConduit.Unregister(InstanceGuid);
            clippingBox = BoundingBox.Empty;
            Message = "No GPU trail";
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
        }

        public override BoundingBox ClippingBox
        {
            get { return clippingBox; }
        }

        internal bool WantsGpuTrailPreview
        {
            get { return !Hidden && !Locked; }
        }

        internal ParticleTrailPreviewDisplayFrame GetDisplayFrame()
        {
            if (Hidden || Locked || particles == null) return null;

            GpuParticleTrailPreviewFrame frame = tryGetGpuTrailPreviewFrame();
            if (frame == null || !frame.IsValid) return null;

            clippingBox = frame.ClippingBox;
            return new ParticleTrailPreviewDisplayFrame
            {
                GpuFrame = frame,
                FreshColor = freshColor,
                OldColor = oldColor,
                FreshColors = freshColors,
                OldColors = oldColors,
                Alpha = alpha,
                FadePower = fadePower,
                DepthFocus = IsPlanar(frame) ? 0.0 : depthFocus,
                ClippingBox = clippingBox,
                HasPoint = clippingBox.IsValid
            };
        }

        GpuParticleTrailPreviewFrame tryGetGpuTrailPreviewFrame()
        {
            ParticleList particleList = particles as ParticleList;
            return particleList != null ? particleList.GetGpuTrailPreviewFrame() : null;
        }

        void updatePalettes(GpuParticleTrailPreviewFrame frame)
        {
            int groupCount = frame != null ? frame.GroupCount : 0;
            float[] groupColorData = frame != null ? frame.GroupColorData : null;
            if (groupCount > 0 && groupColorData != null && groupColorData.Length >= groupCount * 4)
            {
                if (freshColors == null || freshColors.Length != groupCount)
                {
                    freshColors = new Color[groupCount];
                    oldColors = new Color[groupCount];
                }

                for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                {
                    int offset = groupIndex * 4;
                    Color groupColor = Color.FromArgb(
                        255,
                        ClampByte(Clamp01(groupColorData[offset]) * 255.0),
                        ClampByte(Clamp01(groupColorData[offset + 1]) * 255.0),
                        ClampByte(Clamp01(groupColorData[offset + 2]) * 255.0));
                    freshColors[groupIndex] = groupColor;
                    oldColors[groupIndex] = RotateDefaultOldColor(groupColor);
                }

                freshColor = freshColors[0];
                oldColor = oldColors[0];
                return;
            }

            Color representative = RepresentativeParticleColor(particles as ParticleList);
            freshColor = Color.FromArgb(255, representative.R, representative.G, representative.B);
            oldColor = RotateDefaultOldColor(freshColor);
            freshColors = new Color[] { freshColor };
            oldColors = new Color[] { oldColor };
        }

        static Color RepresentativeParticleColor(ParticleList particleList)
        {
            if (particleList == null || particleList.Count == 0)
            {
                return DefaultFreshColor;
            }

            int limit = Math.Min(particleList.Count, 64);
            for (int i = 0; i < limit; i++)
            {
                Particle particle = particleList[i];
                if (particle == null || particle.parentParticleGroup == null)
                {
                    continue;
                }

                Color color = particle.parentParticleGroup.color;
                if (!color.IsEmpty)
                {
                    return Color.FromArgb(255, color.R, color.G, color.B);
                }
            }

            return DefaultFreshColor;
        }

        static Color RotateDefaultOldColor(Color fresh)
        {
            double freshH;
            double freshS;
            double freshL;
            RgbToHsl(fresh, out freshH, out freshS, out freshL);

            if (freshS < 0.04)
            {
                int gray = ClampByte(freshL * 255.0 * 0.42);
                return Color.FromArgb(255, gray, gray, gray);
            }

            double defaultFreshH;
            double defaultFreshS;
            double defaultFreshL;
            RgbToHsl(DefaultFreshColor, out defaultFreshH, out defaultFreshS, out defaultFreshL);

            double defaultOldH;
            double defaultOldS;
            double defaultOldL;
            RgbToHsl(DefaultOldColor, out defaultOldH, out defaultOldS, out defaultOldL);

            double hueOffset = defaultOldH - defaultFreshH;
            double oldH = WrapHue(freshH + hueOffset);
            double oldS = Clamp01(freshS * SafeRatio(defaultOldS, defaultFreshS));
            double oldL = Clamp01(freshL + (defaultOldL - defaultFreshL));
            return HslToRgb(oldH, oldS, oldL);
        }

        static bool IsPlanar(GpuParticleTrailPreviewFrame frame)
        {
            return frame == null || frame.ResX <= 1 || frame.ResY <= 1 || frame.ResZ <= 1;
        }

        static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        static double SafeRatio(double numerator, double denominator)
        {
            return Math.Abs(denominator) > 1e-9 ? numerator / denominator : 1.0;
        }

        static double WrapHue(double hue)
        {
            hue = hue - Math.Floor(hue);
            return hue < 0.0 ? hue + 1.0 : hue;
        }

        static void RgbToHsl(Color color, out double hue, out double saturation, out double lightness)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            lightness = (max + min) * 0.5;
            if (delta < 1e-9)
            {
                hue = 0.0;
                saturation = 0.0;
                return;
            }

            saturation = lightness > 0.5
                ? delta / (2.0 - max - min)
                : delta / (max + min);

            if (Math.Abs(max - r) < 1e-9)
            {
                hue = ((g - b) / delta + (g < b ? 6.0 : 0.0)) / 6.0;
            }
            else if (Math.Abs(max - g) < 1e-9)
            {
                hue = ((b - r) / delta + 2.0) / 6.0;
            }
            else
            {
                hue = ((r - g) / delta + 4.0) / 6.0;
            }
        }

        static Color HslToRgb(double hue, double saturation, double lightness)
        {
            double r;
            double g;
            double b;

            if (saturation <= 1e-9)
            {
                r = lightness;
                g = lightness;
                b = lightness;
            }
            else
            {
                double q = lightness < 0.5
                    ? lightness * (1.0 + saturation)
                    : lightness + saturation - lightness * saturation;
                double p = 2.0 * lightness - q;
                r = HueToRgb(p, q, hue + 1.0 / 3.0);
                g = HueToRgb(p, q, hue);
                b = HueToRgb(p, q, hue - 1.0 / 3.0);
            }

            return Color.FromArgb(255, ClampByte(r * 255.0), ClampByte(g * 255.0), ClampByte(b * 255.0));
        }

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0.0) t += 1.0;
            if (t > 1.0) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        static int ClampByte(double value)
        {
            if (value <= 0.0) return 0;
            if (value >= 255.0) return 255;
            return (int)Math.Round(value);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            ParticleTrailPreviewDisplayConduit.Unregister(InstanceGuid);
            base.RemovedFromDocument(document);
        }

        ParticleList particles;
        static readonly Color DefaultFreshColor = Color.FromArgb(255, 255, 0, 220);
        static readonly Color DefaultOldColor = Color.FromArgb(255, 0, 180, 255);
        Color freshColor = DefaultFreshColor;
        Color oldColor = DefaultOldColor;
        Color[] freshColors = new Color[] { DefaultFreshColor };
        Color[] oldColors = new Color[] { DefaultOldColor };
        double alpha = 0.35;
        const double fadePower = 2.0;
        double depthFocus = 0.55;
        BoundingBox clippingBox = BoundingBox.Empty;

        protected override System.Drawing.Bitmap Icon
        {
            get { return Nuclei3.Properties.Resources.ParticleTrails; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("b17ecf97-0425-4ae2-a6b0-b3f869a5bc72"); }
        }
    }
}
