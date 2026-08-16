using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;

using Rhino.Geometry;
using GH_IO.Serialization;

namespace Nuclei3
{
    public class Voxels_OR : GH_Component, IGH_VariableParameterComponent
    {

        private string m_dataTest = "";

        public string DataTest
        {
            get
            {
                return m_dataTest;
            }
            set
            {
                m_dataTest = value;
                Message = m_dataTest;
            }
        }

        /// <summary>
        /// Initializes a new instance of the Voxels_Merge class.
        /// </summary>
        public Voxels_OR()
              : base("Voxel Selection Union", "Voxel Selection Union",
              "Perform Union on Two or More Voxel Values (OR)",
              "Nuclei3", " Environment")
        {
            DataTest = "";
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Voxel", "V1", "Connects to Voxels", GH_ParamAccess.item);
            //1
            pManager.AddGenericParameter("Voxel", "V2", "Connects to Voxels", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
        }

        #region menu items

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("Minimum", this.min);
            writer.SetBoolean("Maximum", this.max);
            writer.SetBoolean("Average", this.average);

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            this.min = false;
            reader.TryGetBoolean("Minimum", ref this.min);

            this.max = false;
            reader.TryGetBoolean("Maximum", ref this.max);

            this.average = true;
            reader.TryGetBoolean("Average", ref this.average);

            return base.Read(reader);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            var minToggle = Menu_AppendItem(menu, "Minimum", minHandler, true, this.min);
            minToggle.ToolTipText = "Minimum";

            var maxToggle = Menu_AppendItem(menu, "Maximum", maxHandler, true, this.max);
            maxToggle.ToolTipText = "Maximum";

            var averageToggle = Menu_AppendItem(menu, "Average", averageHandler, true, this.average);
            averageToggle.ToolTipText = "Average";
        }

        protected void handler(object sender, EventArgs e)
        {
            this.min = !this.min;

            this.max = !this.max;

            this.average = !this.average;

            this.ExpireSolution(true);
        }

        protected void minHandler(object sender, EventArgs e)
        {
            this.min = true;
            this.max = false;
            this.average = false;
            this.ExpireSolution(true);
        }

        protected void maxHandler(object sender, EventArgs e)
        {
            this.min = false;
            this.max = true;
            this.average = false;
            this.ExpireSolution(true);
        }

        protected void averageHandler(object sender, EventArgs e)
        {
            this.min = false;
            this.max = false;
            this.average = true;
            this.ExpireSolution(true);
        }

        #endregion

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.senary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (trySolveWithSidecar(DA))
            {
                return;
            }

            //determine voxel settings
            DA.GetData(0, ref inputVoxels);

            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

            double _voxelSize = Globals.voxelSize;

            //create empty voxels
            voxels = new Voxel[resX, resY, resZ];

