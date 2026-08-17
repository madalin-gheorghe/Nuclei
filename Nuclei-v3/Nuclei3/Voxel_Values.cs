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

namespace Nuclei3
{
    public class Voxel_Values : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the EnvironmentValues class.
        /// </summary>
        public Voxel_Values()
          : base("Define Voxel Values", "Define Voxel Values",
              "Define Voxel Differentiated Values",
              "Nuclei3", " Environment")
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
            writer.SetInt32("ValueOrderMode", (int)valueOrderMode);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            int storedValueOrderMode = (int)valueOrderMode;
            reader.TryGetInt32("ValueOrderMode", ref storedValueOrderMode);
            valueOrderMode = clampValueOrderMode(storedValueOrderMode);
            return base.Read(reader);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);

            var autoToggle = Menu_AppendItem(menu, "Auto Value Order", valueOrderAutoHandler, true, valueOrderMode == VoxelValueOrderMode.Auto);
            autoToggle.ToolTipText = "Use image row order when the input tree looks like image rows; otherwise keep voxel list order.";

            var voxelToggle = Menu_AppendItem(menu, "Voxel List Order", valueOrderVoxelHandler, true, valueOrderMode == VoxelValueOrderMode.Voxel);
            voxelToggle.ToolTipText = "Use the native Nuclei voxel order: X, then Y, then Z.";

            var imageRowsToggle = Menu_AppendItem(menu, "Image Row Order", valueOrderImageRowsHandler, true, valueOrderMode == VoxelValueOrderMode.ImageRows);
            imageRowsToggle.ToolTipText = "Use raster/image order: rows by Y and X changing fastest inside each row.";
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
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
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Food", "6"));

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
            DA.GetData("Voxels", ref inputVoxels);
            DA.GetData("Type", ref valueIndex);
            DA.GetDataTree(2, out valueMultiplierTree);

            VoxelGridData inputData = VoxelGridRegistry.GetOrCapture(inputVoxels, Globals.voxelSize);
            if (tryReuseCachedOutput(inputData, valueMultiplierTree, 0, false))
            {
                DA.SetData(0, voxels);
                return;
            }

            valueMultipliers = buildValueList(inputData, valueMultiplierTree);
            validateValueCount(inputData, valueMultipliers);
            long valueHash = hashValues(valueMultipliers);
            if (tryReuseCachedOutput(inputData, valueMultiplierTree, valueHash, true))
            {
                DA.SetData(0, voxels);
                return;
            }

            VoxelGridData outputData = inputData.WithScalarValues(valueIndex, valueMultipliers);
            voxels = outputData.ToVoxelArray(true);
            VoxelGridRegistry.Set(voxels, outputData);
            cacheOutput(inputData, valueMultiplierTree, valueHash, outputData, voxels);

            DA.SetData(0, voxels);
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] inputVoxels;
        int valueIndex;
        List<double> valueMultipliers;
        GH_Structure<GH_Number> valueMultiplierTree;
        VoxelValueOrderMode valueOrderMode = VoxelValueOrderMode.Auto;

        //outputs
        Voxel[,,] voxels;
        VoxelGridData cachedInputData;
        GH_Structure<GH_Number> cachedInputValueTree;
        int cachedValueIndex = int.MinValue;
        VoxelValueOrderMode cachedValueOrderMode = VoxelValueOrderMode.Auto;
        long cachedValueHash;
        bool cachedValueHashValid;
        VoxelGridData cachedOutputData;
        Voxel[,,] cachedOutputVoxels;

        //-------------------------------------------------------------------

        enum VoxelValueOrderMode
        {
            Auto = 0,
            Voxel = 1,
            ImageRows = 2
        }

        void valueOrderAutoHandler(object sender, EventArgs e)
        {
            setValueOrderMode(VoxelValueOrderMode.Auto);
        }

        void valueOrderVoxelHandler(object sender, EventArgs e)
        {
            setValueOrderMode(VoxelValueOrderMode.Voxel);
        }

        void valueOrderImageRowsHandler(object sender, EventArgs e)
        {
            setValueOrderMode(VoxelValueOrderMode.ImageRows);
        }

        void setValueOrderMode(VoxelValueOrderMode mode)
        {
            valueOrderMode = mode;
            ExpireSolution(true);
        }

        static VoxelValueOrderMode clampValueOrderMode(int value)
        {
            if (value == (int)VoxelValueOrderMode.Voxel) return VoxelValueOrderMode.Voxel;
            if (value == (int)VoxelValueOrderMode.ImageRows) return VoxelValueOrderMode.ImageRows;
            return VoxelValueOrderMode.Auto;
        }

        List<double> buildValueList(VoxelGridData voxelData, GH_Structure<GH_Number> valueTree)
        {
            List<double> values = flattenValueTree(valueTree);
            if (values.Count <= 1 || voxelData == null || voxelData.Count == 0)
            {
                setValueOrderMessage(resolveValueOrderMode(voxelData, valueTree, values));
                return values;
            }

            VoxelValueOrderMode resolvedMode = resolveValueOrderMode(voxelData, valueTree, values);
            setValueOrderMessage(resolvedMode);

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

        void setValueOrderMessage(VoxelValueOrderMode mode)
        {
            if (mode == VoxelValueOrderMode.ImageRows)
            {
                Message = "Order: Image Rows";
            }
            else
            {
                Message = "Order: Voxel";
            }
        }

        bool tryReuseCachedOutput(VoxelGridData inputData, GH_Structure<GH_Number> valueTree, long valueHash, bool hasValueHash)
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

            bool sameValues = ReferenceEquals(cachedInputValueTree, valueTree)
                || (hasValueHash && cachedValueHashValid && cachedValueHash == valueHash);

            if (!sameValues)
            {
                return false;
            }

            voxels = cachedOutputVoxels;
            VoxelGridRegistry.Set(voxels, cachedOutputData);
            return true;
        }

        void cacheOutput(VoxelGridData inputData, GH_Structure<GH_Number> valueTree, long valueHash, VoxelGridData outputData, Voxel[,,] outputVoxels)
        {
            cachedInputData = inputData;
            cachedInputValueTree = valueTree;
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
                return Nuclei3.Properties.Resources.EnvironmentWithValues2;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("7bb29b30-5834-956d-6d0c-08383aab9a99"); }
        }
    }
}

