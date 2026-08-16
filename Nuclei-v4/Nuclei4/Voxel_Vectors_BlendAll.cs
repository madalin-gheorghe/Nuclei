using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Voxel_Vectors_BlendAll : GH_Component
    {
        public Voxel_Vectors_BlendAll()
          : base("Voxel Vectors Blend", "Blend Vectorfield",
              "Blend All Vectors By Averaging Their Neighbours",
              "Nuclei4", " Environment")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
            pManager.AddNumberParameter("Blend Strength", "blendStrength", "Strength of Blend", GH_ParamAccess.item, 0.25);
            pManager[1].Optional = true;
            pManager.AddIntegerParameter("Blend Range", "range", "The Range of Blend", GH_ParamAccess.item, 1);
            pManager[2].Optional = true;
            pManager.AddIntegerParameter("Blend Iterations", "iterations", "Blend Number of Iterations", GH_ParamAccess.item, 1);
            pManager[3].Optional = true;
            pManager.AddBooleanParameter("Wrap Blend", "wrap", "Boundary conditions", GH_ParamAccess.item, false);
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.quarternary; }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double blendDiffuse = 0.25;
            int blendRange = 1;
            int blendIterations = 1;
            bool wrapBlend = false;

            VoxelField inputField;
            if (!VoxelFieldAccess.TryGet(DA, 0, Globals.voxelSize, out inputField)) return;
            DA.GetData(1, ref blendDiffuse);
            DA.GetData(2, ref blendRange);
            DA.GetData(3, ref blendIterations);
            DA.GetData(4, ref wrapBlend);

            VoxelGridData data = inputField.Data;
            if (data.ActiveCount == 0)
            {
                data = VoxelGridData.CreateFullDomain(data.ResX, data.ResY, data.ResZ, data.VoxelSize);
            }

            float[] current = data.VectorData != null ? Copy(data.VectorData) : new float[checked(data.Count * 3)];
            float[] next = new float[checked(data.Count * 3)];

            bool planarXY = data.ResZ == 1;
            bool planarXZ = data.ResY == 1 && data.ResZ != 1;
            bool planarYZ = data.ResX == 1 && data.ResY != 1 && data.ResZ != 1;

            for (int iteration = 0; iteration < blendIterations; iteration++)
            {
                float[] source = current;
                float[] destination = next;
                Parallel.For(0, data.ActiveCount, ordinal =>
                {
                    int flatIndex = data.ActiveFlatIndexAt(ordinal);
                    int x;
                    int y;
                    int z;
                    data.CoordinatesFromFlatIndex(flatIndex, out x, out y, out z);
                    Vector3d neighbourSum = Vector3d.Zero;

                    for (int u = -blendRange; u <= blendRange; u++)
                    {
                        for (int v = -blendRange; v <= blendRange; v++)
                        {
                            for (int w = -blendRange; w <= blendRange; w++)
                            {
                                int nx = x + u;
                                int ny = y + v;
                                int nz = z + w;

                                if (wrapBlend)
                                {
                                    if (nx < 0) nx += data.ResX;
                                    if (nx >= data.ResX) nx -= data.ResX;
                                    if (ny < 0) ny += data.ResY;
                                    if (ny >= data.ResY) ny -= data.ResY;
                                    if (nz < 0) nz += data.ResZ;
                                    if (nz >= data.ResZ) nz -= data.ResZ;
                                }

                                if (nx < 0 || nx >= data.ResX ||
                                    ny < 0 || ny >= data.ResY ||
                                    nz < 0 || nz >= data.ResZ)
                                {
                                    continue;
                                }

                                int neighbourIndex = data.FlatIndex(nx, ny, nz);
                                if (!data.IsActive(neighbourIndex)) continue;

                                Vector3d neighbour = ReadVector(source, neighbourIndex);
                                neighbour.Unitize();
                                neighbourSum += neighbour;
                            }
                        }
                    }

                    neighbourSum.Unitize();
                    Vector3d currentVector = ReadVector(source, flatIndex);
                    currentVector.Unitize();
                    Vector3d result = (1.0 - blendDiffuse) * currentVector + blendDiffuse * neighbourSum;
                    result.Unitize();

                    if (planarXY) result = new Vector3d(result.X, result.Y, 0);
                    else if (planarXZ) result = new Vector3d(result.X, 0, result.Z);
                    else if (planarYZ) result = new Vector3d(0, result.Y, result.Z);
                    result.Unitize();
                    int offset = flatIndex * 3;
                    destination[offset] = (float)result.X;
                    destination[offset + 1] = (float)result.Y;
                    destination[offset + 2] = (float)result.Z;
                });

                float[] swap = current;
                current = next;
                next = swap;
            }

            VoxelGridData outputData = data.WithPackedVectorMapValues(current);
            DA.SetData(0, inputField.WithData(outputData));
        }

        static float[] Copy(float[] source)
        {
            float[] result = new float[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }

        static Vector3d ReadVector(float[] source, int flatIndex)
        {
            int offset = flatIndex * 3;
            return new Vector3d(source[offset], source[offset + 1], source[offset + 2]);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Nuclei3.Properties.Resources.VoxelBlendVectors2; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("ee2dadbd-e610-457d-8a08-e603062c4a45"); }
        }
    }
}
