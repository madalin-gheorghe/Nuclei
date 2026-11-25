# V3 ant correctness and performance probe

Build V3 before the probe; always suppress the project's automatic installation:

```powershell
dotnet build Nuclei-v3/Nuclei3/Nuclei3.csproj -c Release -p:SkipGrasshopperInstall=true
dotnet build tools/Nuclei.V3AntProbe -c Release
dotnet tools/Nuclei.V3AntProbe/bin/Release/net8.0-windows/Nuclei.V3AntProbe.dll --json .codex-temp/v3-ants/regression.json
```

Behavior tests initialize an isolated Rhino 9 WIP runtime with no window or user
document. The local Rhino installation and license must be available. The field
benchmark does not initialize Rhino. Wait for the build to finish before running.

CPU checks use the compiled solver, with private methods invoked through
reflection. A separate full-grid Jacobi reference verifies 960 field fixtures
across 3D/XY/XZ/YZ, all six axis-order steps, independent rates, sparse fields,
obstacles, limits, boundaries and radii exceeding an axis length. Falloff 0, 0.4 and 1 are checked in both fields. Another 144 fixtures exercise the production SIMD path against that independent reference. Particle tests
check sensing, steering, source emission, pickup, return state, movement,
deposition, occupancy and wrapped/non-wrapped boundaries along every active axis.

For comparisons against the actual V4 hardware engine, build V4 and the existing
`tools/Nuclei.AntDiffusionProbe` first. This reuses that probe's fixture builder
and GPU loader, then compares V3 CPU fields with V4 D3D11 readbacks:

```powershell
dotnet tools/Nuclei.V3AntProbe/bin/Release/net8.0-windows/Nuclei.V3AntProbe.dll --gpu-parity --json .codex-temp/v3-ants/gpu-parity.json
```

Use `--v4-output <directory>` to select another built V4 output. These 288 fixtures
run eight steps each, including fixed food emission; they reject software GPU
fallback. Tolerance is 0.00002 because V3 uses doubles and V4 uses floats. They do
not compare random particle trajectories.

Save the baseline GHA before editing, then run each build in a separate process:

```powershell
dotnet tools/Nuclei.V3AntProbe/bin/Release/net8.0-windows/Nuclei.V3AntProbe.dll --benchmark --shape 3d --size 100 --assembly .codex-temp/v3-ants/before/Nuclei3.gha --json .codex-temp/v3-ants/before.json
dotnet tools/Nuclei.V3AntProbe/bin/Release/net8.0-windows/Nuclei.V3AntProbe.dll --benchmark --shape 3d --size 100 --json .codex-temp/v3-ants/after.json
```

Use `--shape xy --size 512` for the 2D benchmark. The timer measures completed
**food-source projection, diffusion and decay only**, after 12 warm-up steps, for
36 steps. It excludes particles and previews. Use paired fresh-process runs in
ABBA order, retain every sample, and report the median of process medians. Never
present this ratio as a complete simulation or V3/V4 throughput ratio.

The authored settings, aggregate timings and limitations are recorded in
[`docs/performance/README.md`](../../docs/performance/README.md).

The full and `--nest-only` runs also check 160 map-scaled trail deposits across individual home positions, speeds, both trail types and all planar/3D layouts. The 2% age floor retains the existing Base Pheromone food-presence factor.
`--nest-only` also checks 56 actual-food priorities across all planar/3D sensor directions, including stronger competing scent, depletion, blocked food and returning ants.
