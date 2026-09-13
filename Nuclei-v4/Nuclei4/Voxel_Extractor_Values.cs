using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Drawing;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper;
using Rhino;

namespace Nuclei4
{
    public class Voxel_Extractor_Values : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Extractor_Density class.
        /// </summary>
        public Voxel_Extractor_Values()
          : base("Extract Voxel Values", "Extract Voxel Values",
              "Extract Voxel Values",
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
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Voxel Values", "voxelValues", "Voxel Values", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.septenary; }
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            document.SolutionStart -= PrepareValueLists;
            document.SolutionStart += PrepareValueLists;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            document.SolutionStart -= PrepareValueLists;
            base.RemovedFromDocument(document);
        }

        void PrepareValueLists(object sender, GH_SolutionEventArgs args) => VoxelTypeChoices.Ensure(this, 9, 280);

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            VoxelFoodValueList.EnsureSeparateFoodChoices(this, 1);

            //set inputs
            DA.GetData("Type", ref valueIndex);

            VoxelField field;
            if (!VoxelFieldAccess.TryGet(DA, 0, Globals.voxelSize, out field)) return;
            field.EnsureDynamicStateCurrent();

            VoxelGridData voxelData = field.Data;
            ensureOutputCapacity(voxelData.ActiveCount);
            if ((valueIndex >= 0 && valueIndex <= 9) || valueIndex == VoxelPreviewField.AntFood)
            {
                for (int ordinal = 0; ordinal < voxelData.ActiveCount; ordinal++)
                {
                    outputVoxelValues.Add(field.GetScalarValue(valueIndex, voxelData.ActiveFlatIndexAt(ordinal)));
                }
            }
           

            DA.SetDataList(0, outputVoxelValues);
        }

        void ensureOutputCapacity(int totalVoxelCount)
        {
            if (outputVoxelValues == null)
            {
                outputVoxelValues = new List<double>(totalVoxelCount);
            }
            else
            {
                outputVoxelValues.Clear();
                if (outputVoxelValues.Capacity < totalVoxelCount)
                {
                    outputVoxelValues.Capacity = totalVoxelCount;
                }
            }
        }

        //-------------------------------------------------------------------

        //inputs
        int valueIndex;

        //-------------------------------------------------------------------

        //outputs
        List<double> outputVoxelValues;

        //-------------------------------------------------------------------

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Nuclei4.Properties.Resources.VoxelDensity;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("9668d334-4d67-464e-9e19-c581c49c26a7"); }
        }
    }
}
