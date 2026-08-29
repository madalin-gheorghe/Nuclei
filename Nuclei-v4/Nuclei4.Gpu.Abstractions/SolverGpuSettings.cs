using System;
using System.Collections.Generic;

namespace Nuclei4
{
    internal sealed class SolverGpuSettings
    {
        public double Diffuse = 0.1;
        public int DiffuseRange = 1;
        public double DiffusionGradual = 1.0;
        public double RandomDivisionProbability = 0;
        public double RandomDeathProbability = 0;
        public int RandomPopulationFrequency = 1;
        public double Decay = 0.03;
        public double AntFoodDiffuse = 0.05;
        public double AntFoodDecay = 0.005;
        public double AntBaseDiffuse = 0.1;
        public double AntBaseDecay = 0.01;
        public int AntDiffuseRange = 1;
        public double SlimeAntFood = 0;
        public double SlimeAntBase = 0;
        public double AntSlime = 0;
        public bool WrapBoundaries = false;
        public int MaxIterations = 100000;
        public int TrailSize = 0;
        public int TrailFreq = 1;
        public bool DynamicPopulation = false;
        public bool Division = false;
        public bool Death = false;
        public int MinimumPopulation = 100;
        public int MaximumPopulation = 20000;
        public int DivisionMinimumAge = 10;
        public int DivisionRange = 3;
        public int DivisionMinimumNeighbours = 0;
        public int DivisionMaximumNeighbours = 10;
        public int DivisionFrequency = 5;
        public int DeathMinimumAge = 10;
        public int DeathRange = 3;
        public int DeathMinimumNeighbours = 0;
        public int DeathMaximumNeighbours = 10;
        public int DeathFrequency = 5;

        public static SolverGpuSettings FromStrings(IList<string> settings)
        {
            SolverGpuSettings parsed = new SolverGpuSettings();
            if (settings == null)
            {
                return parsed;
            }

            for (int i = 0; i < settings.Count; i++)
            {
                string inputSettings = settings[i];
                if (string.IsNullOrWhiteSpace(inputSettings))
                {
                    continue;
                }

                string[] parts = inputSettings.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                switch (parts[0])
                {
                    case "VoxelSettingsSlime":
                        if (parts.Length > 1) parsed.Diffuse = Convert.ToDouble(parts[1]);
                        if (parts.Length > 2) parsed.DiffuseRange = Convert.ToInt32(parts[2]);
                        if (parts.Length > 3) parsed.Decay = Convert.ToDouble(parts[3]);
                        if (parts.Length > 4) parsed.DiffusionGradual = Convert.ToDouble(parts[4]);
                        if (parsed.DiffuseRange < 0) parsed.DiffuseRange = 0;
                        break;

                    case "VoxelSettingsAnt":
                        if (parts.Length > 1) parsed.AntFoodDiffuse = Convert.ToDouble(parts[1]);
                        if (parts.Length > 2) parsed.AntFoodDecay = Convert.ToDouble(parts[2]);
                        if (parts.Length > 3) parsed.AntBaseDiffuse = Convert.ToDouble(parts[3]);
                        if (parts.Length > 4) parsed.AntBaseDecay = Convert.ToDouble(parts[4]);
                        if (parts.Length > 5) parsed.AntDiffuseRange = Convert.ToInt32(parts[5]);
                        parsed.AntDiffuseRange = Math.Max(0, parsed.AntDiffuseRange);
                        break;

                    case "SpeciesInteractionSettings":
                        if (parts.Length > 1) parsed.SlimeAntFood = Convert.ToDouble(parts[1]);
                        if (parts.Length > 2) parsed.SlimeAntBase = Convert.ToDouble(parts[2]);
                        if (parts.Length > 3) parsed.AntSlime = Convert.ToDouble(parts[3]);
                        break;

                    case "WrapSettings":
                        if (parts.Length > 1) parsed.WrapBoundaries = Convert.ToBoolean(parts[1]);
                        break;

                    case "SolverSettings":
                        if (parts.Length > 1) parsed.MaxIterations = Convert.ToInt32(parts[1]);
                        if (parsed.MaxIterations < 0) parsed.MaxIterations = 0;
                        break;

                    case "TrailSettings":
                        if (parts.Length > 1) parsed.TrailSize = Convert.ToInt32(parts[1]);
                        if (parts.Length > 2) parsed.TrailFreq = Convert.ToInt32(parts[2]);
                        if (parsed.TrailSize < 0) parsed.TrailSize = 0;
                        if (parsed.TrailFreq < 1) parsed.TrailFreq = 1;
                        break;

                    case "DivisionSettings":
                        if (parts.Length > 1) parsed.Division = Convert.ToBoolean(parts[1]);
                        if (parts.Length > 2) parsed.DivisionMinimumAge = Convert.ToInt32(parts[2]);
                        if (parts.Length > 3) parsed.DivisionRange = Convert.ToInt32(parts[3]);
                        if (parts.Length > 4) parsed.DivisionMinimumNeighbours = Convert.ToInt32(parts[4]);
                        if (parts.Length > 5) parsed.DivisionMaximumNeighbours = Convert.ToInt32(parts[5]);
                        if (parts.Length > 6) parsed.DivisionFrequency = Convert.ToInt32(parts[6]);
                        break;

                    case "DeathSettings":
                        if (parts.Length > 1) parsed.Death = Convert.ToBoolean(parts[1]);
                        if (parts.Length > 2) parsed.DeathMinimumAge = Convert.ToInt32(parts[2]);
                        if (parts.Length > 3) parsed.DeathRange = Convert.ToInt32(parts[3]);
                        if (parts.Length > 4) parsed.DeathMinimumNeighbours = Convert.ToInt32(parts[4]);
                        if (parts.Length > 5) parsed.DeathMaximumNeighbours = Convert.ToInt32(parts[5]);
                        if (parts.Length > 6) parsed.DeathFrequency = Convert.ToInt32(parts[6]);
                        break;

                    case "PopulationSettings":
                        if (parts.Length > 1) parsed.MinimumPopulation = Convert.ToInt32(parts[1]);
                        if (parts.Length > 2) parsed.MaximumPopulation = Convert.ToInt32(parts[2]);
                        if (parts.Length > 3) parsed.RandomDivisionProbability = Convert.ToDouble(parts[3]);
                        if (parts.Length > 4) parsed.RandomDeathProbability = Convert.ToDouble(parts[4]);
                        if (parts.Length > 5) parsed.RandomPopulationFrequency = Convert.ToInt32(parts[5]);
                        break;

                }
            }

            parsed.RandomDivisionProbability = Clamp01(parsed.RandomDivisionProbability);
            parsed.RandomDeathProbability = Clamp01(parsed.RandomDeathProbability);
            parsed.RandomPopulationFrequency = Math.Max(1, parsed.RandomPopulationFrequency);
            // Random-only configurations must still run the population pass.
            parsed.DynamicPopulation = parsed.Division || parsed.Death
                || parsed.RandomDivisionProbability > 0 || parsed.RandomDeathProbability > 0;
            parsed.DiffusionGradual = NormalizeDiffusionGradual(parsed.DiffusionGradual);
            parsed.AntFoodDiffuse = Math.Max(0, parsed.AntFoodDiffuse);
            parsed.AntFoodDecay = Math.Max(0, parsed.AntFoodDecay);
            parsed.AntBaseDiffuse = Math.Max(0, parsed.AntBaseDiffuse);
            parsed.AntBaseDecay = Math.Max(0, parsed.AntBaseDecay);
            parsed.SlimeAntFood = Clamp01(parsed.SlimeAntFood);
            parsed.SlimeAntBase = Clamp01(parsed.SlimeAntBase);
            parsed.AntSlime = Math.Max(0, parsed.AntSlime);
            parsed.MinimumPopulation = Math.Max(0, parsed.MinimumPopulation);
            parsed.MaximumPopulation = Math.Max(0, parsed.MaximumPopulation);
            if (parsed.MinimumPopulation > parsed.MaximumPopulation)
            {
                int swap = parsed.MinimumPopulation;
                parsed.MinimumPopulation = parsed.MaximumPopulation;
                parsed.MaximumPopulation = swap;
            }

            parsed.DivisionMinimumAge = Math.Max(0, parsed.DivisionMinimumAge);
            parsed.DivisionFrequency = Math.Max(1, parsed.DivisionFrequency);
            NormalizeNeighbourRange(ref parsed.DivisionMinimumNeighbours, ref parsed.DivisionMaximumNeighbours);

            parsed.DeathMinimumAge = Math.Max(0, parsed.DeathMinimumAge);
            parsed.DeathFrequency = Math.Max(1, parsed.DeathFrequency);
            NormalizeNeighbourRange(ref parsed.DeathMinimumNeighbours, ref parsed.DeathMaximumNeighbours);

            return parsed;
        }

