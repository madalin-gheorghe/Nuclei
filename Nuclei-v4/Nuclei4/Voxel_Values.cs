using System;
using System.Collections.Generic;
using System.Drawing;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Nuclei4
{
    public class Voxel_Values : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the EnvironmentValues class.
        /// </summary>
        public Voxel_Values()
          : base("Define Voxel Values", "Define Voxel Values",
              "Define Voxel Differentiated Values",
              "Nuclei4", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
            //1
            pManager.AddIntegerParameter("Type", "type", "Type of Voxel Value", GH_ParamAccess.item, 0);
            //2
            pManager.AddNumberParameter("Multiplier Value", "multiplier", "Value Assigned to Voxel will Multiply the Particle Settings", GH_ParamAccess.tree);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.tertiary; }
        }

        public override bool Write(GH_IWriter writer)
        {
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            valueOrderMode = VoxelValueOrderMode.Auto;
            return base.Read(reader);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            valueOrderMode = VoxelValueOrderMode.Auto;
            Message = string.Empty;
            VoxelFoodValueList.EnsureSeparateFoodChoices(this, 1);

            //add value list
            if (Params.Input[1].SourceCount == 0)
            {
                //instantiate new value list
                var vallist = new Grasshopper.Kernel.Special.GH_ValueList();
                vallist.ListMode = Grasshopper.Kernel.Special.GH_ValueListMode.DropDown;
                vallist.CreateAttributes();

                //customise value list position
                GH_Component Component = this;
                GH_Document GrasshopperDocument = this.OnPingDocument();
                float xCoord = (float)Component.Attributes.Pivot.X - 250;
                float yCoord = (float)Component.Attributes.Pivot.Y - 31;
                PointF cornerPt = new PointF(xCoord, yCoord);
                vallist.Attributes.Pivot = cornerPt;

                //populate value list with our own data
                vallist.ListItems.Clear();
                var items = new List<Grasshopper.Kernel.Special.GH_ValueListItem>();
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Minimum Density", "0"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Maximum Density", "1"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Speed", "2"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Sensor Distance", "3"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Sensor Angle", "4"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Rotation Angle", "5"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Slime Food", "6"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ant Food", "13"));

                vallist.ListItems.AddRange(items);
                // Until now, the slider is a hypothetical object.
                // This command makes it 'real' and adds it to the canvas.
                GrasshopperDocument.AddObject(vallist, false);
                //Connect the new slider to this component
                Component.Params.Input[1].AddSource(vallist);
                Component.Params.Input[1].CollectData();
            }

            valueMultipliers = new List<double>();
            valueMultiplierTree = new GH_Structure<GH_Number>();

            //set inputs
            if (!VoxelFieldAccess.TryGet(DA, "Voxels", Globals.voxelSize, out inputVoxelField))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "A valid voxel field is required.");
                return;
            }
            DA.GetData("Type", ref valueIndex);
            DA.GetDataTree(2, out valueMultiplierTree);

            VoxelGridData inputData = inputVoxelField.Data;
            valueMultipliers = buildValueList(inputData, valueMultiplierTree);
            validateValueCount(inputData, valueMultipliers);
            long valueHash = hashValues(valueMultipliers);
            if (tryReuseCachedOutput(inputData, valueHash))
            {
                DA.SetData(0, voxels);
                return;
            }

            VoxelGridData outputData = inputData.WithScalarValues(valueIndex, valueMultipliers);
            voxels = inputVoxelField.WithData(outputData);
            cacheOutput(inputData, valueHash, outputData, voxels);

            DA.SetData(0, voxels);
        }

        //-------------------------------------------------------------------

        //inputs
        VoxelField inputVoxelField;
        int valueIndex;
        List<double> valueMultipliers;
        GH_Structure<GH_Number> valueMultiplierTree;
        VoxelValueOrderMode valueOrderMode = VoxelValueOrderMode.Auto;

        //outputs
        VoxelField voxels;
        VoxelGridData cachedInputData;
        int cachedValueIndex = int.MinValue;
        VoxelValueOrderMode cachedValueOrderMode = VoxelValueOrderMode.Auto;
        long cachedValueHash;
        bool cachedValueHashValid;
        VoxelGridData cachedOutputData;
        VoxelField cachedOutputVoxels;

        //-------------------------------------------------------------------

        enum VoxelValueOrderMode
        {
            Auto = 0,
            Voxel = 1,
            ImageRows = 2
        }

        List<double> buildValueList(VoxelGridData voxelData, GH_Structure<GH_Number> valueTree)
        {
            List<double> values = flattenValueTree(valueTree);
            if (values.Count <= 1 || voxelData == null || voxelData.Count == 0)
            {
                return values;
            }

            VoxelValueOrderMode resolvedMode = resolveValueOrderMode(voxelData, valueTree, values);

            if (resolvedMode == VoxelValueOrderMode.ImageRows && values.Count == voxelData.Count)
            {
                return imageRowValuesToVoxelOrder(voxelData, values);
            }

            return values;
        }

        static List<double> flattenValueTree(GH_Structure<GH_Number> valueTree)
        {
            List<double> values = new List<double>();
            if (valueTree == null) return values;

            for (int i = 0; i < valueTree.Branches.Count; i++)
            {
                List<GH_Number> branch = valueTree.Branches[i];
                if (branch == null) continue;

                for (int j = 0; j < branch.Count; j++)
                {
                    GH_Number value = branch[j];
                    if (value != null)
                    {
                        values.Add(value.Value);
                    }
                }
            }

            return values;
        }

        VoxelValueOrderMode resolveValueOrderMode(VoxelGridData voxelData, GH_Structure<GH_Number> valueTree, List<double> values)
        {
            if (valueOrderMode != VoxelValueOrderMode.Auto)
            {
                return valueOrderMode;
            }

            if (looksLikeImageRowTree(voxelData, valueTree, values))
            {
                return VoxelValueOrderMode.ImageRows;
            }

            return VoxelValueOrderMode.Voxel;
        }

        static bool looksLikeImageRowTree(VoxelGridData voxelData, GH_Structure<GH_Number> valueTree, List<double> values)
        {
            if (voxelData == null || valueTree == null || values == null) return false;
            if (voxelData.Count <= 0 || values.Count != voxelData.Count) return false;

            int expectedRowCount = voxelData.ResY * voxelData.ResZ;
            if (expectedRowCount <= 1) return false;
            if (valueTree.Branches.Count != expectedRowCount) return false;

            for (int i = 0; i < valueTree.Branches.Count; i++)
            {
                List<GH_Number> branch = valueTree.Branches[i];
                if (branch == null || branch.Count != voxelData.ResX)
                {
                    return false;
                }
            }

            return true;
        }

        static List<double> imageRowValuesToVoxelOrder(VoxelGridData voxelData, List<double> imageRowValues)
        {
            double[] voxelValues = new double[voxelData.Count];

            for (int z = 0; z < voxelData.ResZ; z++)
            {
                for (int y = 0; y < voxelData.ResY; y++)
                {
                    for (int x = 0; x < voxelData.ResX; x++)
                    {
                        int imageIndex = z * voxelData.ResX * voxelData.ResY + y * voxelData.ResX + x;
                        int voxelIndex = voxelData.FlatIndex(x, y, z);
                        voxelValues[voxelIndex] = imageRowValues[imageIndex];
                    }
                }
            }

            return new List<double>(voxelValues);
        }

        void validateValueCount(VoxelGridData voxelData, List<double> values)
        {
            if (voxelData == null || values == null) return;
            if (values.Count == 0 || values.Count == 1 || values.Count == voxelData.Count || values.Count == voxelData.ActiveCount) return;

            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Multiplier count (" + values.Count + ") does not match active voxels (" + voxelData.ActiveCount + ") or full voxel grid (" + voxelData.Count + "). Values were not remapped.");
        }

        bool tryReuseCachedOutput(VoxelGridData inputData, long valueHash)
        {
            if (cachedOutputVoxels == null || cachedOutputData == null)
            {
                return false;
            }

            if (!ReferenceEquals(cachedInputData, inputData)
                || cachedValueIndex != valueIndex
                || cachedValueOrderMode != valueOrderMode)
            {
                return false;
            }

            // Grasshopper may mutate values inside an existing GH_Structure.
            // Object identity therefore does not prove that its contents are
            // unchanged; only reuse output after comparing the value hash.
            bool sameValues = cachedValueHashValid
                && cachedValueHash == valueHash;

            if (!sameValues)
            {
                return false;
            }

            voxels = cachedOutputVoxels;
            return true;
        }

        void cacheOutput(VoxelGridData inputData, long valueHash, VoxelGridData outputData, VoxelField outputVoxels)
        {
            cachedInputData = inputData;
            cachedValueIndex = valueIndex;
            cachedValueOrderMode = valueOrderMode;
            cachedValueHash = valueHash;
            cachedValueHashValid = true;
            cachedOutputData = outputData;
            cachedOutputVoxels = outputVoxels;
        }

        static long hashValues(IList<double> values)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                int count = values != null ? values.Count : 0;
                hash = hashLong(hash, count);

                for (int i = 0; i < count; i++)
                {
                    hash = hashLong(hash, BitConverter.DoubleToInt64Bits(values[i]));
                }

                return hash;
            }
        }

        static long hashLong(long hash, long value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 1099511628211L;
                return hash;
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Nuclei4.Properties.Resources.EnvironmentWithValues2;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("6a35ef3b-11f7-4d48-8103-683e82b2dd5d"); }
        }
    }
}
