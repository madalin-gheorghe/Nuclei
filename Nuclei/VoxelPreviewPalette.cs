using System;
using System.Collections.Generic;
using System.Drawing;

namespace Nuclei3
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
            int maxAlpha = color.A > 0 ? color.A : 255;

            for (int i = 0; i < PaletteSize; i++)
            {
                double t = i / 255.0;
                colors.Add(FadeColor(color, maxAlpha, t));
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
            int expectedAlpha = ClampToByte((expectedColor.A > 0 ? expectedColor.A : 255) * (128.0 / 255.0));
            return middle.A == expectedAlpha
                && middle.R == expectedColor.R
                && middle.G == expectedColor.G
                && middle.B == expectedColor.B;
        }

        static Color FadeColor(Color color, int maxAlpha, double t)
        {
            return Color.FromArgb(
                ClampToByte(maxAlpha * t),
                color.R,
                color.G,
                color.B);
        }

        static int ClampToByte(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (int)Math.Round(value);
        }
    }
}
