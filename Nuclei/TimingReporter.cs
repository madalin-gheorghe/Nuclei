using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Nuclei3
{
    internal static class TimingReporter
    {
        public const int ReportFrequency = 10;

        public struct SolverContext
        {
            public bool WrapBoundaries;
            public int ResX;
            public int ResY;
            public int ResZ;
            public int ActiveVoxels;
            public bool DenseVoxelGrid;
            public string DimensionMode;
            public double Diffuse;
            public int DiffuseRange;
            public double Decay;
            public bool AntParticles;
            public int DiffuseRangeAnt;
            public int TrailSize;
            public int TrailFreq;
            public bool DynPop;
            public bool Division;
            public bool Death;
            public int MaxIterations;
            public string GpuPreviewMode;
            public bool GpuDensityFieldPreview;
        }

        static readonly object fileLock = new object();
        static readonly string outputDirectory = @"C:\Nuclei\BenchmarkSuite1";
        static readonly string outputPath = Path.Combine(outputDirectory, "NucleiTiming.csv");

        static bool disabled = false;
        static bool headerWritten = false;
        static string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        static readonly string headerLine = "timestamp,run_id,component,iteration_or_call,samples,particles,voxels,preview_step,gpu_preview_mode,gpu_field_preview,wrap_boundaries,res_x,res_y,res_z,active_voxels,dense_voxel_grid,dimension_mode,diffuse,diffuse_range,decay,ant_particles,ant_diffuse_range,trail_size,trail_freq,dyn_pop,division,death,max_iterations,total_ms,settings_ms,inputs_ms,sense_ms,move_ms,trail_ms,diffuse_ms,parent_ms,population_ms,outputs_ms,density_sync_ms,set_particles_ms,set_voxels_ms,rebuild_ms,draw_ms,sense_prepare_ms,sense_particles_ms,sense_ant_ms,move_shuffle_ms,move_particles_ms";

        public static void StartRun()
        {
            runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            WriteRawLine(CreateLine("run_start", 0, 0, 0, 0, 0, new SolverContext(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }

        public static void WriteSolverAverages(
            int iteration,
            int samples,
            int particleCount,
            int voxelCount,
            SolverContext context,
            double totalMs,
            double settingsMs,
            double inputsMs,
            double senseMs,
            double moveMs,
            double trailMs,
            double diffuseMs,
            double parentMs,
            double populationMs,
            double outputsMs,
            double densitySyncMs,
            double setParticlesMs,
            double setVoxelsMs,
            double sensePrepareMs,
            double senseParticlesMs,
            double senseAntMs,
            double moveShuffleMs,
            double moveParticlesMs)
        {
            WriteRawLine(CreateLine("solver", iteration, samples, particleCount, voxelCount, 0, context, totalMs, settingsMs, inputsMs, senseMs, moveMs, trailMs, diffuseMs, parentMs, populationMs, outputsMs, densitySyncMs, setParticlesMs, setVoxelsMs, 0, 0, sensePrepareMs, senseParticlesMs, senseAntMs, moveShuffleMs, moveParticlesMs));
        }

        public static void WriteGpuSolverAverages(
            int iteration,
            int samples,
            int particleCount,
            int voxelCount,
            SolverContext context,
            double totalMs,
            double settingsMs,
            double inputsMs,
            double moveMs,
            double diffuseMs,
            double densitySyncMs,
            double outputsMs,
            double setParticlesMs,
            double setVoxelsMs)
        {
            WriteRawLine(CreateLine("solver_gpu", iteration, samples, particleCount, voxelCount, 0, context, totalMs, settingsMs, inputsMs, 0, moveMs, 0, diffuseMs, 0, 0, outputsMs, densitySyncMs, setParticlesMs, setVoxelsMs, 0, 0, 0, 0, 0, 0, moveMs));
        }

        public static void WritePreviewAverages(
            int call,
            int samples,
            int particleCount,
            int previewStep,
            double totalMs,
            double rebuildMs,
            double drawMs)
        {
            WriteRawLine(CreateLine("preview_particle", call, samples, particleCount, 0, previewStep, new SolverContext(), totalMs, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, rebuildMs, drawMs, 0, 0, 0, 0, 0));
        }

        public static void WriteSolverGpuPreviewAverages(
            int call,
            int samples,
            int particleCount,
            double totalMs,
            double rebuildMs,
            double drawMs)
        {
            WriteRawLine(CreateLine("preview_solver_gpu", call, samples, particleCount, 0, 1, new SolverContext(), totalMs, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, rebuildMs, drawMs, 0, 0, 0, 0, 0));
        }

        public static void WriteGpuDensityFieldPreviewAverages(
            int call,
            int samples,
            int particleCount,
            int voxelCount,
            SolverContext context,
            double totalMs,
            double drawMs)
        {
            WriteRawLine(CreateLine("preview_gpu_field", call, samples, particleCount, voxelCount, 1, context, totalMs, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, drawMs, 0, 0, 0, 0, 0));
        }

        public static double TicksToMilliseconds(long ticks, int samples)
        {
            if (samples <= 0) return 0;
            return ticks * 1000.0 / Stopwatch.Frequency / samples;
        }

        static string CreateLine(
            string component,
            int iterationOrCall,
            int samples,
            int particleCount,
            int voxelCount,
            int previewStep,
            SolverContext context,
            double totalMs,
            double settingsMs,
            double inputsMs,
            double senseMs,
            double moveMs,
            double trailMs,
            double diffuseMs,
            double parentMs,
            double populationMs,
            double outputsMs,
            double densitySyncMs,
            double setParticlesMs,
            double setVoxelsMs,
            double rebuildMs,
            double drawMs,
            double sensePrepareMs,
            double senseParticlesMs,
            double senseAntMs,
            double moveShuffleMs,
            double moveParticlesMs)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            return string.Join(",",
                timestamp,
                runId,
                component,
                iterationOrCall.ToString(CultureInfo.InvariantCulture),
                samples.ToString(CultureInfo.InvariantCulture),
                particleCount.ToString(CultureInfo.InvariantCulture),
                voxelCount.ToString(CultureInfo.InvariantCulture),
                previewStep.ToString(CultureInfo.InvariantCulture),
                context.GpuPreviewMode ?? "",
                Bool(context.GpuDensityFieldPreview),
                Bool(context.WrapBoundaries),
                context.ResX.ToString(CultureInfo.InvariantCulture),
                context.ResY.ToString(CultureInfo.InvariantCulture),
                context.ResZ.ToString(CultureInfo.InvariantCulture),
                context.ActiveVoxels.ToString(CultureInfo.InvariantCulture),
                Bool(context.DenseVoxelGrid),
                context.DimensionMode ?? "",
                Format(context.Diffuse),
                context.DiffuseRange.ToString(CultureInfo.InvariantCulture),
                Format(context.Decay),
                Bool(context.AntParticles),
                context.DiffuseRangeAnt.ToString(CultureInfo.InvariantCulture),
                context.TrailSize.ToString(CultureInfo.InvariantCulture),
                context.TrailFreq.ToString(CultureInfo.InvariantCulture),
                Bool(context.DynPop),
                Bool(context.Division),
                Bool(context.Death),
                context.MaxIterations.ToString(CultureInfo.InvariantCulture),
                Format(totalMs),
                Format(settingsMs),
                Format(inputsMs),
                Format(senseMs),
                Format(moveMs),
                Format(trailMs),
                Format(diffuseMs),
                Format(parentMs),
                Format(populationMs),
                Format(outputsMs),
                Format(densitySyncMs),
                Format(setParticlesMs),
                Format(setVoxelsMs),
                Format(rebuildMs),
                Format(drawMs),
                Format(sensePrepareMs),
                Format(senseParticlesMs),
                Format(senseAntMs),
                Format(moveShuffleMs),
                Format(moveParticlesMs));
        }

        static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string Bool(bool value)
        {
            return value ? "1" : "0";
        }

        static void WriteRawLine(string line)
        {
            if (disabled) return;

            try
            {
                lock (fileLock)
                {
                    Directory.CreateDirectory(outputDirectory);

                    if (!headerWritten)
                    {
                        EnsureHeader();
                        headerWritten = true;
                    }

                    File.AppendAllText(outputPath, line + Environment.NewLine);
                }
            }
            catch
            {
                disabled = true;
            }
        }

        static void EnsureHeader()
        {
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                File.AppendAllText(outputPath, headerLine + Environment.NewLine);
                return;
            }

            string[] lines = File.ReadAllLines(outputPath);
            if (lines.Length == 0)
            {
                File.WriteAllText(outputPath, headerLine + Environment.NewLine);
                return;
            }

            if (lines[0] == headerLine) return;

            lines[0] = headerLine;
            File.WriteAllLines(outputPath, lines);
        }
    }
}