        static void NormalizeNeighbourRange(ref int minimum, ref int maximum)
        {
            minimum = Math.Max(0, minimum);
            maximum = Math.Max(0, maximum);
            if (minimum <= maximum)
            {
                return;
            }

            int swap = minimum;
            minimum = maximum;
            maximum = swap;
        }

        // Mirrors V3 normalizeDiffusionGradual: NaN collapses to 0 so a bad
        // input can never poison the diffusion weight kernel.
        static double NormalizeDiffusionGradual(double gradual)
        {
            if (double.IsNaN(gradual) || gradual < 0) return 0;
            if (double.IsPositiveInfinity(gradual) || gradual > 1) return 1;
            return gradual;
        }

        static double Clamp01(double value)
        {
            return Math.Max(0, Math.Min(1, value));
        }
    }

    internal struct SolverGpuDimensionMode
    {
        public bool Tridimensional;
        public bool PlanarXY;
        public bool PlanarXZ;
        public bool PlanarYZ;

        public static SolverGpuDimensionMode FromResolution(int resX, int resY, int resZ)
        {
            SolverGpuDimensionMode mode = new SolverGpuDimensionMode();

            if (resX > 1 && resY > 1 && resZ > 1)
            {
                mode.Tridimensional = true;
                return mode;
            }

            // V3 performs three independent X/Y/Z planar checks; when more than
            // one resolution is 1, the later Z check wins, then Y, then X.
            if (resZ == 1)
            {
                mode.PlanarXY = true;
            }
            else if (resY == 1)
            {
                mode.PlanarXZ = true;
            }
            else if (resX == 1)
            {
                mode.PlanarYZ = true;
            }
            else
            {
                mode.PlanarXY = true;
            }

            return mode;
        }

        public string Name
        {
            get
            {
                if (Tridimensional) return "3d";
                if (PlanarXY) return "xy";
                if (PlanarXZ) return "xz";
                return "yz";
            }
        }
    }
}
