using System;
using System.Collections.Generic;

namespace Nuclei3
{
    internal sealed class SolverGpuSettings
    {
        public double Diffuse = 0.1;
        public int DiffuseRange = 1;
        public double Decay = 0.03;
        public bool WrapBoundaries = false;
        public int MaxIterations = 100000;

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

                string[] parts = inputSettings.Split(' ');
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
                        if (parsed.DiffuseRange < 0) parsed.DiffuseRange = 0;
                        break;

                    case "WrapSettings":
                        if (parts.Length > 1) parsed.WrapBoundaries = Convert.ToBoolean(parts[1]);
                        break;

                    case "SolverSettings":
                        if (parts.Length > 1) parsed.MaxIterations = Convert.ToInt32(parts[1]);
                        if (parsed.MaxIterations < 0) parsed.MaxIterations = 0;
                        break;
                }
            }

            return parsed;
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

            if (resX == 1)
            {
                mode.PlanarYZ = true;
            }
            else if (resY == 1)
            {
                mode.PlanarXZ = true;
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
