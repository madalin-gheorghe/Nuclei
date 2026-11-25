using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

internal static class CaptureStage
{
    internal static void Run(Assembly main, object obstacles, object food, string output)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type textureType = main.GetType("Nuclei4.PlanarVoxelTexture", true);
        using var texture = (IDisposable)Activator.CreateInstance(textureType, all, null,
            new object[] { 20, 20, (Func<int, int, Color>)((u, v) => Color.Black),
                (Func<double, double, Point3d>)((u, v) => new Point3d(u, v, .0001)) }, null);
        using var doc = RhinoDoc.Create(null);
        var view = doc.Views.Add("Isolated obstacle overlay", DefinedViewportProjection.Top, new Rectangle(0, 0, 1000, 1000), false)
            ?? throw new InvalidOperationException("Could not create capture view.");
        var vp = view.ActiveViewport;
        vp.DisplayMode = DisplayModeDescription.FindByName("Wireframe");
        vp.ConstructionGridVisible = false; vp.ConstructionAxesVisible = false;
        using var conduit = new OverlayConduit(texture, obstacles, food) { Enabled = true };
        vp.ZoomBoundingBox(new BoundingBox(0, 0, -3, 20, 20, 1));
        view.Redraw(); RhinoApp.Wait();
        using var bitmap = new ViewCapture { Width = 1000, Height = 1000, DrawAxes = false, DrawGrid = false, DrawGridAxes = false }
            .CaptureToBitmap(view) ?? throw new InvalidOperationException("Viewport capture failed.");
        bitmap.Save(Path.Combine(output, "cpu-planar-food-obstacles.png"), ImageFormat.Png);
        int purple = 0, black = 0, white = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            var c = bitmap.GetPixel(x, y);
            if (Math.Abs(c.R - 29) <= 3 && Math.Abs(c.G - 19) <= 3 && Math.Abs(c.B - 53) <= 3) purple++;
            if (c.R <= 3 && c.G <= 3 && c.B <= 3) black++;
            if (x > 400 && x < 600 && y > 400 && y < 600 && c.R >= 252 && c.G >= 252 && c.B >= 252) white++;
        }
        if (conduit.Draws == 0 || purple < 10000 || black < 10000 || white < 10000)
            throw new InvalidOperationException($"Food overlay capture failed: draw calls={conduit.Draws}, foreground calls={conduit.ForegroundDraws}, purple={purple}, black={black}, white={white}.");
        Console.WriteLine($"PASS viewport capture: {white} white food pixels over {purple} purple obstacle pixels and {black} black field pixels.");
        File.WriteAllText(Path.Combine(output, "capture-counts.txt"), $"DrawCalls={conduit.Draws}\nPurplePixels={purple}\nBlackPixels={black}\nWhiteFoodPixels={white}\n");
    }

    sealed class OverlayConduit : DisplayConduit, IDisposable
    {
        readonly object texture, obstacles, food;
        readonly MethodInfo drawTexture, drawObstacles, drawFood;
        internal int Draws, ForegroundDraws;
        internal OverlayConduit(object texture, object obstacles, object food)
        {
            this.texture = texture; this.obstacles = obstacles; this.food = food;
            drawTexture = texture.GetType().GetMethod("Draw", BindingFlags.NonPublic | BindingFlags.Instance);
            drawObstacles = obstacles.GetType().GetMethod("Draw", BindingFlags.NonPublic | BindingFlags.Instance);
            drawFood = food.GetType().GetMethod("Draw", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        protected override void PostDrawObjects(DrawEventArgs e)
        {
            drawTexture.Invoke(texture, new object[] { e.Display });
            drawObstacles.Invoke(obstacles, new object[] { e.Display });
            Draws++;
        }
        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            e.IncludeBoundingBox(new BoundingBox(0, 0, -3, 20, 20, 1));
        }
        protected override void DrawForeground(DrawEventArgs e)
        {
            drawFood.Invoke(food, new object[] { e.Display });
            ForegroundDraws++;
        }
        public void Dispose() { Enabled = false; }
    }
}
