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
        }

        static readonly object fileLock = new object();
        static readonly string outputDirectory = @"C:\Nuclei\BenchmarkSuite1";
        static readonly string outputPath = Path.Combine(outputDirectory, "NucleiTiming.csv");

        static bool disabled = false;
        static bool headerWritten = false;
        static string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        public static void StartRun()
        {
            runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            WriteRawLine(CreateLine("run_start", 0, 0, 0, 0, 0, new SolverContext(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
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
            double outputsMs)
        {
            WriteRawLine(CreateLine("solver", iteration, samples, particleCount, voxelCount, 0, context, totalMs, settingsMs, inputsMs, senseMs, moveMs, trailMs, diffuseMs, parentMs, populationMs, outputsMs, 0, 0));
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
            WriteRawLine(CreateLine("preview_particle", call, samples, particleCount, 0, previewStep, new SolverContext(), totalMs, 0, 0, 0, 0, 0, 0, 0, 0, 0, rebuildMs, drawMs));
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
            double rebuildMs,
            double drawMs)
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
                Format(rebuildMs),
                Format(drawMs));
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
                        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                        {
                            File.AppendAllText(outputPath, "timestamp,run_id,component,iteration_or_call,samples,particles,voxels,preview_step,wrap_boundaries,res_x,res_y,res_z,active_voxels,dense_voxel_grid,dimension_mode,diffuse,diffuse_range,decay,ant_particles,ant_diffuse_range,trail_size,trail_freq,dyn_pop,division,death,max_iterations,total_ms,settings_ms,inputs_ms,sense_ms,move_ms,trail_ms,diffuse_ms,parent_ms,population_ms,outputs_ms,rebuild_ms,draw_ms" + Environment.NewLine);
                        }

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
    }
}
