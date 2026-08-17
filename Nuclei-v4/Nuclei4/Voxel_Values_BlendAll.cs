using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Voxel_Values_BlendAll : GH_Component
    {
        public Voxel_Values_BlendAll()
          : base("Voxel Values Blend", "Voxel Values Blend",
              "Blend All Values",
              "Nuclei4", " Environment")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Type", "type", "Type of Voxel Value", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Blend Strength", "blendStrength", "Strength of Blend. VALUES BETWEEN 0 AND 1", GH_ParamAccess.item, 0.25);
            pManager[2].Optional = true;
            pManager.AddIntegerParameter("Blend Range", "range", "The Range of Blend", GH_ParamAccess.item, 1);
            pManager[3].Optional = true;
            pManager.AddIntegerParameter("Blend Iterations", "iterations", "Blend Number of Iterations", GH_ParamAccess.item, 1);
            pManager[4].Optional = true;
            pManager.AddBooleanParameter("Wrap Blend", "wrap", "Boundary conditions", GH_ParamAccess.item, false);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.tertiary; }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            EnsureValueList();

            int valueIndex = 0;
            double diffuse = 0.25;
            int diffuseRange = 1;
            int blendIterations = 1;
            bool wrapBoundaries = false;

            VoxelField inputField;
            if (!VoxelFieldAccess.TryGet(DA, "Voxels", Globals.voxelSize, out inputField)) return;
            DA.GetData("Type", ref valueIndex);
            DA.GetData("Blend Strength", ref diffuse);
            DA.GetData("Blend Range", ref diffuseRange);
            DA.GetData("Blend Iterations", ref blendIterations);
            DA.GetData("Wrap Blend", ref wrapBoundaries);

            if (valueIndex < 0 || valueIndex > 6)
            {
                DA.SetData(0, inputField);
                return;
            }

            if (diffuse > 1) diffuse = 1;
            diffuse *= 0.5;
            blendIterations += blendIterations % 2;
            if (blendIterations < 2) blendIterations = 2;

            VoxelGridData data = inputField.Data;
            Globals.tridimensional = data.ResX > 1 && data.ResY > 1 && data.ResZ > 1;
            if (diffuse <= 0 || data.ActiveCount == 0)
            {
                DA.SetData(0, inputField);
                return;
            }

            if (diffuseRange < 0) diffuseRange = 0;
            double[] weights = PrecomputeWeights(diffuseRange);
            float[] current = new float[data.Count];
            float[] next = new float[data.Count];

            Parallel.For(0, data.Count, flatIndex =>
            {
                current[flatIndex] = (float)inputField.GetScalarValue(valueIndex, flatIndex);
            });

            bool planarXY = false;
            bool planarXZ = false;
            bool planarYZ = false;
            if (data.ResX == 1) planarYZ = true;
            if (data.ResY == 1)
            {
                planarXY = false;
                planarXZ = true;
                planarYZ = false;
            }
            if (data.ResZ == 1)
            {
                planarXY = true;
                planarXZ = false;
                planarYZ = false;
            }
            if (Globals.tridimensional)
            {
                planarXY = false;
                planarXZ = false;
                planarYZ = false;
            }

            for (int iteration = 0; iteration < blendIterations; iteration++)
            {
                if ((iteration & 1) == 0)
                {
                    if (!planarYZ) RunAxisPass(data, weights, diffuseRange, 0, diffuse, wrapBoundaries, valueIndex, ref current, ref next);
                    if (!planarXZ) RunAxisPass(data, weights, diffuseRange, 1, diffuse, wrapBoundaries, valueIndex, ref current, ref next);
                    if (!planarXY) RunAxisPass(data, weights, diffuseRange, 2, diffuse, wrapBoundaries, valueIndex, ref current, ref next);
                }
                else
                {
                    if (!planarXY) RunAxisPass(data, weights, diffuseRange, 2, diffuse, wrapBoundaries, valueIndex, ref current, ref next);
                    if (!planarXZ) RunAxisPass(data, weights, diffuseRange, 1, diffuse, wrapBoundaries, valueIndex, ref current, ref next);
                    if (!planarYZ) RunAxisPass(data, weights, diffuseRange, 0, diffuse, wrapBoundaries, valueIndex, ref current, ref next);
                }
            }

            VoxelGridData outputData = data.WithScalarMapValues(valueIndex, current);
            DA.SetData(0, inputField.WithData(outputData));
        }

        static void RunAxisPass(
            VoxelGridData data,
            double[] weights,
            int range,
            int axis,
            double diffuse,
            bool wrap,
            int valueIndex,
            ref float[] current,
            ref float[] next)
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
                double neighbourSum = 0;

                for (int offset = -range; offset <= range; offset++)
                {
                    int nx = x;
                    int ny = y;
                    int nz = z;
                    if (axis == 0) nx += offset;
                    else if (axis == 1) ny += offset;
                    else nz += offset;

                    if (wrap)
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
                    if (!data.IsWalkableFlatIndex(neighbourIndex)) continue;

                    double neighbourValue = source[neighbourIndex];
                    if (neighbourValue != -1)
                    {
                        neighbourSum += neighbourValue * weights[offset + range];
                    }
                }

                double value = source[flatIndex] * (1 - diffuse) + diffuse * neighbourSum;
                if ((valueIndex == 0 || valueIndex == 1) && value != -1)
                {
                    if (value < 0) value = 0;
                    if (value > 1) value = 1;
                }

                destination[flatIndex] = (float)value;
            });

            float[] swap = current;
            current = next;
            next = swap;
        }

        static double[] PrecomputeWeights(int range)
        {
            int total = (range + 1) * 2 + 1;
            double[] expanded = new double[total];
            double weightSum = 0;

            for (int i = 0; i < total; i++)
            {
                double n = Math.PI * (i - (range + 1)) / (range + 1);
                expanded[i] = (1 + Math.Cos(n)) / 2;
                weightSum += expanded[i];
            }

            double[] weights = new double[total - 2];
            for (int i = 1; i < total - 1; i++)
            {
                weights[i - 1] = expanded[i] / weightSum;
            }

            return weights;
        }

        void EnsureValueList()
        {
            if (Params.Input[1].SourceCount != 0) return;

            var valueList = new Grasshopper.Kernel.Special.GH_ValueList();
            valueList.ListMode = Grasshopper.Kernel.Special.GH_ValueListMode.DropDown;
            valueList.CreateAttributes();
            GH_Document document = OnPingDocument();
            if (document == null) return;

            valueList.Attributes.Pivot = new PointF((float)Attributes.Pivot.X - 250, (float)Attributes.Pivot.Y - 31);
            valueList.ListItems.Clear();
            valueList.ListItems.AddRange(new List<Grasshopper.Kernel.Special.GH_ValueListItem>
            {
                new Grasshopper.Kernel.Special.GH_ValueListItem("Minimum Density", "0"),
                new Grasshopper.Kernel.Special.GH_ValueListItem("Maximum Density", "1"),
                new Grasshopper.Kernel.Special.GH_ValueListItem("Speed", "2"),
                new Grasshopper.Kernel.Special.GH_ValueListItem("Sensor Distance", "3"),
                new Grasshopper.Kernel.Special.GH_ValueListItem("Sensor Angle", "4"),
                new Grasshopper.Kernel.Special.GH_ValueListItem("Rotation Angle", "5"),
                new Grasshopper.Kernel.Special.GH_ValueListItem("Food", "6")
            });
            document.AddObject(valueList, false);
            Params.Input[1].AddSource(valueList);
            Params.Input[1].CollectData();
        }

        protected override Bitmap Icon
        {
            get { return Nuclei3.Properties.Resources.EnvironmentWithValues; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("0a968da4-646c-41fb-b48c-7e1d6c258d94"); }
        }
    }
}
