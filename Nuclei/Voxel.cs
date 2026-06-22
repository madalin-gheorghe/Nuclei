using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper;

namespace Nuclei3
{
    internal sealed class VoxelDensityStore
    {
        public double[] Values;

        public VoxelDensityStore(double[] values)
        {
            Values = values;
        }
    }

    public class Voxel
    {
        public Point3d loc;
        public int idX;
        public int idY;
        public int idZ;
        public int flatIndex;
        public double voxelSize = 1;

        public double minDensity = -1;
        public double maxDensity = -1;
        public double inputMinDensity = -1;
        public double inputMaxDensity = -1;

        double densityValue = 0;
        internal VoxelDensityStore densityStore;

        public double density
        {
            get
            {
                VoxelDensityStore store = densityStore;
                double[] values = store != null ? store.Values : null;
                if (values != null && flatIndex >= 0 && flatIndex < values.Length)
                {
                    return values[flatIndex];
                }

                return densityValue;
            }

            set
            {
                densityValue = value;

                VoxelDensityStore store = densityStore;
                double[] values = store != null ? store.Values : null;
                if (values != null && flatIndex >= 0 && flatIndex < values.Length)
                {
                    values[flatIndex] = value;
                }
            }
        }

        public double towardsFoodPheromone = 0;
        public double towardsBasePheromone = 0;

        public double speedMultiplier = -1;
        public double sensorAngleMultiplier = -1;
        public double sensorDistanceMultiplier = -1;
        public double rotationAngleMultiplier = -1;

        public double food = -1;

        public Vector3d voxelVector = new Vector3d(0,0,0);
        public int frequency = 3;
        public bool vectorField = false;

        public int particleCount = 0;

        public bool boundary = false;

        //-------------------------------------------------------------------

        public Voxel(double _voxelSize, int _idX, int _idY, int _idZ)
        {
            voxelSize = _voxelSize;

            idX = _idX;
            idY = _idY;
            idZ = _idZ;

            loc = new Point3d(idX * voxelSize + voxelSize / 2, idY * voxelSize + voxelSize / 2, idZ * voxelSize + voxelSize / 2);

            minDensity = -1;
            maxDensity = -1;
            density = 0;

            particleCount = 0;
        }
    }
}
