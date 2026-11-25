using System.Drawing;
using System.Reflection;
using System.Text.Json;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

// Diagnose loss in the mapper separately from the viewport mesh sampling budget.
internal static class ImageFidelityChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Assembly assembly, string imagePath, string output)
    {
        var mapperType = assembly.GetType("Nuclei4.Voxel_ImageMapper", true);
        var gridType = assembly.GetType("Nuclei4.VoxelGridData", true);
        var fieldType = assembly.GetType("Nuclei4.VoxelField", true);
        using var document = new GH_Document();
        var mapper = (GH_Component)Activator.CreateInstance(mapperType);
        mapper.CreateAttributes(); document.AddObject(mapper, false);
        mapperType.GetField("embeddedImage", Private).SetValue(mapper, File.ReadAllBytes(imagePath));
        mapperType.GetField("imageName", Private).SetValue(mapper, Path.GetFileName(imagePath));
        mapperType.GetMethod("RestoreImage", Private).Invoke(mapper, null);
        int width = (int)mapperType.GetField("imageWidth", Private).GetValue(mapper);
        int height = (int)mapperType.GetField("imageHeight", Private).GetValue(mapper);
        int[] pixels = (int[])mapperType.GetField("pixels", Private).GetValue(mapper);
        var thumbnail = (Bitmap)mapperType.GetField("thumbnail", Private).GetValue(mapper);
        if (pixels == null || pixels.Length != width * height) throw new Exception("Image decode failed.");

        var grid = gridType.GetMethod("CreateFullDomain").Invoke(null, new object[] { width, height, 1, 1.0 });
        var field = Activator.CreateInstance(fieldType, Private, null, new object[] { grid, null, null }, null);
        ((Param_GenericObject)mapper.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(field));
        var typeSource = new Param_Integer(); typeSource.CreateAttributes(); document.AddObject(typeSource, false);
        typeSource.PersistentData.Append(new GH_Integer(2)); mapper.Params.Input[1].AddSource(typeSource);

        var preview = (GH_Component)Activator.CreateInstance(assembly.GetType("Nuclei4.Preview_Voxel", true));
        preview.CreateAttributes(); document.AddObject(preview, false);
        preview.Params.Input[0].AddSource(mapper.Params.Output[0]);
        preview.Params.Input[1].AddSource(typeSource);
        document.Enabled = true; GH_Document.EnableSolutions = true;
        document.NewSolution(false);
        if (mapper.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any() || preview.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any())
            throw new Exception(string.Join("; ", mapper.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(preview.RuntimeMessages(GH_RuntimeMessageLevel.Error))));
        float[] values = (float[])mapperType.GetField("mappedValues", Private).GetValue(mapper);
        double maximumError = 0, sumError = 0;
        int changedAboveHalfCode = 0;
        for (int u = 0; u < width; u++)
        for (int v = 0; v < height; v++)
        {
            uint bgra = unchecked((uint)pixels[(height - 1 - v) * width + u]);
            double gray = (.2126 * ((bgra >> 16) & 255) + .7152 * ((bgra >> 8) & 255) + .0722 * (bgra & 255)) / 255;
            double alpha = (bgra >> 24) / 255.0;
            double expected = 1 - alpha + alpha * gray;
            double error = Math.Abs(values[u * height + v] - expected);
            maximumError = Math.Max(maximumError, error); sumError += error;
            if (error > .5 / 255) changedAboveHalfCode++;
        }
        var texture = preview.GetType().GetField("planarPreviewTexture", Private).GetValue(preview);
        if (texture == null) throw new Exception("Planar preview texture was not produced.");
        int sampledX = (int)texture.GetType().GetField("Width", Private).GetValue(texture);
        int sampledY = (int)texture.GetType().GetField("Height", Private).GetValue(texture);
        if (sampledX != width || sampledY != height) throw new Exception("Preview was downsampled.");
        var tiles = ((System.Collections.IEnumerable)texture.GetType().GetField("Tiles", Private).GetValue(texture)).Cast<object>().ToArray();
        long texels = 0;
        foreach (var tile in tiles)
        {
            int tw = (int)tile.GetType().GetField("Width", Private).GetValue(tile);
            int th = (int)tile.GetType().GetField("Height", Private).GetValue(tile);
            texels += (long)tw * th;
            string name = (string)tile.GetType().GetField("TextureName", Private).GetValue(tile);
            var size = Rhino.Display.TextureCache.GetSize(name);
            if (size.Width != tw + 2 || size.Height != th + 2) throw new Exception("Uploaded tile size mismatch.");
        }
        if (texels != values.Length) throw new Exception("Texture coverage lost voxels.");
        RequireMessage(preview);
        preview.ExpireSolution(false); document.NewSolution(false);
        RequireMessage(preview);
        if (!ReferenceEquals(texture, preview.GetType().GetField("planarPreviewTexture", Private).GetValue(preview)))
            throw new Exception("Unchanged preview did not reuse its texture.");
        TexturePreviewCapture.Capture(texture, width, height, output);
        var report = new
        {
            Source = Path.GetFileName(imagePath), ImageWidth = width, ImageHeight = height,
            DecodedPixels = pixels.Length, CanvasThumbnail = new { thumbnail.Width, thumbnail.Height },
            VoxelWidth = width, VoxelHeight = height, MappedValues = values.Length,
            MaximumMapperError = maximumError, MeanMapperError = sumError / values.Length,
            PixelsWithErrorOverHalf8BitStep = changedAboveHalfCode,
            PreviewWidth = sampledX, PreviewHeight = sampledY,
            PreviewMessage = preview.Message,
            TextureTiles = tiles.Length, PreviewTexels = texels,
            DisplaySamplesKeptPercent = 100.0 * texels / values.Length
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "image-fidelity.json"), json);
        Console.WriteLine(json);
        if (changedAboveHalfCode != 0) throw new Exception("Mapping differs from source by more than half an 8-bit grayscale step.");
        CheckResolutionChanges(document, preview, gridType, fieldType);
        foreach (var tile in tiles)
        {
            string name = (string)tile.GetType().GetField("TextureName", Private).GetValue(tile);
            if (Rhino.Display.TextureCache.Contains(name)) throw new Exception("Replaced texture was not released.");
        }
        File.WriteAllText(Path.Combine(output, "passed.txt"), "Full-resolution image mapping verified against every source pixel; actual preview resolution verified for texture caching and viewport rendering, all planes, 3D, disconnected input and GPU texture dimensions.");
    }

    static void RequireMessage(GH_Component preview)
    {
        if (!preview.Message.Contains(" to ") || preview.Message.Contains("Preview:")
            || preview.Message.Contains('\n') || preview.Message.Contains('\r'))
            throw new Exception("Incorrect preview message: " + preview.Message);
    }

    static void CheckResolutionChanges(GH_Document document, GH_Component preview, Type gridType, Type fieldType)
    {
        var source = new Param_GenericObject(); source.CreateAttributes(); document.AddObject(source, false);
        preview.Params.Input[0].RemoveAllSources(); preview.Params.Input[0].AddSource(source);
        foreach (var dimensions in new[] { new[] { 37, 23, 1 }, new[] { 37, 1, 23 }, new[] { 1, 37, 23 }, new[] { 1, 1, 23 }, new[] { 3, 4, 5 } })
        {
            var grid = gridType.GetMethod("CreateFullDomain").Invoke(null, new object[] { dimensions[0], dimensions[1], dimensions[2], 1.0 });
            var field = Activator.CreateInstance(fieldType, Private, null, new object[] { grid, null, null }, null);
            source.PersistentData.Clear(); source.PersistentData.Append(new GH_ObjectWrapper(field));
            source.ExpireSolution(false); document.NewSolution(false);
            RequireMessage(preview);
        }
        preview.Params.Input[0].RemoveAllSources(); preview.ExpireSolution(false); document.NewSolution(false);
        RequireMessage(preview);

        // A GPU preview can have a different texture size from its source voxel domain.
        var update = preview.GetType().GetMethod("updateGpuPreviewResolution", Private);
        Type frameType = update.GetParameters()[0].ParameterType;
        var frame = Activator.CreateInstance(frameType, true);
        void Set(string name, int value) => frameType.GetField(name).SetValue(frame, value);
        Set("ResX", 37); Set("ResY", 23); Set("ResZ", 1); Set("Width", 370); Set("Height", 230);
        update.Invoke(preview, new[] { frame }); RequireMessage(preview);
        Set("ResZ", 5);
        update.Invoke(preview, new[] { frame }); RequireMessage(preview);
    }
}