            if (average)
            {
                //create "0" value arrays

                //minDensity
                double[,,] _minDensity = new double[resX, resY, resZ];
                int[,,] _minDensity_Counter = new int[resX, resY, resZ];

                //maxDensity
                double[,,] _maxDensity = new double[resX, resY, resZ];
                int[,,] _maxDensity_Counter = new int[resX, resY, resZ];

                //speedMultiplier
                double[,,] _speedMultiplier = new double[resX, resY, resZ];
                int[,,] _speedMultiplier_Counter = new int[resX, resY, resZ];

                //sensorDistanceMultiplier
                double[,,] _sensorDistanceMultiplier = new double[resX, resY, resZ];
                int[,,] _sensorDistanceMultiplier_Counter = new int[resX, resY, resZ];

                //sensorAngleMultiplier
                double[,,] _sensorAngleMultiplier = new double[resX, resY, resZ];
                int[,,] _sensorAngleMultiplier_Counter = new int[resX, resY, resZ];

                //rotationAngleMultiplier
                double[,,] _rotationAngleMultiplier = new double[resX, resY, resZ];
                int[,,] _rotationAngleMultiplier_Counter = new int[resX, resY, resZ];

                //foodMultiplier
                double[,,] _food = new double[resX, resY, resZ];
                int[,,] _food_Counter = new int[resX, resY, resZ];

                //voxelVector
                Vector3d[,,] _voxelVector = new Vector3d[resX, resY, resZ];

                //frequency
                int[,,] _frequency = new int[resX, resY, resZ];
                int[,,] _frequency_counter = new int[resX, resY, resZ];

                for (int p = 0; p < Params.Input.Count; p++)
                {
                    if (DA.GetData(p, ref inputVoxels))
                    {

                        //check to see if input voxel comes from construct voxels and is null
                        bool fromConstructVoxels = false;
                        int activeVoxelsCounter = 0;

                        //count active voxels
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

                        //if there are 0 active voxels then set boolean to true
                        if (activeVoxelsCounter == 0) fromConstructVoxels = true;

                        //populate value and counter array lists
                        Parallel.For(0, resX, i =>
                        {
                            for (int j = 0; j < resY; j++)
                            {
                                for (int k = 0; k < resZ; k++)
                                {
                                    if (inputVoxels[i, j, k] != null)
                                    {
                                        Voxel initialV = inputVoxels[i, j, k];

                                        //if not instantiated already or input comes from construct voxels, instantiate voxel NOW
                                        if (voxels[i, j, k] == null)
                                        {
                                            voxels[i, j, k] = new Voxel(_voxelSize, i, j, k);
                                        }

                                        Voxel V = voxels[i, j, k];

                                        if (initialV.minDensity != -1)
                                        {
                                            _minDensity[i, j, k] += initialV.minDensity;
                                            _minDensity_Counter[i, j, k]++;
                                        }

                                        if (initialV.maxDensity != -1)
                                        {
                                            _maxDensity[i, j, k] += initialV.maxDensity;
                                            _maxDensity_Counter[i, j, k]++;
                                        }

                                        if (initialV.speedMultiplier != -1)
                                        {
                                            _speedMultiplier[i, j, k] += initialV.speedMultiplier;
                                            _speedMultiplier_Counter[i, j, k]++;
                                        }

                                        if (initialV.sensorDistanceMultiplier != -1)
                                        {
                                            _sensorDistanceMultiplier[i, j, k] += initialV.sensorDistanceMultiplier;
                                            _sensorDistanceMultiplier_Counter[i, j, k]++;
                                        }

                                        if (initialV.sensorAngleMultiplier != -1)
                                        {
                                            _sensorAngleMultiplier[i, j, k] += initialV.sensorAngleMultiplier;
                                            _sensorAngleMultiplier_Counter[i, j, k]++;
                                        }

                                        if (initialV.rotationAngleMultiplier != -1)
                                        {
                                            _rotationAngleMultiplier[i, j, k] += initialV.rotationAngleMultiplier;
                                            _rotationAngleMultiplier_Counter[i, j, k]++;
                                        }

                                        if (initialV.food != -1)
                                        {
                                            _food[i, j, k] += initialV.food;
                                            _food_Counter[i, j, k]++;
                                        }

                                        _voxelVector[i, j, k] += initialV.voxelVector;

                                        _frequency[i, j, k] += initialV.frequency;
                                        _frequency_counter[i, j, k]++;
                                    } 

                                    if(inputVoxels[i,j,k] == null && fromConstructVoxels)
                                    {
                                        if (voxels[i, j, k] == null)
                                        {
                                            voxels[i, j, k] = new Voxel(_voxelSize, i, j, k);
                                        }
                                    }
                                }
                            }
                        }
                        );
                    }
                }

                //assign values to voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if (voxels[i, j, k] != null)
                            {
                                Voxel V = voxels[i, j, k];

                                if (_minDensity_Counter[i, j, k] != 0) V.minDensity = _minDensity[i,j,k] / _minDensity_Counter[i, j, k];
                                else V.minDensity = -1;

                                if (_maxDensity_Counter[i, j, k] != 0) V.maxDensity = _maxDensity[i, j, k] / _maxDensity_Counter[i, j, k];
                                else V.maxDensity = -1;


                                if (_speedMultiplier_Counter[i, j, k] != 0) V.speedMultiplier = _speedMultiplier[i, j, k] / _speedMultiplier_Counter[i, j, k];
                                else V.speedMultiplier = -1;

                                if (_sensorDistanceMultiplier_Counter[i, j, k] != 0) V.sensorDistanceMultiplier = _sensorDistanceMultiplier[i, j, k] / _sensorDistanceMultiplier_Counter[i, j, k];
                                else V.sensorDistanceMultiplier = -1;


                                if (_sensorAngleMultiplier_Counter[i, j, k] != 0) V.sensorAngleMultiplier = _sensorAngleMultiplier[i, j, k] / _sensorAngleMultiplier_Counter[i, j, k];
                                else V.sensorAngleMultiplier = -1;

                                if (_rotationAngleMultiplier_Counter[i, j, k] != 0) V.rotationAngleMultiplier = _rotationAngleMultiplier[i, j, k] / _rotationAngleMultiplier_Counter[i, j, k];
                                else V.rotationAngleMultiplier = -1;

                                if (_food_Counter[i, j, k] != 0) V.food = _food[i, j, k] / _food_Counter[i, j, k];
                                else V.food = -1;


                                Vector3d voxelVector = _voxelVector[i, j, k];
                                voxelVector.Unitize();
                                V.voxelVector = voxelVector;

                                if (_frequency_counter[i, j, k] != 0) V.frequency = System.Convert.ToInt32(_frequency[i, j, k] / _frequency_counter[i, j, k]);
                                else V.frequency = 1;
                                if (V.frequency < 1) V.frequency = 1;
                            }
                        }
                    }
                }
                );

                
            }

            if (min)
            {
                //create "min" dummy values

                //minDensity
                double[,,] _minDensity = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _minDensity[i, j, k] = 999;
                        }
                    }
                }
                );

                //maxDensity
                double[,,] _maxDensity = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _maxDensity[i, j, k] = 999;
                        }
                    }
                }
                );

                //speedMultiplier
                double[,,] _speedMultiplier = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _speedMultiplier[i, j, k] = 999;
                        }
                    }
                }
                );

                //sensorDistanceMultiplier
                double[,,] _sensorDistanceMultiplier = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _sensorDistanceMultiplier[i, j, k] = 999;
                        }
                    }
                }
                );

                //sensorAngleMultiplier
                double[,,] _sensorAngleMultiplier = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _sensorAngleMultiplier[i, j, k] = 999;
                        }
                    }
                }
                );

                //rotationAngleMultiplier
                double[,,] _rotationAngleMultiplier = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _rotationAngleMultiplier[i, j, k] = 999;
                        }
                    }
                }
                );

                //foodMultiplier
                double[,,] _food = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _food[i, j, k] = 999;
                        }
                    }
                }
                );

                //voxelVector
                Vector3d[,,] _voxelVector = new Vector3d[resX, resY, resZ];

                //frequency
                int[,,] _frequency = new int[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _frequency[i, j, k] = 999;
                        }
                    }
                }
                );

                for (int p = 0; p < Params.Input.Count; p++)
                {
           
                    if (DA.GetData(p, ref inputVoxels))
                    {

                        //check to see if input voxel comes from construct voxels and is null
                        bool fromConstructVoxels = false;
                        int activeVoxelsCounter = 0;

                        //count active voxels
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

                        //if there are 0 active voxels then set boolean to true
                        if (activeVoxelsCounter == 0) fromConstructVoxels = true;

                        //populate value and counter array lists
                        Parallel.For(0, resX, i =>
                        {
                            for (int j = 0; j < resY; j++)
                            {
                                for (int k = 0; k < resZ; k++)
                                {
                                    if (inputVoxels[i, j, k] != null)
                                    {
                                        Voxel initialV = inputVoxels[i, j, k];

                                        //if not instantiated already, instantiate voxel NOW
                                        if (voxels[i, j, k] == null)
                                        {
                                            voxels[i, j, k] = new Voxel(_voxelSize, i, j, k);
                                        }

                                        Voxel V = voxels[i, j, k];

                                        if (initialV.minDensity != -1)
                                        {
                                            if (initialV.minDensity < _minDensity[i,j,k]) _minDensity[i,j,k] = initialV.minDensity;
                                        }

                                        if (initialV.maxDensity != -1)
                                        {
                                            if (initialV.maxDensity < _maxDensity[i, j, k]) _maxDensity[i, j, k] = initialV.maxDensity;
                                        }

                                        if (initialV.speedMultiplier != -1)
                                        {
                                            if (initialV.speedMultiplier < _speedMultiplier[i, j, k]) _speedMultiplier[i, j, k] = initialV.speedMultiplier;
                                        }

                                        if (initialV.sensorDistanceMultiplier != -1)
                                        {
                                            if (initialV.sensorDistanceMultiplier < _sensorDistanceMultiplier[i, j, k]) _sensorDistanceMultiplier[i, j, k] = initialV.sensorDistanceMultiplier;
                                        }

                                        if (initialV.sensorAngleMultiplier != -1)
                                        {
                                            if (initialV.sensorAngleMultiplier < _sensorAngleMultiplier[i, j, k]) _sensorAngleMultiplier[i, j, k] = initialV.sensorAngleMultiplier;
                                        }

                                        if (initialV.rotationAngleMultiplier != -1)
                                        {
                                            if (initialV.rotationAngleMultiplier < _rotationAngleMultiplier[i, j, k]) _rotationAngleMultiplier[i, j, k] = initialV.rotationAngleMultiplier;
                                        }

                                        if (initialV.food != -1)
                                        {
                                            if (initialV.food < _food[i, j, k]) _food[i, j, k] = initialV.food;
                                        }

                                        _voxelVector[i, j, k] += initialV.voxelVector;

                                        if (initialV.frequency < _frequency[i, j, k]) _frequency[i, j, k] = initialV.frequency;
                                    }

                                    if (inputVoxels[i, j, k] == null && fromConstructVoxels)
                                    {
                                        if (voxels[i, j, k] == null)
                                        {
                                            voxels[i, j, k] = new Voxel(_voxelSize, i, j, k);
                                        }
                                    }
                                }
                            }
                        }
                        );
                    }
                }

                //assign values to voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if (voxels[i, j, k] != null)
                            {
                                Voxel V = voxels[i, j, k];

                                if(_minDensity[i, j, k] != 999) V.minDensity = _minDensity[i, j, k];

                                if (_maxDensity[i, j, k] != 999) V.maxDensity = _maxDensity[i, j, k];


                                if (_speedMultiplier[i,j,k] != 999) V.speedMultiplier = _speedMultiplier[i,j,k];

                                if (_sensorDistanceMultiplier[i, j, k] != 999) V.sensorDistanceMultiplier = _sensorDistanceMultiplier[i, j, k];


                                if (_sensorAngleMultiplier[i, j, k] != 999) V.sensorAngleMultiplier = _sensorAngleMultiplier[i, j, k];

                                if (_rotationAngleMultiplier[i, j, k] != 999) V.rotationAngleMultiplier = _rotationAngleMultiplier[i, j, k];

                                if (_food[i, j, k] != 999) V.food = _food[i, j, k];


                                Vector3d voxelVector = _voxelVector[i, j, k];
                                voxelVector.Unitize();
                                V.voxelVector = voxelVector;

                                if (_frequency[i, j, k] != 999) V.frequency = _frequency[i, j, k];
                            }
                        }
                    }
                }
                );
            }

            if (max)
            {
                //create "max" dummy values

                //minDensity
                double[,,] _minDensity = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _minDensity[i, j, k] = -999;
                        }
                    }
                }
                );

                //maxDensity
                double[,,] _maxDensity = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _maxDensity[i, j, k] = -999;
                        }
                    }
                }
                );

                //speedMultiplier
                double[,,] _speedMultiplier = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _speedMultiplier[i, j, k] = -999;
                        }
                    }
                }
                );

                //sensorDistanceMultiplier
                double[,,] _sensorDistanceMultiplier = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _sensorDistanceMultiplier[i, j, k] = -999;
                        }
                    }
                }
                );

                //sensorAngleMultiplier
                double[,,] _sensorAngleMultiplier = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _sensorAngleMultiplier[i, j, k] = -999;
                        }
                    }
                }
                );

                //rotationAngleMultiplier
                double[,,] _rotationAngleMultiplier = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _rotationAngleMultiplier[i, j, k] = -999;
                        }
                    }
                }
                );

                //foodMultiplier
                double[,,] _food = new double[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _food[i, j, k] = -999;
                        }
                    }
                }
                );

                //voxelVector
                Vector3d[,,] _voxelVector = new Vector3d[resX, resY, resZ];

                //frequency
                int[,,] _frequency = new int[resX, resY, resZ];
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            _frequency[i, j, k] = -999;
                        }
                    }
                }
                );



                for (int p = 0; p < Params.Input.Count; p++)
                {
                    if (DA.GetData(p, ref inputVoxels))
                    {

                        //check to see if input voxel comes from construct voxels and is null
                        bool fromConstructVoxels = false;
                        int activeVoxelsCounter = 0;

                        //count active voxels
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

                        //if there are 0 active voxels then set boolean to true
                        if (activeVoxelsCounter == 0) fromConstructVoxels = true;

                        //populate value and counter array lists
                        Parallel.For(0, resX, i =>
                        {
                            for (int j = 0; j < resY; j++)
                            {
                                for (int k = 0; k < resZ; k++)
                                {

                                    if (inputVoxels[i, j, k] != null)
                                    {
                                        Voxel initialV = inputVoxels[i, j, k];

                                        //if not instantiated already, instantiate voxel NOW
                                        if (voxels[i, j, k] == null)
                                        {
                                            voxels[i, j, k] = new Voxel(_voxelSize, i, j, k);
                                        }

                                        Voxel V = voxels[i, j, k];

                                        if (initialV.minDensity != -1)
                                        {
                                            if (initialV.minDensity > _minDensity[i, j, k]) _minDensity[i, j, k] = initialV.minDensity;
                                        }

                                        if (initialV.maxDensity != -1)
                                        {
                                            if (initialV.maxDensity > _maxDensity[i, j, k]) _maxDensity[i, j, k] = initialV.maxDensity;
                                        }

                                        if (initialV.speedMultiplier != -1)
                                        {
                                            if (initialV.speedMultiplier > _speedMultiplier[i, j, k]) _speedMultiplier[i, j, k] = initialV.speedMultiplier;
                                        }

                                        if (initialV.sensorDistanceMultiplier != -1)
                                        {
                                            if (initialV.sensorDistanceMultiplier > _sensorDistanceMultiplier[i, j, k]) _sensorDistanceMultiplier[i, j, k] = initialV.sensorDistanceMultiplier;
                                        }

                                        if (initialV.sensorAngleMultiplier != -1)
                                        {
                                            if (initialV.sensorAngleMultiplier > _sensorAngleMultiplier[i, j, k]) _sensorAngleMultiplier[i, j, k] = initialV.sensorAngleMultiplier;
                                        }

                                        if (initialV.rotationAngleMultiplier != -1)
                                        {
                                            if (initialV.rotationAngleMultiplier > _rotationAngleMultiplier[i, j, k]) _rotationAngleMultiplier[i, j, k] = initialV.rotationAngleMultiplier;
                                        }

                                        if (initialV.food != -1)
                                        {
                                            if (initialV.food > _food[i, j, k]) _food[i, j, k] = initialV.food;
                                        }

                                        _voxelVector[i, j, k] += initialV.voxelVector;

                                        if (initialV.frequency > _frequency[i, j, k]) _frequency[i, j, k] = initialV.frequency;
                                    }

                                    if (inputVoxels[i, j, k] == null && fromConstructVoxels)
                                    {
                                        if (voxels[i, j, k] == null)
                                        {
                                            voxels[i, j, k] = new Voxel(_voxelSize, i, j, k);
                                        }
                                    }
                                }
                            }
                        }
                        );
                    }
                }

                //assign values to voxels
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            if (voxels[i, j, k] != null)
                            {
                                Voxel V = voxels[i, j, k];

                                if (_minDensity[i, j, k] != -999) V.minDensity = _minDensity[i, j, k];

                                if (_maxDensity[i, j, k] != -999) V.maxDensity = _maxDensity[i, j, k];


                                if (_speedMultiplier[i, j, k] != -999) V.speedMultiplier = _speedMultiplier[i, j, k];

                                if (_sensorDistanceMultiplier[i, j, k] != -999) V.sensorDistanceMultiplier = _sensorDistanceMultiplier[i, j, k];


                                if (_sensorAngleMultiplier[i, j, k] != -999) V.sensorAngleMultiplier = _sensorAngleMultiplier[i, j, k];

                                if (_rotationAngleMultiplier[i, j, k] != -999) V.rotationAngleMultiplier = _rotationAngleMultiplier[i, j, k];

                                if (_food[i, j, k] != -999) V.food = _food[i, j, k];


                                Vector3d voxelVector = _voxelVector[i, j, k];
                                voxelVector.Unitize();
                                V.voxelVector = voxelVector;

                                if (_frequency[i, j, k] != -999) V.frequency = _frequency[i, j, k];
                            }
                        }
                    }
                }
                );
            }

            DA.SetData(0, voxels);

            if (min) this.Message = "Minimum";
            if (max) this.Message = "Maximum";
            if (average) this.Message = "Average";
        }

        //-------------------------------------------------------------------

        bool trySolveWithSidecar(IGH_DataAccess DA)
        {
            List<VoxelGridData> inputs = new List<VoxelGridData>();
            VoxelGridData first = null;

            for (int p = 0; p < Params.Input.Count; p++)
            {
                Voxel[,,] current = null;
                if (!DA.GetData(p, ref current) || current == null)
                {
                    continue;
                }

                VoxelGridData data = VoxelGridRegistry.GetOrCapture(current, Globals.voxelSize);
                if (first == null)
                {
                    first = data;
                }
                else if (data.ResX != first.ResX || data.ResY != first.ResY || data.ResZ != first.ResZ)
                {
                    return false;
                }

                inputs.Add(data);
            }

            if (inputs.Count == 0)
            {
                return false;
            }

            VoxelGridData outputData = VoxelGridCombiner.Union(inputs, currentMergeMode());
            voxels = outputData.ToVoxelArray(true);
            VoxelGridRegistry.Set(voxels, outputData);
            DA.SetData(0, voxels);

            if (min) this.Message = "Minimum";
            if (max) this.Message = "Maximum";
            if (average) this.Message = "Average";
            return true;
        }

        VoxelGridMergeMode currentMergeMode()
        {
            if (min) return VoxelGridMergeMode.Minimum;
            if (max) return VoxelGridMergeMode.Maximum;
            return VoxelGridMergeMode.Average;
        }

        //-------------------------------------------------------------------

        //inputs
        public bool min = false;
        public bool max = false;
        public bool average = true;
        
        Voxel[,,] inputVoxels;

        //-------------------------------------------------------------------

        //outputs
        Voxel[,,] voxels;

        //-------------------------------------------------------------------

        #region VARIABLE COMPONENT INTERFACE IMPLEMENTATION
        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {

            // Only insert parameters on input side. This can be changed if you like/need
            // side== GH_ParameterSide.Output
            if (side == GH_ParameterSide.Input)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int index)
        {
            // Only allowed to remove parameters if there are more than 2
            // from the input side
            if (side == GH_ParameterSide.Input && Params.Input.Count > 2)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int index)
        {

            // Has to return a parameter object!
            Grasshopper.Kernel.Parameters.Param_GenericObject param = new Grasshopper.Kernel.Parameters.Param_GenericObject();

            int count = 0;
            for (int i = 0; i < Params.Input.Count; i++)
            {
                count += i;
            }

            param.Name = "V" + (Params.Input.Count+1).ToString();
            param.NickName = param.Name;
            param.Description = "Voxels";
            param.Optional = true;
            return param;
        }


        public bool DestroyParameter(GH_ParameterSide side, int index)
        {
            //This function will be called when a parameter is about to be removed. 
            //You do not need to do anything, but this would be a good time to remove 
            //any event handlers that might be attached to the parameter in question.


            return true;
        }

        public void VariableParameterMaintenance()
        {
            //This method will be called when a closely related set of variable parameter operations completes. 
            //This would be a good time to ensure all Nicknames and parameter properties are correct. This method will also be 
            //called upon IO operations such as Open, Paste, Undo and Redo.

            //throw new NotImplementedException();
        }


        #endregion

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
                return Nuclei3.Properties.Resources.VoxelsUnion;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("e77e3067-0a34-5b11-89b2-e9725a510eb8"); }
        }
    }
}
