using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Rhino.Display;
using Rhino.Geometry;

namespace Nuclei4
{
    // One texel per voxel. Small tiles avoid GPU texture-size limits; the extra
    // border texels allow filtering across tile boundaries without visible seams.
    internal sealed class PlanarVoxelTexture : IDisposable
    {
        internal const int TileSize = 1022; // Including borders, at most 1024 x 1024.
        internal readonly List<Tile> Tiles = new List<Tile>();
        internal readonly int Width;
        internal readonly int Height;
        static readonly Type CacheType = typeof(DisplayPipeline).Assembly.GetType("Rhino.Display.TextureCache");
        static readonly MethodInfo CacheSet = CacheType?.GetMethod("Set", new[] { typeof(string), typeof(Bitmap) });
        static readonly MethodInfo CacheRemove = CacheType?.GetMethod("Remove", new[] { typeof(string) });

        internal PlanarVoxelTexture(int width, int height, Func<int, int, Color> colorAt,
            Func<double, double, Point3d> pointAt)
        {
            Width = width;
            Height = height;
            try
            {
                for (int u = 0; u < width; u += TileSize)
                for (int v = 0; v < height; v += TileSize)
                {
                    var tile = new Tile { U = u, V = v, Width = Math.Min(TileSize, width - u), Height = Math.Min(TileSize, height - v) };
                    Tiles.Add(tile);
                    int tw = tile.Width + 2, th = tile.Height + 2;
                    using (var bitmap = new Bitmap(tw, th, PixelFormat.Format32bppArgb))
                    {
                        var row = new int[tw];
                        var bits = bitmap.LockBits(new Rectangle(0, 0, tw, th), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            for (int y = 0; y < th; y++)
                            {
                                int sourceV = Math.Max(0, Math.Min(height - 1, v + tile.Height - y));
                                for (int x = 0; x < tw; x++)
                                    row[x] = colorAt(Math.Max(0, Math.Min(width - 1, u + x - 1)), sourceV).ToArgb();
                                Marshal.Copy(row, 0, IntPtr.Add(bits.Scan0, y * bits.Stride), tw);
                            }
                        }
                        finally { bitmap.UnlockBits(bits); }

                        // Rhino 9 supports memory-backed textures; Rhino 8 uses a disposable PNG.
                        tile.InMemory = CacheSet != null && CacheRemove != null;
                        tile.TextureName = tile.InMemory ? "Nuclei4:VoxelPreview:" + Guid.NewGuid().ToString("N")
                            : Path.Combine(Path.GetTempPath(), "Nuclei4-VoxelPreview-" + Guid.NewGuid().ToString("N") + ".png");
                        if (tile.InMemory)
                        {
                            if (!(bool)CacheSet.Invoke(null, new object[] { tile.TextureName, bitmap }))
                                throw new InvalidOperationException("Could not upload the 2D voxel preview texture.");
                        }
                        else bitmap.Save(tile.TextureName, ImageFormat.Png);
                    }

                    using (var material = new Rhino.DocObjects.Material())
                    {
                        material.DiffuseColor = Color.White;
                        material.DisableLighting = true;
                        tile.Material = new DisplayMaterial(material);
                    }
                    tile.Material.IsTwoSided = false;
                    tile.Material.SetBitmapTexture(tile.TextureName, true);
                    var mesh = tile.Rectangle = new Mesh();
                    mesh.Vertices.Add(pointAt(u, v));
                    mesh.Vertices.Add(pointAt(u + tile.Width, v));
                    mesh.Vertices.Add(pointAt(u + tile.Width, v + tile.Height));
                    mesh.Vertices.Add(pointAt(u, v + tile.Height));
                    mesh.TextureCoordinates.Add(1f / tw, 1f / th);
                    mesh.TextureCoordinates.Add((tw - 1f) / tw, 1f / th);
                    mesh.TextureCoordinates.Add((tw - 1f) / tw, (th - 1f) / th);
                    mesh.TextureCoordinates.Add(1f / tw, (th - 1f) / th);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    mesh.Normals.ComputeNormals();
                }
            }
            catch { Dispose(); throw; }
        }

        internal void Draw(DisplayPipeline display)
        {
            foreach (var tile in Tiles) display.DrawMeshShaded(tile.Rectangle, tile.Material);
        }

        public void Dispose()
        {
            foreach (var tile in Tiles)
            {
                tile.Rectangle?.Dispose();
                tile.Material?.Dispose();
                if (tile.TextureName == null) continue;
                if (tile.InMemory) CacheRemove.Invoke(null, new object[] { tile.TextureName });
                else
                {
                    try { File.Delete(tile.TextureName); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            Tiles.Clear();
        }

        internal sealed class Tile
        {
            internal int U, V, Width, Height;
            internal string TextureName;
            internal bool InMemory;
            internal Mesh Rectangle;
            internal DisplayMaterial Material;
        }
    }
}
