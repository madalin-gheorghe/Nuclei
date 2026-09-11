using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace Nuclei2.OldBanner
{
    public static class OldBannerPriority
    {
        private const string LegacyAssemblyName = "Nuclei2";

        private static readonly HashSet<GH_Canvas> AttachedCanvases = new HashSet<GH_Canvas>();
        private static readonly Font BannerFont = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);
        private static readonly Brush BannerBrush = new SolidBrush(Color.FromArgb(238, 185, 34, 34));
        private static readonly Brush TextBrush = new SolidBrush(Color.White);
        private static readonly Pen BorderPen = new Pen(Color.FromArgb(230, 105, 14, 14), 1.0f);
        private static readonly StringFormat CenteredText = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None
        };

        private static bool registered;

        public static void Register()
        {
            int rhinoVersion = Rhino.RhinoApp.ExeVersion;
            if ((rhinoVersion != 8 && rhinoVersion != 9) || registered)
                return;

            registered = true;
            NucleiRibbonGroup.Register();
            Instances.CanvasCreated += OnCanvasCreated;
            Instances.CanvasDestroyed += OnCanvasDestroyed;

            if (Instances.ActiveCanvas != null)
                Attach(Instances.ActiveCanvas);

        }

        private static void OnCanvasCreated(GH_Canvas canvas)
        {
            Attach(canvas);
        }

        private static void OnCanvasDestroyed(GH_Canvas canvas)
        {
            if (canvas == null || !AttachedCanvases.Remove(canvas))
                return;

            canvas.CanvasPostPaintObjects -= PaintOldBanners;
        }

        private static void Attach(GH_Canvas canvas)
        {
            if (canvas == null || !AttachedCanvases.Add(canvas))
                return;

            canvas.CanvasPostPaintObjects += PaintOldBanners;
        }

        private static void PaintOldBanners(GH_Canvas canvas)
        {
            GH_Document document = canvas?.Document;
            Graphics graphics = canvas?.Graphics;
            if (document == null || graphics == null || !canvas.ValidGraphics)
                return;

            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                foreach (IGH_DocumentObject item in document.Objects)
                {
                    GH_Component component = item as GH_Component;
                    if (component?.Attributes == null || !IsLegacyNucleiComponent(component))
                        continue;

                    RectangleF bounds = component.Attributes.Bounds;
                    if (bounds.Width < 8.0f || bounds.Height < 8.0f)
                        continue;

                    float bannerHeight = Math.Min(14.0f, Math.Max(10.0f, bounds.Height * 0.18f));
                    RectangleF banner = new RectangleF(
                        bounds.Left + 1.0f,
                        bounds.Top - bannerHeight - 1.0f,
                        Math.Max(1.0f, bounds.Width - 2.0f),
                        bannerHeight);

                    graphics.FillRectangle(BannerBrush, banner);
                    graphics.DrawRectangle(BorderPen, banner.X, banner.Y, banner.Width, banner.Height);
                    graphics.DrawString("old v2", BannerFont, TextBrush, banner, CenteredText);
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static bool IsLegacyNucleiComponent(GH_Component component)
        {
            return string.Equals(
                component.GetType().Assembly.GetName().Name,
                LegacyAssemblyName,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
