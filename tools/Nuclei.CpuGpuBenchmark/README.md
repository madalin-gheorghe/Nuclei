# Matched CPU/GPU benchmark

Runs the current compiled V3 CPU and V4 D3D11 engines with a shared deterministic fixture. This is a solver-calculation benchmark, not a Rhino viewport or Grasshopper benchmark. Production sources are not modified.

Build both production projects in Release for `net7.0-windows` with `-p:SkipGrasshopperInstall=true`, then build this project and `tools/Nuclei.GpuResearchProbe` in Release. The GPU research probe supplies assembly loading only; this harness creates its own matching inputs.

```powershell
dotnet build tools/Nuclei.CpuGpuBenchmark -c Release
powershell -NoProfile -File tools/Nuclei.CpuGpuBenchmark/Run-Matrix.ps1 -OutputDirectory docs/performance/latest-cpu-gpu-20260913-validated
```

Requires Windows, Rhino 9 WIP's native runtime and a hardware D3D11 adapter. Both backends run in separate hidden Rhino processes; the user's open Rhino document is not used. The bootstrap defaults to the current V3 build, not the installed Grasshopper plugin.

## Workload and timing

- Full wrapping grids, voxel size 1; 500 × 500 × 1, 64³ and 128³.
- Slime: 25,000 / 1,572 / 12,582 particles respectively; speed 0.75 and sensor distance 3 for small networks, both doubled for large networks. Angles 45°, deposit 1, no wandering.
- Ants: separate 500 × 500 × 1 and 64³ cases, same particle counts and small-scale motion. All start searching, with a shared home at the domain center.
- Identical float-valued initial positions, axes, source maps and randomized chemical fields are generated for each backend. CPU uses doubles internally; GPU uses floats and normal parallel scheduling, so trajectories need not coincide.
- Eight source cells: slime emission 0.04, or 100 units of edible ant food. No obstacles or voxel overrides.
- Slime diffusion 0.1, decay 0.01, radius 1, gradual 1. Ant food/base diffusion 0.1, decay 0.0001/0.0005, radius 1, falloff 0. The unused scalar map is disabled for ant-only GPU fixtures, matching the current CPU's ant-only work.
- CPU uses its normal `Parallel.For` scheduling and production settings initialization. No serial-particle reproducibility override.
- Each fresh process runs 300 warmup steps followed by five timed batches of 30 steps (450 steps total). Two process rounds reverse CPU/GPU order. Report the median of all ten batch averages per backend.
- Stopwatch timings include sensing, movement, occupancy, deposits, source emission, diffusion and decay. GPU batches include a completion fence through active-count synchronization, rather than timing command submission alone. This reads a count once per batch; bulk field/particle readbacks happen only after timing for validation.
- Preview generation, trails, output conversion, dynamic population, startup and fixture construction are excluded.
- Every accepted run checks fixed population, finite nonempty fields and that at least half the particles have changed position. Results store input hashes, binary hashes, raw samples and field summaries. These are sanity checks, not a CPU/GPU behavioral-equivalence certification.

The first diagnostic directory `latest-cpu-gpu-20260913` is marked invalid: it exposed missing CPU settings initialization. Its timings must not be used. Only the corrected `-validated` suite is reportable.
