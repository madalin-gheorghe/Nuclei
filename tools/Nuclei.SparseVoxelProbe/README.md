# Sparse voxel GPU probe

Headless D3D11 validation and GPU timestamp benchmark for inactive-voxel diffusion guards. It loads an existing Nuclei v4 deployment by reflection, refuses software fallback, and does not launch Rhino or modify a Grasshopper definition.

Build:

```powershell
dotnet build tools/Nuclei.SparseVoxelProbe/Nuclei.SparseVoxelProbe.csproj -c Release
```

Before rebuilding the plugin, copy its complete `net7.0-windows` output directory to an isolated baseline directory. Then record baseline snapshots in a separate process:

```powershell
dotnet tools/Nuclei.SparseVoxelProbe/bin/Release/net8.0-windows/Nuclei.SparseVoxelProbe.dll --nuclei-output .codex-temp/sparse-voxel-check/baseline --mode correctness --record-dir .codex-temp/sparse-voxel-check/reference
dotnet tools/Nuclei.SparseVoxelProbe/bin/Release/net8.0-windows/Nuclei.SparseVoxelProbe.dll --nuclei-output .codex-temp/sparse-voxel-check/baseline --mode transitions --record-dir .codex-temp/sparse-voxel-check/reference
```

After rebuilding, compare the new deployment against those exact snapshots:

```powershell
dotnet tools/Nuclei.SparseVoxelProbe/bin/Release/net8.0-windows/Nuclei.SparseVoxelProbe.dll --mode correctness --reference-dir .codex-temp/sparse-voxel-check/reference
dotnet tools/Nuclei.SparseVoxelProbe/bin/Release/net8.0-windows/Nuclei.SparseVoxelProbe.dll --mode transitions --reference-dir .codex-temp/sparse-voxel-check/reference
```

The 72 primary cases cover 3D and all three planes; partial edge tiles; sparse, full and empty selections; wrapped and fixed boundaries with live boundary changes; range 5 and maximum tiled range 16; direct diffusion with separate decay; and default tiled diffusion with fused decay. Initial density is deliberately nonzero even outside the selection. Minimum constraints and active voxels blocked by maximum-density constraints are included. Each case compares four steps of density, particle position/direction/axis, live count, and ownership bit for bit.

Eight additional cases shrink a live full selection to alternating voxels, expand it back, and switch to decay-only processing, with two steps in each state. They cover 3D and planar fields, both boundary modes, and optional ant pheromone channels.

Run timing in separate baseline and rebuilt processes while no other GPU checks are running:

```powershell
dotnet tools/Nuclei.SparseVoxelProbe/bin/Release/net8.0-windows/Nuclei.SparseVoxelProbe.dll --nuclei-output .codex-temp/sparse-voxel-check/baseline --mode performance --grid 350 --steps 6 --repeats 5 --json .codex-temp/sparse-voxel-check/baseline-performance.json
dotnet tools/Nuclei.SparseVoxelProbe/bin/Release/net8.0-windows/Nuclei.SparseVoxelProbe.dll --mode performance --grid 350 --steps 6 --repeats 5 --json .codex-temp/sparse-voxel-check/optimized-performance.json
```

Defaults are grid 350, four warmup steps, four steps per sample, and three samples. Optional `--selection full|10|25|function|all` chooses the selection. Percentage masks are contiguous X slabs rounded to an integer voxel width. The Function mask uses the same GPU selector and saved settings as `_Function Voxels.gh`: `Math.Sin(x*x) + Math.Sin(y*y) + Math.Sin(z*z)`, scale 3.15, iso 0, range 0..4, voxel size 1. It selects about 8.9% of the 350³ grid.

Timings use D3D11 timestamp queries after warmup, with one tiny particle readback fencing each batch outside the measured GPU range. Scalar settings are diffuse 0.15, range 5, decay 0.03; `--decay` can override the benchmark decay. Correctness fixtures retain decay 0.013. One stationary slime particle retains normal solver deposit/ownership overhead; preview and per-step CPU readback are disabled. These timings isolate solver work and do not measure a complete Grasshopper solution. GPU workload and clock variation can affect samples, so use multiple repeats. Six steps balance the rotating 3D diffusion-axis order.
