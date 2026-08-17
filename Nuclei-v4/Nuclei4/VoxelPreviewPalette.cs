using System;
using System.Collections.Generic;
using System.Drawing;

namespace Nuclei4
{
    internal static class VoxelPreviewPalette
    {
        const int PaletteSize = 256;

        static readonly Color DefaultStaticColor = Color.White;
        static readonly Color ChemoAttractantsColor = Color.FromArgb(255, 223, 255, 123);
        static readonly Color AntFoodPheromonesColor = Color.FromArgb(255, 57, 255, 170);
        static readonly Color AntBasePheromonesColor = Color.FromArgb(255, 255, 0, 100);

        public static void EnsureInitialized()
        {
            if (PalettesAreCurrent()) return;

            Globals.voxelColorList_White = CreateValuePalette(DefaultStaticColor);
            Globals.voxelColorList_chemoAttractants = CreateValuePalette(ChemoAttractantsColor);
            Globals.voxelColorList_antFoodPheromones = CreateValuePalette(AntFoodPheromonesColor);
            Globals.voxelColorList_antBasePheromones = CreateValuePalette(AntBasePheromonesColor);
        }

        public static List<Color> CreateValuePalette(Color color)
        {
            var colors = new List<Color>(PaletteSize);

            for (int i = 0; i < PaletteSize; i++)
            {
                double t = i / 255.0;
                colors.Add(FadeColor(color, t));
            }

            return colors;
        }

        static bool PalettesAreCurrent()
        {
            return IsCurrent(Globals.voxelColorList_White, DefaultStaticColor)
                && IsCurrent(Globals.voxelColorList_chemoAttractants, ChemoAttractantsColor)
                && IsCurrent(Globals.voxelColorList_antFoodPheromones, AntFoodPheromonesColor)
                && IsCurrent(Globals.voxelColorList_antBasePheromones, AntBasePheromonesColor);
        }

        static bool IsCurrent(List<Color> colors, Color expectedColor)
        {
            if (colors == null || colors.Count != PaletteSize) return false;

            Color middle = colors[128];
            double t = 128.0 / 255.0;
            return middle.A == 255
                && middle.R == ClampToByte(expectedColor.R * t)
                && middle.G == ClampToByte(expectedColor.G * t)
                && middle.B == ClampToByte(expectedColor.B * t);
        }

        static Color FadeColor(Color color, double t)
        {
            return Color.FromArgb(
                255,
                ClampToByte(color.R * t),
                ClampToByte(color.G * t),
                ClampToByte(color.B * t));
        }

        static int ClampToByte(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (int)Math.Round(value);
        }
    }
}
