using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Nuclei4
{
    public sealed class Voxel_ImageMapper : GH_Component
    {
        byte[] embeddedImage;
        string imageName, imageError;
        int[] pixels;
        int imageWidth, imageHeight;
        Bitmap thumbnail;
        GpuImageMapper gpu;
        float[] mappedValues;
        int cachedX, cachedY, cachedZ;
        double cachedStart, cachedEnd;
        bool cachedClamp;
        VoxelGridData cachedInput, cachedOutput;
        int cachedType = -1;

        public Voxel_ImageMapper() : base("Image Mapper for Voxels", "Image Mapper",
            "Map an embedded image to a 2D voxel field on the GPU. Black maps to targetStart and white to targetEnd; color uses grayscale luminance.",
            "Nuclei4", " Environment") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Voxels", "voxels", "A 2D voxel field in XY, XZ or YZ. The entire grid domain maps to 0–1 on both axes.", GH_ParamAccess.item);
            p.AddIntegerParameter("Type", "type", "Voxel property to define, as in Define Voxel Values.", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Target Start", "targetStart", "Value assigned to black. Can be higher than targetEnd to reverse the mapping.", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Target End", "targetEnd", "Value assigned to white. Equal endpoints create a constant map. Density types retain the limits of Define Voxel Values.", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p) =>
            p.AddGenericParameter("Output Voxels", "voxels", "Voxels with the selected property defined by the remapped image.", GH_ParamAccess.item);

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Message = string.Empty;
            if (!VoxelFieldAccess.TryGet(da, 0, Globals.voxelSize, out var field)) return;
            var data = field.Data;
            if (data.ResX > 1 && data.ResY > 1 && data.ResZ > 1)
            {
                Report("Only 2D Voxel Field Allowed", GH_RuntimeMessageLevel.Error);
                return;
            }
            if (data.Count == 0 || data.ActiveCount == 0)
            {
                Report("Voxel field is empty", GH_RuntimeMessageLevel.Warning);
                return;
            }
            int type = 0;
            double start = 0, end = 1;
            if (!da.GetData(1, ref type) || !da.GetData(2, ref start) || !da.GetData(3, ref end)) return;
            if ((type < 0 || type > 6) && type != 13)
            {
                Report("Choose a valid voxel value Type", GH_RuntimeMessageLevel.Error);
                return;
            }
            if (!Finite(start) || !Finite(end))
            {
                Report("Target values must be finite and within float range", GH_RuntimeMessageLevel.Error);
                return;
            }
            if (pixels == null)
            {
                Report(imageError ?? "Double-click to choose an image", GH_RuntimeMessageLevel.Warning);
                return;
            }
            int horizontal = data.ResZ == 1 || data.ResY == 1 ? data.ResX : data.ResY;
            int vertical = data.ResZ == 1 ? data.ResY : data.ResZ;
            bool stretched = (long)horizontal * imageHeight != (long)vertical * imageWidth;
            bool clamp = type == 0 || type == 1;
            try
            {
                if (mappedValues == null || cachedX != data.ResX || cachedY != data.ResY || cachedZ != data.ResZ ||
                    cachedStart != start || cachedEnd != end || cachedClamp != clamp)
                {
                    if (gpu == null) gpu = new GpuImageMapper();
                    mappedValues = gpu.Map(pixels, imageWidth, imageHeight, data.ResX, data.ResY, data.ResZ, (float)start, (float)end, clamp);
                    cachedX = data.ResX; cachedY = data.ResY; cachedZ = data.ResZ;
                    cachedStart = start; cachedEnd = end; cachedClamp = clamp;
                    cachedInput = null; cachedOutput = null;
                }
                if (!ReferenceEquals(cachedInput, data) || cachedType != type)
                {
                    float[] values = mappedValues;
                    if (!data.AllVoxelsActive)
                    {
                        // Keep the complete domain for coordinates; only modify selected voxels.
                        values = data.GetMap((VoxelScalarField)type).ToDenseArray(data.Count);
                        for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
                        {
                            int i = data.ActiveFlatIndexAt(ordinal);
                            values[i] = mappedValues[i];
                        }
                    }
                    cachedOutput = data.WithScalarMapValues(type, values);
                    cachedInput = data; cachedType = type;
                }
                da.SetData(0, field.WithData(cachedOutput));
                Message = stretched ? "Mapping stretched: different aspect ratios" : "GPU Image Mapper";
                if (gpu.SoftwareFallback)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Hardware GPU unavailable; using software image mapping.");
                    Message = stretched ? "Software mapping\nStretched: different aspect ratios" : "Software Image Mapper";
                }
            }
            catch (Exception error)
            {
                ReleaseMapping();
                Report("Image mapping failed: " + error.Message, GH_RuntimeMessageLevel.Error);
            }
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= float.MaxValue;
        void Report(string message, GH_RuntimeMessageLevel level) { Message = message; AddRuntimeMessage(level, message); }

        internal void ChooseImage()
        {
            using (var dialog = new OpenFileDialog { Title = "Choose voxel map image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*", CheckFileExists = true })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                try
                {
                    byte[] bytes = File.ReadAllBytes(dialog.FileName);
                    // Decode first, so a failed import leaves the previous image intact.
                    using (var decoded = DecodedImage.Load(bytes))
                    {
                        RecordUndoEvent("Choose voxel map image");
                        ApplyImage(bytes, Path.GetFileName(dialog.FileName), decoded);
                    }
                    ExpireSolution(true);
                }
                catch (Exception error) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cannot load image: " + error.Message); }
            }
        }

        void ApplyImage(byte[] bytes, string name, DecodedImage decoded)
        {
            InvalidateMapping();
            thumbnail?.Dispose();
            embeddedImage = bytes; imageName = name; imageError = null;
            pixels = decoded.Pixels; imageWidth = decoded.Width; imageHeight = decoded.Height;
            thumbnail = decoded.Thumbnail; decoded.Thumbnail = null;
            Attributes?.ExpireLayout();
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Choose image…", (s, e) => ChooseImage());
            Menu_AppendItem(menu, "Clear image", (s, e) =>
            {
                RecordUndoEvent("Clear voxel map image");
                ReleaseImage(); embeddedImage = null; imageName = null; imageError = null;
                ExpireSolution(true);
            }, embeddedImage != null);
        }

        public override bool Write(GH_IWriter writer)
        {
            if (embeddedImage != null)
            {
                writer.SetByteArray("EmbeddedMapImage", embeddedImage);
                writer.SetString("MapImageName", imageName ?? "Image");
            }
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            ReleaseImage(); embeddedImage = null; imageName = null; imageError = null;
            bool result = base.Read(reader);
            if (reader.ItemExists("EmbeddedMapImage"))
            {
                embeddedImage = reader.GetByteArray("EmbeddedMapImage");
                reader.TryGetString("MapImageName", ref imageName);
                RestoreImage();
            }
            return result;
        }

        void RestoreImage()
        {
            if (embeddedImage == null || pixels != null) return;
            try { using (var decoded = DecodedImage.Load(embeddedImage)) ApplyImage(embeddedImage, imageName, decoded); }
            catch (Exception error) { imageError = "Cannot read embedded image: " + error.Message; }
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            RestoreImage();
            document.SolutionStart -= PrepareValueLists;
            document.SolutionStart += PrepareValueLists;
        }
        void PrepareValueLists(object sender, GH_SolutionEventArgs args) => VoxelTypeChoices.Ensure(this, 6, 390);
        public override void RemovedFromDocument(GH_Document document)
        {
            document.SolutionStart -= PrepareValueLists;
            ReleaseImage();
            base.RemovedFromDocument(document);
        }
        void ReleaseMapping()
        {
            gpu?.Dispose(); gpu = null;
            InvalidateMapping();
        }
        void InvalidateMapping() { mappedValues = null; cachedInput = null; cachedOutput = null; }
        void ReleaseImage()
        {
            ReleaseMapping(); thumbnail?.Dispose(); thumbnail = null; pixels = null;
        }

        public override void CreateAttributes() => m_attributes = new ImageAttributes(this);
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override Bitmap Icon => Properties.Resources.VoxelImageMapper;
        public override Guid ComponentGuid => new Guid("d33e509c-f8ae-41b3-83e3-40e33685396f");

        // Decode once per import/archive restore. Canvas rendering uses a small thumbnail.
        sealed class DecodedImage : IDisposable
        {
            internal int[] Pixels;
            internal int Width, Height;
            internal Bitmap Thumbnail;
            internal static DecodedImage Load(byte[] bytes)
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var source = Image.FromStream(stream, true, true))
                using (var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                    }
                    var result = new DecodedImage { Width = bitmap.Width, Height = bitmap.Height, Pixels = new int[checked(bitmap.Width * bitmap.Height)] };
                    var bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int y = 0; y < bitmap.Height; y++)
                            Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), result.Pixels, y * bitmap.Width, bitmap.Width);
                    }
                    finally { bitmap.UnlockBits(bits); }
                    double scale = Math.Min(1.0, 480.0 / Math.Max(bitmap.Width, bitmap.Height));
                    result.Thumbnail = new Bitmap(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
                    using (var g = Graphics.FromImage(result.Thumbnail))
                    using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        g.Clear(Color.White);
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(bitmap, new Rectangle(0, 0, result.Thumbnail.Width, result.Thumbnail.Height),
                            0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
                    }
                    return result;
                }
            }
            public void Dispose() { Thumbnail?.Dispose(); }
        }

        sealed class ImageAttributes : GH_ComponentAttributes
        {
            const float DefaultWidth = 220, DefaultHeight = 200;
            const float MinimumWidth = 180, MinimumHeight = 160, MaximumSize = 4096;
            readonly Voxel_ImageMapper component;
            float previewWidth = DefaultWidth, previewHeight = DefaultHeight;
            bool resizing, resizeUndoRecorded, overResizeGrip;
            PointF resizeStart;
            SizeF resizeStartSize;
            public ImageAttributes(Voxel_ImageMapper owner) : base(owner) { component = owner; }

            // Keep preview dimensions in the layout archive, alongside the pivot.
            public override bool Write(GH_IWriter writer)
            {
                writer.SetSingle("ImageMapperWidth", previewWidth);
                writer.SetSingle("ImageMapperHeight", previewHeight);
                return base.Write(writer);
            }
            public override bool Read(GH_IReader reader)
            {
                bool result = base.Read(reader);
                float width = DefaultWidth, height = DefaultHeight;
                reader.TryGetSingle("ImageMapperWidth", ref width);
                reader.TryGetSingle("ImageMapperHeight", ref height);
                previewWidth = ClampSize(width, MinimumWidth, DefaultWidth);
                previewHeight = ClampSize(height, MinimumHeight, DefaultHeight);
                resizing = false;
                ExpireLayout();
                return result;
            }
            static float ClampSize(float value, float minimum, float fallback) =>
                float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(minimum, Math.Min(MaximumSize, value));

            protected override void Layout()
            {
                base.Layout();
                // Keep the original upper-left anchor fixed while the grip moves.
                m_innerBounds = new RectangleF(Pivot.X - DefaultWidth / 2, Pivot.Y - DefaultHeight / 2, previewWidth, previewHeight);
                LayoutInputParams(Owner, m_innerBounds);
                LayoutOutputParams(Owner, m_innerBounds);
                Bounds = LayoutBounds(Owner, m_innerBounds);
            }

            RectangleF ResizeGrip(GH_Canvas canvas)
            {
                // Keep the corner easy to catch when zoomed out, without letting
                // its hit region cover the image or the output socket.
                float size = Math.Min(48, Math.Max(30, 24 / Math.Max(0.1f, canvas.Viewport.Zoom)));
                return new RectangleF(Bounds.Right - size, Bounds.Bottom - size, size, size);
            }

            public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                if (e.Button == MouseButtons.Left && ResizeGrip(sender).Contains(e.CanvasLocation))
                {
                    resizing = true; resizeUndoRecorded = false;
                    resizeStart = e.CanvasLocation;
                    resizeStartSize = new SizeF(previewWidth, previewHeight);
                    sender.Cursor = Cursors.SizeNWSE;
                    return GH_ObjectResponse.Capture;
                }
                return base.RespondToMouseDown(sender, e);
            }

            public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                if (resizing)
                {
                    if (e.Button != MouseButtons.Left)
                    {
                        resizing = false;
                        sender.Cursor = Cursors.Default;
                        return GH_ObjectResponse.Release;
                    }
                    ResizeTo(sender, e.CanvasLocation);
                    return GH_ObjectResponse.Handled;
                }
                if (e.Button == MouseButtons.None && ResizeGrip(sender).Contains(e.CanvasLocation))
                {
                    overResizeGrip = true;
                    sender.Cursor = Cursors.SizeNWSE;
                    return GH_ObjectResponse.Handled;
                }
                if (overResizeGrip) { overResizeGrip = false; sender.Cursor = Cursors.Default; }
                return base.RespondToMouseMove(sender, e);
            }

            void ResizeTo(GH_Canvas canvas, PointF location)
            {
                float width = ClampSize(resizeStartSize.Width + location.X - resizeStart.X, MinimumWidth, DefaultWidth);
                float height = ClampSize(resizeStartSize.Height + location.Y - resizeStart.Y, MinimumHeight, DefaultHeight);
                if (width == previewWidth && height == previewHeight) return;
                var document = component.OnPingDocument();
                if (!resizeUndoRecorded)
                {
                    document?.UndoUtil.RecordEvent("Resize Image Mapper", new ResizeUndoAction(component, resizeStartSize));
                    resizeUndoRecorded = true;
                }
                previewWidth = width; previewHeight = height;
                ExpireLayout();
                PerformLayout();
                document?.Modified();
                canvas.Invalidate();
            }

            public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                if (resizing)
                {
                    ResizeTo(sender, e.CanvasLocation);
                    resizing = false; overResizeGrip = false;
                    sender.Cursor = Cursors.Default;
                    return GH_ObjectResponse.Release;
                }
                return base.RespondToMouseUp(sender, e);
            }

            // The standard layout action only restores Bounds/Pivot. Our content
            // dimensions need their own small undo record, with no image or GPU data.
            sealed class ResizeUndoAction : Grasshopper.Kernel.Undo.GH_ObjectUndoAction
            {
                SizeF size;
                internal ResizeUndoAction(Voxel_ImageMapper owner, SizeF previousSize) : base(owner.InstanceGuid) { size = previousSize; }
                public override bool ExpiresSolution => false;
                public override bool ExpiresDisplay => true;
                protected override void Object_Undo(GH_Document document, IGH_DocumentObject obj)
                {
                    var attributes = obj.Attributes as ImageAttributes;
                    if (attributes == null) return;
                    var current = new SizeF(attributes.previewWidth, attributes.previewHeight);
                    attributes.previewWidth = size.Width; attributes.previewHeight = size.Height;
                    attributes.ExpireLayout(); attributes.PerformLayout();
                    size = current;
                    document.Modified();
                }
                protected override void Object_Redo(GH_Document document, IGH_DocumentObject obj) => Object_Undo(document, obj);
            }
            protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
            {
                base.Render(canvas, graphics, channel);
                if (channel != GH_CanvasChannel.Objects) return;
                var box = m_innerBounds;
                using (var background = new SolidBrush(Color.FromArgb(244, 244, 244)))
                    graphics.FillRectangle(background, box.X + 2, box.Y + 2, box.Width - 4, box.Height - 4);
                var area = new RectangleF(box.X + 10, box.Y + 29, box.Width - 20, box.Height - 54);
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                {
                    graphics.DrawString("Image Mapper", GH_FontServer.StandardBold, Brushes.Black,
                        new RectangleF(box.X + 8, box.Y + 5, box.Width - 16, 20), format);
                    graphics.FillRectangle(Brushes.White, area);
                    if (component.thumbnail != null)
                    {
                        float scale = Math.Min(area.Width / component.thumbnail.Width, area.Height / component.thumbnail.Height);
                        var size = new SizeF(component.thumbnail.Width * scale, component.thumbnail.Height * scale);
                        var target = new RectangleF(area.X + (area.Width-size.Width)/2, area.Y + (area.Height-size.Height)/2, size.Width, size.Height);
                        using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                        {
                            attributes.SetWrapMode(WrapMode.TileFlipXY);
                            graphics.DrawImage(component.thumbnail, Rectangle.Round(target), 0, 0,
                                component.thumbnail.Width, component.thumbnail.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    else graphics.DrawString("Double-click to\nchoose an image", GH_FontServer.Standard, Brushes.Gray, area, format);
                    graphics.DrawRectangle(Pens.Gray, area.X, area.Y, area.Width, area.Height);
                    string caption = component.pixels == null ? "2D voxel image map" : component.imageName + "  ·  " + component.imageWidth + " × " + component.imageHeight;
                    graphics.DrawString(caption, GH_FontServer.Small, Brushes.Black,
                        new RectangleF(box.X + 8, box.Bottom - 22, box.Width - 16, 17), format);
                }
                using (var gripPen = new Pen(Color.FromArgb(115, 115, 115)))
                    for (int length = 5; length <= 15; length += 5)
                        graphics.DrawLine(gripPen, Bounds.Right - 4 - length, Bounds.Bottom - 4,
                            Bounds.Right - 4, Bounds.Bottom - 4 - length);
            }
            public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                if (ResizeGrip(sender).Contains(e.CanvasLocation)) return GH_ObjectResponse.Handled;
                if (e.Button == MouseButtons.Left && m_innerBounds.Contains(e.CanvasLocation))
                {
                    component.ChooseImage();
                    return GH_ObjectResponse.Handled;
                }
                return base.RespondToMouseDoubleClick(sender, e);
            }
        }
    }
}
