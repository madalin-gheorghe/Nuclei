using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;


using Rhino;
using Rhino.Geometry;
using Rhino.Display;
using Rhino.DocObjects;

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Drawing;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.CompilerServices;
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
            long solveStart = Stopwatch.GetTimestamp();

            DA.GetData(0, ref reset);

            if (reset == true)
            {
                resetTimingAverages();
                TimingReporter.StartRun();

                //iteration counter
                iteration = 0;
                antParticles = false;
                particleCountsRequireFullReset = true;
                particleCountTouchedCount = 0;

                //utility
                random = new System.Random(89);
                precomputeAngles();
                createWanderVectors();

                //read solver settings before deriving voxel boundary state
                settings = new List<String>();
                DA.GetDataList(3, settings);
                readSolverSettings();

                //set voxels
                DA.GetData(1, ref inputVoxels);

                inheritVoxels();

                //set particles
                inputParticleGroups = new List<ParticleGroup>();
                DA.GetType();
                DA.GetDataList(2, inputParticleGroups);

                particles = new ParticleList();

                inheritParticleGroups();
                particleCheckParentVoxel();

                //global colors
                //initializeParticleColors();

                //ant utility
                if (antParticles)
                {
                    createAntAgeMultipliers();
                }

                //create discrete vectors
                //createDiscreteVectors();

                this.Message = "Solution is Reset";
            }
            else
            {
                long settingsTicks = 0;
                long inputsTicks = 0;
                long senseTicks = 0;
                long moveTicks = 0;
                long trailTicks = 0;
                long diffuseTicks = 0;
                long parentTicks = 0;
                long populationTicks = 0;
                long outputsTicks = 0;
                long densitySyncTicks = 0;
                long setParticlesTicks = 0;
                long setVoxelsTicks = 0;
                long sensePrepareTicks = 0;
                long senseParticlesTicks = 0;
                long senseAntTicks = 0;
                long moveShuffleTicks = 0;
                long moveParticlesTicks = 0;
                long stageStart = Stopwatch.GetTimestamp();

                //read solver settings
                bool previousWrapBoundaries = wrapBoundaries;
                settings = new List<String>();
                DA.GetDataList(3, settings);
                readSolverSettings();
                bool wrapBoundaryChanged = previousWrapBoundaries != wrapBoundaries;
                refreshVoxelBoundaryDensityLimitsIfNeeded();
                settingsTicks = Stopwatch.GetTimestamp() - stageStart;

                stageStart = Stopwatch.GetTimestamp();
                inputParticleGroups = new List<ParticleGroup>();
                DA.GetType();
                DA.GetDataList(2, inputParticleGroups);
                updateParticleGroups();
                if (wrapBoundaryChanged)
                {
                    applyParticleBoundaryStateAfterWrapChange();
                }
                inputsTicks = Stopwatch.GetTimestamp() - stageStart;

                if (iteration < maxIterations)
                {
                    //run algorithm
                    if (iteration > 1)
                    {
                        stageStart = Stopwatch.GetTimestamp();
                        particleSenseValuesAndVectors(out sensePrepareTicks, out senseParticlesTicks);
                        if (antParticles)
                        {
                            long antSenseStart = Stopwatch.GetTimestamp();
                            particleSense_Ant();
                            senseAntTicks = Stopwatch.GetTimestamp() - antSenseStart;
                        }
                        senseTicks = Stopwatch.GetTimestamp() - stageStart;

                        stageStart = Stopwatch.GetTimestamp();
                        particleMoveAndDeposit(out moveShuffleTicks, out moveParticlesTicks);
                        moveTicks = Stopwatch.GetTimestamp() - stageStart;
                    }

                    //particleDepositChemoattractors();
                    stageStart = Stopwatch.GetTimestamp();
                    particleRecordTrail(); //careful with list.Add, better make arrays
                    trailTicks = Stopwatch.GetTimestamp() - stageStart;

                    //voxel logics
                    stageStart = Stopwatch.GetTimestamp();
                    diffuseVoxels();
                    diffuseTicks = Stopwatch.GetTimestamp() - stageStart;

                    //reorder data
                    stageStart = Stopwatch.GetTimestamp();
                    particleCheckParentVoxel();
                    parentTicks = Stopwatch.GetTimestamp() - stageStart;

                    //adaptive population
                    stageStart = Stopwatch.GetTimestamp();
                    if (iteration > 1 && dynPop)
                    {
                        particleCheckNeighbourCount();
                        killParticles();
                        divideParticles();
                    }
                    populationTicks = Stopwatch.GetTimestamp() - stageStart;

                    iteration++;

                }

                //set outputs
                stageStart = Stopwatch.GetTimestamp();
                long outputStageStart = Stopwatch.GetTimestamp();
                syncScalarDensityToVoxelsIfNeeded();
                densitySyncTicks = Stopwatch.GetTimestamp() - outputStageStart;
                outputStageStart = Stopwatch.GetTimestamp();
                DA.SetData(0, particles);
                setParticlesTicks = Stopwatch.GetTimestamp() - outputStageStart;
                outputStageStart = Stopwatch.GetTimestamp();
                DA.SetData(1, voxels);
                setVoxelsTicks = Stopwatch.GetTimestamp() - outputStageStart;
                outputsTicks = Stopwatch.GetTimestamp() - stageStart;

                bool reachedMaxIterations = iteration >= maxIterations;
                if (!reachedMaxIterations)
                {
                    recordTimingAverages(
                        settingsTicks,
                        inputsTicks,
                        senseTicks,
                        moveTicks,
                        trailTicks,
                        diffuseTicks,
                        parentTicks,
                        populationTicks,
                        outputsTicks,
                        densitySyncTicks,
                        setParticlesTicks,
                        setVoxelsTicks,
                        sensePrepareTicks,
                        senseParticlesTicks,
                        senseAntTicks,
                        moveShuffleTicks,
                        moveParticlesTicks,
                        Stopwatch.GetTimestamp() - solveStart);
                }

                this.Message = reachedMaxIterations
                    ? "Complete: " + iteration + "/" + maxIterations
                    : "Iteration: " + iteration;
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
        Voxel[] voxelFlat;
        Voxel[] activeVoxels;
        double[] scalarVoxelDensity;
        double[] scalarVoxelScratch;
        VoxelDensityStore scalarDensityStore;
        bool scalarVoxelDensityAuthoritative;
        bool scalarVoxelDensityDirtyForOutput;
        bool voxelHasPositiveFood;
        Voxel[] particleCountTouchedVoxels = Array.Empty<Voxel>();
        int particleCountTouchedCount = 0;
        bool particleCountsRequireFullReset = true;
        bool denseVoxelGrid;
        bool densityLimitsOnlyBoundaryVoxels;
        bool densityLimitsDisabled;
        bool boundaryLimitsWrapState;

        /////////////////////////////////////////////

        //reset
        bool reset = true;

        //iteration
        int iteration = 0;
        int maxIterations = 100000;

        /////////////////////////////////////////////

        //voxel dimensions
        double voxelSize;
        double voxelSizeInverse;
        int resX;
        int resY;
        int resZ;
        int voxelStrideX;
        int voxelStrideY;

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

        //discrete 
        bool discretizeMovement = true;
        Vector3d[] discreteVectors = Array.Empty<Vector3d>();

        //ant settings
        bool antParticles = false;

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

        //reusable arrays for diffusion logic
        double[] reusableWeights;
        double[] reusableAntWeights;
        int reusableWeightsRange = int.MinValue;
        int reusableAntWeightsRange = int.MinValue;

        /////////////////////////////////////////////

        //random
        System.Random random = new System.Random(89);
        private static readonly ThreadLocal<System.Random> threadLocalRandom = new ThreadLocal<System.Random>(() => new System.Random(Guid.NewGuid().GetHashCode()));
        private static readonly ThreadLocal<double[]> threadLocalValues = new ThreadLocal<double[]>(() => new double[5]);
        private static readonly ThreadLocal<Voxel[]> threadLocalNeighbors = new ThreadLocal<Voxel[]>(() => new Voxel[27]);

        /////////////////////////////////////////////

        //timing
        int timingSampleCount = 0;
        long timingTotalTicks = 0;
        long timingSettingsTicks = 0;
        long timingInputsTicks = 0;
        long timingSenseTicks = 0;
        long timingMoveTicks = 0;
        long timingTrailTicks = 0;
        long timingDiffuseTicks = 0;
        long timingParentTicks = 0;
        long timingPopulationTicks = 0;
        long timingOutputsTicks = 0;
        long timingDensitySyncTicks = 0;
        long timingSetParticlesTicks = 0;
        long timingSetVoxelsTicks = 0;
        long timingSensePrepareTicks = 0;
        long timingSenseParticlesTicks = 0;
        long timingSenseAntTicks = 0;
        long timingMoveShuffleTicks = 0;
        long timingMoveParticlesTicks = 0;
        TimingReporter.SolverContext timingContext;
        string timingContextKey = "";

        /////////////////////////////////////////////

        //-------------------------------------------------------------------

        void resetTimingAverages()
        {
            writeTimingAverages();
            clearTimingCounters();
        }

        void clearTimingCounters()
        {
            timingSampleCount = 0;
            timingTotalTicks = 0;
            timingSettingsTicks = 0;
            timingInputsTicks = 0;
            timingSenseTicks = 0;
            timingMoveTicks = 0;
            timingTrailTicks = 0;
            timingDiffuseTicks = 0;
            timingParentTicks = 0;
            timingPopulationTicks = 0;
            timingOutputsTicks = 0;
            timingDensitySyncTicks = 0;
            timingSetParticlesTicks = 0;
            timingSetVoxelsTicks = 0;
            timingSensePrepareTicks = 0;
            timingSenseParticlesTicks = 0;
            timingSenseAntTicks = 0;
            timingMoveShuffleTicks = 0;
            timingMoveParticlesTicks = 0;
            timingContext = new TimingReporter.SolverContext();
            timingContextKey = "";
        }

        void recordTimingAverages(
            long settingsTicks,
            long inputsTicks,
            long senseTicks,
            long moveTicks,
            long trailTicks,
            long diffuseTicks,
            long parentTicks,
            long populationTicks,
            long outputsTicks,
            long densitySyncTicks,
            long setParticlesTicks,
            long setVoxelsTicks,
            long sensePrepareTicks,
            long senseParticlesTicks,
            long senseAntTicks,
            long moveShuffleTicks,
            long moveParticlesTicks,
            long totalTicks)
        {
            TimingReporter.SolverContext currentContext = createTimingContext();
            string currentContextKey = createTimingContextKey(currentContext);

            if (timingSampleCount > 0 && timingContextKey != currentContextKey)
            {
                writeTimingAverages();
                clearTimingCounters();
            }

            if (timingSampleCount == 0)
            {
                timingContext = currentContext;
                timingContextKey = currentContextKey;
            }

            timingSampleCount++;
            timingTotalTicks += totalTicks;
            timingSettingsTicks += settingsTicks;
            timingInputsTicks += inputsTicks;
            timingSenseTicks += senseTicks;
            timingMoveTicks += moveTicks;
            timingTrailTicks += trailTicks;
            timingDiffuseTicks += diffuseTicks;
            timingParentTicks += parentTicks;
            timingPopulationTicks += populationTicks;
            timingOutputsTicks += outputsTicks;
            timingDensitySyncTicks += densitySyncTicks;
            timingSetParticlesTicks += setParticlesTicks;
            timingSetVoxelsTicks += setVoxelsTicks;
            timingSensePrepareTicks += sensePrepareTicks;
            timingSenseParticlesTicks += senseParticlesTicks;
            timingSenseAntTicks += senseAntTicks;
            timingMoveShuffleTicks += moveShuffleTicks;
            timingMoveParticlesTicks += moveParticlesTicks;

            if (timingSampleCount < TimingReporter.ReportFrequency) return;

            writeTimingAverages();
            clearTimingCounters();
        }

        void writeTimingAverages()
        {
            if (timingSampleCount <= 0) return;

            int particleCount = particles != null ? particles.Count : 0;
            int voxelCount = activeVoxels != null ? activeVoxels.Length : 0;

            TimingReporter.WriteSolverAverages(
                iteration,
                timingSampleCount,
                particleCount,
                voxelCount,
                timingContext,
                TimingReporter.TicksToMilliseconds(timingTotalTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSettingsTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingInputsTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSenseTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingMoveTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingTrailTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingDiffuseTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingParentTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingPopulationTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingOutputsTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingDensitySyncTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSetParticlesTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSetVoxelsTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSensePrepareTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSenseParticlesTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSenseAntTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingMoveShuffleTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingMoveParticlesTicks, timingSampleCount));
        }

        TimingReporter.SolverContext createTimingContext()
        {
            TimingReporter.SolverContext context = new TimingReporter.SolverContext();
            context.WrapBoundaries = wrapBoundaries;
            context.ResX = resX;
            context.ResY = resY;
            context.ResZ = resZ;
            context.ActiveVoxels = activeVoxels != null ? activeVoxels.Length : 0;
            context.DenseVoxelGrid = denseVoxelGrid;
            context.DimensionMode = tridimensional ? "3d" : planarXY ? "xy" : planarXZ ? "xz" : planarYZ ? "yz" : "unknown";
            context.Diffuse = diffuse;
            context.DiffuseRange = diffuseRange;
            context.Decay = decay;
            context.AntParticles = antParticles;
            context.DiffuseRangeAnt = diffuseRange_Ant;
            context.TrailSize = trailSize;
            context.TrailFreq = trailFreq;
            context.DynPop = dynPop;
            context.Division = division;
            context.Death = death;
            context.MaxIterations = maxIterations;
            return context;
        }

        string createTimingContextKey(TimingReporter.SolverContext context)
        {
            return (context.WrapBoundaries ? "1" : "0")
                + "|" + context.ResX
                + "|" + context.ResY
                + "|" + context.ResZ
                + "|" + context.ActiveVoxels
                + "|" + (context.DenseVoxelGrid ? "1" : "0")
                + "|" + context.DimensionMode
                + "|" + context.Diffuse
                + "|" + context.DiffuseRange
                + "|" + context.Decay
                + "|" + (context.AntParticles ? "1" : "0")
                + "|" + context.DiffuseRangeAnt
                + "|" + context.TrailSize
                + "|" + context.TrailFreq
                + "|" + (context.DynPop ? "1" : "0")
                + "|" + (context.Division ? "1" : "0")
                + "|" + (context.Death ? "1" : "0")
                + "|" + context.MaxIterations;
        }

        //-------------------------------------------------------------------

        void inheritVoxels()
        {
            //determine voxel settings
            resX = inputVoxels.GetLength(0);
            resY = inputVoxels.GetLength(1);
            resZ = inputVoxels.GetLength(2);

            //determine voxelSize
            voxelSize = Globals.voxelSize;
            voxelSizeInverse = 1.0 / voxelSize;

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
            voxelStrideY = resZ;
            voxelStrideX = resY * voxelStrideY;
            int voxelCount = resX * resY * resZ;
            voxelFlat = new Voxel[voxelCount];
            scalarVoxelDensity = new double[voxelCount];
            scalarVoxelScratch = new double[voxelCount];
            scalarDensityStore = new VoxelDensityStore(scalarVoxelDensity);
            scalarVoxelDensityAuthoritative = true;
            scalarVoxelDensityDirtyForOutput = false;
            voxelHasPositiveFood = false;

            Voxel[] tempActiveVoxels = new Voxel[voxelCount];
            int activeVoxelCount = 0;

            Parallel.For(0, resX, i =>
            {
                for (int j = 0; j < resY; j++)
                {
                    for (int k = 0; k < resZ; k++)
                    {
                        if (inputVoxels[i, j, k] != null)
                        {
                            Voxel initialV = inputVoxels[i, j, k];

                            int flatIndex = i * voxelStrideX + j * voxelStrideY + k;
                            Voxel V = new Voxel(voxelSize, i, j, k);
                            V.flatIndex = flatIndex;
                            V.densityStore = scalarDensityStore;
                            voxels[i, j, k] = V;
                            voxelFlat[flatIndex] = V;

                            //assign the voxel values from the initial voxels
                            V.minDensity = initialV.minDensity;
                            V.maxDensity = initialV.maxDensity;
                            V.inputMinDensity = initialV.minDensity;
                            V.inputMaxDensity = initialV.maxDensity;

                            V.speedMultiplier = initialV.speedMultiplier;
                            V.sensorAngleMultiplier = initialV.sensorAngleMultiplier;
                            V.sensorDistanceMultiplier = initialV.sensorDistanceMultiplier;
                            V.rotationAngleMultiplier = initialV.rotationAngleMultiplier;

                            V.food = initialV.food;
                            if (V.food > 0)
                            {
                                voxelHasPositiveFood = true;
                            }

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
                            scalarVoxelDensity[flatIndex] = V.density;

                            //list of all active voxels
                            int idx = System.Threading.Interlocked.Increment(ref activeVoxelCount) - 1;
                            tempActiveVoxels[idx] = V;
                        }
                    }
                }
            }
            );

            //if all voxels are NULL, then instantiate new blank voxels
            if (activeVoxelCount == 0)
            {
                Parallel.For(0, resX, i =>
                {
                    for (int j = 0; j < resY; j++)
                    {
                        for (int k = 0; k < resZ; k++)
                        {
                            int flatIndex = i * voxelStrideX + j * voxelStrideY + k;
                            Voxel V = new Voxel(voxelSize, i, j, k);
                            V.flatIndex = flatIndex;
                            V.densityStore = scalarDensityStore;
                            voxels[i, j, k] = V;
                            voxelFlat[flatIndex] = V;
                            scalarVoxelDensity[flatIndex] = V.density;
                            int idx = System.Threading.Interlocked.Increment(ref activeVoxelCount) - 1;
                            tempActiveVoxels[idx] = V;
                        }
                    }
                }
            );
            }

            if (activeVoxelCount == tempActiveVoxels.Length)
            {
                activeVoxels = tempActiveVoxels;
                denseVoxelGrid = true;
            }
            else
            {
                activeVoxels = new Voxel[activeVoxelCount];
                Array.Copy(tempActiveVoxels, activeVoxels, activeVoxelCount);
                denseVoxelGrid = false;
            }

            refreshVoxelBoundaryDensityLimits();
            
            reusableWeightsRange = int.MinValue;
            reusableAntWeightsRange = int.MinValue;
            ensureReusableDiffusionWeights();
        }

        void refreshVoxelBoundaryDensityLimitsIfNeeded()
        {
            if (activeVoxels == null || voxels == null || boundaryLimitsWrapState == wrapBoundaries) return;
            refreshVoxelBoundaryDensityLimits();
        }

        void refreshVoxelBoundaryDensityLimits()
        {
            Voxel[,,] voxelGrid = voxels;
            Voxel[] active = activeVoxels;
            bool wrap = wrapBoundaries;
            int maxX = resX - 1;
            int maxY = resY - 1;
            int maxZ = resZ - 1;

            Parallel.For(0, active.Length, index =>
            {
                Voxel V = active[index];
                int i = V.idX;
                int j = V.idY;
                int k = V.idZ;
                bool boundary = false;

                V.boundary = false;
                V.minDensity = V.inputMinDensity;
                V.maxDensity = V.inputMaxDensity;

                if (!wrap)
                {
                    if (tridimensional)
                    {
                        boundary = i == 0 || i == maxX || j == 0 || j == maxY || k == 0 || k == maxZ;
                    }
                    else if (planarXY)
                    {
                        boundary = i == 0 || i == maxX || j == 0 || j == maxY;
                    }
                    else if (planarXZ)
                    {
                        boundary = i == 0 || i == maxX || k == 0 || k == maxZ;
                    }
                    else if (planarYZ)
                    {
                        boundary = j == 0 || j == maxY || k == 0 || k == maxZ;
                    }
                }

                for (int u = i - 1; !boundary && u <= i + 1; u++)
                {
                    for (int v = j - 1; !boundary && v <= j + 1; v++)
                    {
                        for (int w = k - 1; w <= k + 1; w++)
                        {
                            if (u >= 0 && u < resX && v >= 0 && v < resY && w >= 0 && w < resZ && voxelGrid[u, v, w] == null)
                            {
                                boundary = true;
                                break;
                            }
                        }
                    }
                }

                if (boundary)
                {
                    V.maxDensity = 0.01;
                    V.boundary = true;
                }
            });

            bool limitsOnlyBoundary = true;
            bool noDensityLimits = true;
            for (int i = 0; i < active.Length; i++)
            {
                Voxel V = active[i];
                bool hasLimit = V.minDensity != -1 || V.maxDensity != -1;
                if (!hasLimit) continue;

                noDensityLimits = false;
                if (!V.boundary)
                {
                    limitsOnlyBoundary = false;
                    break;
                }
            }

            densityLimitsOnlyBoundaryVoxels = limitsOnlyBoundary;
            densityLimitsDisabled = noDensityLimits;
            boundaryLimitsWrapState = wrap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool useScalarDensityPath()
        {
            return denseVoxelGrid &&
                   !antParticles &&
                   !voxelHasPositiveFood &&
                   scalarVoxelDensity != null &&
                   scalarVoxelScratch != null &&
                   (densityLimitsDisabled || densityLimitsOnlyBoundaryVoxels);
        }

        void syncScalarDensityToVoxelsIfNeeded()
        {
            if (!scalarVoxelDensityDirtyForOutput || scalarVoxelDensity == null) return;
            if (scalarDensityStore != null) scalarDensityStore.Values = scalarVoxelDensity;
            scalarVoxelDensityDirtyForOutput = false;
        }

        void ensureScalarDensityAuthoritative()
        {
            if (scalarVoxelDensityAuthoritative || scalarVoxelDensity == null || activeVoxels == null) return;

            double[] density = scalarVoxelDensity;
            Voxel[] active = activeVoxels;

            Parallel.For(0, active.Length, i =>
            {
                Voxel V = active[i];
                density[V.flatIndex] = V.density;
            });

            scalarVoxelDensityAuthoritative = true;
            scalarVoxelDensityDirtyForOutput = false;
        }

        //-------------------------------------------------------------------

        void diffuseVoxels()
        {
            ensureReusableDiffusionWeights();

            if (useScalarDensityPath())
            {
                ensureScalarDensityAuthoritative();

                if (diffuse > 0)
                {
                    diffuseScalarVoxels();
                }

                applyBoundaryAndDecayScalar();
                return;
            }

            scalarVoxelDensityAuthoritative = false;

            if (diffuse > 0)
            {
                if (!planarYZ)
                {
                    xPassInPlace(reusableWeights);
                }

                if (!planarXZ)
                {
                    yPassInPlace(reusableWeights);
                }

                if (!planarXY)
                {
                    zPassInPlace(reusableWeights);
                }

                /*
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
                */

            }

            //ant particles
            if (antParticles)
            {
                if (baseDiffuseRate > 0 || foodDiffuseRate > 0)
                {
                    if (iteration % 2 == 0)
                    {
                        if (!planarYZ)
                        {
                            ants_xPass(reusableAntWeights);
                        }
                        if (!planarXZ)
                        {
                            ants_yPass(reusableAntWeights);
                        }

                        if (!planarXY)
                        {
                            ants_zPass(reusableAntWeights);
                        }
                    }

                    else
                    {
                        if (!planarXY)
                        {
                           ants_zPass(reusableAntWeights);
                        }

                        if (!planarXZ)
                        {
                            ants_yPass(reusableAntWeights);
                        }

                        if (!planarYZ)
                        {
                            ants_xPass(reusableAntWeights);
                        }
                    }
                }
            }


            applyBoundaryAndDecay();
        }

        //-------------

        void applyBoundaryAndDecay()
        {
            Voxel[] active = activeVoxels;
            int activeCount = active.Length;
            double densityDecay = decay;

            if (!wrapBoundaries)
            {
                bool ant = antParticles;
                int maxX = resX - 1;
                int maxY = resY - 1;
                int maxZ = resZ - 1;
                double foodDecay = foodDecayRate;
                double baseDecay = baseDecayRate;

                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    bool boundary = false;

                    if (tridimensional)
                    {
                        boundary = V.idX == 0 || V.idX == maxX || V.idY == 0 || V.idY == maxY || V.idZ == 0 || V.idZ == maxZ;
                    }
                    else if (planarXY)
                    {
                        boundary = V.idX == 0 || V.idX == maxX || V.idY == 0 || V.idY == maxY;
                    }
                    else if (planarXZ)
                    {
                        boundary = V.idX == 0 || V.idX == maxX || V.idZ == 0 || V.idZ == maxZ;
                    }
                    else if (planarYZ)
                    {
                        boundary = V.idY == 0 || V.idY == maxY || V.idZ == 0 || V.idZ == maxZ;
                    }

                    if (boundary)
                    {
                        V.density = 0;
                        if (ant) V.towardsFoodPheromone = 0;
                    }

                    V.density -= densityDecay;
                    if (V.density < 0) V.density = 0;

                    if (ant)
                    {
                        V.towardsFoodPheromone -= foodDecay;
                        V.towardsBasePheromone -= baseDecay;

                        if (V.towardsFoodPheromone < 0) V.towardsFoodPheromone = 0;
                        if (V.towardsBasePheromone < 0) V.towardsBasePheromone = 0;
                    }
                });
            }
            else if (antParticles)
            {
                double foodDecay = foodDecayRate;
                double baseDecay = baseDecayRate;

                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];

                    V.density -= densityDecay;
                    if (V.density < 0) V.density = 0;

                    V.towardsFoodPheromone -= foodDecay;
                    V.towardsBasePheromone -= baseDecay;

                    if (V.towardsFoodPheromone < 0) V.towardsFoodPheromone = 0;
                    if (V.towardsBasePheromone < 0) V.towardsBasePheromone = 0;
                });
            }
            else
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    V.density -= densityDecay;
                    if (V.density < 0) V.density = 0;
                });
            }
        }

        void diffuseScalarVoxels()
        {
            double[] weights = reusableWeights;
            double keep = 1 - diffuse;
            double diffuseAmount = diffuse;

            if (!planarYZ)
            {
                diffuseScalarXPass(weights, keep, diffuseAmount);
                swapScalarDensityBuffers();
            }

            if (!planarXZ)
            {
                diffuseScalarYPass(weights, keep, diffuseAmount);
                swapScalarDensityBuffers();
            }

            if (!planarXY)
            {
                diffuseScalarZPass(weights, keep, diffuseAmount);
                swapScalarDensityBuffers();
            }

            scalarVoxelDensityDirtyForOutput = true;
        }

        void swapScalarDensityBuffers()
        {
            double[] temp = scalarVoxelDensity;
            scalarVoxelDensity = scalarVoxelScratch;
            scalarVoxelScratch = temp;
            if (scalarDensityStore != null) scalarDensityStore.Values = scalarVoxelDensity;
        }

        sealed class ScalarPrefixDiffusionBuffers
        {
            public double[] Sum = Array.Empty<double>();
            public double[] Cos = Array.Empty<double>();
            public double[] Sin = Array.Empty<double>();

            public void EnsureCapacity(int length)
            {
                if (Sum.Length < length) Sum = new double[length];
                if (Cos.Length < length) Cos = new double[length];
                if (Sin.Length < length) Sin = new double[length];
            }
        }

        void applyBoundaryAndDecayScalar()
        {
            double[] density = scalarVoxelDensity;
            double densityDecay = decay;

            if (wrapBoundaries)
            {
                Parallel.For(0, density.Length, i =>
                {
                    double value = density[i] - densityDecay;
                    density[i] = value > 0 ? value : 0;
                });
            }
            else if (planarYZ)
            {
                int maxY = resY - 1;
                int maxZ = resZ - 1;
                Parallel.For(0, resY, y =>
                {
                    bool boundaryY = y == 0 || y == maxY;
                    int baseIndex = y * voxelStrideY;
                    for (int z = 0; z < resZ; z++)
                    {
                        int index = baseIndex + z;
                        if (boundaryY || z == 0 || z == maxZ)
                        {
                            density[index] = 0;
                        }
                        else
                        {
                            double value = density[index] - densityDecay;
                            density[index] = value > 0 ? value : 0;
                        }
                    }
                });
            }
            else
            {
                int maxX = resX - 1;
                int maxY = resY - 1;
                int maxZ = resZ - 1;
                bool is3d = tridimensional;
                bool isXY = planarXY;
                bool isXZ = planarXZ;

                Parallel.For(0, resX, x =>
                {
                    bool boundaryX = x == 0 || x == maxX;
                    int xBase = x * voxelStrideX;

                    for (int y = 0; y < resY; y++)
                    {
                        bool boundaryY = y == 0 || y == maxY;
                        int baseIndex = xBase + y * voxelStrideY;

                        for (int z = 0; z < resZ; z++)
                        {
                            bool boundary = is3d
                                ? boundaryX || boundaryY || z == 0 || z == maxZ
                                : isXY
                                    ? boundaryX || boundaryY
                                    : isXZ
                                        ? boundaryX || z == 0 || z == maxZ
                                        : boundaryY || z == 0 || z == maxZ;

                            int index = baseIndex + z;
                            if (boundary)
                            {
                                density[index] = 0;
                            }
                            else
                            {
                                double value = density[index] - densityDecay;
                                density[index] = value > 0 ? value : 0;
                            }
                        }
                    }
                });
            }

            scalarVoxelDensityDirtyForOutput = true;
        }

        void diffuseScalarXPass(double[] weights, double keep, double diffuseAmount)
        {
            double[] source = scalarVoxelDensity;
            double[] destination = scalarVoxelScratch;
            int xCount = resX;
            int strideX = voxelStrideX;
            int strideY = voxelStrideY;
            bool wrap = wrapBoundaries;
            int range = diffuseRange;

            if (range != 1)
            {
                diffuseScalarXPassPrefix(source, destination, weights, range, keep, diffuseAmount, xCount, strideX, strideY, wrap);
                return;
            }

            if (tridimensional)
            {
                int lineCount = resY * resZ;
                Parallel.For(0, lineCount, line =>
                {
                    int y = line / resZ;
                    int z = line % resZ;
                    diffuseScalarXLine(source, destination, weights, y, z, xCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
            else if (planarXY)
            {
                Parallel.For(0, resY, y =>
                {
                    diffuseScalarXLine(source, destination, weights, y, 0, xCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
            else if (planarXZ)
            {
                Parallel.For(0, resZ, z =>
                {
                    diffuseScalarXLine(source, destination, weights, 0, z, xCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
        }

        void diffuseScalarYPass(double[] weights, double keep, double diffuseAmount)
        {
            double[] source = scalarVoxelDensity;
            double[] destination = scalarVoxelScratch;
            int yCount = resY;
            int strideX = voxelStrideX;
            int strideY = voxelStrideY;
            bool wrap = wrapBoundaries;
            int range = diffuseRange;

            if (range != 1)
            {
                diffuseScalarYPassPrefix(source, destination, weights, range, keep, diffuseAmount, yCount, strideX, strideY, wrap);
                return;
            }

            if (tridimensional)
            {
                int lineCount = resX * resZ;
                Parallel.For(0, lineCount, line =>
                {
                    int x = line / resZ;
                    int z = line % resZ;
                    diffuseScalarYLine(source, destination, weights, x, z, yCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
            else if (planarXY)
            {
                Parallel.For(0, resX, x =>
                {
                    diffuseScalarYLine(source, destination, weights, x, 0, yCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
            else if (planarYZ)
            {
                Parallel.For(0, resZ, z =>
                {
                    diffuseScalarYLine(source, destination, weights, 0, z, yCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
        }

        void diffuseScalarZPass(double[] weights, double keep, double diffuseAmount)
        {
            double[] source = scalarVoxelDensity;
            double[] destination = scalarVoxelScratch;
            int zCount = resZ;
            int strideX = voxelStrideX;
            int strideY = voxelStrideY;
            bool wrap = wrapBoundaries;
            int range = diffuseRange;

            if (range != 1)
            {
                diffuseScalarZPassPrefix(source, destination, weights, range, keep, diffuseAmount, zCount, strideX, strideY, wrap);
                return;
            }

            if (tridimensional)
            {
                int lineCount = resX * resY;
                Parallel.For(0, lineCount, line =>
                {
                    int x = line / resY;
                    int y = line % resY;
                    diffuseScalarZLine(source, destination, weights, x, y, zCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
            else if (planarXZ)
            {
                Parallel.For(0, resX, x =>
                {
                    diffuseScalarZLine(source, destination, weights, x, 0, zCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
            else if (planarYZ)
            {
                Parallel.For(0, resY, y =>
                {
                    diffuseScalarZLine(source, destination, weights, 0, y, zCount, strideX, strideY, wrap, keep, diffuseAmount);
                });
            }
        }

        void diffuseScalarXPassPrefix(double[] source, double[] destination, double[] weights, int range, double keep, double diffuseAmount, int xCount, int strideX, int strideY, bool wrap)
        {
            createCosinePrefixTables(xCount, range, wrap, out double[] cosTable, out double[] sinTable);
            double weightScale = diffusionWeightScale(weights, range);

            if (tridimensional)
            {
                int lineCount = resY * resZ;
                Parallel.For(0, lineCount, () => new ScalarPrefixDiffusionBuffers(), (line, loopState, buffers) =>
                {
                    int y = line / resZ;
                    int z = line % resZ;
                    diffuseScalarXLinePrefix(source, destination, buffers, cosTable, sinTable, y, z, xCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
            else if (planarXY)
            {
                Parallel.For(0, resY, () => new ScalarPrefixDiffusionBuffers(), (y, loopState, buffers) =>
                {
                    diffuseScalarXLinePrefix(source, destination, buffers, cosTable, sinTable, y, 0, xCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
            else if (planarXZ)
            {
                Parallel.For(0, resZ, () => new ScalarPrefixDiffusionBuffers(), (z, loopState, buffers) =>
                {
                    diffuseScalarXLinePrefix(source, destination, buffers, cosTable, sinTable, 0, z, xCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
        }

        void diffuseScalarYPassPrefix(double[] source, double[] destination, double[] weights, int range, double keep, double diffuseAmount, int yCount, int strideX, int strideY, bool wrap)
        {
            createCosinePrefixTables(yCount, range, wrap, out double[] cosTable, out double[] sinTable);
            double weightScale = diffusionWeightScale(weights, range);

            if (tridimensional)
            {
                int lineCount = resX * resZ;
                Parallel.For(0, lineCount, () => new ScalarPrefixDiffusionBuffers(), (line, loopState, buffers) =>
                {
                    int x = line / resZ;
                    int z = line % resZ;
                    diffuseScalarYLinePrefix(source, destination, buffers, cosTable, sinTable, x, z, yCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
            else if (planarXY)
            {
                Parallel.For(0, resX, () => new ScalarPrefixDiffusionBuffers(), (x, loopState, buffers) =>
                {
                    diffuseScalarYLinePrefix(source, destination, buffers, cosTable, sinTable, x, 0, yCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
            else if (planarYZ)
            {
                Parallel.For(0, resZ, () => new ScalarPrefixDiffusionBuffers(), (z, loopState, buffers) =>
                {
                    diffuseScalarYLinePrefix(source, destination, buffers, cosTable, sinTable, 0, z, yCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
        }

        void diffuseScalarZPassPrefix(double[] source, double[] destination, double[] weights, int range, double keep, double diffuseAmount, int zCount, int strideX, int strideY, bool wrap)
        {
            createCosinePrefixTables(zCount, range, wrap, out double[] cosTable, out double[] sinTable);
            double weightScale = diffusionWeightScale(weights, range);

            if (tridimensional)
            {
                int lineCount = resX * resY;
                Parallel.For(0, lineCount, () => new ScalarPrefixDiffusionBuffers(), (line, loopState, buffers) =>
                {
                    int x = line / resY;
                    int y = line % resY;
                    diffuseScalarZLinePrefix(source, destination, buffers, cosTable, sinTable, x, y, zCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
            else if (planarXZ)
            {
                Parallel.For(0, resX, () => new ScalarPrefixDiffusionBuffers(), (x, loopState, buffers) =>
                {
                    diffuseScalarZLinePrefix(source, destination, buffers, cosTable, sinTable, x, 0, zCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
            else if (planarYZ)
            {
                Parallel.For(0, resY, () => new ScalarPrefixDiffusionBuffers(), (y, loopState, buffers) =>
                {
                    diffuseScalarZLinePrefix(source, destination, buffers, cosTable, sinTable, 0, y, zCount, strideX, strideY, wrap, range, weightScale, keep, diffuseAmount);
                    return buffers;
                }, buffers => { });
            }
        }

        void createCosinePrefixTables(int count, int range, bool wrap, out double[] cosTable, out double[] sinTable)
        {
            int tableLength = wrap ? count + range * 2 : count;
            cosTable = new double[tableLength];
            sinTable = new double[tableLength];

            double theta = Math.PI / (range + 1);
            int coordinateStart = wrap ? -range : 0;

            for (int i = 0; i < tableLength; i++)
            {
                double angle = theta * (coordinateStart + i);
                cosTable[i] = Math.Cos(angle);
                sinTable[i] = Math.Sin(angle);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double diffusionWeightScale(double[] weights, int range)
        {
            return weights[range] * 0.5;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int wrapLineIndex(int index, int count)
        {
            if (index >= 0 && index < count) return index;
            index %= count;
            return index < 0 ? index + count : index;
        }

        void diffuseScalarXLinePrefix(double[] source, double[] destination, ScalarPrefixDiffusionBuffers buffers, double[] cosTable, double[] sinTable, int y, int z, int xCount, int strideX, int strideY, bool wrap, int range, double weightScale, double keep, double diffuseAmount)
        {
            int prefixLength = wrap ? xCount + range * 2 : xCount;
            buffers.EnsureCapacity(prefixLength + 1);

            double[] sumPrefix = buffers.Sum;
            double[] cosPrefix = buffers.Cos;
            double[] sinPrefix = buffers.Sin;
            int baseIndex = y * strideY + z;
            int coordinateStart = wrap ? -range : 0;

            sumPrefix[0] = 0;
            cosPrefix[0] = 0;
            sinPrefix[0] = 0;

            for (int i = 0; i < prefixLength; i++)
            {
                int x = wrap ? wrapLineIndex(coordinateStart + i, xCount) : i;
                double density = source[baseIndex + x * strideX];
                int next = i + 1;
                sumPrefix[next] = sumPrefix[i] + density;
                cosPrefix[next] = cosPrefix[i] + density * cosTable[i];
                sinPrefix[next] = sinPrefix[i] + density * sinTable[i];
            }

            int centerTableOffset = wrap ? range : 0;
            for (int x = 0; x < xCount; x++)
            {
                int start = wrap ? x : Math.Max(0, x - range);
                int endExclusive = wrap ? x + range * 2 + 1 : Math.Min(xCount - 1, x + range) + 1;
                int centerTableIndex = x + centerTableOffset;

                double sum = sumPrefix[endExclusive] - sumPrefix[start];
                double cosSum = cosPrefix[endExclusive] - cosPrefix[start];
                double sinSum = sinPrefix[endExclusive] - sinPrefix[start];
                double weightedSum = weightScale * (sum + cosTable[centerTableIndex] * cosSum + sinTable[centerTableIndex] * sinSum);

                int index = baseIndex + x * strideX;
                double value = source[index] * keep + diffuseAmount * weightedSum;
                destination[index] = clampScalarDensity(value, x, y, z);
            }
        }

        void diffuseScalarYLinePrefix(double[] source, double[] destination, ScalarPrefixDiffusionBuffers buffers, double[] cosTable, double[] sinTable, int x, int z, int yCount, int strideX, int strideY, bool wrap, int range, double weightScale, double keep, double diffuseAmount)
        {
            int prefixLength = wrap ? yCount + range * 2 : yCount;
            buffers.EnsureCapacity(prefixLength + 1);

            double[] sumPrefix = buffers.Sum;
            double[] cosPrefix = buffers.Cos;
            double[] sinPrefix = buffers.Sin;
            int baseIndex = x * strideX + z;
            int coordinateStart = wrap ? -range : 0;

            sumPrefix[0] = 0;
            cosPrefix[0] = 0;
            sinPrefix[0] = 0;

            for (int i = 0; i < prefixLength; i++)
            {
                int y = wrap ? wrapLineIndex(coordinateStart + i, yCount) : i;
                double density = source[baseIndex + y * strideY];
                int next = i + 1;
                sumPrefix[next] = sumPrefix[i] + density;
                cosPrefix[next] = cosPrefix[i] + density * cosTable[i];
                sinPrefix[next] = sinPrefix[i] + density * sinTable[i];
            }

            int centerTableOffset = wrap ? range : 0;
            for (int y = 0; y < yCount; y++)
            {
                int start = wrap ? y : Math.Max(0, y - range);
                int endExclusive = wrap ? y + range * 2 + 1 : Math.Min(yCount - 1, y + range) + 1;
                int centerTableIndex = y + centerTableOffset;

                double sum = sumPrefix[endExclusive] - sumPrefix[start];
                double cosSum = cosPrefix[endExclusive] - cosPrefix[start];
                double sinSum = sinPrefix[endExclusive] - sinPrefix[start];
                double weightedSum = weightScale * (sum + cosTable[centerTableIndex] * cosSum + sinTable[centerTableIndex] * sinSum);

                int index = baseIndex + y * strideY;
                double value = source[index] * keep + diffuseAmount * weightedSum;
                destination[index] = clampScalarDensity(value, x, y, z);
            }
        }

        void diffuseScalarZLinePrefix(double[] source, double[] destination, ScalarPrefixDiffusionBuffers buffers, double[] cosTable, double[] sinTable, int x, int y, int zCount, int strideX, int strideY, bool wrap, int range, double weightScale, double keep, double diffuseAmount)
        {
            int prefixLength = wrap ? zCount + range * 2 : zCount;
            buffers.EnsureCapacity(prefixLength + 1);

            double[] sumPrefix = buffers.Sum;
            double[] cosPrefix = buffers.Cos;
            double[] sinPrefix = buffers.Sin;
            int baseIndex = x * strideX + y * strideY;
            int coordinateStart = wrap ? -range : 0;

            sumPrefix[0] = 0;
            cosPrefix[0] = 0;
            sinPrefix[0] = 0;

            for (int i = 0; i < prefixLength; i++)
            {
                int z = wrap ? wrapLineIndex(coordinateStart + i, zCount) : i;
                double density = source[baseIndex + z];
                int next = i + 1;
                sumPrefix[next] = sumPrefix[i] + density;
                cosPrefix[next] = cosPrefix[i] + density * cosTable[i];
                sinPrefix[next] = sinPrefix[i] + density * sinTable[i];
            }

            int centerTableOffset = wrap ? range : 0;
            for (int z = 0; z < zCount; z++)
            {
                int start = wrap ? z : Math.Max(0, z - range);
                int endExclusive = wrap ? z + range * 2 + 1 : Math.Min(zCount - 1, z + range) + 1;
                int centerTableIndex = z + centerTableOffset;

                double sum = sumPrefix[endExclusive] - sumPrefix[start];
                double cosSum = cosPrefix[endExclusive] - cosPrefix[start];
                double sinSum = sinPrefix[endExclusive] - sinPrefix[start];
                double weightedSum = weightScale * (sum + cosTable[centerTableIndex] * cosSum + sinTable[centerTableIndex] * sinSum);

                int index = baseIndex + z;
                double value = source[index] * keep + diffuseAmount * weightedSum;
                destination[index] = clampScalarDensity(value, x, y, z);
            }
        }

        void diffuseScalarXLine(double[] source, double[] destination, double[] weights, int y, int z, int xCount, int strideX, int strideY, bool wrap, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = y * strideY + z;
            int lastX = xCount - 1;

            for (int x = 0; x < xCount; x++)
            {
                int index = baseIndex + x * strideX;
                double centerDensity = source[index];
                double sum;

                if (wrap)
                {
                    int leftIndex = x == 0 ? baseIndex + lastX * strideX : index - strideX;
                    int rightIndex = x == lastX ? baseIndex : index + strideX;
                    sum = source[leftIndex] * leftWeight + centerDensity * centerWeight + source[rightIndex] * rightWeight;
                }
                else
                {
                    sum = centerDensity * centerWeight;
                    if (x > 0) sum += source[index - strideX] * leftWeight;
                    if (x < lastX) sum += source[index + strideX] * rightWeight;
                }

                double value = centerDensity * keep + diffuseAmount * sum;
                destination[index] = clampScalarDensity(value, x, y, z);
            }
        }

        void diffuseScalarYLine(double[] source, double[] destination, double[] weights, int x, int z, int yCount, int strideX, int strideY, bool wrap, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = x * strideX + z;
            int lastY = yCount - 1;

            for (int y = 0; y < yCount; y++)
            {
                int index = baseIndex + y * strideY;
                double centerDensity = source[index];
                double sum;

                if (wrap)
                {
                    int leftIndex = y == 0 ? baseIndex + lastY * strideY : index - strideY;
                    int rightIndex = y == lastY ? baseIndex : index + strideY;
                    sum = source[leftIndex] * leftWeight + centerDensity * centerWeight + source[rightIndex] * rightWeight;
                }
                else
                {
                    sum = centerDensity * centerWeight;
                    if (y > 0) sum += source[index - strideY] * leftWeight;
                    if (y < lastY) sum += source[index + strideY] * rightWeight;
                }

                double value = centerDensity * keep + diffuseAmount * sum;
                destination[index] = clampScalarDensity(value, x, y, z);
            }
        }

        void diffuseScalarZLine(double[] source, double[] destination, double[] weights, int x, int y, int zCount, int strideX, int strideY, bool wrap, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = x * strideX + y * strideY;
            int lastZ = zCount - 1;

            for (int z = 0; z < zCount; z++)
            {
                int index = baseIndex + z;
                double centerDensity = source[index];
                double sum;

                if (wrap)
                {
                    int leftIndex = z == 0 ? baseIndex + lastZ : index - 1;
                    int rightIndex = z == lastZ ? baseIndex : index + 1;
                    sum = source[leftIndex] * leftWeight + centerDensity * centerWeight + source[rightIndex] * rightWeight;
                }
                else
                {
                    sum = centerDensity * centerWeight;
                    if (z > 0) sum += source[index - 1] * leftWeight;
                    if (z < lastZ) sum += source[index + 1] * rightWeight;
                }

                double value = centerDensity * keep + diffuseAmount * sum;
                destination[index] = clampScalarDensity(value, x, y, z);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double clampScalarDensity(double value, int x, int y, int z)
        {
            if (value > 1) value = 1;

            if (!wrapBoundaries && isBoundaryIndex(x, y, z) && value > 0.01)
            {
                value = 0.01;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool isBoundaryIndex(int x, int y, int z)
        {
            if (tridimensional)
            {
                return x == 0 || x == resX - 1 || y == 0 || y == resY - 1 || z == 0 || z == resZ - 1;
            }

            if (planarXY)
            {
                return x == 0 || x == resX - 1 || y == 0 || y == resY - 1;
            }

            if (planarXZ)
            {
                return x == 0 || x == resX - 1 || z == 0 || z == resZ - 1;
            }

            return y == 0 || y == resY - 1 || z == 0 || z == resZ - 1;
        }

        //-------------

        void xPassInPlace(double[] weights)
        {
            Voxel[,,] voxelGrid = voxels;
            Voxel[] flatGrid = voxelFlat;
            int range = diffuseRange;
            int xCount = resX;
            int strideX = voxelStrideX;
            int strideY = voxelStrideY;
            bool wrap = wrapBoundaries;
            double keep = 1 - diffuse;
            double diffuseAmount = diffuse;
            bool useInteriorFastPath = denseVoxelGrid && densityLimitsOnlyBoundaryVoxels && xCount > 2;
            bool useDenseWrapFastPath = denseVoxelGrid && densityLimitsDisabled;

            if (!wrap && range == 1)
            {
                if (tridimensional)
                {
                    int lineCount = resY * resZ;
                    Parallel.For(0, lineCount, line =>
                    {
                        int y = line / resZ;
                        int z = line % resZ;
                        if (useInteriorFastPath && y > 0 && y < resY - 1 && z > 0 && z < resZ - 1)
                        {
                            diffuseXLineInteriorRange1NoWrapDense(flatGrid, weights, y, z, xCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseXLineRange1NoWrap(voxelGrid, weights, y, z, xCount, keep, diffuseAmount);
                        }
                    });
                }
                else if (planarXY)
                {
                    Parallel.For(0, resY, y =>
                    {
                        if (useInteriorFastPath && y > 0 && y < resY - 1)
                        {
                            diffuseXLineInteriorRange1NoWrapDense(flatGrid, weights, y, 0, xCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseXLineRange1NoWrap(voxelGrid, weights, y, 0, xCount, keep, diffuseAmount);
                        }
                    });
                }
                else if (planarXZ)
                {
                    Parallel.For(0, resZ, z =>
                    {
                        if (useInteriorFastPath && z > 0 && z < resZ - 1)
                        {
                            diffuseXLineInteriorRange1NoWrapDense(flatGrid, weights, 0, z, xCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseXLineRange1NoWrap(voxelGrid, weights, 0, z, xCount, keep, diffuseAmount);
                        }
                    });
                }

                return;
            }

            if (wrap && range == 1)
            {
                if (tridimensional)
                {
                    int lineCount = resY * resZ;
                    Parallel.For(0, lineCount, () => new double[xCount], (line, loopState, lineDensity) =>
                    {
                        int y = line / resZ;
                        int z = line % resZ;
                        if (useDenseWrapFastPath)
                        {
                            diffuseXLineRange1WrapDense(flatGrid, weights, lineDensity, y, z, xCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseXLineRange1Wrap(voxelGrid, weights, lineDensity, y, z, xCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }
                else if (planarXY)
                {
                    Parallel.For(0, resY, () => new double[xCount], (y, loopState, lineDensity) =>
                    {
                        if (useDenseWrapFastPath)
                        {
                            diffuseXLineRange1WrapDense(flatGrid, weights, lineDensity, y, 0, xCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseXLineRange1Wrap(voxelGrid, weights, lineDensity, y, 0, xCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }
                else if (planarXZ)
                {
                    Parallel.For(0, resZ, () => new double[xCount], (z, loopState, lineDensity) =>
                    {
                        if (useDenseWrapFastPath)
                        {
                            diffuseXLineRange1WrapDense(flatGrid, weights, lineDensity, 0, z, xCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseXLineRange1Wrap(voxelGrid, weights, lineDensity, 0, z, xCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }

                return;
            }

            if (tridimensional)
            {
                int lineCount = resY * resZ;
                Parallel.For(0, lineCount, () => new double[xCount], (line, loopState, lineDensity) =>
                {
                    int y = line / resZ;
                    int z = line % resZ;
                    diffuseXLine(voxelGrid, weights, lineDensity, y, z, range, xCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
            else if (planarXY)
            {
                Parallel.For(0, resY, () => new double[xCount], (y, loopState, lineDensity) =>
                {
                    diffuseXLine(voxelGrid, weights, lineDensity, y, 0, range, xCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
            else if (planarXZ)
            {
                Parallel.For(0, resZ, () => new double[xCount], (z, loopState, lineDensity) =>
                {
                    diffuseXLine(voxelGrid, weights, lineDensity, 0, z, range, xCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
        }

        void diffuseXLine(Voxel[,,] voxelGrid, double[] weights, double[] lineDensity, int y, int z, int range, int xCount, bool wrap, double keep, double diffuseAmount)
        {
            if (!wrap)
            {
                for (int x = 0; x < xCount; x++)
                {
                    Voxel V = voxelGrid[x, y, z];
                    if (V == null) continue;

                    double sum = 0;
                    int startOffset = Math.Max(-range, -x);
                    int endOffset = Math.Min(range, xCount - 1 - x);
                    int weightIndex = startOffset + range;

                    for (int offset = startOffset; offset <= endOffset; offset++)
                    {
                        Voxel neighbour = voxelGrid[x + offset, y, z];
                        if (neighbour != null && neighbour.maxDensity != 0)
                        {
                            sum += neighbour.density * weights[weightIndex];
                        }
                        weightIndex++;
                    }

                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    lineDensity[x] = val;
                }

                for (int x = 0; x < xCount; x++)
                {
                    Voxel V = voxelGrid[x, y, z];
                    if (V != null) V.density = lineDensity[x];
                }

                return;
            }

            for (int x = 0; x < xCount; x++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V == null) continue;

                double sum = 0;
                int weightIndex = 0;

                for (int offset = -range; offset <= range; offset++)
                {
                    int d_xID = x + offset;

                    if (wrap)
                    {
                        if (d_xID < 0) d_xID += xCount;
                        if (d_xID >= xCount) d_xID -= xCount;
                    }

                    if (d_xID >= 0 && d_xID < xCount)
                    {
                        Voxel neighbour = voxelGrid[d_xID, y, z];
                        if (neighbour != null && neighbour.maxDensity != 0)
                        {
                            sum += neighbour.density * weights[weightIndex];
                        }
                    }
                    weightIndex++;
                }

                double val = V.density * keep + diffuseAmount * sum;
                if (val > 1) val = 1;
                if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                lineDensity[x] = val;
            }

            for (int x = 0; x < xCount; x++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V != null) V.density = lineDensity[x];
            }
        }

        void yPassInPlace(double[] weights)
        {
            Voxel[,,] voxelGrid = voxels;
            Voxel[] flatGrid = voxelFlat;
            int range = diffuseRange;
            int yCount = resY;
            int strideX = voxelStrideX;
            int strideY = voxelStrideY;
            bool wrap = wrapBoundaries;
            double keep = 1 - diffuse;
            double diffuseAmount = diffuse;
            bool useInteriorFastPath = denseVoxelGrid && densityLimitsOnlyBoundaryVoxels && yCount > 2;
            bool useDenseWrapFastPath = denseVoxelGrid && densityLimitsDisabled;

            if (!wrap && range == 1)
            {
                if (tridimensional)
                {
                    int lineCount = resX * resZ;
                    Parallel.For(0, lineCount, line =>
                    {
                        int x = line / resZ;
                        int z = line % resZ;
                        if (useInteriorFastPath && x > 0 && x < resX - 1 && z > 0 && z < resZ - 1)
                        {
                            diffuseYLineInteriorRange1NoWrapDense(flatGrid, weights, x, z, yCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseYLineRange1NoWrap(voxelGrid, weights, x, z, yCount, keep, diffuseAmount);
                        }
                    });
                }
                else if (planarXY)
                {
                    Parallel.For(0, resX, x =>
                    {
                        if (useInteriorFastPath && x > 0 && x < resX - 1)
                        {
                            diffuseYLineInteriorRange1NoWrapDense(flatGrid, weights, x, 0, yCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseYLineRange1NoWrap(voxelGrid, weights, x, 0, yCount, keep, diffuseAmount);
                        }
                    });
                }
                else if (planarYZ)
                {
                    Parallel.For(0, resZ, z =>
                    {
                        if (useInteriorFastPath && z > 0 && z < resZ - 1)
                        {
                            diffuseYLineInteriorRange1NoWrapDense(flatGrid, weights, 0, z, yCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseYLineRange1NoWrap(voxelGrid, weights, 0, z, yCount, keep, diffuseAmount);
                        }
                    });
                }

                return;
            }

            if (wrap && range == 1)
            {
                if (tridimensional)
                {
                    int lineCount = resX * resZ;
                    Parallel.For(0, lineCount, () => new double[yCount], (line, loopState, lineDensity) =>
                    {
                        int x = line / resZ;
                        int z = line % resZ;
                        if (useDenseWrapFastPath)
                        {
                            diffuseYLineRange1WrapDense(flatGrid, weights, lineDensity, x, z, yCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseYLineRange1Wrap(voxelGrid, weights, lineDensity, x, z, yCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }
                else if (planarXY)
                {
                    Parallel.For(0, resX, () => new double[yCount], (x, loopState, lineDensity) =>
                    {
                        if (useDenseWrapFastPath)
                        {
                            diffuseYLineRange1WrapDense(flatGrid, weights, lineDensity, x, 0, yCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseYLineRange1Wrap(voxelGrid, weights, lineDensity, x, 0, yCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }
                else if (planarYZ)
                {
                    Parallel.For(0, resZ, () => new double[yCount], (z, loopState, lineDensity) =>
                    {
                        if (useDenseWrapFastPath)
                        {
                            diffuseYLineRange1WrapDense(flatGrid, weights, lineDensity, 0, z, yCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseYLineRange1Wrap(voxelGrid, weights, lineDensity, 0, z, yCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }

                return;
            }

            if (tridimensional)
            {
                int lineCount = resX * resZ;
                Parallel.For(0, lineCount, () => new double[yCount], (line, loopState, lineDensity) =>
                {
                    int x = line / resZ;
                    int z = line % resZ;
                    diffuseYLine(voxelGrid, weights, lineDensity, x, z, range, yCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
            else if (planarXY)
            {
                Parallel.For(0, resX, () => new double[yCount], (x, loopState, lineDensity) =>
                {
                    diffuseYLine(voxelGrid, weights, lineDensity, x, 0, range, yCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
            else if (planarYZ)
            {
                Parallel.For(0, resZ, () => new double[yCount], (z, loopState, lineDensity) =>
                {
                    diffuseYLine(voxelGrid, weights, lineDensity, 0, z, range, yCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
        }

        void diffuseYLine(Voxel[,,] voxelGrid, double[] weights, double[] lineDensity, int x, int z, int range, int yCount, bool wrap, double keep, double diffuseAmount)
        {
            if (!wrap)
            {
                for (int y = 0; y < yCount; y++)
                {
                    Voxel V = voxelGrid[x, y, z];
                    if (V == null) continue;

                    double sum = 0;
                    int startOffset = Math.Max(-range, -y);
                    int endOffset = Math.Min(range, yCount - 1 - y);
                    int weightIndex = startOffset + range;

                    for (int offset = startOffset; offset <= endOffset; offset++)
                    {
                        Voxel neighbour = voxelGrid[x, y + offset, z];
                        if (neighbour != null && neighbour.maxDensity != 0)
                        {
                            sum += neighbour.density * weights[weightIndex];
                        }
                        weightIndex++;
                    }

                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    lineDensity[y] = val;
                }

                for (int y = 0; y < yCount; y++)
                {
                    Voxel V = voxelGrid[x, y, z];
                    if (V != null) V.density = lineDensity[y];
                }

                return;
            }

            for (int y = 0; y < yCount; y++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V == null) continue;

                double sum = 0;
                int weightIndex = 0;

                for (int offset = -range; offset <= range; offset++)
                {
                    int d_yID = y + offset;

                    if (wrap)
                    {
                        if (d_yID < 0) d_yID += yCount;
                        if (d_yID >= yCount) d_yID -= yCount;
                    }

                    if (d_yID >= 0 && d_yID < yCount)
                    {
                        Voxel neighbour = voxelGrid[x, d_yID, z];
                        if (neighbour != null && neighbour.maxDensity != 0)
                        {
                            sum += neighbour.density * weights[weightIndex];
                        }
                    }
                    weightIndex++;
                }

                double val = V.density * keep + diffuseAmount * sum;
                if (val > 1) val = 1;
                if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                lineDensity[y] = val;
            }

            for (int y = 0; y < yCount; y++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V != null) V.density = lineDensity[y];
            }
        }

        void zPassInPlace(double[] weights)
        {
            Voxel[,,] voxelGrid = voxels;
            Voxel[] flatGrid = voxelFlat;
            int range = diffuseRange;
            int zCount = resZ;
            int strideX = voxelStrideX;
            int strideY = voxelStrideY;
            bool wrap = wrapBoundaries;
            double keep = 1 - diffuse;
            double diffuseAmount = diffuse;
            bool useInteriorFastPath = denseVoxelGrid && densityLimitsOnlyBoundaryVoxels && zCount > 2;
            bool useDenseWrapFastPath = denseVoxelGrid && densityLimitsDisabled;

            if (!wrap && range == 1)
            {
                if (tridimensional)
                {
                    int lineCount = resX * resY;
                    Parallel.For(0, lineCount, line =>
                    {
                        int x = line / resY;
                        int y = line % resY;
                        if (useInteriorFastPath && x > 0 && x < resX - 1 && y > 0 && y < resY - 1)
                        {
                            diffuseZLineInteriorRange1NoWrapDense(flatGrid, weights, x, y, zCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseZLineRange1NoWrap(voxelGrid, weights, x, y, zCount, keep, diffuseAmount);
                        }
                    });
                }
                else if (planarXZ)
                {
                    Parallel.For(0, resX, x =>
                    {
                        if (useInteriorFastPath && x > 0 && x < resX - 1)
                        {
                            diffuseZLineInteriorRange1NoWrapDense(flatGrid, weights, x, 0, zCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseZLineRange1NoWrap(voxelGrid, weights, x, 0, zCount, keep, diffuseAmount);
                        }
                    });
                }
                else if (planarYZ)
                {
                    Parallel.For(0, resY, y =>
                    {
                        if (useInteriorFastPath && y > 0 && y < resY - 1)
                        {
                            diffuseZLineInteriorRange1NoWrapDense(flatGrid, weights, 0, y, zCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseZLineRange1NoWrap(voxelGrid, weights, 0, y, zCount, keep, diffuseAmount);
                        }
                    });
                }

                return;
            }

            if (wrap && range == 1)
            {
                if (tridimensional)
                {
                    int lineCount = resX * resY;
                    Parallel.For(0, lineCount, () => new double[zCount], (line, loopState, lineDensity) =>
                    {
                        int x = line / resY;
                        int y = line % resY;
                        if (useDenseWrapFastPath)
                        {
                            diffuseZLineRange1WrapDense(flatGrid, weights, lineDensity, x, y, zCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseZLineRange1Wrap(voxelGrid, weights, lineDensity, x, y, zCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }
                else if (planarXZ)
                {
                    Parallel.For(0, resX, () => new double[zCount], (x, loopState, lineDensity) =>
                    {
                        if (useDenseWrapFastPath)
                        {
                            diffuseZLineRange1WrapDense(flatGrid, weights, lineDensity, x, 0, zCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseZLineRange1Wrap(voxelGrid, weights, lineDensity, x, 0, zCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }
                else if (planarYZ)
                {
                    Parallel.For(0, resY, () => new double[zCount], (y, loopState, lineDensity) =>
                    {
                        if (useDenseWrapFastPath)
                        {
                            diffuseZLineRange1WrapDense(flatGrid, weights, lineDensity, 0, y, zCount, strideX, strideY, keep, diffuseAmount);
                        }
                        else
                        {
                            diffuseZLineRange1Wrap(voxelGrid, weights, lineDensity, 0, y, zCount, keep, diffuseAmount);
                        }
                        return lineDensity;
                    }, lineDensity => { });
                }

                return;
            }

            if (tridimensional)
            {
                int lineCount = resX * resY;
                Parallel.For(0, lineCount, () => new double[zCount], (line, loopState, lineDensity) =>
                {
                    int x = line / resY;
                    int y = line % resY;
                    diffuseZLine(voxelGrid, weights, lineDensity, x, y, range, zCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
            else if (planarXZ)
            {
                Parallel.For(0, resX, () => new double[zCount], (x, loopState, lineDensity) =>
                {
                    diffuseZLine(voxelGrid, weights, lineDensity, x, 0, range, zCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
            else if (planarYZ)
            {
                Parallel.For(0, resY, () => new double[zCount], (y, loopState, lineDensity) =>
                {
                    diffuseZLine(voxelGrid, weights, lineDensity, 0, y, range, zCount, wrap, keep, diffuseAmount);
                    return lineDensity;
                }, lineDensity => { });
            }
        }

        void diffuseZLine(Voxel[,,] voxelGrid, double[] weights, double[] lineDensity, int x, int y, int range, int zCount, bool wrap, double keep, double diffuseAmount)
        {
            if (!wrap)
            {
                for (int z = 0; z < zCount; z++)
                {
                    Voxel V = voxelGrid[x, y, z];
                    if (V == null) continue;

                    double sum = 0;
                    int startOffset = Math.Max(-range, -z);
                    int endOffset = Math.Min(range, zCount - 1 - z);
                    int weightIndex = startOffset + range;

                    for (int offset = startOffset; offset <= endOffset; offset++)
                    {
                        Voxel neighbour = voxelGrid[x, y, z + offset];
                        if (neighbour != null && neighbour.maxDensity != 0)
                        {
                            sum += neighbour.density * weights[weightIndex];
                        }
                        weightIndex++;
                    }

                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    lineDensity[z] = val;
                }

                for (int z = 0; z < zCount; z++)
                {
                    Voxel V = voxelGrid[x, y, z];
                    if (V != null) V.density = lineDensity[z];
                }

                return;
            }

            for (int z = 0; z < zCount; z++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V == null) continue;

                double sum = 0;
                int weightIndex = 0;

                for (int offset = -range; offset <= range; offset++)
                {
                    int d_zID = z + offset;

                    if (wrap)
                    {
                        if (d_zID < 0) d_zID += zCount;
                        if (d_zID >= zCount) d_zID -= zCount;
                    }

                    if (d_zID >= 0 && d_zID < zCount)
                    {
                        Voxel neighbour = voxelGrid[x, y, d_zID];
                        if (neighbour != null && neighbour.maxDensity != 0)
                        {
                            sum += neighbour.density * weights[weightIndex];
                        }
                    }
                    weightIndex++;
                }

                double val = V.density * keep + diffuseAmount * sum;
                if (val > 1) val = 1;
                if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                lineDensity[z] = val;
            }

            for (int z = 0; z < zCount; z++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V != null) V.density = lineDensity[z];
            }
        }

        void diffuseXLineRange1Wrap(Voxel[,,] voxelGrid, double[] weights, double[] lineDensity, int y, int z, int xCount, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            for (int x = 0; x < xCount; x++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V == null) continue;

                int leftIndex = x == 0 ? xCount - 1 : x - 1;
                int rightIndex = x == xCount - 1 ? 0 : x + 1;

                Voxel left = voxelGrid[leftIndex, y, z];
                Voxel right = voxelGrid[rightIndex, y, z];

                double sum = V.maxDensity == 0 ? 0 : V.density * centerWeight;
                if (left != null && left.maxDensity != 0) sum += left.density * leftWeight;
                if (right != null && right.maxDensity != 0) sum += right.density * rightWeight;

                double val = V.density * keep + diffuseAmount * sum;
                if (val > 1) val = 1;
                if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                lineDensity[x] = val;
            }

            for (int x = 0; x < xCount; x++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V != null) V.density = lineDensity[x];
            }
        }

        void diffuseXLineRange1WrapDense(Voxel[] voxelGrid, double[] weights, double[] lineDensity, int y, int z, int xCount, int strideX, int strideY, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = y * strideY + z;

            if (xCount == 1)
            {
                Voxel V = voxelGrid[baseIndex];
                double sum = V.density * (leftWeight + centerWeight + rightWeight);
                double val = V.density * keep + diffuseAmount * sum;
                V.density = val > 1 ? 1 : val;
                return;
            }

            int lastIndex = baseIndex + (xCount - 1) * strideX;
            Voxel first = voxelGrid[baseIndex];
            double firstSum = voxelGrid[lastIndex].density * leftWeight + first.density * centerWeight + voxelGrid[baseIndex + strideX].density * rightWeight;
            double firstValue = first.density * keep + diffuseAmount * firstSum;
            lineDensity[0] = firstValue > 1 ? 1 : firstValue;

            double leftDensity = first.density;
            Voxel centerVoxel = voxelGrid[baseIndex + strideX];
            double centerDensity = centerVoxel.density;
            int voxelIndex = baseIndex + strideX;
            for (int x = 1; x < xCount - 1; x++)
            {
                double rightDensity = voxelGrid[voxelIndex + strideX].density;
                double sum = leftDensity * leftWeight + centerDensity * centerWeight + rightDensity * rightWeight;
                double val = centerDensity * keep + diffuseAmount * sum;
                lineDensity[x] = val > 1 ? 1 : val;
                leftDensity = centerDensity;
                centerDensity = rightDensity;
                voxelIndex += strideX;
            }

            Voxel last = voxelGrid[lastIndex];
            double lastSum = voxelGrid[lastIndex - strideX].density * leftWeight + last.density * centerWeight + voxelGrid[baseIndex].density * rightWeight;
            double lastValue = last.density * keep + diffuseAmount * lastSum;
            lineDensity[xCount - 1] = lastValue > 1 ? 1 : lastValue;

            voxelIndex = baseIndex;
            for (int x = 0; x < xCount; x++)
            {
                voxelGrid[voxelIndex].density = lineDensity[x];
                voxelIndex += strideX;
            }
        }

        void diffuseYLineRange1Wrap(Voxel[,,] voxelGrid, double[] weights, double[] lineDensity, int x, int z, int yCount, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            for (int y = 0; y < yCount; y++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V == null) continue;

                int leftIndex = y == 0 ? yCount - 1 : y - 1;
                int rightIndex = y == yCount - 1 ? 0 : y + 1;

                Voxel left = voxelGrid[x, leftIndex, z];
                Voxel right = voxelGrid[x, rightIndex, z];

                double sum = V.maxDensity == 0 ? 0 : V.density * centerWeight;
                if (left != null && left.maxDensity != 0) sum += left.density * leftWeight;
                if (right != null && right.maxDensity != 0) sum += right.density * rightWeight;

                double val = V.density * keep + diffuseAmount * sum;
                if (val > 1) val = 1;
                if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                lineDensity[y] = val;
            }

            for (int y = 0; y < yCount; y++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V != null) V.density = lineDensity[y];
            }
        }

        void diffuseYLineRange1WrapDense(Voxel[] voxelGrid, double[] weights, double[] lineDensity, int x, int z, int yCount, int strideX, int strideY, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = x * strideX + z;

            if (yCount == 1)
            {
                Voxel V = voxelGrid[baseIndex];
                double sum = V.density * (leftWeight + centerWeight + rightWeight);
                double val = V.density * keep + diffuseAmount * sum;
                V.density = val > 1 ? 1 : val;
                return;
            }

            int lastIndex = baseIndex + (yCount - 1) * strideY;
            Voxel first = voxelGrid[baseIndex];
            double firstSum = voxelGrid[lastIndex].density * leftWeight + first.density * centerWeight + voxelGrid[baseIndex + strideY].density * rightWeight;
            double firstValue = first.density * keep + diffuseAmount * firstSum;
            lineDensity[0] = firstValue > 1 ? 1 : firstValue;

            double leftDensity = first.density;
            Voxel centerVoxel = voxelGrid[baseIndex + strideY];
            double centerDensity = centerVoxel.density;
            int voxelIndex = baseIndex + strideY;
            for (int y = 1; y < yCount - 1; y++)
            {
                double rightDensity = voxelGrid[voxelIndex + strideY].density;
                double sum = leftDensity * leftWeight + centerDensity * centerWeight + rightDensity * rightWeight;
                double val = centerDensity * keep + diffuseAmount * sum;
                lineDensity[y] = val > 1 ? 1 : val;
                leftDensity = centerDensity;
                centerDensity = rightDensity;
                voxelIndex += strideY;
            }

            Voxel last = voxelGrid[lastIndex];
            double lastSum = voxelGrid[lastIndex - strideY].density * leftWeight + last.density * centerWeight + voxelGrid[baseIndex].density * rightWeight;
            double lastValue = last.density * keep + diffuseAmount * lastSum;
            lineDensity[yCount - 1] = lastValue > 1 ? 1 : lastValue;

            voxelIndex = baseIndex;
            for (int y = 0; y < yCount; y++)
            {
                voxelGrid[voxelIndex].density = lineDensity[y];
                voxelIndex += strideY;
            }
        }

        void diffuseZLineRange1Wrap(Voxel[,,] voxelGrid, double[] weights, double[] lineDensity, int x, int y, int zCount, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            for (int z = 0; z < zCount; z++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V == null) continue;

                int leftIndex = z == 0 ? zCount - 1 : z - 1;
                int rightIndex = z == zCount - 1 ? 0 : z + 1;

                Voxel left = voxelGrid[x, y, leftIndex];
                Voxel right = voxelGrid[x, y, rightIndex];

                double sum = V.maxDensity == 0 ? 0 : V.density * centerWeight;
                if (left != null && left.maxDensity != 0) sum += left.density * leftWeight;
                if (right != null && right.maxDensity != 0) sum += right.density * rightWeight;

                double val = V.density * keep + diffuseAmount * sum;
                if (val > 1) val = 1;
                if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                lineDensity[z] = val;
            }

            for (int z = 0; z < zCount; z++)
            {
                Voxel V = voxelGrid[x, y, z];
                if (V != null) V.density = lineDensity[z];
            }
        }

        void diffuseZLineRange1WrapDense(Voxel[] voxelGrid, double[] weights, double[] lineDensity, int x, int y, int zCount, int strideX, int strideY, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = x * strideX + y * strideY;

            if (zCount == 1)
            {
                Voxel V = voxelGrid[baseIndex];
                double sum = V.density * (leftWeight + centerWeight + rightWeight);
                double val = V.density * keep + diffuseAmount * sum;
                V.density = val > 1 ? 1 : val;
                return;
            }

            int lastIndex = baseIndex + zCount - 1;
            Voxel first = voxelGrid[baseIndex];
            double firstSum = voxelGrid[lastIndex].density * leftWeight + first.density * centerWeight + voxelGrid[baseIndex + 1].density * rightWeight;
            double firstValue = first.density * keep + diffuseAmount * firstSum;
            lineDensity[0] = firstValue > 1 ? 1 : firstValue;

            double leftDensity = first.density;
            Voxel centerVoxel = voxelGrid[baseIndex + 1];
            double centerDensity = centerVoxel.density;
            int voxelIndex = baseIndex + 1;
            for (int z = 1; z < zCount - 1; z++)
            {
                double rightDensity = voxelGrid[voxelIndex + 1].density;
                double sum = leftDensity * leftWeight + centerDensity * centerWeight + rightDensity * rightWeight;
                double val = centerDensity * keep + diffuseAmount * sum;
                lineDensity[z] = val > 1 ? 1 : val;
                leftDensity = centerDensity;
                centerDensity = rightDensity;
                voxelIndex++;
            }

            Voxel last = voxelGrid[lastIndex];
            double lastSum = voxelGrid[lastIndex - 1].density * leftWeight + last.density * centerWeight + voxelGrid[baseIndex].density * rightWeight;
            double lastValue = last.density * keep + diffuseAmount * lastSum;
            lineDensity[zCount - 1] = lastValue > 1 ? 1 : lastValue;

            voxelIndex = baseIndex;
            for (int z = 0; z < zCount; z++)
            {
                voxelGrid[voxelIndex].density = lineDensity[z];
                voxelIndex++;
            }
        }

        void diffuseXLineInteriorRange1NoWrapDense(Voxel[] voxelGrid, double[] weights, int y, int z, int xCount, int strideX, int strideY, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = y * strideY + z;

            Voxel left = voxelGrid[baseIndex];
            Voxel center = voxelGrid[baseIndex + strideX];

            double firstValue = left.density * keep + diffuseAmount * (left.density * centerWeight + center.density * rightWeight);
            if (firstValue > 1) firstValue = 1;
            if (left.maxDensity != -1 && firstValue > left.maxDensity) firstValue = left.maxDensity;
            if (left.minDensity != -1 && firstValue > 0 && left.minDensity > firstValue) firstValue = left.minDensity;

            Voxel previousVoxel = left;
            double previousValue = firstValue;
            int voxelIndex = baseIndex + strideX;

            for (int x = 1; x < xCount - 1; x++)
            {
                Voxel right = voxelGrid[voxelIndex + strideX];
                double val = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight + right.density * rightWeight);
                if (val > 1) val = 1;

                previousVoxel.density = previousValue;
                previousVoxel = center;
                previousValue = val;

                left = center;
                center = right;
                voxelIndex += strideX;
            }

            double lastValue = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight);
            if (lastValue > 1) lastValue = 1;
            if (center.maxDensity != -1 && lastValue > center.maxDensity) lastValue = center.maxDensity;
            if (center.minDensity != -1 && lastValue > 0 && center.minDensity > lastValue) lastValue = center.minDensity;

            previousVoxel.density = previousValue;
            center.density = lastValue;
        }

        void diffuseYLineInteriorRange1NoWrapDense(Voxel[] voxelGrid, double[] weights, int x, int z, int yCount, int strideX, int strideY, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = x * strideX + z;

            Voxel left = voxelGrid[baseIndex];
            Voxel center = voxelGrid[baseIndex + strideY];

            double firstValue = left.density * keep + diffuseAmount * (left.density * centerWeight + center.density * rightWeight);
            if (firstValue > 1) firstValue = 1;
            if (left.maxDensity != -1 && firstValue > left.maxDensity) firstValue = left.maxDensity;
            if (left.minDensity != -1 && firstValue > 0 && left.minDensity > firstValue) firstValue = left.minDensity;

            Voxel previousVoxel = left;
            double previousValue = firstValue;
            int voxelIndex = baseIndex + strideY;

            for (int y = 1; y < yCount - 1; y++)
            {
                Voxel right = voxelGrid[voxelIndex + strideY];
                double val = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight + right.density * rightWeight);
                if (val > 1) val = 1;

                previousVoxel.density = previousValue;
                previousVoxel = center;
                previousValue = val;

                left = center;
                center = right;
                voxelIndex += strideY;
            }

            double lastValue = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight);
            if (lastValue > 1) lastValue = 1;
            if (center.maxDensity != -1 && lastValue > center.maxDensity) lastValue = center.maxDensity;
            if (center.minDensity != -1 && lastValue > 0 && center.minDensity > lastValue) lastValue = center.minDensity;

            previousVoxel.density = previousValue;
            center.density = lastValue;
        }

        void diffuseZLineInteriorRange1NoWrapDense(Voxel[] voxelGrid, double[] weights, int x, int y, int zCount, int strideX, int strideY, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];
            int baseIndex = x * strideX + y * strideY;

            Voxel left = voxelGrid[baseIndex];
            Voxel center = voxelGrid[baseIndex + 1];

            double firstValue = left.density * keep + diffuseAmount * (left.density * centerWeight + center.density * rightWeight);
            if (firstValue > 1) firstValue = 1;
            if (left.maxDensity != -1 && firstValue > left.maxDensity) firstValue = left.maxDensity;
            if (left.minDensity != -1 && firstValue > 0 && left.minDensity > firstValue) firstValue = left.minDensity;

            Voxel previousVoxel = left;
            double previousValue = firstValue;
            int voxelIndex = baseIndex + 1;

            for (int z = 1; z < zCount - 1; z++)
            {
                Voxel right = voxelGrid[voxelIndex + 1];
                double val = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight + right.density * rightWeight);
                if (val > 1) val = 1;

                previousVoxel.density = previousValue;
                previousVoxel = center;
                previousValue = val;

                left = center;
                center = right;
                voxelIndex++;
            }

            double lastValue = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight);
            if (lastValue > 1) lastValue = 1;
            if (center.maxDensity != -1 && lastValue > center.maxDensity) lastValue = center.maxDensity;
            if (center.minDensity != -1 && lastValue > 0 && center.minDensity > lastValue) lastValue = center.minDensity;

            previousVoxel.density = previousValue;
            center.density = lastValue;
        }

        void diffuseXLineRange1NoWrap(Voxel[,,] voxelGrid, double[] weights, int y, int z, int xCount, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            Voxel left = null;
            Voxel center = xCount > 0 ? voxelGrid[0, y, z] : null;
            Voxel previousVoxel = null;
            double previousValue = 0;

            for (int x = 0; x < xCount; x++)
            {
                Voxel right = x + 1 < xCount ? voxelGrid[x + 1, y, z] : null;

                if (center != null)
                {
                    double sum = center.maxDensity == 0 ? 0 : center.density * centerWeight;
                    if (left != null && left.maxDensity != 0) sum += left.density * leftWeight;
                    if (right != null && right.maxDensity != 0) sum += right.density * rightWeight;

                    double val = center.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (center.maxDensity != -1 || center.minDensity != -1)
                    {
                        if (center.maxDensity != -1 && val > center.maxDensity) val = center.maxDensity;
                        if (center.minDensity != -1 && val > 0 && center.minDensity > val) val = center.minDensity;
                    }

                    if (previousVoxel != null) previousVoxel.density = previousValue;
                    previousVoxel = center;
                    previousValue = val;
                }
                else
                {
                    if (previousVoxel != null)
                    {
                        previousVoxel.density = previousValue;
                        previousVoxel = null;
                    }
                }

                left = center;
                center = right;
            }

            if (previousVoxel != null) previousVoxel.density = previousValue;
        }

        void diffuseXLineInteriorRange1NoWrap(Voxel[,,] voxelGrid, double[] weights, int y, int z, int xCount, double keep, double diffuseAmount)
        {
            if (xCount <= 2)
            {
                diffuseXLineRange1NoWrap(voxelGrid, weights, y, z, xCount, keep, diffuseAmount);
                return;
            }

            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            Voxel left = voxelGrid[0, y, z];
            Voxel center = voxelGrid[1, y, z];

            double firstValue = left.density * keep + diffuseAmount * (left.density * centerWeight + center.density * rightWeight);
            if (firstValue > 1) firstValue = 1;
            if (left.maxDensity != -1 && firstValue > left.maxDensity) firstValue = left.maxDensity;
            if (left.minDensity != -1 && firstValue > 0 && left.minDensity > firstValue) firstValue = left.minDensity;

            Voxel previousVoxel = left;
            double previousValue = firstValue;

            for (int x = 1; x < xCount - 1; x++)
            {
                Voxel right = voxelGrid[x + 1, y, z];
                double val = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight + right.density * rightWeight);
                if (val > 1) val = 1;

                previousVoxel.density = previousValue;
                previousVoxel = center;
                previousValue = val;

                left = center;
                center = right;
            }

            double lastValue = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight);
            if (lastValue > 1) lastValue = 1;
            if (center.maxDensity != -1 && lastValue > center.maxDensity) lastValue = center.maxDensity;
            if (center.minDensity != -1 && lastValue > 0 && center.minDensity > lastValue) lastValue = center.minDensity;

            previousVoxel.density = previousValue;
            center.density = lastValue;
        }

        void diffuseYLineRange1NoWrap(Voxel[,,] voxelGrid, double[] weights, int x, int z, int yCount, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            Voxel left = null;
            Voxel center = yCount > 0 ? voxelGrid[x, 0, z] : null;
            Voxel previousVoxel = null;
            double previousValue = 0;

            for (int y = 0; y < yCount; y++)
            {
                Voxel right = y + 1 < yCount ? voxelGrid[x, y + 1, z] : null;

                if (center != null)
                {
                    double sum = center.maxDensity == 0 ? 0 : center.density * centerWeight;
                    if (left != null && left.maxDensity != 0) sum += left.density * leftWeight;
                    if (right != null && right.maxDensity != 0) sum += right.density * rightWeight;

                    double val = center.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (center.maxDensity != -1 || center.minDensity != -1)
                    {
                        if (center.maxDensity != -1 && val > center.maxDensity) val = center.maxDensity;
                        if (center.minDensity != -1 && val > 0 && center.minDensity > val) val = center.minDensity;
                    }

                    if (previousVoxel != null) previousVoxel.density = previousValue;
                    previousVoxel = center;
                    previousValue = val;
                }
                else
                {
                    if (previousVoxel != null)
                    {
                        previousVoxel.density = previousValue;
                        previousVoxel = null;
                    }
                }

                left = center;
                center = right;
            }

            if (previousVoxel != null) previousVoxel.density = previousValue;
        }

        void diffuseYLineInteriorRange1NoWrap(Voxel[,,] voxelGrid, double[] weights, int x, int z, int yCount, double keep, double diffuseAmount)
        {
            if (yCount <= 2)
            {
                diffuseYLineRange1NoWrap(voxelGrid, weights, x, z, yCount, keep, diffuseAmount);
                return;
            }

            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            Voxel left = voxelGrid[x, 0, z];
            Voxel center = voxelGrid[x, 1, z];

            double firstValue = left.density * keep + diffuseAmount * (left.density * centerWeight + center.density * rightWeight);
            if (firstValue > 1) firstValue = 1;
            if (left.maxDensity != -1 && firstValue > left.maxDensity) firstValue = left.maxDensity;
            if (left.minDensity != -1 && firstValue > 0 && left.minDensity > firstValue) firstValue = left.minDensity;

            Voxel previousVoxel = left;
            double previousValue = firstValue;

            for (int y = 1; y < yCount - 1; y++)
            {
                Voxel right = voxelGrid[x, y + 1, z];
                double val = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight + right.density * rightWeight);
                if (val > 1) val = 1;

                previousVoxel.density = previousValue;
                previousVoxel = center;
                previousValue = val;

                left = center;
                center = right;
            }

            double lastValue = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight);
            if (lastValue > 1) lastValue = 1;
            if (center.maxDensity != -1 && lastValue > center.maxDensity) lastValue = center.maxDensity;
            if (center.minDensity != -1 && lastValue > 0 && center.minDensity > lastValue) lastValue = center.minDensity;

            previousVoxel.density = previousValue;
            center.density = lastValue;
        }

        void diffuseZLineRange1NoWrap(Voxel[,,] voxelGrid, double[] weights, int x, int y, int zCount, double keep, double diffuseAmount)
        {
            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            Voxel left = null;
            Voxel center = zCount > 0 ? voxelGrid[x, y, 0] : null;
            Voxel previousVoxel = null;
            double previousValue = 0;

            for (int z = 0; z < zCount; z++)
            {
                Voxel right = z + 1 < zCount ? voxelGrid[x, y, z + 1] : null;

                if (center != null)
                {
                    double sum = center.maxDensity == 0 ? 0 : center.density * centerWeight;
                    if (left != null && left.maxDensity != 0) sum += left.density * leftWeight;
                    if (right != null && right.maxDensity != 0) sum += right.density * rightWeight;

                    double val = center.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (center.maxDensity != -1 || center.minDensity != -1)
                    {
                        if (center.maxDensity != -1 && val > center.maxDensity) val = center.maxDensity;
                        if (center.minDensity != -1 && val > 0 && center.minDensity > val) val = center.minDensity;
                    }

                    if (previousVoxel != null) previousVoxel.density = previousValue;
                    previousVoxel = center;
                    previousValue = val;
                }
                else
                {
                    if (previousVoxel != null)
                    {
                        previousVoxel.density = previousValue;
                        previousVoxel = null;
                    }
                }

                left = center;
                center = right;
            }

            if (previousVoxel != null) previousVoxel.density = previousValue;
        }

        void diffuseZLineInteriorRange1NoWrap(Voxel[,,] voxelGrid, double[] weights, int x, int y, int zCount, double keep, double diffuseAmount)
        {
            if (zCount <= 2)
            {
                diffuseZLineRange1NoWrap(voxelGrid, weights, x, y, zCount, keep, diffuseAmount);
                return;
            }

            double leftWeight = weights[0];
            double centerWeight = weights[1];
            double rightWeight = weights[2];

            Voxel left = voxelGrid[x, y, 0];
            Voxel center = voxelGrid[x, y, 1];

            double firstValue = left.density * keep + diffuseAmount * (left.density * centerWeight + center.density * rightWeight);
            if (firstValue > 1) firstValue = 1;
            if (left.maxDensity != -1 && firstValue > left.maxDensity) firstValue = left.maxDensity;
            if (left.minDensity != -1 && firstValue > 0 && left.minDensity > firstValue) firstValue = left.minDensity;

            Voxel previousVoxel = left;
            double previousValue = firstValue;

            for (int z = 1; z < zCount - 1; z++)
            {
                Voxel right = voxelGrid[x, y, z + 1];
                double val = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight + right.density * rightWeight);
                if (val > 1) val = 1;

                previousVoxel.density = previousValue;
                previousVoxel = center;
                previousValue = val;

                left = center;
                center = right;
            }

            double lastValue = center.density * keep + diffuseAmount * (left.density * leftWeight + center.density * centerWeight);
            if (lastValue > 1) lastValue = 1;
            if (center.maxDensity != -1 && lastValue > center.maxDensity) lastValue = center.maxDensity;
            if (center.minDensity != -1 && lastValue > 0 && center.minDensity > lastValue) lastValue = center.minDensity;

            previousVoxel.density = previousValue;
            center.density = lastValue;
        }

        //-------------

        double[] xPass(double[] newDensity, double[] weights)
        {
            Voxel[,,] voxelGrid = voxels;
            Voxel[] active = activeVoxels;
            int activeCount = active.Length;
            int range = diffuseRange;
            int xCount = resX;
            bool wrap = wrapBoundaries;
            double keep = 1 - diffuse;
            double diffuseAmount = diffuse;

            if (tridimensional)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idY = V.idY;
                    int idZ = V.idZ;

                    for (int x = -range; x <= range; x++)
                    {
                        int d_xID = V.idX + x;

                        if (wrap)
                        {
                            if (d_xID < 0) d_xID += xCount;
                            if (d_xID >= xCount) d_xID -= xCount;
                        }

                        if (d_xID >= 0 && d_xID < xCount)
                        {
                            Voxel neighbour = voxelGrid[d_xID, idY, idZ];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }
            else if (planarXY)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idY = V.idY;

                    for (int x = -range; x <= range; x++)
                    {
                        int d_xID = V.idX + x;

                        if (wrap)
                        {
                            if (d_xID < 0) d_xID += xCount;
                            if (d_xID >= xCount) d_xID -= xCount;
                        }

                        if (d_xID >= 0 && d_xID < xCount)
                        {
                            Voxel neighbour = voxelGrid[d_xID, idY, 0];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }
            else if (planarXZ)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idZ = V.idZ;

                    for (int x = -range; x <= range; x++)
                    {
                        int d_xID = V.idX + x;

                        if (wrap)
                        {
                            if (d_xID < 0) d_xID += xCount;
                            if (d_xID >= xCount) d_xID -= xCount;
                        }

                        if (d_xID >= 0 && d_xID < xCount)
                        {
                            Voxel neighbour = voxelGrid[d_xID, 0, idZ];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }

            return newDensity;
        }

        double[] yPass(double[] newDensity, double[] weights)
        {
            Voxel[,,] voxelGrid = voxels;
            Voxel[] active = activeVoxels;
            int activeCount = active.Length;
            int range = diffuseRange;
            int yCount = resY;
            bool wrap = wrapBoundaries;
            double keep = 1 - diffuse;
            double diffuseAmount = diffuse;

            if (tridimensional)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idX = V.idX;
                    int idZ = V.idZ;

                    for (int y = -range; y <= range; y++)
                    {
                        int d_yID = V.idY + y;

                        if (wrap)
                        {
                            if (d_yID < 0) d_yID += yCount;
                            if (d_yID >= yCount) d_yID -= yCount;
                        }

                        if (d_yID >= 0 && d_yID < yCount)
                        {
                            Voxel neighbour = voxelGrid[idX, d_yID, idZ];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }
            else if (planarXY)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idX = V.idX;

                    for (int y = -range; y <= range; y++)
                    {
                        int d_yID = V.idY + y;

                        if (wrap)
                        {
                            if (d_yID < 0) d_yID += yCount;
                            if (d_yID >= yCount) d_yID -= yCount;
                        }

                        if (d_yID >= 0 && d_yID < yCount)
                        {
                            Voxel neighbour = voxelGrid[idX, d_yID, 0];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }
            else if (planarYZ)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idZ = V.idZ;

                    for (int y = -range; y <= range; y++)
                    {
                        int d_yID = V.idY + y;

                        if (wrap)
                        {
                            if (d_yID < 0) d_yID += yCount;
                            if (d_yID >= yCount) d_yID -= yCount;
                        }

                        if (d_yID >= 0 && d_yID < yCount)
                        {
                            Voxel neighbour = voxelGrid[0, d_yID, idZ];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }

            return newDensity;
        }

        double[] zPass(double[] newDensity, double[] weights)
        {
            Voxel[,,] voxelGrid = voxels;
            Voxel[] active = activeVoxels;
            int activeCount = active.Length;
            int range = diffuseRange;
            int zCount = resZ;
            bool wrap = wrapBoundaries;
            double keep = 1 - diffuse;
            double diffuseAmount = diffuse;

            if (tridimensional)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idX = V.idX;
                    int idY = V.idY;

                    for (int z = -range; z <= range; z++)
                    {
                        int d_zID = V.idZ + z;

                        if (wrap)
                        {
                            if (d_zID < 0) d_zID += zCount;
                            if (d_zID >= zCount) d_zID -= zCount;
                        }

                        if (d_zID >= 0 && d_zID < zCount)
                        {
                            Voxel neighbour = voxelGrid[idX, idY, d_zID];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }
            else if (planarXZ)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idX = V.idX;

                    for (int z = -range; z <= range; z++)
                    {
                        int d_zID = V.idZ + z;

                        if (wrap)
                        {
                            if (d_zID < 0) d_zID += zCount;
                            if (d_zID >= zCount) d_zID -= zCount;
                        }

                        if (d_zID >= 0 && d_zID < zCount)
                        {
                            Voxel neighbour = voxelGrid[idX, 0, d_zID];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }
            else if (planarYZ)
            {
                Parallel.For(0, activeCount, i =>
                {
                    Voxel V = active[i];
                    double sum = 0;
                    int weightIndex = 0;
                    int idY = V.idY;

                    for (int z = -range; z <= range; z++)
                    {
                        int d_zID = V.idZ + z;

                        if (wrap)
                        {
                            if (d_zID < 0) d_zID += zCount;
                            if (d_zID >= zCount) d_zID -= zCount;
                        }

                        if (d_zID >= 0 && d_zID < zCount)
                        {
                            Voxel neighbour = voxelGrid[0, idY, d_zID];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                sum += neighbour.density * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    //calculate new density
                    double val = V.density * keep + diffuseAmount * sum;
                    if (val > 1) val = 1;
                    if (V.maxDensity != -1 && val > V.maxDensity) val = V.maxDensity;
                    if (V.minDensity != -1 && val > 0 && V.minDensity > val) val = V.minDensity;
                    newDensity[i] = val;
                });
            }

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

        void ensureReusableDiffusionWeights()
        {
            if (reusableWeights == null || reusableWeightsRange != diffuseRange)
            {
                reusableWeights = precomputeWeights(diffuseRange);
                reusableWeightsRange = diffuseRange;
            }

            if (antParticles && (reusableAntWeights == null || reusableAntWeightsRange != diffuseRange_Ant))
            {
                reusableAntWeights = precomputeWeights(diffuseRange_Ant);
                reusableAntWeightsRange = diffuseRange_Ant;
            }
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
            if (foodDiffuseRate <= 0 && baseDiffuseRate <= 0) return;

            if (tridimensional)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[d_xID, V.idY, V.idZ];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
            }
            else if (planarXY)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[d_xID, V.idY, 0];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
            }
            else if (planarXZ)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[d_xID, 0, V.idZ];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
            }
        }

        void ants_yPass(double[] weights)
        {
            if (foodDiffuseRate <= 0 && baseDiffuseRate <= 0) return;

            if (tridimensional)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[V.idX, d_yID, V.idZ];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
            }
            else if (planarXY)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[V.idX, d_yID, 0];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
            }
            else if (planarYZ)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[0, d_yID, V.idZ];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
            }
        }

        void ants_zPass(double[] weights)
        {
            if (foodDiffuseRate <= 0 && baseDiffuseRate <= 0) return;

            if (tridimensional)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[V.idX, V.idY, d_zID];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
            }
            else if (planarXZ)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[V.idX, 0, d_zID];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
            }
            else if (planarYZ)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    Voxel V = activeVoxels[i];
                    double foodSum = 0;
                    double baseSum = 0;
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
                            Voxel neighbour = voxels[0, V.idY, d_zID];
                            if (neighbour != null && neighbour.maxDensity != 0)
                            {
                                if (foodDiffuseRate > 0) foodSum += neighbour.towardsFoodPheromone * weights[weightIndex];
                                if (baseDiffuseRate > 0) baseSum += neighbour.towardsBasePheromone * weights[weightIndex];
                            }
                        }
                        weightIndex++;
                    }

                    if (foodDiffuseRate > 0)
                    {
                        double fVal = V.towardsFoodPheromone * (1 - foodDiffuseRate) + foodDiffuseRate * foodSum;
                        if (fVal > 1) fVal = 1;
                        if (V.maxDensity != -1 && fVal > V.maxDensity) fVal = V.maxDensity;
                        if (V.minDensity != -1 && fVal < V.minDensity) fVal = V.minDensity;
                        V.towardsFoodPheromone = fVal;
                    }
                    if (baseDiffuseRate > 0)
                    {
                        double bVal = V.towardsBasePheromone * (1 - baseDiffuseRate) + baseDiffuseRate * baseSum;
                        if (bVal > 1) bVal = 1;
                        if (V.maxDensity != -1 && bVal > V.maxDensity) bVal = V.maxDensity;
                        if (V.minDensity != -1 && bVal < V.minDensity) bVal = V.minDensity;
                        V.towardsBasePheromone = bVal;
                    }
                });
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

                ParticleGroup PG = new ParticleGroup(inputPG.speed, inputPG.sensorDistance, inputPG.sensorAngle, inputPG.rotationAngle, inputPG.depositValue, inputPG.wanderFrequency, inputPG.baseWanderFrequency, inputPG.color);
                particleGroups.Add(PG);

                for (int i = 0; i < inputPG.particles.Count; i++)
                {
                    Particle initialP = inputPG.particles[i];
                    Plane particlePlane = initialP.pPlane;

                    //check initialP parent voxel
                    int xID = (int)(initialP.pPlane.Origin.X / voxelSize);
                    int yID = (int)(initialP.pPlane.Origin.Y / voxelSize);
                    int zID = (int)(initialP.pPlane.Origin.Z / voxelSize);

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
                                    P.parentVoxel = initialP.parentVoxel;
                                    
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
                                    P.parentVoxel = initialP.parentVoxel;

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
                PG.baseWanderFrequency = inputPG.baseWanderFrequency;
                PG.color = inputPG.color;

                if (!PG.ant) PG.updateWanderFrequency();
                if (PG.ant)
                {
                    PG.updateBaseWanderFrequency();
                };
            }
        }

        //-------------------------------------------------------------------

        void particleCheckParentVoxel()
        {
            resetParticleCountsForCurrentFrame();
            ensureParticleCountTouchedCapacity(particles.Count);
            int particleCount = particles.Count;
            ParticlePreviewCache previewCache = (particles as ParticleList)?.PreviewCache;
            if (previewCache != null && !shouldBuildParticlePreviewCache())
            {
                previewCache.Invalidate(particleCount);
                previewCache = null;
            }

            object previewCacheLock = previewCache != null ? previewCache.SyncRoot : null;
            int previewCacheInitialCapacity = previewCache != null ? Math.Max(128, particleCount / Math.Max(System.Environment.ProcessorCount * 4, 1)) : 0;
            if (previewCache != null)
            {
                previewCache.BeginBuild(particleCount);
            }

            //count particles
            Parallel.For(0, particleCount,
                () => previewCache != null ? new ParticlePreviewBuildCache(previewCacheInitialCapacity) : null,
                (i, loopState, localPreviewCache) =>
                {
                    Particle P = particles[i];
                    P.age++;
                    particleCountTouchedVoxels[i] = null;

                    Voxel parentVoxel = P.parentVoxel;
                    if (parentVoxel == null)
                    {
                        Point3d origin = P.pPlane.Origin;
                        parentVoxel = getParentVoxel(origin.X, origin.Y, origin.Z);
                    }

                    if (parentVoxel != null)
                    {
                        P.parentVoxel = parentVoxel;
                        particleCountTouchedVoxels[i] = parentVoxel;
                        System.Threading.Interlocked.Increment(ref parentVoxel.particleCount);

                        //ant particles
                        if (P.parentParticleGroup.ant && iteration > 1)
                        {
                            //found food
                            if (parentVoxel.food > 0 && P.foundFood == false)
                            {
                                P.foundFood = true;
                                P.age = 0;

                                if (P.age == 0)
                                {
                                    parentVoxel.food -= 1;
                                    P.age++;
                                }
                            }

                            //returned home
                            if (P.pPlane.Origin.DistanceTo(P.home.Origin) < retrieveSpeed(P))
                            {
                                P.foundFood = false;
                                P.age = 1;
                            }
                        }

                        if (parentVoxel.maxDensity == 0)
                        {
                            P.die = true;
                            setWorkingDensity(parentVoxel, 0);
                        }
                    }
                    else
                    {
                        P.parentVoxel = null;
                        P.die = true;
                    }

                    if (localPreviewCache != null)
                    {
                        localPreviewCache.AddParticle(P);
                    }

                    return localPreviewCache;
                },
                localPreviewCache =>
                {
                    if (localPreviewCache == null || !localPreviewCache.HasPoint) return;
                    lock (previewCacheLock)
                    {
                        previewCache.Merge(localPreviewCache);
                    }
                });

            if (previewCache != null)
            {
                previewCache.CompleteBuild();
            }

            particleCountTouchedCount = particleCount;
        }

        bool shouldBuildParticlePreviewCache()
        {
            if (Params == null || Params.Output == null || Params.Output.Count == 0) return false;

            return hasVisibleParticlePreviewRecipient(Params.Output[0], new HashSet<IGH_Param>());
        }

        bool hasVisibleParticlePreviewRecipient(IGH_Param sourceParam, HashSet<IGH_Param> visited)
        {
            if (sourceParam == null || sourceParam.Recipients == null) return false;

            foreach (IGH_Param recipient in sourceParam.Recipients)
            {
                if (recipient == null || !visited.Add(recipient)) continue;

                Preview_Particle preview = getOwnerComponent(recipient) as Preview_Particle;
                if (preview != null)
                {
                    if (preview.WantsSolverPreviewCache) return true;
                    continue;
                }

                if (hasVisibleParticlePreviewRecipient(recipient, visited))
                {
                    return true;
                }
            }

            return false;
        }

        GH_Component getOwnerComponent(IGH_Param param)
        {
            if (param == null || param.Attributes == null) return null;

            GH_LinkedParamAttributes linkedAttributes = param.Attributes as GH_LinkedParamAttributes;
            if (linkedAttributes != null && linkedAttributes.Parent != null)
            {
                return linkedAttributes.Parent.DocObject as GH_Component;
            }

            return param.Attributes.DocObject as GH_Component;
        }

        void resetParticleCountsForCurrentFrame()
        {
            if (particleCountsRequireFullReset)
            {
                Parallel.For(0, activeVoxels.Length, i =>
                {
                    activeVoxels[i].particleCount = 0;
                }
                );

                particleCountsRequireFullReset = false;
                particleCountTouchedCount = 0;
                return;
            }

            for (int i = 0; i < particleCountTouchedCount; i++)
            {
                Voxel touchedVoxel = particleCountTouchedVoxels[i];
                if (touchedVoxel != null)
                {
                    touchedVoxel.particleCount = 0;
                    particleCountTouchedVoxels[i] = null;
                }
            }

            particleCountTouchedCount = 0;
        }

        void ensureParticleCountTouchedCapacity(int count)
        {
            if (particleCountTouchedVoxels.Length < count)
            {
                Array.Resize(ref particleCountTouchedVoxels, count);
            }
        }

        void applyParticleBoundaryStateAfterWrapChange()
        {
            if (particles == null) return;

            Parallel.For(0, particles.Count, i =>
            {
                Particle P = particles[i];
                Point3d origin = P.pPlane.Origin;
                Point3d boundaryOrigin = boundaries(P, origin);

                P.pPlane.Origin = boundaryOrigin;
                P.parentVoxel = getParentVoxel(boundaryOrigin.X, boundaryOrigin.Y, boundaryOrigin.Z);
            }
            );

            particleCountsRequireFullReset = true;
        }

        Voxel particleCheckParentVoxel(Particle P)
        {

            Voxel output = null;

            P.age++;

            Point3d origin = P.pPlane.Origin;
            output = getParentVoxel(origin.X, origin.Y, origin.Z);

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
        void particleSenseValuesAndVectors(out long prepareTicks, out long particlesTicks)
        {
            prepareTicks = 0;
            particlesTicks = 0;

            if (!antParticles)
            {
                particleSenseSlimeOnly(out prepareTicks, out particlesTicks);
                return;
            }

            long particlesStart = Stopwatch.GetTimestamp();

            //sense for next iteration
            Parallel.For(0, particles.Count, p =>
            {
                Particle P = particles[p];
                Voxel parentVoxel = P.parentVoxel;
                if (parentVoxel == null) return;

                ParticleGroup parentGroup = P.parentParticleGroup;
                bool ant = parentGroup.ant;

                if (parentVoxel.vectorField)
                {
                    if (parentVoxel.frequency == 1 || iteration % parentVoxel.frequency == p % parentVoxel.frequency)
                    {
                        P.moveVector += parentVoxel.voxelVector;
                    }
                }

                double sensorAngleMultiplier = parentVoxel.sensorAngleMultiplier == -1 ? 1 : parentVoxel.sensorAngleMultiplier;
                double sensorDistanceMultiplier = parentVoxel.sensorDistanceMultiplier == -1 ? 1 : parentVoxel.sensorDistanceMultiplier;
                double sensorDistance = parentGroup.sensorDistance * sensorDistanceMultiplier;

                double sensorAngle = radAngle[parentGroup.sensorAngle] * sensorAngleMultiplier;
                double sensorCos = Math.Cos(sensorAngle);
                double sensorSin = Math.Sin(sensorAngle);
                Point3d origin = P.pPlane.Origin;
                Vector3d planeX = P.pPlane.XAxis;
                Vector3d planeY = P.pPlane.YAxis;

                double[] previousValues = ant ? threadLocalValues.Value : null;
                Point3d sensorPos0 = sensorSamplePosition(P, origin + (planeX * sensorCos - planeY * sensorSin) * sensorDistance);
                Point3d sensorPos1 = sensorSamplePosition(P, origin + planeX * sensorDistance);
                Point3d sensorPos2 = sensorSamplePosition(P, origin + (planeX * sensorCos + planeY * sensorSin) * sensorDistance);

                double value0 = sampleSensorValue(sensorPos0, parentVoxel, P, ant, ant ? previousValues[0] : -1, p);
                double value1 = sampleSensorValue(sensorPos1, parentVoxel, P, ant, ant ? previousValues[1] : -1, p);
                double value2 = sampleSensorValue(sensorPos2, parentVoxel, P, ant, ant ? previousValues[2] : -1, p);
                double value3 = -1;
                double value4 = -1;

                if (tridimensional)
                {
                    Vector3d vectorU = planeX;
                    vectorU.Rotate(sensorAngle, P.pPlane.YAxis);
                    Point3d sensorPos3 = sensorSamplePosition(P, origin + vectorU * sensorDistance);

                    Vector3d vectorD = planeX;
                    vectorD.Rotate(-sensorAngle, P.pPlane.YAxis);
                    Point3d sensorPos4 = sensorSamplePosition(P, origin + vectorD * sensorDistance);

                    value3 = sampleSensorValue(sensorPos3, parentVoxel, P, ant, ant ? previousValues[3] : -1, p);
                    value4 = sampleSensorValue(sensorPos4, parentVoxel, P, ant, ant ? previousValues[4] : -1, p);
                }

                if (ant)
                {
                    previousValues[0] = value0;
                    previousValues[1] = value1;
                    previousValues[2] = value2;
                    if (tridimensional)
                    {
                        previousValues[3] = value3;
                        previousValues[4] = value4;
                    }
                }

                int bestIndex = chooseBestSensorIndex(value0, value1, value2, value3, value4, tridimensional);
                applySensorMoveForce(P, parentVoxel, parentGroup, bestIndex, p);
            }
            );

            particlesTicks = Stopwatch.GetTimestamp() - particlesStart;
        }

        void particleSenseSlimeOnly(out long prepareTicks, out long particlesTicks)
        {
            prepareTicks = 0;
            particlesTicks = 0;

            bool useScalarSensors = useScalarDensityPath();
            if (useScalarSensors)
            {
                long prepareStart = Stopwatch.GetTimestamp();
                ensureScalarDensityAuthoritative();
                prepareTicks = Stopwatch.GetTimestamp() - prepareStart;
            }

            long particlesStart = Stopwatch.GetTimestamp();

            Parallel.For(0, particles.Count, p =>
            {
                Particle P = particles[p];
                Voxel parentVoxel = P.parentVoxel;
                if (parentVoxel == null) return;

                ParticleGroup parentGroup = P.parentParticleGroup;

                if (parentVoxel.vectorField)
                {
                    if (parentVoxel.frequency == 1 || iteration % parentVoxel.frequency == p % parentVoxel.frequency)
                    {
                        P.moveVector += parentVoxel.voxelVector;
                    }
                }

                double sensorAngleMultiplier = parentVoxel.sensorAngleMultiplier == -1 ? 1 : parentVoxel.sensorAngleMultiplier;
                double sensorDistanceMultiplier = parentVoxel.sensorDistanceMultiplier == -1 ? 1 : parentVoxel.sensorDistanceMultiplier;
                double sensorDistance = parentGroup.sensorDistance * sensorDistanceMultiplier;

                double sensorAngle = radAngle[parentGroup.sensorAngle] * sensorAngleMultiplier;
                double sensorCos = Math.Cos(sensorAngle);
                double sensorSin = Math.Sin(sensorAngle);
                Point3d origin = P.pPlane.Origin;
                Vector3d planeX = P.pPlane.XAxis;
                Vector3d planeY = P.pPlane.YAxis;

                Point3d sensorPos0 = sensorSamplePosition(P, origin + (planeX * sensorCos - planeY * sensorSin) * sensorDistance);
                Point3d sensorPos1 = sensorSamplePosition(P, origin + planeX * sensorDistance);
                Point3d sensorPos2 = sensorSamplePosition(P, origin + (planeX * sensorCos + planeY * sensorSin) * sensorDistance);

                double value0 = useScalarSensors ? sampleSlimeSensorValueScalar(sensorPos0) : sampleSlimeSensorValue(sensorPos0);
                double value1 = useScalarSensors ? sampleSlimeSensorValueScalar(sensorPos1) : sampleSlimeSensorValue(sensorPos1);
                double value2 = useScalarSensors ? sampleSlimeSensorValueScalar(sensorPos2) : sampleSlimeSensorValue(sensorPos2);
                double value3 = -1;
                double value4 = -1;

                if (tridimensional)
                {
                    Vector3d vectorU = planeX;
                    vectorU.Rotate(sensorAngle, P.pPlane.YAxis);
                    Point3d sensorPos3 = sensorSamplePosition(P, origin + vectorU * sensorDistance);

                    Vector3d vectorD = planeX;
                    vectorD.Rotate(-sensorAngle, P.pPlane.YAxis);
                    Point3d sensorPos4 = sensorSamplePosition(P, origin + vectorD * sensorDistance);

                    value3 = useScalarSensors ? sampleSlimeSensorValueScalar(sensorPos3) : sampleSlimeSensorValue(sensorPos3);
                    value4 = useScalarSensors ? sampleSlimeSensorValueScalar(sensorPos4) : sampleSlimeSensorValue(sensorPos4);
                }

                int bestIndex = chooseBestSensorIndex(value0, value1, value2, value3, value4, tridimensional);
                applySensorMoveForce(P, parentVoxel, parentGroup, bestIndex, p);
            }
            );

            particlesTicks = Stopwatch.GetTimestamp() - particlesStart;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double sampleSlimeSensorValueScalar(Point3d potPos)
        {
            int p_xID = planarYZ ? 0 : (int)(potPos.X * voxelSizeInverse);
            int p_yID = planarXZ ? 0 : (int)(potPos.Y * voxelSizeInverse);
            int p_zID = planarXY ? 0 : (int)(potPos.Z * voxelSizeInverse);

            if (p_xID < 0 || p_xID >= resX || p_yID < 0 || p_yID >= resY || p_zID < 0 || p_zID >= resZ)
            {
                return -1;
            }

            if (!wrapBoundaries && isBoundaryIndex(p_xID, p_yID, p_zID))
            {
                return -1;
            }

            return scalarVoxelDensity[p_xID * voxelStrideX + p_yID * voxelStrideY + p_zID];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double sampleSlimeSensorValue(Point3d potPos)
        {
            Voxel potentialVoxel = getParentVoxel(potPos.X, potPos.Y, potPos.Z);
            if (potentialVoxel == null || potentialVoxel.maxDensity == 0) return -1;
            if (!wrapBoundaries && potentialVoxel.boundary) return -1;

            double voxelValue = potentialVoxel.density;
            if (potentialVoxel.food > 0)
            {
                voxelValue = Math.Max(voxelValue, potentialVoxel.food);
            }

            if (slime_antFood > 0)
            {
                voxelValue += potentialVoxel.towardsFoodPheromone * slime_antFood;
            }

            if (slime_antBase > 0)
            {
                voxelValue += potentialVoxel.towardsBasePheromone * slime_antBase;
            }

            return voxelValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double sampleSensorValue(Point3d potPos, Voxel parentVoxel, Particle P, bool ant, double currentValue, int particleIndex)
        {
            Voxel potentialVoxel = getParentVoxel(potPos.X, potPos.Y, potPos.Z);
            if (potentialVoxel == null) return -1;
            if (!wrapBoundaries && potentialVoxel.boundary) return -1;

            double voxelValue = -99;

            if (!ant)
            {
                voxelValue = potentialVoxel.density;
                if (potentialVoxel.food > 0) voxelValue = Math.Max(potentialVoxel.density, potentialVoxel.food);

                if (antParticles)
                {
                    if (slime_antFood > 0) voxelValue += potentialVoxel.towardsFoodPheromone * slime_antFood;
                    if (slime_antBase > 0) voxelValue += potentialVoxel.towardsBasePheromone * slime_antBase;
                }
            }
            else
            {
                if (P.foundFood)
                {
                    voxelValue = potentialVoxel.towardsBasePheromone;
                    if (ant_slime > 0) voxelValue += potentialVoxel.density * ant_slime;
                }
                else
                {
                    if (parentVoxel.food <= 0)
                    {
                        if (potentialVoxel.towardsFoodPheromone > 0)
                        {
                            voxelValue = potentialVoxel.towardsFoodPheromone;
                            if (ant_slime > 0) voxelValue += potentialVoxel.density * ant_slime;
                        }
                        else if ((iteration + particleIndex) % 3 == 0)
                        {
                            voxelValue = potentialVoxel.towardsBasePheromone;
                            if (ant_slime > 0) voxelValue += potentialVoxel.density * ant_slime;
                        }
                    }
                    else
                    {
                        voxelValue = 1;
                    }
                }
            }

            if (voxelValue != -99)
            {
                currentValue = voxelValue;
            }

            if (potentialVoxel.maxDensity == 0)
            {
                currentValue = -1;
            }

            return currentValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int chooseBestSensorIndex(double value0, double value1, double value2, double value3, double value4, bool include3d)
        {
            double minValue = 9999;
            double maxValue = -1;
            int bestIndex = -1;

            updateBestSensor(value0, 0, ref minValue, ref maxValue, ref bestIndex);
            updateBestSensor(value1, 1, ref minValue, ref maxValue, ref bestIndex);
            updateBestSensor(value2, 2, ref minValue, ref maxValue, ref bestIndex);

            if (include3d)
            {
                updateBestSensor(value3, 3, ref minValue, ref maxValue, ref bestIndex);
                updateBestSensor(value4, 4, ref minValue, ref maxValue, ref bestIndex);
            }

            if (minValue == maxValue)
            {
                bestIndex = 1;
            }

            return bestIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void updateBestSensor(double value, int index, ref double minValue, ref double maxValue, ref int bestIndex)
        {
            if (value > maxValue)
            {
                maxValue = value;
                bestIndex = index;
            }

            if (value < minValue)
            {
                minValue = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void applySensorMoveForce(Particle P, Voxel parentVoxel, ParticleGroup parentGroup, int bestIndex, int particleIndex)
        {
            if (bestIndex == -1)
            {
                if (!wrapBoundaries)
                {
                    double rotA = parentGroup.rotationAngle * particleIndex;
                    if (rotA < 0) rotA = 360 - (rotA % 360);
                    if (rotA > 360) rotA %= 360;
                    double RA = radAngle[Convert.ToInt32(rotA)];
                    P.pPlane.Rotate(RA, P.pPlane.ZAxis, P.pPlane.Origin);
                }

                return;
            }

            Vector3d valueForce = P.pPlane.XAxis;
            if (bestIndex != 1)
            {
                double rotationAngleMultiplier = parentVoxel.rotationAngleMultiplier == -1 ? 1 : parentVoxel.rotationAngleMultiplier;
                double rotationAngle = radAngle[parentGroup.rotationAngle] * rotationAngleMultiplier;

                if (bestIndex == 0)
                {
                    double rotationCos = Math.Cos(rotationAngle);
                    double rotationSin = Math.Sin(rotationAngle);
                    valueForce = P.pPlane.XAxis * rotationCos - P.pPlane.YAxis * rotationSin;
                }
                else if (bestIndex == 2)
                {
                    double rotationCos = Math.Cos(rotationAngle);
                    double rotationSin = Math.Sin(rotationAngle);
                    valueForce = P.pPlane.XAxis * rotationCos + P.pPlane.YAxis * rotationSin;
                }
                else if (bestIndex == 3 && tridimensional)
                {
                    valueForce.Rotate(rotationAngle, P.pPlane.YAxis);
                }
                else if (bestIndex == 4 && tridimensional)
                {
                    valueForce.Rotate(-rotationAngle, P.pPlane.YAxis);
                }
            }

            valueForce.Unitize();
            P.moveVector += valueForce;
        }

        //----------------------------------

        void particleSense_Ant()
        {
            //ant paricles
            double maxDist = Math.Max(dimX, dimY);
            maxDist = Math.Max(maxDist, dimZ);

            shuffleParticlesInPlace(particles);

            //sense for next iteration
            Parallel.For(0, particles.Count, p =>
            {
                Particle P = particles[p];
                if (P.parentVoxel != null)
                {
                    ParticleGroup parentGroup = P.parentParticleGroup;
                    if (parentGroup.ant)
                    {
                        double sensorDistance = parentGroup.sensorDistance;
                        Vector3d outsideVector = P.pPlane.Origin - P.home.Origin;
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
                        if (p % 7 == 0)
                        {
                            P.moveVector += wanderVectors[(p + iteration) % wanderVectors.Count];
                        }

                        //when it's close to base wonder out
                        if (P.age < 30 && outsideVector.Length < sensorDistance * 3)
                        {
                            outsideVector.Unitize();
                            P.moveVector += outsideVector * 10;
                        }

                        //towards home
                        if (P.foundFood)
                        {
                            if (p % (int) parentGroup.baseWanderFrequency == 0)
                            {
                                P.moveVector += towardsHomeVector;
                            }
                        }

                        if (!P.foundFood && P.age > 100)
                        {
                            P.moveVector += towardsHomeVector * 0.01 * P.age / 100;
                        }

                        //when close to home, visit
                        if (outsideVector.Length <= sensorDistance * 2 && P.age > 30)
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

        void particleMoveAndDeposit(out long shuffleTicks, out long particlesTicks)
        {
            bool shuffleMovementParticles = antParticles || dynPop;
            long stageStart = Stopwatch.GetTimestamp();
            if (shuffleMovementParticles)
            {
                shuffleParticlesInPlace(particles);
            }
            shuffleTicks = Stopwatch.GetTimestamp() - stageStart;

            int currentIteration = iteration;
            uint movementIterationSeed = unchecked((uint)currentIteration * 747796405u);
            int wanderVectorCount = wanderVectors != null ? wanderVectors.Count : 0;

            stageStart = Stopwatch.GetTimestamp();
            Parallel.For(0, particles.Count, i =>
            {
                Particle P = particles[i];

                Voxel parentVoxel = P.parentVoxel;
                if (parentVoxel != null)
                {
                    ParticleGroup parentGroup = P.parentParticleGroup;
  
                    Vector3d xVector = P.pPlane.XAxis;
                    xVector.Unitize();
                    Vector3d moveVector = P.moveVector;
                    moveVector.Unitize();
                    moveVector += xVector;

                    //slime wander movement
                    if (!parentGroup.ant)
                    {
                        int wanderFrequency = (int) parentGroup.wanderFrequency;

                        if (wanderFrequency > 0 && wanderVectorCount > 0)
                        {
                            bool addWander = false;
                            int wanderIndex = 0;

                            if (shuffleMovementParticles)
                            {
                                addWander = i % wanderFrequency == 0;
                                if (addWander)
                                {
                                    wanderIndex = (i % wanderVectorCount + currentIteration % wanderVectorCount) % wanderVectorCount;
                                }
                            }
                            else
                            {
                                uint movementKey = movementParticleKey(i, movementIterationSeed);
                                addWander = movementKey % (uint)wanderFrequency == 0;
                                if (addWander)
                                {
                                    wanderIndex = (int)(movementKey % (uint)wanderVectorCount);
                                }
                            }

                            if (addWander)
                            {
                                Vector3d wanderVector = wanderVectors[wanderIndex];
                                moveVector += 1.5 * wanderVector;
                                moveVector.Unitize();
                            }
                        }
                    }

                    P.moveVector = new Vector3d();

                    //if 2D, adapt vector
                    if (planarXY) moveVector.Z = 0;
                    if (planarXZ) moveVector.Y = 0;
                    if (planarYZ) moveVector.X = 0;

                    double speedMultiplier = 1;
                    if (parentVoxel.speedMultiplier != -1) speedMultiplier = parentVoxel.speedMultiplier;
                    double moveSpeed = parentGroup.speed * speedMultiplier;

                    if (discretizeMovement == true)
                    {
                        Vector3d discreteMoveVector = discretizeVector(moveVector);
                        moveVector = discreteMoveVector;
                    }
                    else if (!moveVector.Unitize())
                    {
                        return;
                    }

                    alignParticleToUnitMoveVector(P, moveVector);
                    moveVector *= moveSpeed;
                    Point3d nextLoc = P.pPlane.Origin + moveVector;

                    //if 2D, adapt coordinates
                    if (planarXY) nextLoc.Z = dimZ / 2;
                    if (planarXZ) nextLoc.Y = dimY / 2;
                    if (planarYZ) nextLoc.X = dimX / 2;

                    //apply boundaries for new location
                    nextLoc = boundaries(P, nextLoc);

                    //find parent voxel for new location
                    Voxel nextVoxel = getParentVoxel(nextLoc.X, nextLoc.Y, nextLoc.Z);

                    //account for maxDensity == 0
                    if (nextVoxel != null)
                    {
                        if (nextVoxel.maxDensity == 0) nextVoxel = null;
                    }

                    //move to a random neighbour
                    if (nextVoxel == null || nextVoxel.boundary)
                    {
                        bool moveToRandomNeighbour = true;
                        int idX = parentVoxel.idX;
                        int idY = parentVoxel.idY;
                        int idZ = parentVoxel.idZ;

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
                            Voxel[] neighborArray = threadLocalNeighbors.Value;
                            int neighborCount = 0;

                            //create list with all viable neighbours
                            int range = (int)(moveSpeed / voxelSize);
                            if (range < 1) range = 1;

                            for (int u = idX - range; u <= idX + range; u += range)
                            {
                                for (int v = idY - range; v <= idY + range; v += range)
                                {
                                    for (int w = idZ - range; w <= idZ + range; w += range)
                                    {
                                        if (u >= 0 && u < resX && v >= 0 && v < resY && w >= 0 && w < resZ)
                                        {
                                            Voxel neighborV = voxels[u, v, w];
                                            if (neighborV != null && neighborV.maxDensity != 0 && !neighborV.boundary)
                                            {
                                                if (neighborCount < 27)
                                                {
                                                    neighborArray[neighborCount++] = neighborV;
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (neighborCount > 0)
                            {
                                //pick random one
                                int randomIndex = threadLocalRandom.Value.Next(neighborCount);
                                nextVoxel = neighborArray[randomIndex];
                                nextLoc = nextVoxel.loc;
                            }
                        }
                    }

                    if (nextVoxel != null)
                    {
                        //assign new location
                        P.pPlane.Origin = nextLoc;
                        //P.pPlane.Origin = nextVoxel.loc;
                        P.parentVoxel = nextVoxel;

                        //check if the next parent voxel is occupied by other particles
                        if (nextVoxel.particleCount == 0)
                        {
                            particleDeposit(P, parentGroup.depositValue);
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

            particlesTicks = Stopwatch.GetTimestamp() - stageStart;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        uint movementParticleKey(int particleIndex, uint iterationSeed)
        {
            unchecked
            {
                uint value = (uint)particleIndex + iterationSeed;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                return value;
            }
        }

        //----------------------------------

        void createDiscreteVectors()
        {
            Vector3d v1 = new Vector3d(1, 0, 0);
            Vector3d v2 = new Vector3d(0, 1, 0);
            Vector3d v3 = new Vector3d(-1, 0, 0);
            Vector3d v4 = new Vector3d(0, -1, 0);

            Vector3d v5 = new Vector3d(1, 1, 0);
            Vector3d v6 = new Vector3d(-1, -1, 0);
            Vector3d v7 = new Vector3d(1, -1, 0);
            Vector3d v8 = new Vector3d(-1, 1, 0);

            v1.Unitize();
            v2.Unitize();
            v3.Unitize();
            v4.Unitize();

            v5.Unitize();
            v6.Unitize();
            v7.Unitize();
            v8.Unitize();

            discreteVectors = new[] { v1, v2, v3, v4, v5, v6, v7, v8 };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Vector3d discretizeVector(Vector3d V)
        {
            if (discreteVectors.Length == 0) return new Vector3d(0, 0, 0);

            Vector3d unitV = V;
            if (!unitV.Unitize())
            {
                return discreteVectors[0];
            }

            Vector3d resultV = discreteVectors[0];
            double maxDot = unitV.X * resultV.X + unitV.Y * resultV.Y + unitV.Z * resultV.Z;

            for (int i = 1; i < discreteVectors.Length; i++)
            {
                Vector3d discreteV = discreteVectors[i];
                double dot = unitV.X * discreteV.X + unitV.Y * discreteV.Y + unitV.Z * discreteV.Z;

                if (dot > maxDot)
                {
                    maxDot = dot;
                    resultV = discreteV;
                }
            }

            return resultV;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void alignParticleToUnitMoveVector(Particle P, Vector3d unitMoveVector)
        {
            const double planeTolerance = 1e-9;

            if (planarXY && Math.Abs(unitMoveVector.Z) < planeTolerance)
            {
                P.alignToUnitVectorPlanarXY(unitMoveVector);
                return;
            }

            if (planarXZ && Math.Abs(unitMoveVector.Y) < planeTolerance)
            {
                P.alignToUnitVectorPlanarXZ(unitMoveVector);
                return;
            }

            if (planarYZ && Math.Abs(unitMoveVector.X) < planeTolerance)
            {
                P.alignToUnitVectorPlanarYZ(unitMoveVector);
                return;
            }

            P.alignToUnitVector(unitMoveVector);
        }

        //----------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void particleDeposit(Particle P, double depositValue)
        {
            Voxel parentVoxel = P.parentVoxel;
            if (parentVoxel == null || parentVoxel.maxDensity == 0) return;

            ParticleGroup parentGroup = P.parentParticleGroup;
            if (!wrapBoundaries && !canDepositAtVoxel(parentVoxel, parentGroup.sensorDistance)) return;

            depositAtVoxel(P, parentVoxel, parentGroup, depositValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void setWorkingDensity(Voxel V, double value)
        {
            if (useScalarDensityPath())
            {
                scalarVoxelDensity[V.flatIndex] = value;
                scalarVoxelDensityDirtyForOutput = true;
                return;
            }

            V.density = value;
            scalarVoxelDensityAuthoritative = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void addWorkingDensity(Voxel V, double value)
        {
            if (useScalarDensityPath())
            {
                scalarVoxelDensity[V.flatIndex] += value;
                scalarVoxelDensityDirtyForOutput = true;
                return;
            }

            V.density += value;
            scalarVoxelDensityAuthoritative = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool canDepositAtVoxel(Voxel parentVoxel, double sensorDistance)
        {
            int boundaryRange = 1;
            double sensorDiameter = sensorDistance * 2;

            if (tridimensional)
            {
                if (dimX > sensorDiameter && dimY > sensorDiameter && dimZ > sensorDiameter)
                {
                    boundaryRange = Convert.ToInt32(sensorDistance);
                }

                return parentVoxel.idX >= boundaryRange && parentVoxel.idX < resX - boundaryRange &&
                       parentVoxel.idY >= boundaryRange && parentVoxel.idY < resY - boundaryRange &&
                       parentVoxel.idZ >= boundaryRange && parentVoxel.idZ < resZ - boundaryRange;
            }

            if (planarXY)
            {
                if (dimX > sensorDiameter && dimY > sensorDiameter)
                {
                    boundaryRange = Convert.ToInt32(sensorDistance);
                }

                return parentVoxel.idX >= boundaryRange && parentVoxel.idX < resX - boundaryRange &&
                       parentVoxel.idY >= boundaryRange && parentVoxel.idY < resY - boundaryRange;
            }

            if (planarXZ)
            {
                if (dimX > sensorDiameter && dimZ > sensorDiameter)
                {
                    boundaryRange = Convert.ToInt32(sensorDistance);
                }

                return parentVoxel.idX >= boundaryRange && parentVoxel.idX < resX - boundaryRange &&
                       parentVoxel.idZ >= boundaryRange && parentVoxel.idZ < resZ - boundaryRange;
            }

            if (planarYZ)
            {
                if (dimY > sensorDiameter && dimZ > sensorDiameter)
                {
                    boundaryRange = Convert.ToInt32(sensorDistance);
                }

                return parentVoxel.idY >= boundaryRange && parentVoxel.idY < resY - boundaryRange &&
                       parentVoxel.idZ >= boundaryRange && parentVoxel.idZ < resZ - boundaryRange;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void depositAtVoxel(Particle P, Voxel parentVoxel, ParticleGroup parentGroup, double depositValue)
        {
            if (parentGroup.ant)
            {
                double baseMultiplier;
                double foodMultiplier;

                if (P.age < maxAge)
                {
                    baseMultiplier = multiplierBase[P.age];
                    foodMultiplier = multiplierFood[P.age];
                }
                else
                {
                    baseMultiplier = minBase;
                    foodMultiplier = minFood;
                }

                if (P.foundFood)
                {
                    parentVoxel.towardsFoodPheromone += depositValue * foodMultiplier;
                }
                else
                {
                    parentVoxel.towardsBasePheromone += (parentVoxel.towardsFoodPheromone > 0 ? 1.1 : 0.9) * depositValue * baseMultiplier;
                }

                return;
            }

            double slimeDeposit = P.highDeposit ? depositValue : depositValue / 4;
            if (slime_antBase == 0 && slime_antFood == 0)
            {
                addWorkingDensity(parentVoxel, slimeDeposit);
                return;
            }

            if (slime_antFood > 0)
            {
                addWorkingDensity(parentVoxel, slimeDeposit * (1 - slime_antFood) + parentVoxel.towardsFoodPheromone * slime_antFood);
            }

            if (slime_antBase > 0)
            {
                addWorkingDensity(parentVoxel, slimeDeposit * (1 - slime_antBase) + parentVoxel.towardsBasePheromone * slime_antBase);
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
                bool sampleTrail = iteration % trailFreq == 0;

                Parallel.For(0, particles.Count, i =>
                {
                    Particle P = particles[i];
                    List<Point3d> trails = P.trails;

                    if (P.parentVoxel != null)
                    {
                        if (trailSize > 1)
                        {
                            if (trails.Capacity < trailSize)
                            {
                                trails.Capacity = trailSize;
                            }

                            Point3d origin = P.pPlane.Origin;

                            if (sampleTrail)
                            {
                                if (trails.Count > 0)
                                {
                                    trails.Insert(0, origin);
                                }
                                else trails.Add(origin);

                                if (trails.Count > trailSize)
                                {
                                    trails.RemoveAt(trails.Count - 1);
                                }
                            }
                            else
                            {
                                if (trails.Count > 0)
                                {
                                    trails[0] = origin;
                                }
                                else trails.Add(origin);
                            }
                        }
                        else
                        {
                            if (trails.Count > 0) trails.Clear();
                        }
                    }
                }
                );
            }
        }

        //-------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Voxel getParentVoxel(Point3d p)
        {
            return getParentVoxel(p.X, p.Y, p.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Voxel getParentVoxel(double x, double y, double z)
        {
            int p_xID = planarYZ ? 0 : (int)(x * voxelSizeInverse);
            int p_yID = planarXZ ? 0 : (int)(y * voxelSizeInverse);
            int p_zID = planarXY ? 0 : (int)(z * voxelSizeInverse);

            if (p_xID < 0 || p_xID >= resX || p_yID < 0 || p_yID >= resY || p_zID < 0 || p_zID >= resZ)
            {
                return null;
            }

            return voxelFlat[p_xID * voxelStrideX + p_yID * voxelStrideY + p_zID];
        }

        //----------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Point3d sensorSamplePosition(Particle P, Point3d p)
        {
            if (wrapBoundaries)
            {
                return sensorBoundaries(p);
            }

            return boundaries(P, p);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Point3d sensorBoundaries(Point3d p)
        {
            double x = p.X;
            double y = p.Y;
            double z = p.Z;

            if (!wrapBoundaries)
            {
                double boundaryDistance = voxelSize;

                if (!planarYZ)
                {
                    if (x <= boundaryDistance) x = boundaryDistance;
                    else if (x >= dimX - boundaryDistance) x = dimX - boundaryDistance;
                }

                if (!planarXZ)
                {
                    if (y <= boundaryDistance) y = boundaryDistance;
                    else if (y >= dimY - boundaryDistance) y = dimY - boundaryDistance;
                }

                if (!planarXY)
                {
                    if (z <= boundaryDistance) z = boundaryDistance;
                    else if (z >= dimZ - boundaryDistance) z = dimZ - boundaryDistance;
                }
            }
            else
            {
                const double wrapDistance = 0.01;

                if (!planarYZ)
                {
                    if (x < wrapDistance) x = dimX - 0.1;
                    else if (x > dimX - wrapDistance) x = 0.1;
                }

                if (!planarXZ)
                {
                    if (y < wrapDistance) y = dimY - 0.1;
                    else if (y > dimY - wrapDistance) y = 0.1;
                }

                if (!planarXY)
                {
                    if (z < wrapDistance) z = dimZ - 0.1;
                    else if (z > dimZ - wrapDistance) z = 0.1;
                }
            }

            if (x == p.X && y == p.Y && z == p.Z) return p;
            return new Point3d(x, y, z);
        }

        //----------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Point3d boundaries(Particle P, Point3d p)
        {
            Point3d nextLoc = p;

            if (wrapBoundaries == false)
            {
                double boundaryDistance = voxelSize;
                if ((planarYZ || (nextLoc.X > boundaryDistance && nextLoc.X < dimX - boundaryDistance)) &&
                    (planarXZ || (nextLoc.Y > boundaryDistance && nextLoc.Y < dimY - boundaryDistance)) &&
                    (planarXY || (nextLoc.Z > boundaryDistance && nextLoc.Z < dimZ - boundaryDistance)))
                {
                    return nextLoc;
                }

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
                const double wrapDistance = 0.01;
                if ((planarYZ || (nextLoc.X >= wrapDistance && nextLoc.X <= dimX - wrapDistance)) &&
                    (planarXZ || (nextLoc.Y >= wrapDistance && nextLoc.Y <= dimY - wrapDistance)) &&
                    (planarXY || (nextLoc.Z >= wrapDistance && nextLoc.Z <= dimZ - wrapDistance)))
                {
                    return nextLoc;
                }

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
                shuffleParticlesInPlace(particles);

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
                    List<Particle> shuffledParticles = createShuffledParticleList(particles);

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
                                    if (emptyNeighbours.Count > 0)
                                    {
                                        int randomIndex = random.Next(emptyNeighbours.Count);
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
                                            int randomIndex = random.Next(emptyNeighbours.Count);
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
                                            int randomIndex = random.Next(emptyNeighbours.Count);
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
                                            int randomIndex = random.Next(emptyNeighbours.Count);
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
                    particles = new ParticleList(newParticles);

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
                    List<Particle> shuffledParticles = createShuffledParticleList(particles);

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

                            Vector3d newP_Vector = P.pPlane.XAxis + P.moveVector;
                            newP_Vector.Unitize();

                            Vector3d newP_Vector_R = new Vector3d(newP_Vector.X, newP_Vector.Y, newP_Vector.Z);
                            Vector3d newP_Vector_L = new Vector3d(newP_Vector.X, newP_Vector.Y, newP_Vector.Z);

                            newP_Vector_R.Rotate(retrieveRotationAngle(P) / 4, P.pPlane.ZAxis);
                            newP_Vector_L.Rotate(-retrieveRotationAngle(P) / 4, P.pPlane.ZAxis);

                            P.alignToVector(P.pPlane.XAxis - newP_Vector_R);
                            newP.alignToVector(P.pPlane.XAxis - newP_Vector_L);

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
                    particles = new ParticleList(newParticles);

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

            //reset discretize
            discretizeMovement = false;

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
                        if (diffuseRange < 0) diffuseRange = 0;
                        decay = Convert.ToDouble(inputSettings_components[3]);
                        break;

                    case "VoxelSettingsAnt":
                        foodDiffuseRate = Convert.ToDouble(inputSettings_components[1]);
                        foodDecayRate = Convert.ToDouble(inputSettings_components[2]);
                        baseDiffuseRate = Convert.ToDouble(inputSettings_components[3]);
                        baseDecayRate = Convert.ToDouble(inputSettings_components[4]);
                        diffuseRange_Ant = Convert.ToInt32(inputSettings_components[5]);
                        if (diffuseRange_Ant < 0) diffuseRange_Ant = 0;
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

                    case "DiscreteVectors":

                        List<Vector3d> inputDiscreteVectors = new List<Vector3d>();

                        for (int s = 1; s < inputSettings_components.Length; s++)
                        {
                            string[] coordinates = inputSettings_components[s].Split(',');
                            double x = Convert.ToDouble(coordinates[0]);
                            double y = Convert.ToDouble(coordinates[1]);
                            double z = Convert.ToDouble(coordinates[2]);

                            Vector3d discreteVector = new Vector3d(x, y, z);
                            if (discreteVector.Unitize())
                            {
                                inputDiscreteVectors.Add(discreteVector);
                            }
                        }

                        discreteVectors = inputDiscreteVectors.ToArray();

                        if (discreteVectors.Length > 1)
                        {
                            discretizeMovement = true;
                        } else
                        {
                            discretizeMovement = false;
                        }

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

        List<Particle> createShuffledParticleList(List<Particle> source)
        {
            List<Particle> shuffledParticles = new List<Particle>(source);
            shuffleParticlesInPlace(shuffledParticles);
            return shuffledParticles;
        }

        void shuffleParticlesInPlace(List<Particle> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Particle temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
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
