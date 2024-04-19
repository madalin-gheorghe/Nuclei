using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;


using Rhino;
using Rhino.Geometry;
using Rhino.Display;
using Rhino.DocObjects;

using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Drawing;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using static Nuclei3.ParticleGroup;

namespace Nuclei3
{
    public class Solver : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Solver class.
        /// </summary>
        public Solver()
          : base("Nuclei3 Solver", "Solver",
              "Where the magic happens",
              "Nuclei3", " Solver")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddBooleanParameter("Reset", "reset", "Reset Boolean", GH_ParamAccess.item);

            //1
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);

            //2
            pManager.AddParameter(new ParticleGroupParameter(), "Particles", "particles", "Connects to Particle Constructors", GH_ParamAccess.list);
            pManager[2].DataMapping = GH_DataMapping.Flatten;

            //3
            pManager.AddTextParameter("Solver Settings", "settings", "Connects to Settings", GH_ParamAccess.list);
            pManager[3].Optional = true;
            pManager[3].DataMapping = GH_DataMapping.Flatten;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Output Particles", "particles", "Output Particles", GH_ParamAccess.item);
            //1
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref reset);

            if (reset == true)
            {
                //iteration counter
                iteration = 0;
                antParticles = false;

                //utility
                random = new System.Random(89);
                precomputeAngles();
                createWanderVectors();

                //set voxels
                DA.GetData(1, ref inputVoxels);

                inheritVoxels();

                //read solver settings
                settings = new List<String>();
                DA.GetDataList(3, settings);
                readSolverSettings();

                //set particles
                inputParticleGroups = new List<ParticleGroup>();
                DA.GetType();
                DA.GetDataList(2, inputParticleGroups);

                particles = new List<Particle>();

                inheritParticleGroups();
                particleCheckParentVoxel();

                //global colors
                //initializeParticleColors();

                //ant utility
                if (antParticles)
                {
                    createAntAgeMultipliers();
                }

                this.Message = "Solution is Reset";
            }
            else
            {
                //read solver settings
                settings = new List<String>();
                DA.GetDataList(3, settings);
                readSolverSettings();

                inputParticleGroups = new List<ParticleGroup>();
                DA.GetType();
                DA.GetDataList(2, inputParticleGroups);
                updateParticleGroups();

                if (iteration < maxIterations)
                {
                    //run algorithm
                    if (iteration > 1)
                    {
                        particleSenseValuesAndVectors();
                        if (antParticles) particleSense_Ant();

                        particleMoveAndDeposit();
                    }

                    //particleDepositChemoattractors();
                    particleRecordTrail(); //careful with list.Add, better make arrays

                    //voxel logics
                    diffuseVoxels();
                    decayVoxels();

                    //reorder data
                    particleCheckParentVoxel();

                    //adaptive population
                    if (iteration > 1 && dynPop)
                    {
                        particleCheckNeighbourCount();
                        killParticles();
                        divideParticles();
                    }

                    iteration++;

                    this.Message = "Iteration: " + iteration;
                }

                //set outputs
                DA.SetData(0, particles);
                DA.SetData(1, voxels);
            }
        }

        //-------------------------------------------------------------------

        //inputs
        List<String> settings;

        List<ParticleGroup> inputParticleGroups;
        List<ParticleGroup> particleGroups;
        List<Particle> particles;

        Voxel[,,] inputVoxels;
        Voxel[,,] voxels;
        Voxel[] activeVoxels;

        /////////////////////////////////////////////

        //reset
        bool reset = true;

        //iteration
        int iteration = 0;
        int maxIterations = 100000;

        /////////////////////////////////////////////

        //voxel dimensions
        double voxelSize;
        int resX;
        int resY;
        int resZ;

        double dimX;
        double dimY;
        double dimZ;

        //voxel settings slime
        double diffuse = 0.1;
        int diffuseRange = 1;
        double decay = 0.03;
        bool wrapBoundaries = false;

        //voxel settings ant
        double foodDiffuseRate = 0.05;
        double foodDecayRate = 0.005;

        double baseDiffuseRate = 0.1;
        double baseDecayRate = 0.01;

        int diffuseRange_Ant = 1;

        //voxel particularities
        bool planarXY = false;
        bool planarXZ = false;
        bool planarYZ = false;
        bool tridimensional = false;

        /////////////////////////////////////////////

        //population settings
        bool dynPop = false;
        int minPopulation = 100;
        int maxPopulation = 20000;

        //division settings
        bool division = false;
        int divMinAge = 10;
        int divRange = 6;
        int minDivN = 100;
        int maxDivN = 500;
        int divFreq = 5;

        //death settings
        bool death = false;
        int dieMinAge = 10;
        int dieRange = 1;
        int minDieN = 0;
        int maxDieN = 100;
        int dieFreq = 5;

        //wander
        List<Vector3d> wanderVectors = new List<Vector3d>();

        //ant settings
        bool antParticles = false;

        double ageMultiplierBase = 1;
        double ageMultiplierFood = 1;
        int maxAge = 100;
        double minBase = 0.1;
        double minFood = 0.2;
        double max = 1;
        double[] multiplierBase;
        double[] multiplierFood;

        //ant slime settings
        double ant_slime = 0;
        double slime_antFood = 0;
        double slime_antBase = 0;

        //trail settings
        int trailSize = 0;
        int trailFreq = 1;

        /////////////////////////////////////////////

        //lists
        List<double> radAngle;

        /////////////////////////////////////////////

        //random
        System.Random random = new System.Random(89);

        /////////////////////////////////////////////

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
                            } else
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

            int range = 1;

            //assign maxDensity = 0.01 for boundary voxels
            Parallel.For(0, resX, i =>
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (voxels[i, j, k] != null)
                        {
                            Voxel V = voxels[i, j, k];

                            if (wrapBoundaries==false)
                            {
                                if (tridimensional)
                                {
                                    if (i == 0 || i == resX - 1 || j == 0 || j == resY - 1 || k == 0 || k == resZ - 1)
                                    {
                                        V.maxDensity = 0.01;
                                        V.boundary = true;
                                    }
                                } else if (planarXY)
                                {
                                    if (i == 0 || i == resX - 1 || j == 0 || j == resY - 1)
                                    {
                                        V.maxDensity = 0.01;
                                        V.boundary = true;
                                    }
                                } else if (planarXZ)
                                {
                                    if (i == 0 || i == resX - 1 || k == 0 || k == resZ - 1)
                                    {
                                        V.maxDensity = 0.01;
                                        V.boundary = true;
                                    }
                                } else if (planarYZ)
                                {
                                    if (j == 0 || j == resY - 1 || k == 0 || k == resZ - 1)
                                    {
                                        V.maxDensity = 0.01;
                                        V.boundary = true;
                                    }
                                }
                            }

                            //check neighbours
                            for (int u = i - range; u <= i + range; u++)
                            {
                                for (int v = j - range; v <= j + range; v++)
                                {
                                    for (int w = k - range; w <= k + range; w++)
                                    {
                                        if (u >= 0 && u < resX && v >= 0 && v < resY && w >= 0 && w < resZ)
                                        {
                                            if (voxels[u, v, w] == null)
                                            {
                                                V.maxDensity = 0.01;
                                                V.boundary = true;
                                                break;
                                            }
                                        } 
                                    }
                                }
                            }
                        }
                    }
                }
            }
            );
        }

        //-------------------------------------------------------------------

        void diffuseVoxels()
        {
            if (diffuse > 0)
            {
                double[] newVoxelDensity = new double[activeVoxels.Length];
                
                double[] weights = new double[diffuseRange * 2 + 1];
                weights = precomputeWeights(diffuseRange);

                if (iteration % 2 == 0)
                {
                    if (!planarYZ)
                    {
                        newVoxelDensity = xPass(newVoxelDensity, weights);
                        assignPassDensityToVoxel(newVoxelDensity);
                    }

                    if (!planarXZ)
                    {
                        newVoxelDensity = yPass(newVoxelDensity, weights);
                        assignPassDensityToVoxel(newVoxelDensity);
                    }

                    if (!planarXY)
                    {
                        newVoxelDensity = zPass(newVoxelDensity, weights);
                        assignPassDensityToVoxel(newVoxelDensity);
                    }
                }

                else
                {
                    if (!planarXY)
                    {
                        newVoxelDensity = zPass(newVoxelDensity, weights);
                        assignPassDensityToVoxel(newVoxelDensity);
                    }

                    if (!planarXZ)
                    {
                        newVoxelDensity = yPass(newVoxelDensity, weights);
                        assignPassDensityToVoxel(newVoxelDensity);
                    }

                    if (!planarYZ)
                    {
                        newVoxelDensity = xPass(newVoxelDensity, weights);
                        assignPassDensityToVoxel(newVoxelDensity);
                    }
                }

            }

            //ant particles
            if (antParticles)
            {
                if (baseDiffuseRate > 0 || foodDiffuseRate > 0)
                {
                    double[] weights_ant = new double[diffuseRange_Ant * 2 + 1];
                    weights_ant = precomputeWeights(diffuseRange_Ant);

                    if (iteration % 2 == 0)
                    {
                        if (!planarYZ)
                        {
                            ants_xPass(weights_ant);
                        }
                        if (!planarXZ)
                        {
                            ants_yPass(weights_ant);
                        }

                        if (!planarXY)
                        {
                            ants_zPass(weights_ant);
                        }
                    }

                    else
                    {
                        if (!planarXY)
                        {
                           ants_zPass(weights_ant);
                        }

                        if (!planarXZ)
                        {
                            ants_yPass(weights_ant);
                        }

                        if (!planarYZ)
                        {
                            ants_xPass(weights_ant);
                        }
                    }
                }
            }


            //zero density for voxels at borders
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];

                if (wrapBoundaries == false)
                {                  
                    if (tridimensional)
                    {
                        if (V.idX == 0 || V.idX == resX - 1 || V.idY == 0 || V.idY == resY - 1 || V.idZ == 0 || V.idZ == resZ - 1)
                        {
                            V.density = 0;
                            
                            //ant particles
                            if (antParticles)
                            {
                                V.towardsFoodPheromone = 0;
                            }
                        }
                    }
                    else
                    {
                        if (planarXY)
                        {
                            if (V.idX == 0 || V.idX == resX - 1 || V.idY == 0 || V.idY == resY - 1)
                            {
                                V.density = 0;

                                //ant particles
                                if (antParticles)
                                {
                                    V.towardsFoodPheromone = 0;
                                }
                            }
                        }
                        else if (planarXZ)
                        {
                            if (V.idX == 0 || V.idX == resX - 1 || V.idZ == 0 || V.idZ == resZ - 1)
                            {
                                V.density = 0;

                                //ant particles
                                if (antParticles)
                                {
                                    V.towardsFoodPheromone = 0;
                                }
                            }
                        }
                        else if (planarYZ)
                        {
                            if (V.idY == 0 || V.idY == resY - 1 || V.idZ == 0 || V.idZ == resZ - 1)
                            {
                                V.density = 0;

                                //ant particles
                                if (antParticles)
                                {
                                    V.towardsFoodPheromone = 0;
                                }

                            }
                        }
                    }
                }
            }
            );
        }

        //-------------

        double[] xPass(double[] newDensity, double[] weights)
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

                                /*
                                if (V.food != -1)
                                {
                                    if (V.particleCount != 0) neighbour.density += V.food / 2;
                                    if (V.particleCount == 0) neighbour.density += V.food;
                                }
                                */

                                if (neighbour.maxDensity != 0)
                                {
                                    neighbourSum[i] += neighbour.density * weights[weightIndex];
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

                                    /*
                                    if (V.food != -1)
                                    {
                                        if (V.particleCount != 0) neighbour.density += V.food / 2;
                                        if (V.particleCount == 0) neighbour.density += V.food;
                                    }
                                    */

                                    if (neighbour.maxDensity != 0)
                                    {
                                        neighbourSum[i] += neighbour.density * weights[weightIndex];
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

                                    /*
                                    if (V.food != -1)
                                    {
                                        if (V.particleCount != 0) neighbour.density += V.food / 2;
                                        if (V.particleCount == 0) neighbour.density += V.food;
                                    }
                                    */

                                    if (neighbour.maxDensity != 0)
                                    {
                                        neighbourSum[i] += neighbour.density * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXY || planarXZ)
                {
                    //calculate new density
                    newDensity[i] = V.density * (1 - diffuse) + diffuse * neighbourSum[i];

                    if (newDensity[i] > 1) newDensity[i] = 1;

                    //override values with staticDensity a.k.a. initial voxel values map
                    if (V.maxDensity != -1)
                    {
                        if (newDensity[i] > V.maxDensity) newDensity[i] = V.maxDensity;
                    }

                    if (V.minDensity != -1)
                    {
                        //if (newDensity[i] < V.minDensity) newDensity[i] = V.minDensity;
                        if (newDensity[i] > 0 && V.minDensity > newDensity[i]) newDensity[i] = V.minDensity;
                    }
                }
            }
            );

            return newDensity;
        }

        double[] yPass(double[] newDensity, double[] weights)
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

                                /*
                                if (V.food != -1)
                                {
                                    if (V.particleCount != 0) neighbour.density += V.food / 2;
                                    if (V.particleCount == 0) neighbour.density += V.food;
                                }
                                */

                                if (neighbour.maxDensity != 0)
                                {
                                    neighbourSum[i] += neighbour.density * weights[weightIndex];
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

                                    /*
                                    if (V.food != -1)
                                    {
                                        if (V.particleCount != 0) neighbour.density += V.food / 2;
                                        if (V.particleCount == 0) neighbour.density += V.food;
                                    }
                                    */

                                    if (neighbour.maxDensity != 0)
                                    {
                                        neighbourSum[i] += neighbour.density * weights[weightIndex];
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

                                    /*
                                    if (V.food != -1)
                                    {
                                        if (V.particleCount != 0) neighbour.density += V.food / 2;
                                        if (V.particleCount == 0) neighbour.density += V.food;
                                    }
                                    */

                                    if (neighbour.maxDensity != 0)
                                    {
                                        neighbourSum[i] += neighbour.density * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXY || planarYZ)
                {
                    //calculate new density
                    newDensity[i] = V.density * (1 - diffuse) + diffuse * neighbourSum[i];

                    if (newDensity[i] > 1) newDensity[i] = 1;

                    //override values with staticDensity a.k.a. initial voxel values map
                    if (V.maxDensity != -1)
                    {
                        if (newDensity[i] > V.maxDensity) newDensity[i] = V.maxDensity;
                    }

                    if (V.minDensity != -1)
                    {
                        //if (newDensity[i] < V.minDensity) newDensity[i] = V.minDensity;
                        if (newDensity[i] > 0 && V.minDensity > newDensity[i]) newDensity[i] = V.minDensity;
                    }
                }
            }
            );

            return newDensity;
        }

        double[] zPass(double[] newDensity, double[] weights)
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

                                /*
                                if (V.food != -1)
                                {
                                    if (V.particleCount != 0) neighbour.density += V.food / 2;
                                    if (V.particleCount == 0) neighbour.density += V.food;
                                }
                                */

                                if (neighbour.maxDensity != 0)
                                {
                                    neighbourSum[i] += neighbour.density * weights[weightIndex];
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

                                    /*
                                    if (V.food != -1)
                                    {
                                        if (V.particleCount != 0) neighbour.density += V.food / 2;
                                        if (V.particleCount == 0) neighbour.density += V.food;
                                    }
                                    */

                                    if (neighbour.maxDensity != 0)
                                    {
                                        neighbourSum[i] += neighbour.density * weights[weightIndex];
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

                                    /*
                                    if (V.food != -1)
                                    {
                                        if (V.particleCount != 0) neighbour.density += V.food / 2;
                                        if (V.particleCount == 0) neighbour.density += V.food;
                                    }
                                    */

                                    if (neighbour.maxDensity != 0)
                                    {
                                        neighbourSum[i] += neighbour.density * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXZ || planarYZ)
                {
                    //calculate new density
                    newDensity[i] = V.density * (1 - diffuse) + diffuse * neighbourSum[i];

                    if (newDensity[i] > 1) newDensity[i] = 1;

                    //override values with staticDensity a.k.a. initial voxel values map
                    if (V.maxDensity != -1)
                    {
                        if (newDensity[i] > V.maxDensity) newDensity[i] = V.maxDensity;
                    }

                    if (V.minDensity != -1)
                    {
                        //if (newDensity[i] < V.minDensity) newDensity[i] = V.minDensity;
                        if (newDensity[i] > 0 && V.minDensity > newDensity[i]) newDensity[i] = V.minDensity;
                    }

                }
            }
            );

            return newDensity;
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
                weightsWithoutEnds[i - 1] = weights[i]/weightSum;
            }

            return weightsWithoutEnds;
        }

        //-------------

        void assignPassDensityToVoxel(double[] newDensity)
        {
            //assign the temporary stored value (calculated in the previous steps) to voxel density
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];
                V.density = newDensity[i];
            }
            );
        }

        //-------------

        void ants_xPass(double[] weights)
        {
            //ant particles
            //results
            double[] newTowardsFoodPheromone = new double[activeVoxels.Length];
            double[] newTowardsBasePheromone = new double[activeVoxels.Length];

            //sums
            double[] ant_neighbourSum_towardsFood = new double[activeVoxels.Length];
            double[] ant_neighbourSum_towardsBase = new double[activeVoxels.Length];

            //calculate density for each voxel taking into account whether voxel is active
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];

                //diffuse
                if (tridimensional)
                {
                    int weightIndex = 0;

                    for (int x = -diffuseRange_Ant; x <= diffuseRange_Ant; x++)
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
                                    if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                    if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
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

                        for (int x = -diffuseRange_Ant; x <= diffuseRange_Ant; x++)
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
                                        if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                        if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                    if (planarXZ)
                    {
                        int weightIndex = 0;

                        for (int x = -diffuseRange_Ant; x <= diffuseRange_Ant; x++)
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
                                        if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                        if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXY || planarXZ)
                {
                    //calculate new density
                    if (foodDiffuseRate > 0)
                    {
                        //make sure it's not larger than 1
                        if (ant_neighbourSum_towardsFood[i] > 1)
                        {
                            ant_neighbourSum_towardsFood[i] = 1;
                        }
                        
                        //override values with staticDensity a.k.a. initial voxel values map
                        if (V.maxDensity != -1)
                        {
                            if (ant_neighbourSum_towardsFood[i] > V.maxDensity) ant_neighbourSum_towardsFood[i] = V.maxDensity;
                        }

                        if (V.minDensity != -1)
                        {
                            if (ant_neighbourSum_towardsFood[i] < V.minDensity) ant_neighbourSum_towardsFood[i] = V.minDensity;
                        }
                    }

                    if (baseDiffuseRate > 0)
                    {
                        //make sure it's not larger than 1
                        if (ant_neighbourSum_towardsBase[i] > 1)
                        {
                            ant_neighbourSum_towardsBase[i] = 1;
                        }

                        //override values with staticDensity a.k.a. initial voxel values map
                        if (V.maxDensity != -1)
                        {
                            if (ant_neighbourSum_towardsBase[i] > V.maxDensity) ant_neighbourSum_towardsBase[i] = V.maxDensity;
                        }

                        if (V.minDensity != -1)
                        {
                            if (ant_neighbourSum_towardsBase[i] < V.minDensity) ant_neighbourSum_towardsBase[i] = V.minDensity;
                        }
                    }
                }
            }
            );

            //assign the temporary stored value (calculated in the previous steps) to voxel density
            if (foodDiffuseRate > 0 || baseDiffuseRate > 0)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];

                    if (foodDiffuseRate > 0)
                    {
                        V.towardsFoodPheromone = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * ant_neighbourSum_towardsFood[i];
                        if (V.towardsFoodPheromone > 1) V.towardsFoodPheromone = 1;
                    }

                    if (baseDiffuseRate > 0)
                    {
                        V.towardsBasePheromone = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * ant_neighbourSum_towardsBase[i];
                        if (V.towardsBasePheromone > 1) V.towardsBasePheromone = 1;
                    }
                }
                );
            }
        }

        void ants_yPass(double[] weights)
        {

            //ant particles
            //results
            double[] newTowardsFoodPheromone = new double[activeVoxels.Length];
            double[] newTowardsBasePheromone = new double[activeVoxels.Length];

            //sums
            double[] ant_neighbourSum_towardsFood = new double[activeVoxels.Length];
            double[] ant_neighbourSum_towardsBase = new double[activeVoxels.Length];

            //calculate density for each voxel taking into account whether voxel is active
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];

                //diffuse
                if (tridimensional)
                {
                    int weightIndex = 0;

                    for (int y = -diffuseRange_Ant; y <= diffuseRange_Ant; y++)
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
                                    if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                    if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
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

                        for (int y = -diffuseRange_Ant; y <= diffuseRange_Ant; y++)
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
                                        if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                        if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                    else if (planarYZ)
                    {
                        int weightIndex = 0;

                        for (int y = -diffuseRange_Ant; y <= diffuseRange_Ant; y++)
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
                                        if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                        if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXY || planarYZ)
                {
                    //calculate new density
                    if (foodDiffuseRate > 0)
                    {
                        //make sure it's not larger than 1
                        if (ant_neighbourSum_towardsFood[i] > 1)
                        {
                            ant_neighbourSum_towardsFood[i] = 1;
                        }

                        //override values with staticDensity a.k.a. initial voxel values map
                        if (V.maxDensity != -1)
                        {
                            if (ant_neighbourSum_towardsFood[i] > V.maxDensity) ant_neighbourSum_towardsFood[i] = V.maxDensity;
                        }

                        if (V.minDensity != -1)
                        {
                            if (ant_neighbourSum_towardsFood[i] < V.minDensity) ant_neighbourSum_towardsFood[i] = V.minDensity;
                        }
                    }

                    if (baseDiffuseRate > 0)
                    {
                        //make sure it's not larger than 1
                        if (ant_neighbourSum_towardsBase[i] > 1)
                        {
                            ant_neighbourSum_towardsBase[i] = 1;
                        }

                        //override values with staticDensity a.k.a. initial voxel values map
                        if (V.maxDensity != -1)
                        {
                            if (ant_neighbourSum_towardsBase[i] > V.maxDensity) ant_neighbourSum_towardsBase[i] = V.maxDensity;
                        }

                        if (V.minDensity != -1)
                        {
                            if (ant_neighbourSum_towardsBase[i] < V.minDensity) ant_neighbourSum_towardsBase[i] = V.minDensity;
                        }
                    }
                }
            }
            );

            //assign the temporary stored value (calculated in the previous steps) to voxel density
            if (foodDiffuseRate > 0 || baseDiffuseRate > 0)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];

                    if (foodDiffuseRate > 0)
                    {
                        V.towardsFoodPheromone = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * ant_neighbourSum_towardsFood[i];
                        if (V.towardsFoodPheromone > 1) V.towardsFoodPheromone = 1;
                    }

                    if (baseDiffuseRate > 0)
                    {
                        V.towardsBasePheromone = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * ant_neighbourSum_towardsBase[i];
                        if (V.towardsBasePheromone > 1) V.towardsBasePheromone = 1;
                    }
                }
                );
            }

        }

        void ants_zPass(double[] weights)
        {

            //ant particles
            //results
            double[] newTowardsFoodPheromone = new double[activeVoxels.Length];
            double[] newTowardsBasePheromone = new double[activeVoxels.Length];

            //sums
            double[] ant_neighbourSum_towardsFood = new double[activeVoxels.Length];
            double[] ant_neighbourSum_towardsBase = new double[activeVoxels.Length];

            //calculate density for each voxel taking into account whether voxel is active
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];
                //diffuse
                if (tridimensional)
                {
                    int weightIndex = 0;

                    for (int z = -diffuseRange_Ant; z <= diffuseRange_Ant; z++)
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
                                    if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                    if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
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

                        for (int z = -diffuseRange_Ant; z <= diffuseRange_Ant; z++)
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
                                        if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                        if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                    else if (planarYZ)
                    {
                        int weightIndex = 0;

                        for (int z = -diffuseRange_Ant; z <= diffuseRange_Ant; z++)
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
                                        if (foodDiffuseRate > 0) ant_neighbourSum_towardsFood[i] += neighbour.towardsFoodPheromone * weights[weightIndex];
                                        if (baseDiffuseRate > 0) ant_neighbourSum_towardsBase[i] += neighbour.towardsBasePheromone * weights[weightIndex];
                                    }
                                }
                            }
                            weightIndex++;
                        }
                    }
                }

                if (tridimensional || planarXZ || planarYZ)
                {
                    //calculate new density
                    if (foodDiffuseRate > 0)
                    {
                        //make sure it's not larger than 1
                        if (ant_neighbourSum_towardsFood[i] > 1)
                        {
                            ant_neighbourSum_towardsFood[i] = 1;
                        }

                        //override values with staticDensity a.k.a. initial voxel values map
                        if (V.maxDensity != -1)
                        {
                            if (ant_neighbourSum_towardsFood[i] > V.maxDensity) ant_neighbourSum_towardsFood[i] = V.maxDensity;
                        }

                        if (V.minDensity != -1)
                        {
                            if (ant_neighbourSum_towardsFood[i] < V.minDensity) ant_neighbourSum_towardsFood[i] = V.minDensity;
                        }
                    }

                    if (baseDiffuseRate > 0)
                    {
                        //make sure it's not larger than 1
                        if (ant_neighbourSum_towardsBase[i] > 1)
                        {
                            ant_neighbourSum_towardsBase[i] = 1;
                        }

                        //override values with staticDensity a.k.a. initial voxel values map
                        if (V.maxDensity != -1)
                        {
                            if (ant_neighbourSum_towardsBase[i] > V.maxDensity) ant_neighbourSum_towardsBase[i] = V.maxDensity;
                        }

                        if (V.minDensity != -1)
                        {
                            if (ant_neighbourSum_towardsBase[i] < V.minDensity) ant_neighbourSum_towardsBase[i] = V.minDensity;
                        }
                    }
                }
            }
            );

            //assign the temporary stored value (calculated in the previous steps) to voxel density
            if (foodDiffuseRate > 0 || baseDiffuseRate > 0)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];

                    if (foodDiffuseRate > 0)
                    {
                        V.towardsFoodPheromone = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * ant_neighbourSum_towardsFood[i];
                        if (V.towardsFoodPheromone > 1) V.towardsFoodPheromone = 1;
                    }

                    if (baseDiffuseRate > 0)
                    {
                        V.towardsBasePheromone = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * ant_neighbourSum_towardsBase[i];
                        if (V.towardsBasePheromone > 1) V.towardsBasePheromone = 1;
                    }
                }
                );
            }
        }

        //-------------------------------------------------------------------

        void decayVoxels()
        {
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];

                V.density -= decay;
                if (V.density < 0) V.density = 0;

                //ant particles
                if (antParticles)
                {
                    V.towardsFoodPheromone -= foodDecayRate;
                    V.towardsBasePheromone -= baseDecayRate;

                    if(V.towardsFoodPheromone < 0) V.towardsFoodPheromone = 0;
                    if (V.towardsBasePheromone < 0) V.towardsBasePheromone = 0;
                }
            }
            );
        }

        //-------------------------------------------------------------------

        void inheritParticleGroups()
        {
            particleGroups = new List<ParticleGroup>();

            for(int pg=0; pg<inputParticleGroups.Count; pg++) 
            {

                ParticleGroup inputPG = inputParticleGroups[pg];

                ParticleGroup PG = new ParticleGroup(inputPG.speed, inputPG.sensorDistance, inputPG.sensorAngle, inputPG.rotationAngle, inputPG.depositValue, inputPG.wanderFrequency, inputPG.foodWanderFrequency, inputPG.baseWanderFrequency, inputPG.color);
                particleGroups.Add(PG);

                for (int i = 0; i < inputPG.particles.Count; i++)
                {
                    Particle initialP = inputPG.particles[i];
                    Plane particlePlane = initialP.pPlane;

                    //check initialP parent voxel
                    int xID = System.Convert.ToInt32((initialP.pPlane.Origin.X - Math.Abs(initialP.pPlane.Origin.X % voxelSize)) / voxelSize);
                    int yID = System.Convert.ToInt32((initialP.pPlane.Origin.Y - Math.Abs(initialP.pPlane.Origin.Y % voxelSize)) / voxelSize);
                    int zID = System.Convert.ToInt32((initialP.pPlane.Origin.Z - Math.Abs(initialP.pPlane.Origin.Z % voxelSize)) / voxelSize);

                    if (xID >= 0 && xID < resX && yID >= 0 && yID < resY && zID >= 0 && zID < resZ)
                    {
                        if (voxels[xID, yID, zID] != null)
                        {
                            initialP.parentVoxel = voxels[xID, yID, zID];
                            initialP.die = false;
                            if (initialP.parentVoxel.maxDensity == 0.01) initialP.die = true;
                        }
                        else
                        {
                            initialP.parentVoxel = null;
                            initialP.die = true;
                        }
                    }

                    //copy input slime particles
                    if (initialP.parentVoxel != null)
                    {
                        if (initialP.die == false)
                        {
                            if (initialP.parentVoxel.maxDensity != 0)
                            {
                                if (!initialP.parentParticleGroup.ant)
                                {
                                    //flatten vectors
                                    if (tridimensional == false)
                                    {
                                        if (planarXY)
                                        {
                                            Point3d origin = initialP.pPlane.Origin;

                                            Vector3d xVector = new Vector3d(initialP.pPlane.XAxis.X, initialP.pPlane.XAxis.Y, 0);
                                            xVector.Unitize();

                                            Vector3d yVector = xVector;
                                            yVector.Rotate(Math.PI / 2, Plane.WorldXY.ZAxis);

                                            initialP.pPlane = new Plane(origin, xVector, yVector);
                                        }

                                        if (planarXZ)
                                        {
                                            Point3d origin = initialP.pPlane.Origin;

                                            Vector3d xVector = new Vector3d(initialP.pPlane.XAxis.X, 0, initialP.pPlane.XAxis.Z);
                                            xVector.Unitize();

                                            Vector3d yVector = xVector;
                                            yVector.Rotate(Math.PI / 2, Plane.WorldXY.YAxis);

                                            initialP.pPlane = new Plane(origin, xVector, yVector);
                                        }

                                        if (planarYZ)
                                        {
                                            Point3d origin = initialP.pPlane.Origin;

                                            Vector3d xVector = new Vector3d(0, initialP.pPlane.XAxis.Y, initialP.pPlane.XAxis.Z);
                                            xVector.Unitize();

                                            Vector3d yVector = xVector;
                                            yVector.Rotate(Math.PI / 2, Plane.WorldXY.XAxis);

                                            initialP.pPlane = new Plane(origin, xVector, yVector);
                                        }
                                    }

                                    if (tridimensional && dimX > PG.sensorDistance && dimY > PG.sensorDistance && dimZ > PG.sensorDistance)
                                    {
                                        particlePlane.Rotate(Rhino.RhinoMath.ToRadians(retrieveRotationAngle(initialP)), initialP.pPlane.YAxis, initialP.pPlane.Origin);
                                        initialP.pPlane = particlePlane;
                                    }

                                    initialP.pPlane = new Plane(boundaries(initialP, initialP.pPlane.Origin), initialP.pPlane.XAxis, initialP.pPlane.YAxis);

                                    //copy input particles
                                    Particle P = new Particle(initialP.pPlane);
                                    P.parentParticleGroup = PG;
                                    
                                    PG.particles.Add(P);
                                    particles.Add(P);
                                }
                            }
                        }
                    }

                    //copy input ant particles
                    if (initialP.parentVoxel != null)
                    {
                        if (initialP.die == false)
                        {
                            if (initialP.parentVoxel.maxDensity != 0)
                            {
                                if (initialP.parentParticleGroup.ant)
                                {
                                    //flatten vectors
                                    if (tridimensional == false)
                                    {
                                        if (planarXY)
                                        {
                                            Point3d origin = initialP.pPlane.Origin;

                                            Vector3d xVector = new Vector3d(initialP.pPlane.XAxis.X, initialP.pPlane.XAxis.Y, 0);
                                            xVector.Unitize();

                                            Vector3d yVector = xVector;
                                            yVector.Rotate(Math.PI / 2, Plane.WorldXY.ZAxis);

                                            initialP.pPlane = new Plane(origin, xVector, yVector);
                                        }

                                        if (planarXZ)
                                        {
                                            Point3d origin = initialP.pPlane.Origin;

                                            Vector3d xVector = new Vector3d(initialP.pPlane.XAxis.X, 0, initialP.pPlane.XAxis.Z);
                                            xVector.Unitize();

                                            Vector3d yVector = xVector;
                                            yVector.Rotate(Math.PI / 2, Plane.WorldXY.YAxis);

                                            initialP.pPlane = new Plane(origin, xVector, yVector);
                                        }

                                        if (planarYZ)
                                        {
                                            Point3d origin = initialP.pPlane.Origin;

                                            Vector3d xVector = new Vector3d(0, initialP.pPlane.XAxis.Y, initialP.pPlane.XAxis.Z);
                                            xVector.Unitize();

                                            Vector3d yVector = xVector;
                                            yVector.Rotate(Math.PI / 2, Plane.WorldXY.XAxis);

                                            initialP.pPlane = new Plane(origin, xVector, yVector);
                                        }
                                    }

                                    if (tridimensional && dimX > PG.sensorDistance && dimY > PG.sensorDistance && dimZ > PG.sensorDistance)
                                    {
                                        particlePlane.Rotate(Rhino.RhinoMath.ToRadians(retrieveRotationAngle(initialP)), initialP.pPlane.YAxis, initialP.pPlane.Origin);
                                        initialP.pPlane = particlePlane;
                                    }

                                    initialP.pPlane = new Plane(boundaries(initialP, initialP.pPlane.Origin), initialP.pPlane.XAxis, initialP.pPlane.YAxis);

                                    //copy input ant particles
                                    Particle P = new Particle(initialP.pPlane);

                                    PG.ant = true;
                                    P.parentParticleGroup = PG;

                                    //ant particles
                                    P.home = P.pPlane;
                                    antParticles = true;

                                    PG.particles.Add(P);
                                    particles.Add(P);
                                }
                            }
                        }
                    }
                }

                if(!PG.ant) PG.updateWanderFrequency();
                if (PG.ant)
                {
                    PG.updateFoodWanderFrequency();
                    PG.updateBaseWanderFrequency();
                }
            }

            Globals.particleGroups = new List<ParticleGroup>(particleGroups);
        }

        void updateParticleGroups()
        {
            for(int i=0; i< inputParticleGroups.Count; i++)
            {
                ParticleGroup inputPG = inputParticleGroups[i];
                ParticleGroup PG = particleGroups[i];

                PG.speed = inputPG.speed;
                PG.sensorDistance = inputPG.sensorDistance;
                PG.sensorAngle = inputPG.sensorAngle;
                PG.rotationAngle = inputPG.rotationAngle;
                PG.depositValue = inputPG.depositValue;
                PG.wanderFrequency = inputPG.wanderFrequency;
                PG.foodWanderFrequency = inputPG.wanderFrequency;
                PG.baseWanderFrequency = inputPG.baseWanderFrequency;
                PG.color = inputPG.color;

                if (!PG.ant) PG.updateWanderFrequency();
                if (PG.ant)
                {
                    PG.updateFoodWanderFrequency();
                    PG.updateBaseWanderFrequency();
                };
            }
        }

        //-------------------------------------------------------------------

        void particleCheckParentVoxel()
        {
            //reset voxel count
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel V = activeVoxels[i];
                V.particleCount = 0;
            }
            );

            //count particles
            Parallel.For(0, particles.Count, i =>
            {
                Particle P = particles[i];
                P.age++;

                if (tridimensional)
                {
                    int xID = System.Convert.ToInt32((P.pPlane.Origin.X - Math.Abs(P.pPlane.Origin.X % voxelSize)) / voxelSize);
                    int yID = System.Convert.ToInt32((P.pPlane.Origin.Y - Math.Abs(P.pPlane.Origin.Y % voxelSize)) / voxelSize);
                    int zID = System.Convert.ToInt32((P.pPlane.Origin.Z - Math.Abs(P.pPlane.Origin.Z % voxelSize)) / voxelSize);

                    if (xID >= 0 && xID < resX && yID >= 0 && yID < resY && zID >= 0 && zID < resZ)
                    {
                        if (voxels[xID, yID, zID] != null)
                        {
                            P.parentVoxel = voxels[xID, yID, zID];
                            P.parentVoxel.particleCount++;

                            //ant particles
                            if (P.parentParticleGroup.ant)
                            {
                                if (iteration > 1)
                                {
                                    //found food
                                    if (P.parentVoxel.food > 0 && P.foundFood == false)
                                    {
                                        P.foundFood = true;
                                        P.age = 0;

                                        if (P.age == 0)
                                        {
                                            P.parentVoxel.food -= 1;
                                            P.age++;
                                        }
                                    }

                                    //returned home
                                    if (P.PPlane.Origin.DistanceTo(P.home.Origin) < retrieveSpeed(P))
                                    {
                                        P.foundFood = false;
                                        P.age = 1;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        P.parentVoxel = null;
                        P.die = true;
                    }
                }
                else //tridimensional == false
                {
                    if (planarXY)
                    {
                        int xID = System.Convert.ToInt32((P.pPlane.Origin.X - Math.Abs(P.pPlane.Origin.X % voxelSize)) / voxelSize);
                        int yID = System.Convert.ToInt32((P.pPlane.Origin.Y - Math.Abs(P.pPlane.Origin.Y % voxelSize)) / voxelSize);
                        int zID = 0;

                        if (xID >= 0 && xID < resX && yID >= 0 && yID < resY)
                        {
                            if (voxels[xID, yID, zID] != null)
                            {
                                P.parentVoxel = voxels[xID, yID, zID];
                                P.parentVoxel.particleCount++;

                                //ant particles
                                if (P.parentParticleGroup.ant)
                                {
                                    if (iteration > 1)
                                    {
                                        //found food
                                        if (P.parentVoxel.food > 0 && P.foundFood == false)
                                        {
                                            P.foundFood = true;
                                            P.age = 0;

                                            if (P.age == 0)
                                            {
                                                P.parentVoxel.food -= 1;
                                                P.age++;
                                            }
                                        }

                                        //returned home
                                        if (P.PPlane.Origin.DistanceTo(P.home.Origin) < retrieveSpeed(P))
                                        {
                                            P.foundFood = false;
                                            P.age = 1;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            P.parentVoxel = null;
                            P.die = true;
                        }
                    }
                    else if (planarXZ)
                    {
                        int xID = System.Convert.ToInt32((P.pPlane.Origin.X - Math.Abs(P.pPlane.Origin.X % voxelSize)) / voxelSize);
                        int yID = 0;
                        int zID = System.Convert.ToInt32((P.pPlane.Origin.Z - Math.Abs(P.pPlane.Origin.Z % voxelSize)) / voxelSize);

                        if (xID >= 0 && xID < resX && zID >= 0 && zID < resZ)
                        {
                            if (voxels[xID, yID, zID] != null)
                            {
                                P.parentVoxel = voxels[xID, yID, zID];
                                P.parentVoxel.particleCount++;

                                //ant particles
                                if (P.parentParticleGroup.ant)
                                {
                                    if (iteration > 1)
                                    {

                                        //found food
                                        if (P.parentVoxel.food > 0 && P.foundFood == false)
                                        {
                                            P.foundFood = true;
                                            P.age = 0;

                                            if (P.age == 0)
                                            {
                                                P.parentVoxel.food -= 1;
                                                P.age++;
                                            }
                                        }

                                        //returned home
                                        if (P.PPlane.Origin.DistanceTo(P.home.Origin) < retrieveSpeed(P))
                                        {
                                            P.foundFood = false;
                                            P.age = 1;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            P.parentVoxel = null;
                            P.die = true;
                        }
                    }
                    else if (planarYZ)
                    {
                        int xID = 0;
                        int yID = System.Convert.ToInt32((P.pPlane.Origin.Y - Math.Abs(P.pPlane.Origin.Y % voxelSize)) / voxelSize);
                        int zID = System.Convert.ToInt32((P.pPlane.Origin.Z - Math.Abs(P.pPlane.Origin.Z % voxelSize)) / voxelSize);

                        if (yID >= 0 && yID < resY && zID >= 0 && zID < resZ)
                        {
                            if (voxels[xID, yID, zID] != null)
                            {
                                P.parentVoxel = voxels[xID, yID, zID];
                                P.parentVoxel.particleCount++;

                                //ant particles
                                if (P.parentParticleGroup.ant)
                                {
                                    if (iteration > 1)
                                    {

                                        //found food
                                        if (P.parentVoxel.food > 0 && P.foundFood == false)
                                        {
                                            P.foundFood = true;
                                            P.age = 0;

                                            if (P.age == 0)
                                            {
                                                P.parentVoxel.food -= 1;
                                                P.age++;
                                            }
                                        }

                                        //returned home
                                        if (P.PPlane.Origin.DistanceTo(P.home.Origin) < retrieveSpeed(P))
                                        {
                                            P.foundFood = false;
                                            P.age = 1;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            P.parentVoxel = null;
                            P.die = true;
                        }
                    }
                }

                if (P.parentVoxel != null)
                {
                    if (P.parentVoxel.maxDensity == 0)
                    {
                        P.die = true;
                        P.parentVoxel.density = 0;
                    }
                }
            }
            );
        }

        Voxel particleCheckParentVoxel(Particle P)
        {

            Voxel output = null;

            P.age++;

            int xID = System.Convert.ToInt32((P.pPlane.Origin.X - Math.Abs(P.pPlane.Origin.X % voxelSize)) / voxelSize);
            int yID = System.Convert.ToInt32((P.pPlane.Origin.Y - Math.Abs(P.pPlane.Origin.Y % voxelSize)) / voxelSize);
            int zID = System.Convert.ToInt32((P.pPlane.Origin.Z - Math.Abs(P.pPlane.Origin.Z % voxelSize)) / voxelSize);

            if (xID >= 0 && xID < resX && yID >= 0 && yID < resY && zID >= 0 && zID < resZ)
            {
                output = voxels[xID, yID, zID];
            }

            return output;
        }
        //----------------------------------

        void particleCheckNeighbourCount()
        {
            if (dynPop)
            {
                int checkRange = 0;

                if (death && !division) checkRange = dieRange;
                if (!death && division) checkRange = divRange;

                if (death && division)
                {
                    checkRange = Math.Max(dieRange, divRange);
                }

                Parallel.For(0, particles.Count, p =>
                {
                    Particle P = particles[p];
                    if (P.parentVoxel != null)
                    {
                        if (checkRange > 0)
                        {
                            //check neighbour particle count
                            P.neighbourCount_Die = 0;
                            P.neighbourCount_Div = 0;

                            if (tridimensional)
                            {
                                for (int i = -checkRange; i <= checkRange; i++)
                                {
                                    if (P.parentVoxel.idX + i >= 0 && P.parentVoxel.idX + i < resX)
                                    {
                                        for (int j = -checkRange; j <= checkRange; j++)
                                        {
                                            if (P.parentVoxel.idY + j >= 0 && P.parentVoxel.idY + j < resY)
                                            {
                                                for (int k = -checkRange; k <= checkRange; k++)
                                                {
                                                    if (P.parentVoxel.idZ + k >= 0 && P.parentVoxel.idZ + k < resZ)
                                                    {
                                                        if (voxels[P.parentVoxel.idX + i, P.parentVoxel.idY + j, P.parentVoxel.idZ + k] != null)
                                                        {
                                                            Voxel neighbourV = voxels[P.parentVoxel.idX + i, P.parentVoxel.idY + j, P.parentVoxel.idZ + k];
                                                            if (Math.Abs(i) <= dieRange && Math.Abs(j) <= dieRange && Math.Abs(k) <= dieRange) P.neighbourCount_Die += neighbourV.particleCount;
                                                            if (Math.Abs(i) <= divRange && Math.Abs(j) <= divRange && Math.Abs(k) <= divRange) P.neighbourCount_Div += neighbourV.particleCount;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else //tridimensional == false
                            {
                                if (planarXY)
                                {
                                    for (int i = -checkRange; i <= checkRange; i++)
                                    {
                                        if (P.parentVoxel.idX + i >= 0 && P.parentVoxel.idX + i < resX)
                                        {
                                            for (int j = -checkRange; j <= checkRange; j++)
                                            {
                                                if (P.parentVoxel.idY + j >= 0 && P.parentVoxel.idY + j < resY)
                                                {
                                                    if (voxels[P.parentVoxel.idX + i, P.parentVoxel.idY + j, 0] != null)
                                                    {
                                                        Voxel neighbourV = voxels[P.parentVoxel.idX + i, P.parentVoxel.idY + j, 0];
                                                        if (Math.Abs(i) <= dieRange && Math.Abs(j) <= dieRange) P.neighbourCount_Die += neighbourV.particleCount;
                                                        if (Math.Abs(i) <= divRange && Math.Abs(j) <= divRange) P.neighbourCount_Div += neighbourV.particleCount;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else if (planarXZ)
                                {
                                    for (int i = -checkRange; i <= checkRange; i++)
                                    {
                                        if (P.parentVoxel.idX + i >= 0 && P.parentVoxel.idX + i < resX)
                                        {
                                            for (int k = -checkRange; k <= checkRange; k++)
                                            {
                                                if (P.parentVoxel.idZ + k >= 0 && P.parentVoxel.idZ + k < resZ)
                                                {
                                                    if (voxels[P.parentVoxel.idX + i, 0, P.parentVoxel.idZ + k] != null)
                                                    {
                                                        Voxel neighbourV = voxels[P.parentVoxel.idX + i, 0, P.parentVoxel.idZ + k];
                                                        if (Math.Abs(i) <= dieRange && Math.Abs(k) <= dieRange) P.neighbourCount_Die += neighbourV.particleCount;
                                                        if (Math.Abs(i) <= divRange && Math.Abs(k) <= divRange) P.neighbourCount_Div += neighbourV.particleCount;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else if (planarYZ)
                                {
                                    for (int j = -checkRange; j <= checkRange; j++)
                                    {
                                        if (P.parentVoxel.idY + j >= 0 && P.parentVoxel.idY + j < resY)
                                        {
                                            for (int k = -checkRange; k <= checkRange; k++)
                                            {
                                                if (P.parentVoxel.idZ + k >= 0 && P.parentVoxel.idZ + k < resZ)
                                                {
                                                    if (voxels[0, P.parentVoxel.idY + j, P.parentVoxel.idZ + k] != null)
                                                    {
                                                        Voxel neighbourV = voxels[0, P.parentVoxel.idY + j, P.parentVoxel.idZ + k];
                                                        if (Math.Abs(j) <= dieRange && Math.Abs(k) <= dieRange) P.neighbourCount_Die += neighbourV.particleCount;
                                                        if (Math.Abs(j) <= divRange && Math.Abs(k) <= divRange) P.neighbourCount_Div += neighbourV.particleCount;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (P.neighbourCount_Die > 0) P.neighbourCount_Die -= 1;
                            if (P.neighbourCount_Div > 0) P.neighbourCount_Div -= 1;
                        }
                    }
                }
                );
            }
        }

        //-------------------------------------------------------------------

        //sense values and vectors
        void particleSenseValuesAndVectors()
        {
            //sense for next iteration
            Parallel.For(0, particles.Count, p =>
            {
                Particle P = particles[p];
                if (P.parentVoxel != null)
                {
                    if (P.parentVoxel.vectorField)
                    {
                        if (P.parentVoxel.frequency == 1)
                        {
                            P.moveVector += P.parentVoxel.voxelVector;
                        }
                        else
                        {
                            if (iteration % P.parentVoxel.frequency == p % P.parentVoxel.frequency)
                            {
                                P.moveVector += P.parentVoxel.voxelVector;
                            }
                        }
                    }

                    double sensorAngleMultiplier = 1;
                    if (P.parentVoxel.sensorAngleMultiplier != -1) sensorAngleMultiplier = P.parentVoxel.sensorAngleMultiplier;

                    double sensorDistanceMultiplier = 1;
                    if(P.parentVoxel.sensorDistanceMultiplier != -1) sensorDistanceMultiplier = P.parentVoxel.sensorDistanceMultiplier;

                    //create list of potential sensor positions
                    Point3d[] potentialPos = new Point3d[3];
                    if (tridimensional) potentialPos = new Point3d[5];

                    double SA = radAngle[retrieveSensorAngle(P)];

                    //L
                    Point3d L = new Point3d(P.pPlane.Origin);

                    Vector3d vectorL = new Vector3d(P.pPlane.XAxis);
                    vectorL.Rotate(-SA * sensorAngleMultiplier, P.pPlane.ZAxis);

                    L += vectorL * retrieveSensorDistance(P) * sensorDistanceMultiplier;
                    potentialPos[0] = boundaries(P, L);


                    //C
                    Point3d C = new Point3d(P.pPlane.Origin);

                    Vector3d vectorC = new Vector3d(P.pPlane.XAxis);

                    C += vectorC * retrieveSensorDistance(P) * sensorDistanceMultiplier;
                    potentialPos[1] = boundaries(P, C);


                    //R
                    Point3d R = new Point3d(P.pPlane.Origin);

                    Vector3d vectorR = new Vector3d(P.pPlane.XAxis);
                    vectorR.Rotate(SA * sensorAngleMultiplier, P.pPlane.ZAxis);

                    R += vectorR * retrieveSensorDistance(P) * sensorDistanceMultiplier;
                    potentialPos[2] = boundaries(P, R);


                    if (tridimensional == true)
                    {
                        //U
                        Point3d U = new Point3d(P.pPlane.Origin);

                        Vector3d vectorU = new Vector3d(P.pPlane.XAxis);
                        vectorU.Rotate(SA * sensorAngleMultiplier, P.pPlane.YAxis);

                        U += vectorU * retrieveSensorDistance(P) * sensorDistanceMultiplier;
                        potentialPos[3] = boundaries(P, U);

                        //D
                        Point3d D = new Point3d(P.pPlane.Origin);

                        Vector3d vectorD = new Vector3d(P.pPlane.XAxis);
                        vectorD.Rotate(-SA * sensorAngleMultiplier, P.pPlane.YAxis);

                        D += vectorD * retrieveSensorDistance(P) * sensorDistanceMultiplier;
                        potentialPos[4] = boundaries(P, D);
                    }

                    //-----------------------

                    //sample voxel values according to sensor positions
                    double[] voxelValues = new double[3];
                    if (tridimensional) voxelValues = new double[5];

                    for (int i = 0; i < potentialPos.Length; i++)
                    {
                        Point3d potPos = potentialPos[i];
                        Voxel potentialVoxel = getParentVoxel(potPos);

                        if (potentialVoxel != null)
                        {
                            double voxelValue = -99;

                            if (!P.parentParticleGroup.ant)
                            {
                                voxelValue = potentialVoxel.density;
                                if (potentialVoxel.food > 0) voxelValue = Math.Max(potentialVoxel.density, potentialVoxel.food);

                                if (antParticles)
                                {
                                    if (slime_antFood > 0) voxelValue += potentialVoxel.towardsFoodPheromone * slime_antFood;
                                    if (slime_antBase > 0) voxelValue += potentialVoxel.towardsBasePheromone * slime_antBase;
                                }
                            }

                            //ant particles sense pheromones
                            if (P.parentParticleGroup.ant)
                            {
                                if (P.foundFood)
                                {
                                    voxelValue = potentialVoxel.towardsBasePheromone;
                                    if (ant_slime > 0) voxelValue += potentialVoxel.density * ant_slime;
                                    
                                }
                                else
                                {
                                    if (P.parentVoxel.food <= 0)
                                    {
                                        if (potentialVoxel.towardsFoodPheromone > 0)
                                        {
                                            voxelValue = potentialVoxel.towardsFoodPheromone;
                                            if (ant_slime > 0) voxelValue += potentialVoxel.density * ant_slime;
                                        }
                                        else
                                        {
                                            //if (P.age <= 100 && (iteration+p) % 5 == 0)
                                            if ((iteration + p) % 2 == 0)
                                            {
                                                voxelValue = potentialVoxel.towardsBasePheromone;
                                                if (ant_slime > 0) voxelValue += potentialVoxel.density * ant_slime;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        voxelValue = 1;
                                    }
                                }
                            }

                            //avoid the areas with maxDensity = 0
                            if (voxelValue != -99)
                            {
                                voxelValues[i] = voxelValue;
                            }

                            if (potentialVoxel.maxDensity == 0)
                            {
                                voxelValue = -1;
                                voxelValues[i] = voxelValue;
                            }
                        }
                        else
                        {
                            voxelValues[i] = -1;
                        }
                    }

                    //-----------------------

                    //find the largest voxel value
                    double minValue = 9999;
                    double maxValue = -1;
                    double index = -1;

                    for (int i = 0; i < voxelValues.Length; i++)
                    {
                        double value = voxelValues[i];
                        if (value > maxValue)
                        {
                            maxValue = value;
                            index = i;
                        }
                        if (value < minValue)
                        {
                            minValue = value;
                        }
                    }

                    if (minValue == maxValue)
                    {
                        index = 1;
                    }

                    //-----------------------

                    if (index != -1)
                    {
                        //continue if the sensing provided any viable solutions
                        double RA = radAngle[Convert.ToInt32(retrieveRotationAngle(P))];

                        Vector3d valueForce = new Vector3d(P.PPlane.XAxis.X, P.PPlane.XAxis.Y, P.PPlane.XAxis.Z);

                        double rotationAngleMultiplier = 1;
                        if(P.parentVoxel.rotationAngleMultiplier != -1) rotationAngleMultiplier = P.parentVoxel.rotationAngleMultiplier;

                        if (index == 0)
                        {
                            //L
                            valueForce.Rotate(-RA * rotationAngleMultiplier, P.pPlane.ZAxis);
                        }

                        if (index == 1)
                        {
                            //C
                            //do nothing
                        }

                        if (index == 2)
                        {
                            //R
                            valueForce.Rotate(RA * rotationAngleMultiplier, P.pPlane.ZAxis);
                        }

                        if (index == 3 && tridimensional)
                        {
                            //U
                            valueForce.Rotate(RA * rotationAngleMultiplier, P.pPlane.YAxis);
                        }

                        if (index == 4 && tridimensional)
                        {
                            //D
                            valueForce.Rotate(-RA * rotationAngleMultiplier, P.pPlane.YAxis);
                        }

                        valueForce.Unitize();
                        P.moveVector += valueForce;
                    }
                    else
                    {
                        if (wrapBoundaries == false)
                        {
                            double rotA = retrieveRotationAngle(P) * p;
                            if (rotA < 0) rotA = 360 - (rotA % 360);
                            if (rotA > 360) rotA %= 360;
                            double RA = radAngle[Convert.ToInt32(rotA)];
                            P.pPlane.Rotate(RA, P.pPlane.ZAxis, P.pPlane.Origin);
                        }
                    }
                }
            }
            );
        }

        //----------------------------------

        void particleSense_Ant()
        {
            //ant paricles
            double maxDist = Math.Max(dimX, dimY);
            maxDist = Math.Max(maxDist, dimZ);

            List<Particle> shuffledParticles = particles.OrderBy(a => Guid.NewGuid()).ToList();

            //sense for next iteration
            Parallel.For(0, shuffledParticles.Count, p =>
            {
                Particle P = shuffledParticles[p];
                if (P.parentVoxel != null)
                {
                    if (P.parentParticleGroup.ant)
                    {
                        Vector3d outsideVector = P.PPlane.Origin - P.home.Origin;
                        Vector3d towardsHomeVector = -outsideVector;
                        towardsHomeVector.Unitize();

                        //turn
                        if (P.age < 15)
                        {
                            if (!P.foundFood)
                            {
                                outsideVector.Unitize();

                                Vector3d pVector = P.pPlane.XAxis;
                                pVector.Unitize();

                                Vector3d turnVector = (15 - P.age) / 15 * pVector + outsideVector * P.age / 15;
                                turnVector.Unitize();

                                P.moveVector += turnVector * 2;
                            }

                            if (P.foundFood)
                            {
                                Vector3d pVector = P.pPlane.XAxis;
                                pVector.Unitize();

                                Vector3d turnVector = (15 - P.age) / 15 * pVector + towardsHomeVector * P.age / 15;
                                turnVector.Unitize();

                                P.moveVector += turnVector * 2;
                            }
                        }

                        //wander
                        if (p % retrieveWanderFoodFrequency(P) == 0)
                        {
                            P.moveVector += wanderVectors[(p + iteration) % wanderVectors.Count];
                        }

                        //when it's close to base wonder out
                        if (P.age < 30 && outsideVector.Length < retrieveSensorDistance(P) * 5)
                        {
                            outsideVector.Unitize();
                            P.moveVector += outsideVector * 10;
                        }

                        //towards home
                        if (P.foundFood)
                        {
                            if (p % retrieveWanderBaseFrequency(P) == 0)
                            {
                                P.moveVector += towardsHomeVector;
                            }
                        }

                        if (!P.foundFood && P.age > 100)
                        {
                            P.moveVector += towardsHomeVector * 0.01 * P.age / 100;
                        }

                        //when close to home, visit
                        if (outsideVector.Length <= retrieveSensorDistance(P)*2 && P.age > 30)
                        {
                            P.alignToVector(towardsHomeVector);
                            P.moveVector += towardsHomeVector;
                        }
                    }
                }
            }
            );
        }

        //----------------------------------

        void particleMoveAndDeposit()
        {
            bool moveToRandomNeighbour = true;

            List<Particle> shuffledParticles = particles.OrderBy(a => Guid.NewGuid()).ToList();

            Parallel.For(0, shuffledParticles.Count, i =>
            {
                Particle P = shuffledParticles[i];

                if (P.parentVoxel != null)
                {
  
                    Vector3d xVector = P.pPlane.XAxis;
                    xVector.Unitize();
                    Vector3d moveVector = P.moveVector;
                    moveVector.Unitize();
                    moveVector += xVector;

                    //slime wander movement
                    if (!P.parentParticleGroup.ant)
                    {
                        int wanderFrequency = retrieveWanderFrequency(P);

                        if (i % wanderFrequency == 0)
                        {
                            Vector3d wanderVector = wanderVectors[(i % wanderVectors.Count + iteration % wanderVectors.Count) % wanderVectors.Count];
                            moveVector += 1.5 * wanderVector;
                            moveVector.Unitize();
                            P.alignToVector(moveVector);
                        }
                    }

                    P.moveVector = new Vector3d();

                    //if 2D, adapt vector
                    if (planarXY) moveVector.Z = 0;
                    if (planarXZ) moveVector.Y = 0;
                    if (planarYZ) moveVector.X = 0;

                    double speedMultiplier = 1;
                    if (P.parentVoxel.speedMultiplier != -1) speedMultiplier = P.parentVoxel.speedMultiplier;

                    P.alignToVector(moveVector);
                    moveVector *= retrieveSpeed(P) * speedMultiplier;
                    Point3d nextLoc = P.pPlane.Origin + moveVector;

                    //if 2D, adapt coordinates
                    if (planarXY) nextLoc.Z = dimZ / 2;
                    if (planarXZ) nextLoc.Y = dimY / 2;
                    if (planarYZ) nextLoc.X = dimX / 2;

                    //apply boundaries for new location
                    nextLoc = boundaries(P, nextLoc);

                    //find parent voxel for new location
                    Voxel nextVoxel = getParentVoxel(nextLoc);

                    //account for maxDensity == 0
                    if (nextVoxel != null)
                    {
                        if (nextVoxel.maxDensity == 0) nextVoxel = null;
                    }

                    //move to a random neighbour
                    if (nextVoxel == null || nextVoxel.boundary)
                    {
                        int idX = P.parentVoxel.idX;
                        int idY = P.parentVoxel.idY;
                        int idZ = P.parentVoxel.idZ;

                        // move to random neighbour?
                        if (dynPop == true)
                        {
                            if (idX != 0 && idY != 0 && idZ != 0)
                            {
                                moveToRandomNeighbour = true;
                            }
                            else
                            {
                                P.die = true;
                                moveToRandomNeighbour = false;
                            }
                        }

                        if (moveToRandomNeighbour)
                        {
                            List<Voxel> neighbours = new List<Voxel>();

                            //create list with all viable neighbours
                            int range = System.Convert.ToInt32(retrieveSpeed(P) * speedMultiplier / voxelSize);
                            if (range < 1) range = 1;

                            //int range = 1;

                            for (int u = idX - range; u <= idX + range; u += range)
                            {
                                for (int v = idY - range; v <= idY + range; v += range)
                                {
                                    for (int w = idZ - range; w <= idZ + range; w += range)
                                    {
                                        if (u >= 0 && u < resX && v >= 0 && v < resY && w >= 0 && w < resZ)
                                        {
                                            if (voxels[u, v, w] != null)
                                            {
                                                if (voxels[u, v, w].maxDensity != 0 && voxels[u, v, w].boundary == false)
                                                {
                                                    Voxel neighbour = voxels[u, v, w];
                                                    neighbours.Add(neighbour);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (neighbours.Count > 0)
                            {
                                //pick random one
                                //System.Random random = new System.Random(iteration);
                                int randomIndex = random.Next(0, neighbours.Count - 1);
                                nextLoc = neighbours[randomIndex].loc;
                                nextVoxel = neighbours[randomIndex];
                            }
                        }
                    }

                    if (nextVoxel != null)
                    {
                        //assign new location
                        P.pPlane.Origin = nextLoc;
                        P.parentVoxel = nextVoxel;

                        //check if the next parent voxel is occupied by other particles
                        if (nextVoxel.particleCount == 0)
                        {
                            particleDeposit(P, retrieveDepositValue(P));
                            P.highDeposit = true;
                        }


                        if (nextVoxel.particleCount != 0)
                        {
                            P.highDeposit = false;
                        }
                    }
                }
            }
            );
        }

        //----------------------------------

        void particleDeposit(Particle P, double _depositValue)
        {
            if (P.parentVoxel != null)
            {
                if (P.parentVoxel.maxDensity != 0)
                {
                    if (wrapBoundaries)
                    {
                        //ant particles
                        if (P.parentParticleGroup.ant)
                        {
                            if (P.age < maxAge)
                            {
                                ageMultiplierBase = multiplierBase[P.age];
                                ageMultiplierFood = multiplierFood[P.age];
                            }
                            else
                            {
                                ageMultiplierBase = minBase;
                                ageMultiplierFood = minFood;
                            }

                            if (P.foundFood)
                            {
                                P.parentVoxel.towardsFoodPheromone += _depositValue * ageMultiplierFood;
                            }
                            else
                            {
                                if (P.parentVoxel.towardsFoodPheromone > 0)
                                {
                                    P.parentVoxel.towardsBasePheromone += 1.2 * _depositValue * ageMultiplierBase;
                                }
                                else
                                {
                                    P.parentVoxel.towardsBasePheromone += 0.8 * _depositValue * ageMultiplierBase;
                                }
                            }
                        }
                        else //slime particles
                        {
                            if (P.highDeposit)
                            {
                                if (slime_antBase == 0 && slime_antFood == 0)
                                {
                                    P.parentVoxel.density += _depositValue;
                                }
                                else
                                {
                                    if (slime_antFood > 0) P.parentVoxel.density += _depositValue * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                    if (slime_antBase > 0) P.parentVoxel.density += _depositValue * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                }
                            }
                            else
                            {
                                if (slime_antBase == 0 && slime_antFood == 0)
                                {
                                    P.parentVoxel.density += _depositValue/4;
                                }
                                else
                                {
                                    if (slime_antFood > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                    if (slime_antBase > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                }
                            }
                        }
                    }
                    else //wrapBoundaries == false 
                    {
                            int boundaryRange = 1;

                        if (tridimensional)
                        {
                            if (dimX > retrieveSensorDistance(P) * 2 && dimY > retrieveSensorDistance(P) * 2 && dimZ > retrieveSensorDistance(P) * 2)
                            {
                                boundaryRange = Convert.ToInt32(retrieveSensorDistance(P));
                            }

                            if (P.parentVoxel.idX >= boundaryRange && P.parentVoxel.idX < resX - boundaryRange && P.parentVoxel.idY >= boundaryRange && P.parentVoxel.idY < resY - boundaryRange && P.parentVoxel.idZ >= boundaryRange && P.parentVoxel.idZ < resZ - boundaryRange)
                            {

                                //ant particles
                                if (P.parentParticleGroup.ant)
                                {
                                    if (P.age < maxAge)
                                    {
                                        ageMultiplierBase = multiplierBase[P.age];
                                        ageMultiplierFood = multiplierFood[P.age]; ;
                                    }
                                    else
                                    {
                                        ageMultiplierBase = minBase;
                                        ageMultiplierFood = minFood;
                                    }

                                    if (P.foundFood)
                                    {
                                        P.parentVoxel.towardsFoodPheromone += _depositValue * ageMultiplierFood;
                                    }
                                    else
                                    {
                                        if (P.parentVoxel.towardsFoodPheromone > 0)
                                        {
                                            P.parentVoxel.towardsBasePheromone += 1.2 * _depositValue * ageMultiplierBase;
                                        }
                                        else
                                        {
                                            P.parentVoxel.towardsBasePheromone += 0.8 * _depositValue * ageMultiplierBase;
                                        }
                                    }
                                }
                                else
                                {
                                    if (P.highDeposit)
                                    {
                                        if (slime_antBase == 0 && slime_antFood == 0)
                                        {
                                            P.parentVoxel.density += _depositValue;
                                        }
                                        else
                                        {
                                            if (slime_antFood > 0) P.parentVoxel.density += _depositValue * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                            if (slime_antBase > 0) P.parentVoxel.density += _depositValue * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                        }
                                    }
                                    else
                                    {
                                        if (slime_antBase == 0 && slime_antFood == 0)
                                        {
                                            P.parentVoxel.density += _depositValue/4;
                                        }
                                        else
                                        {
                                            if (slime_antFood > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                            if (slime_antBase > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                        }
                                    }
                                }
                            }
                        }
                        else //tridimensional == false
                        {
                            if (planarXY)
                            {
                                if (dimX > retrieveSensorDistance(P) * 2 && dimY > retrieveSensorDistance(P) * 2)
                                {
                                    boundaryRange = Convert.ToInt32(retrieveSensorDistance(P));
                                }

                                if (P.parentVoxel.idX >= boundaryRange && P.parentVoxel.idX < resX - boundaryRange && P.parentVoxel.idY >= boundaryRange && P.parentVoxel.idY < resY - boundaryRange)
                                {

                                    //ant particles
                                    if (P.parentParticleGroup.ant)
                                    {
                                        if (P.age < maxAge)
                                        {
                                            ageMultiplierBase = multiplierBase[P.age];
                                            ageMultiplierFood = multiplierFood[P.age]; ;
                                        }
                                        else
                                        {
                                            ageMultiplierBase = minBase;
                                            ageMultiplierFood = minFood;
                                        }

                                        if (P.foundFood)
                                        {
                                            P.parentVoxel.towardsFoodPheromone += _depositValue * ageMultiplierFood;
                                        }
                                        else
                                        {
                                            if (P.parentVoxel.towardsFoodPheromone > 0)
                                            {
                                                P.parentVoxel.towardsBasePheromone += 1.2 * _depositValue * ageMultiplierBase;
                                            }
                                            else
                                            {
                                                P.parentVoxel.towardsBasePheromone += 0.8 * _depositValue * ageMultiplierBase;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (P.highDeposit)
                                        {
                                            if (slime_antBase == 0 && slime_antFood == 0)
                                            {
                                                P.parentVoxel.density += _depositValue;
                                            }
                                            else
                                            {
                                                if (slime_antFood > 0) P.parentVoxel.density += _depositValue * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                                if (slime_antBase > 0) P.parentVoxel.density += _depositValue * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                            }
                                        }
                                        else
                                        {
                                            if (slime_antBase == 0 && slime_antFood == 0)
                                            {
                                                P.parentVoxel.density += _depositValue/4;
                                            }
                                            else
                                            {
                                                if (slime_antFood > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                                if (slime_antBase > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                            }
                                        }
                                    }
                                }
                            }
                            else if (planarXZ)
                            {
                                if (dimX > retrieveSensorDistance(P) * 2 && dimZ > retrieveSensorDistance(P) * 2)
                                {
                                    boundaryRange = Convert.ToInt32(retrieveSensorDistance(P));
                                }

                                if (P.parentVoxel.idX >= boundaryRange && P.parentVoxel.idX < resX - boundaryRange && P.parentVoxel.idZ >= boundaryRange && P.parentVoxel.idZ < resZ - boundaryRange)
                                {

                                    //ant particles
                                    if (P.parentParticleGroup.ant)
                                    {
                                        if (P.age < maxAge)
                                        {
                                            ageMultiplierBase = multiplierBase[P.age];
                                            ageMultiplierFood = multiplierFood[P.age]; ;
                                        }
                                        else
                                        {
                                            ageMultiplierBase = minBase;
                                            ageMultiplierFood = minFood;
                                        }

                                        if (P.foundFood)
                                        {
                                            P.parentVoxel.towardsFoodPheromone += _depositValue * ageMultiplierFood;
                                        }
                                        else
                                        {
                                            if (P.parentVoxel.towardsFoodPheromone > 0)
                                            {
                                                P.parentVoxel.towardsBasePheromone += 1.2 * _depositValue * ageMultiplierBase;
                                            }
                                            else
                                            {
                                                P.parentVoxel.towardsBasePheromone += 0.8 * _depositValue * ageMultiplierBase;
                                            }
                                        }

                                    }
                                    else
                                    {
                                        if (P.highDeposit)
                                        {
                                            if (slime_antBase == 0 && slime_antFood == 0)
                                            {
                                                P.parentVoxel.density += _depositValue;
                                            }
                                            else
                                            {
                                                if (slime_antFood > 0) P.parentVoxel.density += _depositValue * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                                if (slime_antBase > 0) P.parentVoxel.density += _depositValue * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                            }
                                        }
                                        else
                                        {
                                            if (slime_antBase == 0 && slime_antFood == 0)
                                            {
                                                P.parentVoxel.density += _depositValue/4;
                                            }
                                            else
                                            {
                                                if (slime_antFood > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                                if (slime_antBase > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                            }
                                        }
                                    }
                                }
                            }
                            else if (planarYZ)
                            {
                                if (dimY > retrieveSensorDistance(P) * 2 && dimZ > retrieveSensorDistance(P) * 2)
                                {
                                    boundaryRange = Convert.ToInt32(retrieveSensorDistance(P));
                                }

                                if (P.parentVoxel.idY >= boundaryRange && P.parentVoxel.idY < resY - boundaryRange && P.parentVoxel.idZ >= boundaryRange && P.parentVoxel.idZ < resZ - boundaryRange)
                                {

                                    //ant particles
                                    if (P.parentParticleGroup.ant)
                                    {
                                        if (P.age < maxAge)
                                        {
                                            ageMultiplierBase = multiplierBase[P.age];
                                            ageMultiplierFood = multiplierFood[P.age]; ;
                                        }
                                        else
                                        {
                                            ageMultiplierBase = minBase;
                                            ageMultiplierFood = minFood;
                                        }

                                        if (P.foundFood)
                                        {
                                            P.parentVoxel.towardsFoodPheromone += _depositValue * ageMultiplierFood;
                                        }
                                        else
                                        {
                                            if (P.parentVoxel.towardsFoodPheromone > 0)
                                            {
                                                P.parentVoxel.towardsBasePheromone += 1.2 * _depositValue * ageMultiplierBase;
                                            }
                                            else
                                            {
                                                P.parentVoxel.towardsBasePheromone += 0.8 * _depositValue * ageMultiplierBase;
                                            }
                                        }

                                    }
                                    else
                                    {
                                        if (P.highDeposit)
                                        {
                                            if (slime_antBase == 0 && slime_antFood == 0)
                                            {
                                                P.parentVoxel.density += _depositValue;
                                            }
                                            else
                                            {
                                                if (slime_antFood > 0) P.parentVoxel.density += _depositValue * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                                if (slime_antBase > 0) P.parentVoxel.density += _depositValue * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                            }
                                        } else
                                        {
                                            if (slime_antBase == 0 && slime_antFood == 0)
                                            {
                                                P.parentVoxel.density += _depositValue/4;
                                            }
                                            else
                                            {
                                                if (slime_antFood > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antFood) + P.parentVoxel.towardsFoodPheromone * slime_antFood;
                                                if (slime_antBase > 0) P.parentVoxel.density += _depositValue/4 * (1 - slime_antBase) + P.parentVoxel.towardsBasePheromone * slime_antBase;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        //----------------------------------

        //deposits for ants are dependent on age
        void createAntAgeMultipliers()
        {
            //create age multiplier list for ant
            maxAge = 100;
            minBase = 0.2;
            minFood = 0.3;
            max = 1;
            multiplierBase = new double[maxAge];
            multiplierFood = new double[maxAge];

            for (int i = 0; i < maxAge; i++)
            {
                multiplierBase[i] = reMapValue(i, 0, maxAge - 1, max, minBase);
                multiplierFood[i] = reMapValue(i, 0, maxAge - 1, max, minFood);
            }
        }

        //----------------------------------

        void particleRecordTrail()
        {
            if (particles != null)
            {
                Parallel.For(0, particles.Count, i =>
                {
                    Particle P = particles[i];
                    if (P.parentVoxel != null)
                    {
                        if (trailSize > 1)
                        {
                            if (iteration % trailFreq == 0)
                            {
                                if (P.trails.Count > 0)
                                {
                                    P.trails.Insert(0, P.pPlane.Origin);
                                }
                                else P.trails.Add(P.pPlane.Origin);

                                if (P.trails.Count > trailSize)
                                {
                                    P.trails.RemoveAt(P.trails.Count - 1);
                                }
                            }
                            else
                            {
                                P.trails.Insert(0, P.pPlane.Origin);
                                if (P.trails.Count > 1) P.trails.RemoveAt(1);
                            }
                        }
                        else
                        {
                            P.trails.Clear();
                        }
                    }
                }
                );
            }
        }

        //-------------------------------------------------------------------

        Voxel getParentVoxel(Point3d p)
        {
            Voxel p_parent = null;

            if (tridimensional)
            {
                int p_xID = System.Convert.ToInt32((p.X - Math.Abs(p.X % voxelSize)) / voxelSize);
                int p_yID = System.Convert.ToInt32((p.Y - Math.Abs(p.Y % voxelSize)) / voxelSize);
                int p_zID = System.Convert.ToInt32((p.Z - Math.Abs(p.Z % voxelSize)) / voxelSize);

                if (p_xID >= 0 && p_xID < resX && p_yID >= 0 && p_yID < resY && p_zID >= 0 && p_zID < resZ)
                {
                    if (voxels[p_xID, p_yID, p_zID] != null)
                    {
                        p_parent = voxels[p_xID, p_yID, p_zID];
                    }
                }
                else
                {
                    p_parent = null;
                }
            }
            else //tridimensional == false
            {
                if (planarXY)
                {
                    int p_xID = System.Convert.ToInt32((p.X - Math.Abs(p.X % voxelSize)) / voxelSize);
                    int p_yID = System.Convert.ToInt32((p.Y - Math.Abs(p.Y % voxelSize)) / voxelSize);
                    int p_zID = 0;

                    if (p_xID >= 0 && p_xID < resX && p_yID >= 0 && p_yID < resY)
                    {
                        if (voxels[p_xID, p_yID, p_zID] != null)
                        {
                            p_parent = voxels[p_xID, p_yID, p_zID];
                        }
                    }
                    else
                    {
                        p_parent = null;
                    }
                }
                else if (planarXZ)
                {
                    int p_xID = System.Convert.ToInt32((p.X - Math.Abs(p.X % voxelSize)) / voxelSize);
                    int p_yID = 0;
                    int p_zID = System.Convert.ToInt32((p.Z - Math.Abs(p.Z % voxelSize)) / voxelSize);

                    if (p_xID >= 0 && p_xID < resX && p_zID >= 0 && p_zID < resZ)
                    {
                        if (voxels[p_xID, p_yID, p_zID] != null)
                        {
                            p_parent = voxels[p_xID, p_yID, p_zID];
                        }
                    }
                    else
                    {
                        p_parent = null;
                    }
                }
                else if (planarYZ)
                {
                    int p_xID = 0;
                    int p_yID = System.Convert.ToInt32((p.Y - Math.Abs(p.Y % voxelSize)) / voxelSize);
                    int p_zID = System.Convert.ToInt32((p.Z - Math.Abs(p.Z % voxelSize)) / voxelSize);

                    if (p_yID >= 0 && p_yID < resY && p_zID >= 0 && p_zID < resZ)
                    {
                        if (voxels[p_xID, p_yID, p_zID] != null)
                        {
                            p_parent = voxels[p_xID, p_yID, p_zID];
                        }
                    }
                    else
                    {
                        p_parent = null;
                    }
                }
            }

            return p_parent;
        }

        //----------------------------------

        Point3d boundaries(Particle P, Point3d p)
        {
            Point3d nextLoc = p;

            if (wrapBoundaries == false)
            {
                double boundaryDistance = voxelSize;

                if (planarYZ == false)
                {
                    if (nextLoc.X <= boundaryDistance)
                    {
                        nextLoc.X = boundaryDistance;

                        Vector3d xV = P.pPlane.XAxis;
                        xV.Unitize();
                        Vector3d newV_X = new Vector3d(-xV.X, xV.Y, xV.Z);
                        Vector3d newV_Y = new Vector3d(newV_X);
                        newV_Y.Rotate(Math.PI / 2, P.pPlane.ZAxis);
                        P.pPlane = new Plane(P.pPlane.Origin, newV_X, newV_Y);
                    }

                    if (nextLoc.X >= dimX - boundaryDistance)
                    {
                        nextLoc.X = dimX - boundaryDistance;

                        Vector3d xV = P.pPlane.XAxis;
                        xV.Unitize();
                        Vector3d newV_X = new Vector3d(-xV.X, xV.Y, xV.Z);
                        Vector3d newV_Y = new Vector3d(newV_X);
                        newV_Y.Rotate(Math.PI / 2, P.pPlane.ZAxis);
                        P.pPlane = new Plane(P.pPlane.Origin, newV_X, newV_Y);
                    }
                }

                if (planarXZ == false)
                {
                    if (nextLoc.Y <= boundaryDistance)
                    {
                        nextLoc.Y = boundaryDistance;

                        Vector3d xV = P.pPlane.XAxis;
                        xV.Unitize();
                        Vector3d newV_X = new Vector3d(xV.X, -xV.Y, xV.Z);
                        Vector3d newV_Y = new Vector3d(newV_X);
                        newV_Y.Rotate(Math.PI / 2, P.pPlane.ZAxis);
                        P.pPlane = new Plane(P.pPlane.Origin, newV_X, newV_Y);
                    }

                    if (nextLoc.Y >= dimY - boundaryDistance)
                    {
                        nextLoc.Y = dimY - boundaryDistance;

                        Vector3d xV = P.pPlane.XAxis;
                        xV.Unitize();
                        Vector3d newV_X = new Vector3d(xV.X, -xV.Y, xV.Z);
                        Vector3d newV_Y = new Vector3d(newV_X);
                        newV_Y.Rotate(Math.PI / 2, P.pPlane.ZAxis);
                        P.pPlane = new Plane(P.pPlane.Origin, newV_X, newV_Y);
                    }
                }

                if (planarXY == false)
                {

                    if (nextLoc.Z <= boundaryDistance)
                    {
                        nextLoc.Z = boundaryDistance;

                        if (tridimensional == true)
                        {
                            Vector3d xV = P.pPlane.XAxis;
                            xV.Unitize();
                            Vector3d newV_X = new Vector3d(xV.X, xV.Y, -xV.Z);
                            Vector3d newV_Y = new Vector3d(newV_X);
                            newV_Y.Rotate(Math.PI / 2, P.pPlane.ZAxis);
                            P.pPlane = new Plane(P.pPlane.Origin, newV_X, newV_Y);
                        }
                    }

                    if (nextLoc.Z >= dimZ - boundaryDistance)
                    {
                        nextLoc.Z = dimZ - boundaryDistance;

                        if (tridimensional == true)
                        {
                            Vector3d xV = P.pPlane.XAxis;
                            xV.Unitize();
                            Vector3d newV_X = new Vector3d(xV.X, xV.Y, -xV.Z);
                            Vector3d newV_Y = new Vector3d(newV_X);
                            newV_Y.Rotate(Math.PI / 2, P.pPlane.ZAxis);
                            P.pPlane = new Plane(P.pPlane.Origin, newV_X, newV_Y);
                        }
                    }
                }
            }

            if (wrapBoundaries == true)
            {
                if (tridimensional == true)
                {
                    double newX = nextLoc.X;
                    double newY = nextLoc.Y;
                    double newZ = nextLoc.Z;

                    if (nextLoc.X < 0.01)
                    {
                        newX = dimX - 0.1;
                        P.trails.Clear();
                    }

                    if (nextLoc.X > dimX - 0.01)
                    {
                        newX = 0.1;
                        P.trails.Clear();
                    }

                    if (nextLoc.Y < 0.01)
                    {
                        newY = dimY - 0.1;
                        P.trails.Clear();
                    }

                    if (nextLoc.Y > dimY - 0.01)
                    {
                        newY = 0.1;
                        P.trails.Clear();
                    }

                    if (nextLoc.Z < 0.01)
                    {
                        newZ = dimZ - 0.1;
                        P.trails.Clear();
                    }

                    if (nextLoc.Z > dimZ - 0.01)
                    {
                        newZ = 0.1;
                        P.trails.Clear();
                    }

                    nextLoc = new Point3d(newX, newY, newZ);
                    
                }
                else // tridimensional == false
                {
                    if (planarXY)
                    {
                        double newX = nextLoc.X;
                        double newY = nextLoc.Y;
                        double newZ = nextLoc.Z;

                        if (nextLoc.X < 0.01)
                        {
                            newX = dimX - 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.X > dimX - 0.01)
                        {
                            newX = 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.Y < 0.01)
                        {
                            newY = dimY - 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.Y > dimY - 0.01)
                        {
                            newY = 0.1;
                            P.trails.Clear();
                        }

                        nextLoc = new Point3d(newX, newY, newZ);
                    }
                    else if (planarXZ)
                    {
                        double newX = nextLoc.X;
                        double newY = nextLoc.Y;
                        double newZ = nextLoc.Z;

                        if (nextLoc.X < 0.01)
                        {
                            newX = dimX - 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.X > dimX - 0.01)
                        {
                            newX = 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.Z < 0.01)
                        {
                            newZ = dimZ - 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.Z > dimZ - 0.01)
                        {
                            newZ = 0.1;
                            P.trails.Clear();
                        }

                        nextLoc = new Point3d(newX, newY, newZ);
                    }
                    else if (planarYZ)
                    {
                        double newX = nextLoc.X;
                        double newY = nextLoc.Y;
                        double newZ = nextLoc.Z;

                        if (nextLoc.Y < 0.01)
                        {
                            newY = dimY - 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.Y > dimY - 0.01)
                        {
                            newY = 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.Z < 0.01)
                        {
                            newZ = dimZ - 0.1;
                            P.trails.Clear();
                        }

                        if (nextLoc.Z > dimZ - 0.01)
                        {
                            newZ = 0.1;
                            P.trails.Clear();
                        }

                        nextLoc = new Point3d(newX, newY, newZ);
                    }
                }
            }

            return nextLoc;
        }

        //-------------------------------------------------------------------

        void killParticles()
        {
            if (iteration % dieFreq == 0)
            {
                //shuffle particles
                particles = particles.OrderBy(a => Guid.NewGuid()).ToList();

                int counter = 0;

                //kill particles according to settings
                if (particles.Count > minPopulation)
                {
                    //check death conditions
                    Parallel.For(0, particles.Count, i =>
                    {
                        Particle P = particles[i];

                        if (minPopulation < particles.Count - counter)
                        {

                            if (death)
                            {
                                if (P.parentVoxel == null)
                                {
                                    P.die = true;
                                    counter++;
                                }
                                else
                                {
                                    if (P.neighbourCount_Die <= minDieN || P.neighbourCount_Die >= maxDieN) P.die = true;
                                    if (P.age < dieMinAge && P.neighbourCount_Die > 2) P.die = false;
                                }
                            }
                        }
                    }
                    );

                    particles.RemoveAll(p => p.die == true);
                }
            }
        }

        //----------------------------------

        void divideParticles_old()
        {
            if (iteration % divFreq == 0)
            {
                if (particles.Count < maxPopulation)
                {
                    //check division conditions
                    Parallel.For(0, particles.Count, i =>
                    {
                        Particle P = particles[i];

                        if (division)
                        {
                            if (P.parentVoxel == null)
                            {
                                P.divide = false;
                            }
                            else
                            {
                                bool particleDivide = false;
                                if (minDivN <= P.neighbourCount_Div && P.neighbourCount_Div <= maxDivN)
                                {
                                    particleDivide = true;

                                    if (P.parentVoxel.minDensity != -1)
                                    {
                                        if (P.age < divMinAge) particleDivide = false;
                                    }
                                }

                                if (particleDivide) P.divide = true;
                            }
                        }
                    }
                    );

                    //shuffle particles
                    List<Particle> shuffledParticles = particles.OrderBy(a => Guid.NewGuid()).ToList();

                    //add particles
                    List<Particle> newParticles = shuffledParticles;

                    for (int i = 0; i < shuffledParticles.Count; i+=2)
                    {
                        Particle P = shuffledParticles[i];

                        if (P.divide && newParticles.Count < maxPopulation)
                        {
                            P.divide = false;

                            //find empty voxel in close proximity
                            Voxel emptyNeighbourV = null;
                            List<Voxel> emptyNeighbours = new List<Voxel>();

                            if (tridimensional)
                            {
                                for (int u = -1; u <= 1; u++)
                                {
                                    for (int v = -1; v <= 1; v++)
                                    {
                                        for (int w = -1; w <= 1; w++)
                                        {
                                            if (P.parentVoxel.idX + u > 1 && P.parentVoxel.idX + u < resX - 1 && P.parentVoxel.idY + v > 1 && P.parentVoxel.idY + v < resY - 1 && P.parentVoxel.idZ + w > 1 && P.parentVoxel.idZ + w < resZ - 1)
                                            {
                                                if (voxels[P.parentVoxel.idX + u, P.parentVoxel.idY + v, P.parentVoxel.idZ + w] != null)
                                                {
                                                    Voxel neighbourV = voxels[P.parentVoxel.idX + u, P.parentVoxel.idY + v, P.parentVoxel.idZ + w];
                                                    if (neighbourV.particleCount == 0 && neighbourV.maxDensity != 0)
                                                    {
                                                        emptyNeighbours.Add(neighbourV);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                //chose random empty neighbour
                                if (emptyNeighbours.Count > 0)
                                {
                                    if (emptyNeighbours.Count == 1)
                                    {
                                        emptyNeighbourV = emptyNeighbours[0];
                                    }
                                    else
                                    {
                                        int randomIndex = (int)Math.Floor(random.NextDouble() * (emptyNeighbours.Count - 1));
                                        emptyNeighbourV = emptyNeighbours[randomIndex];
                                    }
                                }

                            } else //tridimensional == false
                            {
                                if (planarXY)
                                {
                                    for (int u = -1; u <= 1; u++)
                                    {
                                        for (int v = -1; v <= 1; v++)
                                        {
                                            if (P.parentVoxel.idX + u > 1 && P.parentVoxel.idX + u < resX - 1 && P.parentVoxel.idY + v > 1 && P.parentVoxel.idY + v < resY - 1)
                                            {
                                                if (voxels[P.parentVoxel.idX + u, P.parentVoxel.idY + v, 0] != null)
                                                {
                                                    Voxel neighbourV = voxels[P.parentVoxel.idX + u, P.parentVoxel.idY + v, 0];
                                                    if (neighbourV.particleCount == 0 && neighbourV.maxDensity != 0)
                                                    {
                                                        emptyNeighbours.Add(neighbourV);
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    //chose random empty neighbour
                                    if (emptyNeighbours.Count > 0)
                                    {
                                        if (emptyNeighbours.Count == 1)
                                        {
                                            emptyNeighbourV = emptyNeighbours[0];
                                        }
                                        else
                                        {
                                            int randomIndex = (int) Math.Floor(random.NextDouble() * (emptyNeighbours.Count - 1));
                                            emptyNeighbourV = emptyNeighbours[randomIndex];
                                        }
                                    }
                                }
                                else if (planarXZ)
                                {
                                    for (int u = -1; u <= 1; u++)
                                    {
                                        for (int w = -1; w <= 1; w++)
                                        {
                                            if (P.parentVoxel.idX + u > 1 && P.parentVoxel.idX + u < resX - 1 && P.parentVoxel.idZ + w > 1 && P.parentVoxel.idZ + w < resZ - 1)
                                            {
                                                if (voxels[P.parentVoxel.idX + u, 0, P.parentVoxel.idZ + w] != null)
                                                {
                                                    Voxel neighbourV = voxels[P.parentVoxel.idX + u, 0, P.parentVoxel.idZ + w];
                                                    if (neighbourV.particleCount == 0)
                                                    {
                                                        emptyNeighbours.Add(neighbourV);
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    //chose random empty neighbour
                                    if (emptyNeighbours.Count > 0)
                                    {
                                        if (emptyNeighbours.Count == 1)
                                        {
                                            emptyNeighbourV = emptyNeighbours[0];
                                        }
                                        else
                                        {
                                            int randomIndex = (int) Math.Floor(random.NextDouble() * (emptyNeighbours.Count - 1));
                                            emptyNeighbourV = emptyNeighbours[randomIndex];
                                        }
                                    }
                                }
                                else if (planarYZ)
                                {
                                    for (int v = -1; v <= 1; v++)
                                    {
                                        for (int w = -1; w <= 1; w++)
                                        {
                                            if (P.parentVoxel.idY + v > 1 && P.parentVoxel.idY + v < resY - 1 && P.parentVoxel.idZ + w > 1 && P.parentVoxel.idZ + w < resZ - 1)
                                            {
                                                if (voxels[0, P.parentVoxel.idY + v, P.parentVoxel.idZ + w] != null)
                                                {
                                                    Voxel neighbourV = voxels[0, P.parentVoxel.idY + v, P.parentVoxel.idZ + w];
                                                    if (neighbourV.particleCount == 0)
                                                    {
                                                        emptyNeighbours.Add(neighbourV);
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    //chose random empty neighbour
                                    if (emptyNeighbours.Count > 0)
                                    {
                                        if (emptyNeighbours.Count == 1)
                                        {
                                            emptyNeighbourV = emptyNeighbours[0];
                                        }
                                        else
                                        {
                                            int randomIndex = (int) Math.Floor(random.NextDouble() * (emptyNeighbours.Count - 1));
                                            emptyNeighbourV = emptyNeighbours[randomIndex];
                                        }
                                    }
                                }
                            }

                            if (emptyNeighbourV != null)
                            {
                                Plane newPlane = new Plane(P.pPlane);

                                Vector3d xVector = emptyNeighbourV.loc - P.pPlane.Origin;
                                
                                xVector.Unitize();

                                if (tridimensional)
                                {
                                    Plane newPl = new Plane(emptyNeighbourV.loc, xVector);
                                    newPlane = new Plane(newPl.Origin, newPl.ZAxis, newPl.YAxis);
                                    //System.Random random = new System.Random(89 * i);
                                    newPlane.Rotate(reMapValue(random.NextDouble(), 0, 1, -Math.PI * 4, Math.PI * 4), newPlane.XAxis, newPlane.Origin);
                                }
                                else //tridimensional == false
                                {
                                    if (planarXY)
                                    {
                                        xVector = new Vector3d(xVector.X, xVector.Y, 0);
                                        Vector3d yVector = new Vector3d(xVector);
                                        yVector.Rotate(Math.PI / 2, Plane.WorldXY.ZAxis);
                                        newPlane = new Plane(emptyNeighbourV.loc, xVector, yVector);
                                    }
                                    else if (planarXZ)
                                    {
                                        xVector = new Vector3d(xVector.X, 0, xVector.Z);
                                        Vector3d yVector = new Vector3d(xVector);
                                        yVector.Rotate(Math.PI / 2, Plane.WorldZX.ZAxis);
                                        newPlane = new Plane(emptyNeighbourV.loc, xVector, yVector);

                                    }
                                    else if (planarYZ)
                                    {
                                        xVector = new Vector3d(0, xVector.Y, xVector.Z);
                                        Vector3d yVector = new Vector3d(xVector);
                                        yVector.Rotate(Math.PI / 2, Plane.WorldYZ.ZAxis);
                                        newPlane = new Plane(emptyNeighbourV.loc, xVector, yVector);
                                    }
                                }

                                Particle newP = new Particle(newPlane);
                                newP.parentParticleGroup = P.parentParticleGroup;

                                //assign parent voxel
                                newP.parentVoxel = emptyNeighbourV;

                                //increase particle count to parent voxel
                                newP.parentVoxel.particleCount++;

                                //deposit chemoattractors
                                particleDeposit(newP, retrieveDepositValue(newP) * 2);

                                //add new particle to list
                                newParticles.Add(newP);
                                P.parentParticleGroup.particles.Add(newP);
                            }
                        }
                    }

                    particles.Clear();
                    particles = newParticles;

                    particleCheckNeighbourCount();
                }
            }
        }


        void divideParticles()
        {
            if (iteration % divFreq == 0)
            {
                if (particles.Count < maxPopulation)
                {
                    //check division conditions
                    Parallel.For(0, particles.Count, i =>
                    {
                        Particle P = particles[i];

                        if (division)
                        {
                            if (P.parentVoxel == null)
                            {
                                P.divide = false;
                            }
                            else
                            {
                                bool particleDivide = false;
                                if (minDivN <= P.neighbourCount_Div && P.neighbourCount_Div <= maxDivN)
                                {
                                    particleDivide = true;

                                    if (P.parentVoxel.minDensity != -1)
                                    {
                                        if (P.age < divMinAge) particleDivide = false;
                                    }
                                }

                                if (particleDivide) P.divide = true;
                            }
                        }
                    }
                    );

                    //shuffle particles
                    List<Particle> shuffledParticles = particles.OrderBy(a => Guid.NewGuid()).ToList();

                    //add particles
                    List<Particle> newParticles = shuffledParticles;

                    for (int i = 0; i < shuffledParticles.Count; i += 2)
                    {
                        Particle P = shuffledParticles[i];

                        if (P.divide && newParticles.Count < maxPopulation)
                        {
                            P.divide = false;

                            Plane newPlane = new Plane(P.pPlane.Origin, P.pPlane.XAxis, P.pPlane.YAxis);

                            Particle newP = new Particle(newPlane);

                            Vector3d newP_Vector = P.PPlane.XAxis + P.moveVector;
                            newP_Vector.Unitize();

                            Vector3d newP_Vector_R = new Vector3d(newP_Vector.X, newP_Vector.Y, newP_Vector.Z);
                            Vector3d newP_Vector_L = new Vector3d(newP_Vector.X, newP_Vector.Y, newP_Vector.Z);

                            newP_Vector_R.Rotate(retrieveRotationAngle(P) / 4, P.PPlane.ZAxis);
                            newP_Vector_L.Rotate(-retrieveRotationAngle(P) / 4, P.PPlane.ZAxis);

                            P.alignToVector(P.PPlane.XAxis - newP_Vector_R);
                            newP.alignToVector(P.PPlane.XAxis - newP_Vector_L);

                            newP.parentParticleGroup = P.parentParticleGroup;

                            //assign parent voxel
                            newP.parentVoxel = particleCheckParentVoxel(P);
                            if (newP.parentVoxel.maxDensity == 0) newP.die = true;

                            //increase particle count to parent voxel
                            newP.parentVoxel.particleCount++;

                            //deposit chemoattractors
                            //particleDeposit(newP, retrieveDepositValue(newP) * 2);

                            //add new particle to lists
                            if (!newP.die)
                            {
                                newParticles.Add(newP);
                                P.parentParticleGroup.particles.Add(newP);
                            }
                        }
                    }

                    particles.Clear();
                    particles = newParticles;

                    particleCheckNeighbourCount();
                }
            }
        }

        //-------------------------------------------------------------------

        void readSolverSettings()
        {
            //reset voxel settings
            diffuse = 0.1;
            diffuseRange = 1;
            decay = 0.03;

            foodDiffuseRate = 0.05;
            foodDecayRate = 0.005;
            baseDiffuseRate = 0.1;
            baseDecayRate = 0.01;
            diffuseRange_Ant = 1;

            wrapBoundaries = false;

            //reset particle population settings
            dynPop = false;
            minPopulation = 100;
            maxPopulation = 20000;

            //reset particle division settings
            division = false;
            divMinAge = 10;
            divRange = 6;
            minDivN = 100;
            maxDivN = 500;
            divFreq = 5;

            //reset particle death settings
            death = false;
            dieMinAge = 10;
            dieRange = 1;
            minDieN = 0;
            maxDieN = 100;
            dieFreq = 5;

            //reset particle trail settings
            trailSize = 0;
            trailFreq = 1;

            slime_antBase = 0;
            slime_antFood = 0;
            ant_slime = 0;

            //read particle settings
            bool speciesInteractionSettingsExist = false;

            //in case there are no solver iterations settings
            maxIterations = 100000;

            for (int i = 0; i < settings.Count; i++)
            {
                String inputSettings = settings[i];
                String[] inputSettings_components = inputSettings.Split(' ');
                String type = inputSettings_components[0];

                switch (type)
                {
                    case "VoxelSettingsSlime":
                        diffuse = Convert.ToDouble(inputSettings_components[1]);
                        diffuseRange = Convert.ToInt32(inputSettings_components[2]);
                        decay = Convert.ToDouble(inputSettings_components[3]);
                        break;

                    case "VoxelSettingsAnt":
                        foodDiffuseRate = Convert.ToDouble(inputSettings_components[1]);
                        foodDecayRate = Convert.ToDouble(inputSettings_components[2]);
                        baseDiffuseRate = Convert.ToDouble(inputSettings_components[3]);
                        baseDecayRate = Convert.ToDouble(inputSettings_components[4]);
                        diffuseRange_Ant = Convert.ToInt32(inputSettings_components[5]);
                        break;

                    case "WrapSettings":
                        wrapBoundaries = Convert.ToBoolean(inputSettings_components[1]);
                        break;

                    case "SpeciesInteractionSettings":
                        speciesInteractionSettingsExist = true;
                        slime_antFood = Convert.ToDouble(inputSettings_components[1]);
                        slime_antBase = Convert.ToDouble(inputSettings_components[2]);
                        ant_slime = Convert.ToDouble(inputSettings_components[3]);
                        break;

                    case "PopulationSettings":
                        minPopulation = Convert.ToInt32(inputSettings_components[1]);
                        maxPopulation = Convert.ToInt32(inputSettings_components[2]);
                        break;

                    case "DivisionSettings":
                        division = Convert.ToBoolean(inputSettings_components[1]);
                        divMinAge = Convert.ToInt32(inputSettings_components[2]);
                        divRange = Convert.ToInt32(inputSettings_components[3]);
                        minDivN = Convert.ToInt32(inputSettings_components[4]);
                        maxDivN = Convert.ToInt32(inputSettings_components[5]);
                        divFreq = Convert.ToInt32(inputSettings_components[6]);
                        if (divFreq < 1) divFreq = 1;
                        break;

                    case "DeathSettings":
                        death = Convert.ToBoolean(inputSettings_components[1]);
                        dieMinAge = Convert.ToInt32(inputSettings_components[2]);
                        dieRange = Convert.ToInt32(inputSettings_components[3]);
                        minDieN = Convert.ToInt32(inputSettings_components[4]);
                        maxDieN = Convert.ToInt32(inputSettings_components[5]);
                        dieFreq = Convert.ToInt32(inputSettings_components[6]);
                        if (dieFreq < 1) dieFreq = 1;
                        break;

                    case "TrailSettings":
                        trailSize = Convert.ToInt32(inputSettings_components[1]);
                        trailFreq = Convert.ToInt32(inputSettings_components[2]);
                        if (trailFreq < 1) trailFreq = 1;
                        break;

                    case "SolverSettings":
                        maxIterations = Convert.ToInt32(inputSettings_components[1]);
                        break;
                }

                if (division || death)
                {
                    dynPop = true;
                }
                else
                {
                    dynPop = false;
                }
            }

            //in case there are no species interaction settings
            if (speciesInteractionSettingsExist == false)
            {
                slime_antFood = 0;
                slime_antBase = 0;
                ant_slime = 0;
            }
        }

        //-------------------------------------------------------------------

        double retrieveSpeed(Particle P)
        {
            return P.parentParticleGroup.speed;
        }

        double retrieveSensorDistance(Particle P)
        {
            return P.parentParticleGroup.sensorDistance;
        }

        int retrieveSensorAngle(Particle P)
        {
            return P.parentParticleGroup.sensorAngle;
        }

        int retrieveRotationAngle(Particle P)
        {
            return P.parentParticleGroup.rotationAngle;
        }

        double retrieveDepositValue(Particle P)
        {
            return P.parentParticleGroup.depositValue;
        }

        int retrieveWanderFrequency(Particle P)
        {
            return (int) P.parentParticleGroup.wanderFrequency;
        }

        int retrieveWanderFoodFrequency(Particle P)
        {
            return (int) P.parentParticleGroup.foodWanderFrequency;
        }

        int retrieveWanderBaseFrequency(Particle P)
        {
             return (int) P.parentParticleGroup.baseWanderFrequency;
        }

        //-------------------------------------------------------------------

        void initializeParticleColors()
        {
            Globals.particleColorList = new List<Color>();

            Color slime_color1 = System.Drawing.Color.FromArgb(125, 220, 255, 0);
            Color slime_color2 = System.Drawing.Color.FromArgb(125, 255, 174, 48);
            Color slime_color3 = System.Drawing.Color.FromArgb(125, 255, 63, 53);
            Color slime_color4 = System.Drawing.Color.FromArgb(125, 255, 71, 201);
            Color slime_color5 = System.Drawing.Color.FromArgb(125, 132, 0, 255);

            Globals.particleColorList.Add(slime_color1);
            Globals.particleColorList.Add(slime_color2);
            Globals.particleColorList.Add(slime_color3);
            Globals.particleColorList.Add(slime_color4);
            Globals.particleColorList.Add(slime_color5);

            Globals.antColorList = new List<Color>();
            Globals.antColorList_foundFood = new List<Color>();

            Color ant_color1 = System.Drawing.Color.FromArgb(125, 66, 236, 122);
            Color ant_color2 = System.Drawing.Color.FromArgb(125, 45, 239, 222);
            Color ant_color3 = System.Drawing.Color.FromArgb(125, 57, 168, 239);
            Color ant_color4 = System.Drawing.Color.FromArgb(125, 112, 115, 255);
            Color ant_color5 = System.Drawing.Color.FromArgb(125, 143, 238, 32);

            Globals.antColorList.Add(ant_color1);
            Globals.antColorList.Add(ant_color2);
            Globals.antColorList.Add(ant_color3);
            Globals.antColorList.Add(ant_color4);
            Globals.antColorList.Add(ant_color5);
        }


        //-------------------------------------------------------------------

        void precomputeAngles()
        {
             radAngle = new List<double>();

            for (int i = 0; i < 361; i++)
            {
                 radAngle.Add(Rhino.RhinoMath.ToRadians(i));
            }
        }

        //-------------------------------------------------------------------

        void createWanderVectors()
        {
            Random randomVecPos = new Random();
            wanderVectors = new List<Vector3d>();

            wanderVectors.Add(new Vector3d(1, 0, 0));
            wanderVectors.Add(new Vector3d(0, 1, 0));
            wanderVectors.Add(new Vector3d(-1, 0, 0));
            wanderVectors.Add(new Vector3d(0, -1, 0));
            wanderVectors.Add(new Vector3d(-1, -1, 0));
            wanderVectors.Add(new Vector3d(-1, 1, 0));
            wanderVectors.Add(new Vector3d(1, -1, 0));
            wanderVectors.Add(new Vector3d(1, 1, 0));
        }

        //-------------------------------------------------------------------

        double reMapValue(double s, double a1, double a2, double b1, double b2)
        {
            return b1 + (s - a1) * (b2 - b1) / (a2 - a1);
        }

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
                return Nuclei3.Properties.Resources.Solver;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("e14eac59-d6d0-442a-a6b3-3e3a8f30be0f"); }
        }
    }
}