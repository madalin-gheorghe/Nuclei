using System.Runtime.InteropServices;
using System.Text;

using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: Nuclei.ShaderCompiler <GpuFullSlimeSolverEngine.cs> <GpuDensityFieldD3DRenderer.cs> <output-directory>");
    return 2;
}

string solverSourcePath = Path.GetFullPath(args[0]);
string rendererSourcePath = Path.GetFullPath(args[1]);
string outputDirectory = Path.GetFullPath(args[2]);
string solverShaderSource = ExtractVerbatimString(solverSourcePath, "FullSolverShaderSource");
string[] entryPoints =
{
    "ApplyBoundaryModeTransition",
    "MoveParticlesAndDeposit",
    "MoveAntParticlesAndDeposit",
    "ApplyDeposits",
    "ProjectFoodSources",
    "ClearParticleCounts",
    "CountParticles",
    "AdvanceParticleAges",
    "SeedNeighbourCounts",
    "SumNeighbourAxis",
    "ApplyParticleDeath",
    "ApplyParticleDivision",
    "DiffuseAxis",
    "ApplyDecay",
    "BuildDensityPreview",
    "BuildCombinedDensityPreview",
    "BuildDensityGradientPreview",
    "BuildParticlePreview",
    "BuildParticleTrailPreview",
    "SmoothVolumeForMesh",
    "ClassifyVolumeCells",
    "EmitVolumeTriangles"
};

Directory.CreateDirectory(outputDirectory);
foreach (string entryPoint in entryPoints)
{
    CompileToFile(solverShaderSource, entryPoint, "cs_5_0", Path.Combine(outputDirectory, entryPoint + ".cso"));
}

string rendererShaderSource = ExtractVerbatimString(rendererSourcePath, "ShaderSource");
CompileToFile(rendererShaderSource, "VSMain", "vs_4_0", Path.Combine(outputDirectory, "DensityPreviewVS.cso"));
CompileToFile(rendererShaderSource, "PSMain", "ps_5_0", Path.Combine(outputDirectory, "DensityPreviewPS.cso"));
CompileToFile(rendererShaderSource, "CSBuildOccupancy", "cs_5_0", Path.Combine(outputDirectory, "DensityPreviewOccupancy.cso"));
CompileToFile(rendererShaderSource, "CSBuildShadow", "cs_5_0", Path.Combine(outputDirectory, "DensityPreviewShadow.cso"));
CompileToFile(rendererShaderSource, "PSComposite", "ps_5_0", Path.Combine(outputDirectory, "DensityPreviewComposite.cso"));

File.WriteAllText(Path.Combine(outputDirectory, "shaders.complete"), DateTime.UtcNow.ToString("O"));
Console.WriteLine("Compiled " + (entryPoints.Length + 5) + " Nuclei GPU shaders.");
return 0;

static void CompileToFile(string source, string entryPoint, string profile, string outputPath)
{
    Blob? shaderBytecode = null;
    Blob? errorBlob = null;
    Result result = Compiler.Compile(
        source,
        null!,
        null!,
        entryPoint,
        "NucleiGpuFullSlimeSolver",
        profile,
        ShaderFlags.OptimizationLevel3,
        EffectFlags.None,
        out shaderBytecode,
        out errorBlob);

    try
    {
        if (result.Failure || shaderBytecode == null)
        {
            throw new InvalidOperationException(entryPoint + " failed: " + BlobToString(errorBlob));
        }

        byte[] bytes = new byte[(int)shaderBytecode.BufferSize];
        Marshal.Copy(shaderBytecode.BufferPointer, bytes, 0, bytes.Length);
        File.WriteAllBytes(outputPath, bytes);
    }
    finally
    {
        shaderBytecode?.Dispose();
        errorBlob?.Dispose();
    }
}

static string ExtractVerbatimString(string path, string fieldName)
{
    string text = File.ReadAllText(path);
    int fieldIndex = text.IndexOf(fieldName, StringComparison.Ordinal);
    int start = fieldIndex >= 0 ? text.IndexOf("@\"", fieldIndex, StringComparison.Ordinal) : -1;
    if (start < 0)
    {
        throw new InvalidOperationException("Could not find " + fieldName + " in " + path);
    }

    start += 2;
    StringBuilder builder = new StringBuilder();
    for (int i = start; i < text.Length; i++)
    {
        char character = text[i];
        if (character == '"')
        {
            if (i + 1 < text.Length && text[i + 1] == '"')
            {
                builder.Append('"');
                i++;
                continue;
            }

            return builder.ToString();
        }

        builder.Append(character);
    }

    throw new InvalidOperationException("Unterminated shader source in " + path);
}

static string BlobToString(Blob? blob)
{
    if (blob == null || blob.BufferPointer == IntPtr.Zero || blob.BufferSize <= 0)
    {
        return string.Empty;
    }

    byte[] bytes = new byte[(int)blob.BufferSize];
    Marshal.Copy(blob.BufferPointer, bytes, 0, bytes.Length);
    return Encoding.ASCII.GetString(bytes).Trim('\0', '\r', '\n', ' ');
}
