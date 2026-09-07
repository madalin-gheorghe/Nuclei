param(
    [string]$WorkspaceRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Keep the published icon artwork and its alpha channel. Only replace the glyphs.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class NucleiComponentIcons
{
    static readonly Color Ink = Color.FromArgb(28, 0, 40);
    static PointF P(float x, float y) { return new PointF(x, y); }
    static Color Pink(int alpha) { return Color.FromArgb(alpha, 255, 0, 255); }

    static Bitmap Load(string folder, string name)
    {
        using (var source = new Bitmap(Path.Combine(folder, name + ".png")))
            return source.Clone(new Rectangle(0, 0, 24, 24), PixelFormat.Format32bppArgb);
    }

    static Bitmap Render(Action<Graphics> draw)
    {
        using (var large = new Bitmap(192, 192, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(large))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(8, 8);
                draw(g);
            }
            var result = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(large, new Rectangle(0, 0, 24, 24));
            }
            return result;
        }
    }

    // Preserve exact RGBA, including RGB in transparent pixels, outside the edit.
    static void RestoreOutside(Bitmap result, Bitmap original, Rectangle edit)
    {
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
                if (!edit.Contains(x, y)) result.SetPixel(x, y, original.GetPixel(x, y));
    }

    static Bitmap TrailPreview(string folder)
    {
        using (var original = Load(folder, "PreviewParticles"))
        using (var settings = Load(folder, "ParticleTrailSettings"))
        {
            var result = (Bitmap)original.Clone();
            // Recover the pupil-free inner eye from its symmetric pink gradient.
            // All outer eye pixels remain those of PreviewParticles.png.
            for (int y = 9; y <= 14; y++)
                for (int x = 9; x <= 14; x++)
                {
                    double radius = Math.Sqrt((x - 12.0) * (x - 12.0) + (y - 12.0) * (y - 12.0));
                    int alpha = (int)Math.Round(Math.Max(0, 2.4 * radius * radius - 0.5));
                    result.SetPixel(x, y, Pink(Math.Min(alpha, 64)));
                }
            // Reuse the rendered settings dots and their original magenta halos.
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(settings, new RectangleF(6.2f, 6.5f, 11.2f, 11.2f),
                    new RectangleF(2, 2, 20, 20), GraphicsUnit.Pixel);
            }
            // Restore clarity lost when the two smaller source cores are reduced.
            // Their positions, growing sizes, purple color and halos stay intact.
            using (var cores = Render(g =>
            {
                using (var small = new SolidBrush(Color.FromArgb(180, Ink)))
                    g.FillEllipse(small, 8.66f - 0.58f, 15.19f - 0.58f, 1.16f, 1.16f);
                using (var medium = new SolidBrush(Color.FromArgb(170, Ink)))
                    g.FillEllipse(medium, 10.95f - 0.72f, 12.86f - 0.72f, 1.44f, 1.44f);
            }))
            using (var g = Graphics.FromImage(result))
                g.DrawImage(cores, new Rectangle(0, 0, 24, 24), new Rectangle(0, 0, 24, 24), GraphicsUnit.Pixel);
            RestoreOutside(result, original, new Rectangle(7, 7, 11, 11));
            return result;
        }
    }

    static Bitmap NeighborCount(string folder)
    {
        using (var original = Load(folder, "ParticleVectors"))
        using (var trails = Load(folder, "ParticleTrails"))
        using (var neighbors = Load(folder, "ParticleNeighbours"))
        using (var glyph = Render(g =>
        {
            // Preserve the first N's proportions and rounded ends. The original
            // PSD circle spans 12..130 on a 150px canvas: its 24px center is 11.36.
            const float center = 11.36f;
            const float scale = 0.72f;
            using (var pen = new Pen(Color.FromArgb(195, Ink), 1.8f * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                // Fit the centered letter inside the disc, clear of the arrow.
                g.DrawLines(pen, new[] {
                    P(center - 3.25f * scale, center + 4 * scale),
                    P(center - 3.25f * scale, center - 4 * scale),
                    P(center + 3.25f * scale, center + 4 * scale),
                    P(center + 3.25f * scale, center - 4 * scale)
                });
            }
        }))
        {
            var result = (Bitmap)original.Clone();
            var edit = new Rectangle(6, 5, 11, 12);
            for (int y = edit.Top; y < edit.Bottom; y++)
                for (int x = edit.Left; x < edit.Right; x++)
                {
                    int alpha = 0;
                    foreach (var source in new[] { original, trails, neighbors })
                    {
                        Color p = source.GetPixel(x, y);
                        if (p.R == 255 && p.G == 0 && p.B == 255) alpha = Math.Max(alpha, p.A);
                    }
                    if (alpha < 16) alpha = 22;
                    // Match the original glyphs' transparent clearance.
                    double halo = 0;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= 24 || yy >= 24) continue;
                            halo = Math.Max(halo, glyph.GetPixel(xx, yy).A / 255.0 * Math.Exp(-(dx * dx + dy * dy) / 2.0));
                        }
                    result.SetPixel(x, y, Pink((int)Math.Round(alpha * (1 - halo))));
                }
            using (var g = Graphics.FromImage(result))
                g.DrawImage(glyph, new Rectangle(0, 0, 24, 24), new Rectangle(0, 0, 24, 24), GraphicsUnit.Pixel);
            RestoreOutside(result, original, edit);
            RestoreArrow(result, original);
            return result;
        }
    }

    static Bitmap DendroVolume(string folder)
    {
        using (var original = Load(folder, "VoxelBox"))
        using (var divisions = Render(g =>
        {
            // Keep the original box silhouette, square background and arrow.
            using (var pen = new Pen(Color.FromArgb(195, Ink), 0.95f))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new[] { P(5.8f, 8.8f), P(11.2f, 11.8f), P(16.2f, 8.5f) });
                g.DrawLine(pen, 11.2f, 11.8f, 11.2f, 17.1f);
            }
        }))
        {
            var result = (Bitmap)original.Clone();
            using (var g = Graphics.FromImage(result))
                g.DrawImage(divisions, new Rectangle(0, 0, 24, 24), new Rectangle(0, 0, 24, 24), GraphicsUnit.Pixel);
            RestoreOutside(result, original, new Rectangle(5, 8, 12, 10));
            RestoreArrow(result, original);
            return result;
        }
    }

    static void RestoreArrow(Bitmap result, Bitmap original)
    {
        // Include the antialiased start and glow above the visible arrow body.
        for (int y = 15; y < 24; y++)
            for (int x = 12; x < 24; x++) result.SetPixel(x, y, original.GetPixel(x, y));
    }

    public static void Generate(string workspace, string preview)
    {
        string originalFolder = Path.Combine(workspace, "Nuclei-v4", "Nuclei4", "Resources");
        using (var trail = TrailPreview(originalFolder))
        using (var count = NeighborCount(originalFolder))
        using (var volume = DendroVolume(originalFolder))
        {
            foreach (var version in new[] { 3, 4 })
            {
                string folder = Path.Combine(workspace, "Nuclei-v" + version, "Nuclei" + version, "Resources");
                count.Save(Path.Combine(folder, "ParticleNeighborCount.png"), ImageFormat.Png);
                volume.Save(Path.Combine(folder, "NucleiToDendroVolume.png"), ImageFormat.Png);
                if (version == 4) trail.Save(Path.Combine(folder, "PreviewParticleTrails.png"), ImageFormat.Png);
            }
            if (!String.IsNullOrEmpty(preview)) Preview(preview, originalFolder, trail, count, volume);
        }
    }

    static void Preview(string path, string folder, params Bitmap[] icons)
    {
        using (var sheet = new Bitmap(780, 330))
        using (var g = Graphics.FromImage(sheet))
        using (var label = new Font("Segoe UI", 11))
        using (var small = new Font("Segoe UI", 9))
        using (var format = new StringFormat { Alignment = StringAlignment.Center })
        {
            g.Clear(Color.FromArgb(248, 248, 248));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            string[] titles = { "Particle Trail Preview", "Particle Neighbor Count", "Nuclei to Dendro Volume" };
            for (int i = 0; i < icons.Length; i++)
            {
                int x = i * 260;
                g.DrawString(titles[i], label, Brushes.Black, new RectangleF(x, 16, 260, 24), format);
                for (int yy = 56; yy < 216; yy += 8)
                    for (int xx = x + 50; xx < x + 210; xx += 8)
                        g.FillRectangle((((xx - x - 50) / 8 + (yy - 56) / 8) % 2) == 0
                            ? Brushes.White : Brushes.LightGray, xx, yy, 8, 8);
                g.DrawImage(icons[i], new Rectangle(x + 58, 64, 144, 144));
                for (int yy = 240; yy < 280; yy += 5)
                    for (int xx = x + 110; xx < x + 150; xx += 5)
                        g.FillRectangle((((xx - x - 110) / 5 + (yy - 240) / 5) % 2) == 0
                            ? Brushes.White : Brushes.LightGray, xx, yy, 5, 5);
                g.DrawImage(icons[i], new Rectangle(x + 118, 248, 24, 24));
            }
            g.DrawString("Checkerboard shows transparency  |  Actual 24 x 24 size below", small, Brushes.DimGray, new RectangleF(0, 301, 780, 20), format);
            sheet.Save(path, ImageFormat.Png);
        }
    }
}
'@

[NucleiComponentIcons]::Generate($WorkspaceRoot, $PreviewPath)
