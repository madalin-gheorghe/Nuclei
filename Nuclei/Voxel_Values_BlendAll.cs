using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Voxel_Values_BlendAll : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Values_BlendAll class.
        /// </summary>
        public Voxel_Values_BlendAll()
          : base("Voxel Values Blend", "Blend Values",
              "Blend All Values",
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
            pManager.AddNumberParameter("Blend Strength", "blendStrength", "Strength of Blend. VALUES BETWEEN 0 AND 1", GH_ParamAccess.item, 0.25);
            pManager[2].Optional = true;
            //3
            pManager.AddIntegerParameter("Blend Range", "range", "The Range of Blend", GH_ParamAccess.item, 1);
            pManager[3].Optional = true;
            //4
            pManager.AddIntegerParameter("Blend Iterations", "iterations", "Blend Number of Iterations", GH_ParamAccess.item, 1);
            pManager[4].Optional = true;
            //5
            pManager.AddBooleanParameter("Wrap Blend", "wrap", "Boundary conditions", GH_ParamAccess.item, false);
            pManager[5].Optional = true;

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //0
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

            //set inputs
            diffuse = 0.25;
            diffuseRange = 1;
            blendIterations = 1;
            wrapBoundaries = false;

            DA.GetData("Voxels", ref inputVoxels);
            DA.GetData("Type", ref valueIndex);
            DA.GetData("Blend Strength", ref diffuse);
            DA.GetData("Blend Range", ref diffuseRange);
            DA.GetData("Blend Iterations", ref blendIterations);
            DA.GetData("Wrap Blend", ref wrapBoundaries);

            if (diffuse > 1) diffuse = 1;
            diffuse *= 0.5;

            blendIterations = blendIterations + blendIterations % 2;
            if (blendIterations < 2) blendIterations = 2;


            inheritVoxels();
            diffuseVoxels();


            DA.SetData(0, voxels);
        }

        //-------------------------------------------------------------------

        //inputs
        int valueIndex;
        double diffuse;
        int diffuseRange;
        int blendIterations;
        bool wrapBoundaries;
        Voxel[,,] inputVoxels;

        //voxel data
        int resX, resY, resZ;
        double voxelSize;
        double dimX, dimY, dimZ;

        bool planarXY, planarXZ, planarYZ, tridimensional;

        Voxel[] activeVoxels;

        //outputs
        Voxel[,,] voxels;

        //-------------------------------------------------------------------

        void inheritVoxels()
        {
            //determine voxel settings
            resX = inputVoxels.GetLength(0);
            resY = inputVoxels.GetLength(1);
            resZ = inputVoxels.GetLength(2);

            //determine voxelSize
            voxelSize = Globals.voxelSize;

            //determine voxel space dimensions
            dimX = resX * voxelSize;
            dimY = resY * voxelSize;
            dimZ = resZ * voxelSize;

            //determine whether 3D or 2D 
            planarXY = false;
            planarXZ = false;
            planarYZ = false;
            tridimensional = false;

            if (resX == 1)
            {
                planarXY = false;
                planarXZ = false;
                planarYZ = true;
                tridimensional = false;

                Globals.tridimensional = false;
            }
            if (resY == 1)
            {
                planarXY = false;
                planarXZ = true;
                planarYZ = false;
                tridimensional = false;

                Globals.tridimensional = false;
            }
            if (resZ == 1)
            {
                planarXY = true;
                planarXZ = false;
                planarYZ = false;
                tridimensional = false;

                Globals.tridimensional = false;
            }

            if (resX > 1 && resY > 1 && resZ > 1)
            {
                tridimensional = true;
                planarXY = false;
                planarXZ = false;
                planarYZ = false;

                Globals.tridimensional = true;
            }
            else
            {
                tridimensional = false;

                Globals.tridimensional = false;
            }

            //create empty voxels and assign the values from the initial voxels
            voxels = new Voxel[resX, resY, resZ];

            ConcurrentBag<Voxel> activeVoxelsConcurrent = new ConcurrentBag<Voxel>();

            //assign the voxel values from the initial voxels 
            //&
            //create a list of all active voxels
            Parallel.For(0, resX, i =>
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (inputVoxels[i, j, k] != null)
                        {
                            Voxel initialV = inputVoxels[i, j, k];

                            Voxel V = new Voxel(voxelSize, i, j, k);
                            voxels[i, j, k] = V;

                            //assign the voxel values from the initial voxels
                            V.minDensity = initialV.minDensity;
                            V.maxDensity = initialV.maxDensity;

                            V.speedMultiplier = initialV.speedMultiplier;
                            V.sensorAngleMultiplier = initialV.sensorAngleMultiplier;
                            V.sensorDistanceMultiplier = initialV.sensorDistanceMultiplier;
                            V.rotationAngleMultiplier = initialV.rotationAngleMultiplier;

                            V.food = initialV.food;

                            V.voxelVector = initialV.voxelVector;

                            if (V.voxelVector.Length > 0)
                            {
                                V.vectorField = true;

                                if (planarXY)
                                {
                                    V.voxelVector = new Vector3d(V.voxelVector.X, V.voxelVector.Y, 0);
                                }
                                else if (planarXZ)
                                {
                                    V.voxelVector = new Vector3d(V.voxelVector.X, 0, V.voxelVector.Z);
                                }
                                else if (planarYZ)
                                {
                                    V.voxelVector = new Vector3d(0, V.voxelVector.Y, V.voxelVector.Z);
                                }
                            }
                            else
                            {
                                V.vectorField = false;
                            }

                            V.frequency = initialV.frequency;

                            //list of all active voxels
                            activeVoxelsConcurrent.Add(V);
                        }
                    }
                }
            }
            );

            //if all voxels are NULL, then instantiate new blank voxels
            if (activeVoxelsConcurrent.Count == 0)
            {
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            voxels[i, j, k] = new Voxel(voxelSize, i, j, k);
                            activeVoxelsConcurrent.Add(voxels[i, j, k]);
                        }
                    }
                }
            );
            }

            activeVoxels = new Voxel[activeVoxelsConcurrent.Count];
            activeVoxels = activeVoxelsConcurrent.ToArray();
        }

        //-------------

        void diffuseVoxels()
        {
            if (diffuse > 0)
            {
                double[] values = new double[activeVoxels.Length];
                double[] weights = precomputeWeights(diffuseRange);

                for (int i = 0; i < blendIterations; i++)
                {
                    if (i % 2 == 0)
                    {
                        if (!planarYZ)
                        {
                            values = xPass(values, weights, valueIndex);
                            assignPassDensityToVoxel(values, valueIndex);
                        }

                        if (!planarXZ)
                        {
                            values = yPass(values, weights, valueIndex);
                            assignPassDensityToVoxel(values, valueIndex);
                        }

                        if (!planarXY)
                        {
                            values = zPass(values, weights, valueIndex);
                            assignPassDensityToVoxel(values, valueIndex);
                        }
                    }

                    else
                    {
                        if (!planarXY)
                        {
                            values = zPass(values, weights, valueIndex);
                            assignPassDensityToVoxel(values, valueIndex);
                        }

                        if (!planarXZ)
                        {
                            values = yPass(values, weights, valueIndex);
                            assignPassDensityToVoxel(values, valueIndex);
                        }

                        if (!planarYZ)
                        {
                            values = xPass(values, weights, valueIndex);
                            assignPassDensityToVoxel(values, valueIndex);
                        }
                    }
                }
            }
        }

        //-------------

        double[] xPass(double[] newValues, double[] weights, int type)
        {
            double[] neighbourSum = new double[activeVoxels.Length];

            //calculate density for each voxel taking into account whether voxel is active
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];

                //diffuse
                if (tridimensional)
                {
                    int weightIndex = 0;

                    for (int x = -diffuseRange; x <= diffuseRange; x++)
                    {
                        int d_xID = V.idX + x;

                        if (wrapBoundaries)
                        {
                            if (d_xID < 0) d_xID += resX;
                            if (d_xID > resX - 1) d_xID -= resX;
                        }

                        if (d_xID >= 0 && d_xID < resX)
                        {
                            if (voxels[d_xID, V.idY, V.idZ] != null)
                            {
                                Voxel neighbour = voxels[d_xID, V.idY, V.idZ];

                                if (neighbour.maxDensity != 0)
                                {
                                    switch (type)
                                    {
                                        case 0:
                                            if (neighbour.minDensity != -1)
                                            {
                                                neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                            }
                                            break;

                                        case 1:
                                            if (neighbour.maxDensity != -1)
                                            {
                                                neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                            }
                                            break;

                                        case 2:
                                            if (neighbour.speedMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 3:
                                            if (neighbour.sensorDistanceMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 4:
                                            if (neighbour.sensorAngleMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 5:
                                            if (neighbour.rotationAngleMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 6:
                                            if (neighbour.food != -1)
                                            {
                                                neighbourSum[i] += neighbour.food * weights[weightIndex];
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        weightIndex++;
                    }
                }
                else //tridimensional == false
                {
                    if (planarXY)
                    {
                        int weightIndex = 0;

                        for (int x = -diffuseRange; x <= diffuseRange; x++)
                        {
                            int d_xID = V.idX + x;

                            if (wrapBoundaries)
                            {
                                if (d_xID < 0) d_xID += resX;
                                if (d_xID > resX - 1) d_xID -= resX;
                            }

                            if (d_xID >= 0 && d_xID < resX)
                            {
                                if (voxels[d_xID, V.idY, 0] != null)
                                {
                                    Voxel neighbour = voxels[d_xID, V.idY, 0];

                                    if (neighbour.maxDensity != 0)
                                    {
                                        switch (type)
                                        {
                                            case 0:
                                                if (neighbour.minDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 1:
                                                if (neighbour.maxDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 2:
                                                if (neighbour.speedMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 3:
                                                if (neighbour.sensorDistanceMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 4:
                                                if (neighbour.sensorAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 5:
                                                if (neighbour.rotationAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 6:
                                                if (neighbour.food != -1)
                                                {
                                                    neighbourSum[i] += neighbour.food * weights[weightIndex];
                                                }
                                                break;
                                        }
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                    if (planarXZ)
                    {
                        int weightIndex = 0;

                        for (int x = -diffuseRange; x <= diffuseRange; x++)
                        {
                            int d_xID = V.idX + x;

                            if (wrapBoundaries)
                            {
                                if (d_xID < 0) d_xID += resX;
                                if (d_xID > resX - 1) d_xID -= resX;
                            }

                            if (d_xID >= 0 && d_xID < resX)
                            {
                                if (voxels[d_xID, 0, V.idZ] != null)
                                {
                                    Voxel neighbour = voxels[d_xID, 0, V.idZ];

                                    if (neighbour.maxDensity != 0)
                                    {
                                        switch (type)
                                        {
                                            case 0:
                                                if (neighbour.minDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 1:
                                                if (neighbour.maxDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 2:
                                                if (neighbour.speedMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 3:
                                                if (neighbour.sensorDistanceMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 4:
                                                if (neighbour.sensorAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 5:
                                                if (neighbour.rotationAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 6:
                                                if (neighbour.food != -1)
                                                {
                                                    neighbourSum[i] += neighbour.food * weights[weightIndex];
                                                }
                                                break;
                                        }
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXY || planarXZ)
                {
                    //calculate new values
                    switch (type)
                    {
                        case 0:
                            newValues[i] = V.minDensity * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 1:
                            newValues[i] = V.maxDensity * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 2:
                            newValues[i] = V.speedMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 3:
                            newValues[i] = V.sensorDistanceMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 4:
                            newValues[i] = V.sensorAngleMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 5:
                            newValues[i] = V.rotationAngleMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 6:
                            newValues[i] = V.food * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;
                    }

                    //keep min and max density inside bounds
                    if (type == 0 && V.minDensity != -1)
                    {
                        if (V.minDensity < 0) V.minDensity = 0;
                        if (V.minDensity > 1) V.minDensity = 1;
                    }

                    if (type == 1  && V.maxDensity != -1)
                    {
                        if (V.maxDensity < 0) V.maxDensity = 0;
                        if (V.maxDensity > 1) V.maxDensity = 1;
                    }
                }
            }
            );

            return newValues;
        }

        double[] yPass(double[] newValues, double[] weights, int type)
        {
            double[] neighbourSum = new double[activeVoxels.Length];

            //calculate density for each voxel taking into account whether voxel is active
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];

                //diffuse
                if (tridimensional)
                {
                    int weightIndex = 0;

                    for (int y = -diffuseRange; y <= diffuseRange; y++)
                    {
                        int d_yID = V.idY + y;

                        if (wrapBoundaries)
                        {
                            if (d_yID < 0) d_yID += resY;
                            if (d_yID > resY - 1) d_yID -= resY;
                        }

                        if (d_yID >= 0 && d_yID < resY)
                        {
                            if (voxels[V.idX, d_yID, V.idZ] != null)
                            {
                                Voxel neighbour = voxels[V.idX, d_yID, V.idZ];

                                if (neighbour.maxDensity != 0)
                                {
                                    switch (type)
                                    {
                                        case 0:
                                            if (neighbour.minDensity != -1)
                                            {
                                                neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                            }
                                            break;

                                        case 1:
                                            if (neighbour.maxDensity != -1)
                                            {
                                                neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                            }
                                            break;

                                        case 2:
                                            if (neighbour.speedMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 3:
                                            if (neighbour.sensorDistanceMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 4:
                                            if (neighbour.sensorAngleMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 5:
                                            if (neighbour.rotationAngleMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 6:
                                            if (neighbour.food != -1)
                                            {
                                                neighbourSum[i] += neighbour.food * weights[weightIndex];
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        weightIndex++;
                    }
                }
                else //tridimensional == false
                {
                    if (planarXY)
                    {
                        int weightIndex = 0;

                        for (int y = -diffuseRange; y <= diffuseRange; y++)
                        {
                            int d_yID = V.idY + y;

                            if (wrapBoundaries)
                            {
                                if (d_yID < 0) d_yID += resY;
                                if (d_yID > resY - 1) d_yID -= resY;
                            }

                            if (d_yID >= 0 && d_yID < resY)
                            {
                                if (voxels[V.idX, d_yID, 0] != null)
                                {
                                    Voxel neighbour = voxels[V.idX, d_yID, 0];

                                    if (neighbour.maxDensity != 0)
                                    {
                                        switch (type)
                                        {
                                            case 0:
                                                if (neighbour.minDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 1:
                                                if (neighbour.maxDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 2:
                                                if (neighbour.speedMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 3:
                                                if (neighbour.sensorDistanceMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 4:
                                                if (neighbour.sensorAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 5:
                                                if (neighbour.rotationAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 6:
                                                if (neighbour.food != -1)
                                                {
                                                    neighbourSum[i] += neighbour.food * weights[weightIndex];
                                                }
                                                break;
                                        }
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                    else if (planarYZ)
                    {
                        int weightIndex = 0;

                        for (int y = -diffuseRange; y <= diffuseRange; y++)
                        {
                            int d_yID = V.idY + y;

                            if (wrapBoundaries)
                            {
                                if (d_yID < 0) d_yID += resY;
                                if (d_yID > resY - 1) d_yID -= resY;
                            }

                            if (d_yID >= 0 && d_yID < resY)
                            {
                                if (voxels[0, d_yID, V.idZ] != null)
                                {
                                    Voxel neighbour = voxels[0, d_yID, V.idZ];

                                    if (neighbour.maxDensity != 0)
                                    {
                                        switch (type)
                                        {
                                            case 0:
                                                if (neighbour.minDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 1:
                                                if (neighbour.maxDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 2:
                                                if (neighbour.speedMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 3:
                                                if (neighbour.sensorDistanceMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 4:
                                                if (neighbour.sensorAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 5:
                                                if (neighbour.rotationAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 6:
                                                if (neighbour.food != -1)
                                                {
                                                    neighbourSum[i] += neighbour.food * weights[weightIndex];
                                                }
                                                break;
                                        }
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXY || planarYZ)
                {
                    //calculate new values
                    switch (type)
                    {
                        case 0:
                            newValues[i] = V.minDensity * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 1:
                            newValues[i] = V.maxDensity * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 2:
                            newValues[i] = V.speedMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 3:
                            newValues[i] = V.sensorDistanceMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 4:
                            newValues[i] = V.sensorAngleMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 5:
                            newValues[i] = V.rotationAngleMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 6:
                            newValues[i] = V.food * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;
                    }

                    //keep min and max density inside bounds
                    if (type == 0 && V.minDensity != -1)
                    {
                        if (V.minDensity < 0) V.minDensity = 0;
                        if (V.minDensity > 1) V.minDensity = 1;
                    }

                    if (type == 1 && V.maxDensity != -1)
                    {
                        if (V.maxDensity < 0) V.maxDensity = 0;
                        if (V.maxDensity > 1) V.maxDensity = 1;
                    }
                }
            }
            );

            return newValues;
        }

        double[] zPass(double[] newValues, double[] weights, int type)
        {
            double[] neighbourSum = new double[activeVoxels.Length];

            //calculate density for each voxel taking into account whether voxel is active
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];
                //diffuse
                if (tridimensional)
                {
                    int weightIndex = 0;

                    for (int z = -diffuseRange; z <= diffuseRange; z++)
                    {
                        int d_zID = V.idZ + z;

                        if (wrapBoundaries)
                        {
                            if (d_zID < 0) d_zID += resZ;
                            if (d_zID > resZ - 1) d_zID -= resZ;
                        }

                        if (d_zID >= 0 && d_zID < resZ)
                        {
                            if (voxels[V.idX, V.idY, d_zID] != null)
                            {
                                Voxel neighbour = voxels[V.idX, V.idY, d_zID];

                                if (neighbour.maxDensity != 0)
                                {
                                    switch (type)
                                    {
                                        case 0:
                                            if (neighbour.minDensity != -1)
                                            {
                                                neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                            }
                                            break;

                                        case 1:
                                            if (neighbour.maxDensity != -1)
                                            {
                                                neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                            }
                                            break;

                                        case 2:
                                            if (neighbour.speedMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 3:
                                            if (neighbour.sensorDistanceMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 4:
                                            if (neighbour.sensorAngleMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 5:
                                            if (neighbour.rotationAngleMultiplier != -1)
                                            {
                                                neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                            }
                                            break;

                                        case 6:
                                            if (neighbour.food != -1)
                                            {
                                                neighbourSum[i] += neighbour.food * weights[weightIndex];
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        weightIndex++;
                    }
                }
                else //tridimensional == false
                {
                    if (planarXZ)
                    {
                        int weightIndex = 0;

                        for (int z = -diffuseRange; z <= diffuseRange; z++)
                        {
                            int d_zID = V.idZ + z;

                            if (wrapBoundaries)
                            {
                                if (d_zID < 0) d_zID += resZ;
                                if (d_zID > resZ - 1) d_zID -= resZ;
                            }

                            if (d_zID >= 0 && d_zID < resZ)
                            {
                                if (voxels[V.idX, 0, d_zID] != null)
                                {
                                    Voxel neighbour = voxels[V.idX, 0, d_zID];

                                    if (neighbour.maxDensity != 0)
                                    {
                                        switch (type)
                                        {
                                            case 0:
                                                if (neighbour.minDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 1:
                                                if (neighbour.maxDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 2:
                                                if (neighbour.speedMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 3:
                                                if (neighbour.sensorDistanceMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 4:
                                                if (neighbour.sensorAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 5:
                                                if (neighbour.rotationAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 6:
                                                if (neighbour.food != -1)
                                                {
                                                    neighbourSum[i] += neighbour.food * weights[weightIndex];
                                                }
                                                break;
                                        }
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                    else if (planarYZ)
                    {
                        int weightIndex = 0;

                        for (int z = -diffuseRange; z <= diffuseRange; z++)
                        {
                            int d_zID = V.idZ + z;

                            if (wrapBoundaries)
                            {
                                if (d_zID < 0) d_zID += resZ;
                                if (d_zID > resZ - 1) d_zID -= resZ;
                            }

                            if (d_zID >= 0 && d_zID < resZ)
                            {
                                if (voxels[0, V.idY, d_zID] != null)
                                {
                                    Voxel neighbour = voxels[0, V.idY, d_zID];

                                    if (neighbour.maxDensity != 0)
                                    {
                                        switch (type)
                                        {
                                            case 0:
                                                if (neighbour.minDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.minDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 1:
                                                if (neighbour.maxDensity != -1)
                                                {
                                                    neighbourSum[i] += neighbour.maxDensity * weights[weightIndex];
                                                }
                                                break;

                                            case 2:
                                                if (neighbour.speedMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.speedMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 3:
                                                if (neighbour.sensorDistanceMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorDistanceMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 4:
                                                if (neighbour.sensorAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.sensorAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 5:
                                                if (neighbour.rotationAngleMultiplier != -1)
                                                {
                                                    neighbourSum[i] += neighbour.rotationAngleMultiplier * weights[weightIndex];
                                                }
                                                break;

                                            case 6:
                                                if (neighbour.food != -1)
                                                {
                                                    neighbourSum[i] += neighbour.food * weights[weightIndex];
                                                }
                                                break;
                                        }
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXZ || planarYZ)
                {
                    //calculate new values
                    switch (type)
                    {
                        case 0:
                            newValues[i] = V.minDensity * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 1:
                            newValues[i] = V.maxDensity * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 2:
                            newValues[i] = V.speedMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 3:
                            newValues[i] = V.sensorDistanceMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 4:
                            newValues[i] = V.sensorAngleMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 5:
                            newValues[i] = V.rotationAngleMultiplier * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;

                        case 6:
                            newValues[i] = V.food * (1 - diffuse) + diffuse * neighbourSum[i];
                            break;
                    }

                    //keep min and max density inside bounds
                    if (type == 0 && V.minDensity != -1)
                    {
                        if (V.minDensity < 0) V.minDensity = 0;
                        if (V.minDensity > 1) V.minDensity = 1;
                    }

                    if (type == 1 && V.maxDensity != -1)
                    {
                        if (V.maxDensity < 0) V.maxDensity = 0;
                        if (V.maxDensity > 1) V.maxDensity = 1;
                    }
                }
            }
            );

            return newValues;
        }

        //-------------

        double[] precomputeWeights(int diffuseRange)
        {
            int total = (diffuseRange + 1) * 2 + 1;
            double[] weights = new double[total];

            double weightSum = 0;

            for (int i = 0; i < total; i++)
            {
                double n = Math.PI * (i - (diffuseRange + 1)) / (diffuseRange + 1);
                weights[i] = (1 + Math.Cos(n)) / 2;

                weightSum += weights[i];
            }

            double[] weightsWithoutEnds = new double[total - 2];
            for (int i = 1; i < total - 1; i++)
            {
                weightsWithoutEnds[i - 1] = weights[i] / weightSum;
            }

            return weightsWithoutEnds;
        }

        //-------------

        void assignPassDensityToVoxel(double[] valueMultipliers, int type)
        {
            //assign the temporary stored value (calculated in the previous steps) to voxel density
            Parallel.For(0, activeVoxels.Length, i =>
            {

                Voxel V = activeVoxels[i];

                switch (type)
                {
                    case 0:
                        V.minDensity = valueMultipliers[i];
                        if (V.minDensity != -1)
                        {
                            if (V.minDensity < 0) V.minDensity = 0;
                            if (V.minDensity > 1) V.minDensity = 1;
                        }
                        break;

                    case 1:
                        V.maxDensity = valueMultipliers[i];
                        if (V.maxDensity != -1)
                        {
                            if (V.maxDensity < 0) V.maxDensity = 0;
                            if (V.maxDensity > 1) V.maxDensity = 1;
                        }
                        break;

                    case 2:
                        V.speedMultiplier = valueMultipliers[i];
                        break;

                    case 3:
                        V.sensorDistanceMultiplier = valueMultipliers[i];
                        break;

                    case 4:
                        V.sensorAngleMultiplier = valueMultipliers[i];
                        break;

                    case 5:
                        V.rotationAngleMultiplier = valueMultipliers[i];
                        break;

                    case 6:
                        V.food = valueMultipliers[i];
                        break;
                }
            }
            );
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Nuclei3.Properties.Resources.EnvironmentWithValues;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("a8c1c617-ce15-5e8b-a4d9-f7322e1d5d61"); }
        }
    }
}
