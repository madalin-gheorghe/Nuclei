using System;
using System.Collections.Generic;

using Grasshopper.Kernel;

using static Nuclei3.ParticleGroup;

namespace Nuclei3
{
    public class SolverGPU : GH_Component
    {
        public SolverGPU()
          : base("Nuclei3 Solver GPU", "Solver GPU",
              "Experimental GPU compute solver scaffold",
              "Nuclei3", " Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Reset", "reset", "Reset Boolean", GH_ParamAccess.item);
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
            pManager.AddParameter(new ParticleGroupParameter(), "Particles", "particles", "Connects to Particle Constructors", GH_ParamAccess.list);
            pManager[2].DataMapping = GH_DataMapping.Flatten;
            pManager.AddTextParameter("Solver Settings", "settings", "Connects to Settings", GH_ParamAccess.list);
            pManager[3].Optional = true;
            pManager[3].DataMapping = GH_DataMapping.Flatten;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output Particles", "particles", "Output Particles", GH_ParamAccess.item);
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
            pManager.AddTextParameter("GPU Status", "status", "GPU compute status", GH_ParamAccess.item);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.secondary; }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool reset = true;
            Voxel[,,] inputVoxels = null;
            List<ParticleGroup> inputParticleGroups = new List<ParticleGroup>();
            List<string> settings = new List<string>();

            DA.GetData(0, ref reset);
            DA.GetData(1, ref inputVoxels);
            DA.GetDataList(2, inputParticleGroups);
            DA.GetDataList(3, settings);

            if (reset || gpuStatus == null)
            {
                gpuStatus = GpuComputeSmokeTest.RunOnce();
            }

            SolverGpuSettings solverSettings = SolverGpuSettings.FromStrings(settings);
            bool shouldResetState = reset || voxels == null || particles == null || !StateMatches(inputVoxels);

            if (shouldResetState)
            {
                ResetState(inputVoxels, inputParticleGroups);
            }

            GpuDiffusionStepResult diffusionResult = null;
            if (!reset && gpuStatus.Available && voxels != null && iteration < solverSettings.MaxIterations)
            {
                try
                {
                    diffusionResult = RunGpuDiffusionStep(solverSettings);
                    iteration++;
                }
                catch (Exception ex)
                {
                    DisposeDiffusionEngine();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GPU diffusion failed: " + ex.Message);
                    Message = "GPU diffusion error";
                }
            }

            DA.SetData(0, particles);
            DA.SetData(1, voxels);

            string status = CreateStatus(solverSettings, diffusionResult);
            DA.SetData(2, status);

            if (gpuStatus.Available)
            {
                if (Message != "GPU diffusion error")
                {
                    Message = reset ? "Solution is Reset" : "Iteration: " + iteration;
                }
            }
            else
            {
                Message = "GPU unavailable";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, gpuStatus.Message);
            }
        }

        void ResetState(Voxel[,,] inputVoxels, List<ParticleGroup> inputParticleGroups)
        {
            SolverGpuInputSnapshot snapshot = SolverGpuInputSnapshot.Capture(inputVoxels, inputParticleGroups);
            voxels = snapshot.Voxels;
            particles = snapshot.Particles;
            resX = snapshot.ResX;
            resY = snapshot.ResY;
            resZ = snapshot.ResZ;
            activeVoxelCount = snapshot.ActiveVoxelCount;
            iteration = 0;

            DisposeDiffusionEngine();

            if (gpuStatus != null && gpuStatus.Available && snapshot.VoxelDensity != null && snapshot.VoxelDensity.Length > 0)
            {
                diffusionEngine = new GpuScalarDiffusionEngine(resX, resY, resZ, snapshot.VoxelDensity);
            }
        }

        bool StateMatches(Voxel[,,] inputVoxels)
        {
            if (inputVoxels == null)
            {
                return resX == 0 && resY == 0 && resZ == 0;
            }

            return inputVoxels.GetLength(0) == resX
                && inputVoxels.GetLength(1) == resY
                && inputVoxels.GetLength(2) == resZ;
        }

        GpuDiffusionStepResult RunGpuDiffusionStep(SolverGpuSettings settings)
        {
            if (diffusionEngine == null || !diffusionEngine.Matches(resX, resY, resZ))
            {
                float[] density = CaptureCurrentDensity();
                DisposeDiffusionEngine();
                diffusionEngine = new GpuScalarDiffusionEngine(resX, resY, resZ, density);
            }

            SolverGpuDimensionMode dimensionMode = SolverGpuDimensionMode.FromResolution(resX, resY, resZ);
            return diffusionEngine.Step(voxels, settings, dimensionMode);
        }

        float[] CaptureCurrentDensity()
        {
            int count = resX * resY * resZ;
            float[] density = new float[count];

            if (voxels == null)
            {
                return density;
            }

            for (int x = 0; x < resX; x++)
            {
                int xBase = x * resY * resZ;
                for (int y = 0; y < resY; y++)
                {
                    int baseIndex = xBase + y * resZ;
                    for (int z = 0; z < resZ; z++)
                    {
                        Voxel voxel = voxels[x, y, z];
                        if (voxel != null)
                        {
                            density[baseIndex + z] = (float)voxel.density;
                        }
                    }
                }
            }

            return density;
        }

        string CreateStatus(SolverGpuSettings settings, GpuDiffusionStepResult diffusionResult)
        {
            string status = gpuStatus.Message
                + " | driver: " + (string.IsNullOrEmpty(gpuStatus.Driver) ? "none" : gpuStatus.Driver)
                + " | feature: " + gpuStatus.FeatureLevel
                + " | iteration: " + iteration
                + " | particles: " + (particles != null ? particles.Count : 0)
                + " | active voxels: " + activeVoxelCount
                + " | mode: " + SolverGpuDimensionMode.FromResolution(resX, resY, resZ).Name
                + " | wrap: " + settings.WrapBoundaries
                + " | range: " + settings.DiffuseRange;

            if (gpuStatus.Milliseconds > 0)
            {
                status += " | smoke test ms: " + gpuStatus.Milliseconds.ToString("0.###");
            }

            if (diffusionResult != null)
            {
                status += " | diffusion ms: " + diffusionResult.Milliseconds.ToString("0.###")
                    + " | passes: " + diffusionResult.Passes;
            }

            return status;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            DisposeDiffusionEngine();
            base.RemovedFromDocument(document);
        }

        void DisposeDiffusionEngine()
        {
            if (diffusionEngine != null)
            {
                diffusionEngine.Dispose();
                diffusionEngine = null;
            }
        }

        GpuComputeSmokeTestResult gpuStatus;
        GpuScalarDiffusionEngine diffusionEngine;
        Voxel[,,] voxels;
        ParticleList particles;
        int resX;
        int resY;
        int resZ;
        int activeVoxelCount;
        int iteration = 0;

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Nuclei3.Properties.Resources.Solver;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("931b7e8e-700b-476c-8320-b26296c5b661"); }
        }
    }
}
