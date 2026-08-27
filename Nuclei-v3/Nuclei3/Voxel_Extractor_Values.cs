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

namespace Nuclei3
{
    public class Voxel_Extractor_Values : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Extractor_Density class.
        /// </summary>
        public Voxel_Extractor_Values()
          : base("Extract Voxel Values", "Extract Voxel Values",
              "Extract Voxel Values",
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

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
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
                float xCoord = (float)Component.Attributes.Pivot.X - 280;
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
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Slime Chemoattractants", "7"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ant Food Pheromones", "8"));
                items.Add(new Grasshopper.Kernel.Special.GH_ValueListItem("Ant Base Pheromones", "9"));

                vallist.ListItems.AddRange(items);
                // Until now, the slider is a hypothetical object.
                // This command makes it 'real' and adds it to the canvas.
                GrasshopperDocument.AddObject(vallist, false);
                //Connect the new slider to this component
                Component.Params.Input[1].AddSource(vallist);
                Component.Params.Input[1].CollectData();
            }

            //set inputs
            DA.GetData(0, ref voxel);
            DA.GetData("Type", ref valueIndex);

            if ((valueIndex >= 0 && valueIndex <= 7) || valueIndex == VoxelPreviewField.AntFood)
            {
                VoxelGridData voxelData = VoxelGridRegistry.GetOrCapture(voxel, Globals.voxelSize);
                ensureOutputCapacity(voxelData.ActiveCount);

                for (int i = 0; i < voxelData.ActiveCount; i++)
                {
                    outputVoxelValues.Add(voxelData.GetScalarValue(valueIndex, voxelData.ActiveFlatIndexAt(i)));
                }

                DA.SetDataList(0, outputVoxelValues);
                return;
            }

            int resX = voxel != null ? voxel.GetLength(0) : 0;
            int resY = voxel != null ? voxel.GetLength(1) : 0;
            int resZ = voxel != null ? voxel.GetLength(2) : 0;
            ensureOutputCapacity(resX * resY * resZ);

            for (int i = 0; i < resX; i++)
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (voxel[i, j, k] != null)
                        {
                            Voxel V = voxel[i, j, k];

                            switch (valueIndex)
                            {
                                case 0:
                                    outputVoxelValues.Add(V.minDensity);
                                    break;

                                case 1:
                                    outputVoxelValues.Add(V.maxDensity);
                                    break;

                                case 2:
                                    outputVoxelValues.Add(V.speedMultiplier);
                                    break;

                                case 3:
                                    outputVoxelValues.Add(V.sensorDistanceMultiplier);
                                    break;

                                case 4:
                                    outputVoxelValues.Add(V.sensorAngleMultiplier);
                                    break;

                                case 5:
                                    outputVoxelValues.Add(V.rotationAngleMultiplier);
                                    break;

                                case 6:
                                    outputVoxelValues.Add(V.food);
                                    break;
                                case VoxelPreviewField.AntFood:
                                    outputVoxelValues.Add(V.antFood);
                                    break;

                                case 7:
                                    outputVoxelValues.Add(V.density);
                                    break;

                                case 8:
                                    outputVoxelValues.Add(V.towardsFoodPheromone);
                                    break;

                                case 9:
                                    outputVoxelValues.Add(V.towardsBasePheromone);
                                    break;
                            }
                        }
                    }
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
        Voxel[,,] voxel;
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
                return Nuclei3.Properties.Resources.VoxelDensity;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("d305f718-9fab-f05a-9694-c516b66342c9"); }
        }
    }
}

