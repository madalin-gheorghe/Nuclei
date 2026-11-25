using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

internal static class TexturePreviewCapture
{
    internal static void Capture(object texture, int width, int height, string output)
    {
        using var doc = RhinoDoc.Create(null);
        var view = doc.Views.Add("Isolated texture preview", DefinedViewportProjection.Top,
            new Rectangle(0, 0, 1200, 1200), false) ?? throw new Exception("Could not create capture view.");
        var vp = view.ActiveViewport;
        vp.DisplayMode = DisplayModeDescription.FindByName("Wireframe");
        vp.ConstructionGridVisible = false; vp.ConstructionAxesVisible = false;
        using var conduit = new TextureConduit(texture) { Enabled = true };
        void Save(string name, BoundingBox bounds)
        {
            vp.ZoomBoundingBox(bounds);
            view.Redraw(); RhinoApp.Wait();
            var capture = new ViewCapture { Width = 1200, Height = 1200, DrawAxes = false, DrawGrid = false, DrawGridAxes = false };
            using var image = capture.CaptureToBitmap(view) ?? throw new Exception("Capture failed.");
            image.Save(Path.Combine(output, name), ImageFormat.Png);
        }
        // One native capture per host avoids Rhino's repeated offscreen capture failure.
        if (Environment.GetCommandLineArgs().Contains("--texture-detail"))
            Save("texture-detail.png", new BoundingBox(width * .55, 0, 0, width, height * .45, .1));
        else Save("texture-full.png", new BoundingBox(0, 0, 0, width, height, .1));
        if (conduit.Draws == 0) throw new Exception("Texture was not drawn in the viewport.");
    }

    sealed class TextureConduit : DisplayConduit, IDisposable
    {
        readonly object texture;
        readonly MethodInfo draw;
        public int Draws;
        internal TextureConduit(object value)
        {
            texture = value;
            draw = value.GetType().GetMethod("Draw", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        protected override void PostDrawObjects(DrawEventArgs e)
        {
            draw.Invoke(texture, new object[] { e.Display }); Draws++;
        }
        public void Dispose() { Enabled = false; }
    }
}
