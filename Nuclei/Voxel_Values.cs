using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Voxel_Values : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the EnvironmentValues class.
        /// </summary>
        public Voxel_Values()
          : base("Define Voxel Values", "Voxel Values", 
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
            pManager.AddNumberParameter("Multiplier Value", "multiplier", "Value Assigned to Voxel will Multiply the Particle Settings", GH_ParamAccess.list);
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

            //set inputs
            DA.GetData("Voxels", ref inputVoxels);
            DA.GetData("Type", ref valueIndex);
            DA.GetDataList("Multiplier Value", valueMultipliers);

            //determine voxel settings
            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

            double voxelSize = Globals.voxelSize;

            //create list of empty voxels
            voxels = new Voxel[resX, resY, resZ];

            //count active voxels
            int activeVoxelsCounter = 0;
            Parallel.For(0, resX, i =>
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (inputVoxels[i, j, k] != null)
                        {
                            int parallelCounter = System.Threading.Interlocked.Increment(ref activeVoxelsCounter);
                        }
                    }
                }
            }
            );

            int counter = 0;

            voxels = new Voxel[resX, resY, resZ];

            if (activeVoxelsCounter == 0)
            {
                //if there are 0 active voxels then instantiate new voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            voxels[i, j, k] = new Voxel(voxelSize, i, j, k);
                        }
                    }
                }
                );

                counter = resX * resY * resZ;
            }
            else
            {
                //inherit values from input voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if (inputVoxels[i, j, k] != null)
                            {
                                Voxel inV = inputVoxels[i, j, k];
                                if (voxels[i, j, k] == null) voxels[i, j, k] = new Voxel(voxelSize, i, j, k);
                                Voxel outV = voxels[i, j, k];

                                outV.minDensity = inV.minDensity;
                                outV.maxDensity = inV.maxDensity;

                                outV.density = inV.density;

                                outV.speedMultiplier = inV.speedMultiplier;
                                outV.sensorAngleMultiplier = inV.sensorAngleMultiplier;
                                outV.sensorDistanceMultiplier = inV.sensorDistanceMultiplier;
                                outV.rotationAngleMultiplier = inV.rotationAngleMultiplier;

                                outV.food = inV.food;

                                outV.voxelVector = inV.voxelVector;
                                outV.frequency = inV.frequency;
                            }
                        }
                    }
                }
                );

                counter = activeVoxelsCounter;
            }

            //assign values
            if (counter == valueMultipliers.Count)
            {
                int listIndex = 0;

                for(int i=0; i<resX; i++)
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if (voxels[i, j, k] != null)
                            {
                                Voxel V = voxels[i, j, k];

                                //assign the voxel values from the initial voxels
                                if (inputVoxels[i, j, k] != null)
                                {
                                    Voxel initialV = inputVoxels[i, j, k];
                                    V.minDensity = initialV.minDensity;
                                    V.maxDensity = initialV.maxDensity;
                                    V.speedMultiplier = initialV.speedMultiplier;
                                    V.sensorAngleMultiplier = initialV.sensorAngleMultiplier;
                                    V.sensorDistanceMultiplier = initialV.sensorDistanceMultiplier;
                                    V.rotationAngleMultiplier = initialV.rotationAngleMultiplier;
                                    V.food = initialV.food;
                                    V.voxelVector = initialV.voxelVector;
                                    V.frequency = initialV.frequency;
                                }

                                switch (valueIndex)
                                {
                                    case 0:
                                        V.minDensity = valueMultipliers[listIndex];
                                        if(V.minDensity != -1)
                                        {
                                            if (V.minDensity < 0) V.minDensity = 0;
                                            if (V.minDensity > 1) V.minDensity = 1;
                                        }
                                        listIndex++;
                                        break;

                                    case 1:
                                        V.maxDensity = valueMultipliers[listIndex];
                                        if (V.maxDensity != -1)
                                        {
                                            if (V.maxDensity < 0) V.maxDensity = 0;
                                            if (V.maxDensity > 1) V.maxDensity = 1;
                                        }
                                        listIndex++;
                                        break;

                                    case 2:
                                        V.speedMultiplier = valueMultipliers[listIndex];
                                        listIndex++;
                                        break;

                                    case 3:
                                        V.sensorDistanceMultiplier = valueMultipliers[listIndex];
                                        listIndex++;
                                        break;

                                    case 4:
                                        V.sensorAngleMultiplier = valueMultipliers[listIndex];
                                        listIndex++;
                                        break;

                                    case 5:
                                        V.rotationAngleMultiplier = valueMultipliers[listIndex];
                                        listIndex++;
                                        break;

                                    case 6:
                                        V.food = valueMultipliers[listIndex];
                                        listIndex++;
                                        break;
                                }
                            }
                        }
                    }
                }
                //);

            }
            else
            {
                //assign first value uniformly across all voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if (voxels[i, j, k] != null)
                            {
                                Voxel V = voxels[i, j, k];

                                //assign the voxel values from the initial voxels
                                if (inputVoxels[i, j, k] != null)
                                {
                                    
                                    Voxel initialV = inputVoxels[i, j, k];
                                    V.minDensity = initialV.minDensity;
                                    V.maxDensity = initialV.maxDensity;
                                    V.speedMultiplier = initialV.speedMultiplier;
                                    V.sensorAngleMultiplier = initialV.sensorAngleMultiplier;
                                    V.sensorDistanceMultiplier = initialV.sensorDistanceMultiplier;
                                    V.rotationAngleMultiplier = initialV.rotationAngleMultiplier;
                                    V.food = initialV.food;
                                    V.voxelVector = initialV.voxelVector;
                                    V.frequency = initialV.frequency;
                                }

                                switch (valueIndex)
                                {
                                    case 0:
                                        V.minDensity = valueMultipliers[0];
                                        break;

                                    case 1:
                                        V.maxDensity = valueMultipliers[0];
                                        break;

                                    case 2:
                                        V.speedMultiplier = valueMultipliers[0];
                                        break;

                                    case 3:
                                        V.sensorDistanceMultiplier = valueMultipliers[0];
                                        break;

                                    case 4:
                                        V.sensorAngleMultiplier = valueMultipliers[0];
                                        break;

                                    case 5:
                                        V.rotationAngleMultiplier = valueMultipliers[0];
                                        break;

                                    case 6:
                                        V.food = valueMultipliers[0];
                                        break;
                                }
                            }
                        }
                    }
                }
                );
            }

            DA.SetData(0, voxels);
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] inputVoxels;
        int valueIndex;
        List<double> valueMultipliers;

        //outputs
        Voxel[,,] voxels;

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
                return Nuclei3.Properties.Resources.EnvironmentWithValues2;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("bd89c53c-93c6-4da8-8312-0fb911ff4ebe"); }
        }
    }
}