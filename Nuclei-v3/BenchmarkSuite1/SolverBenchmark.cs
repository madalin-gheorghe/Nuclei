using System;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Nuclei3;
using Rhino.Geometry;
using Microsoft.VSDiagnostics;
using NucleiParticle = Nuclei3.Particle;

[CPUUsageDiagnoser]
public class SolverBenchmark
{
    private Solver solver;
    private Voxel[,, ] inputVoxels;
    private double[] newVoxelDensity;
    [GlobalSetup]
    public void Setup()
    {
        // Create a small voxel grid representative of typical workload
        int resX = 20;
        int resY = 20;
        int resZ = 20;
        inputVoxels = new Voxel[resX, resY, resZ];
        for (int i = 0; i < resX; i++)
            for (int j = 0; j < resY; j++)
                for (int k = 0; k < resZ; k++)
                    inputVoxels[i, j, k] = new Voxel(1.0, i, j, k);
        solver = new Solver();
        // use reflection to set private fields used by the component
        var inputVoxelsField = typeof(Solver).GetField("inputVoxels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        inputVoxelsField.SetValue(solver, inputVoxels);
        // invoke inheritVoxels via reflection
        var inheritMethod = typeof(Solver).GetMethod("inheritVoxels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        inheritMethod.Invoke(solver, null);
        // prepare particles list empty
        var particlesField = typeof(Solver).GetField("particles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        particlesField.SetValue(solver, new System.Collections.Generic.List<NucleiParticle>());
        // set diffusion settings
        var diffuseField = typeof(Solver).GetField("diffuse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        diffuseField.SetValue(solver, 0.1);
        var reusableWeightsField = typeof(Solver).GetField("reusableWeights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var precompute = typeof(Solver).GetMethod("precomputeWeights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        reusableWeightsField.SetValue(solver, precompute.Invoke(solver, new object[] { 1, 1.0 }) as double[]);
        var activeVoxelsField = typeof(Solver).GetField("activeVoxels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var activeVoxels = activeVoxelsField.GetValue(solver) as Voxel[];
        newVoxelDensity = new double[activeVoxels.Length];
    }

    [Benchmark]
    public void Diffuse_xPass()
    {
        var xPassMethod = typeof(Solver).GetMethod("xPass", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var reusableWeightsField = typeof(Solver).GetField("reusableWeights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        double[] weights = (double[])reusableWeightsField.GetValue(solver);
        var result = (double[])xPassMethod.Invoke(solver, new object[] { newVoxelDensity, weights });
    }
}
