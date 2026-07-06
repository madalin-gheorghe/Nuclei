# Nuclei Performance History

This repository keeps the performance story in source-controlled summaries rather than raw profiler captures.

The raw Visual Studio `.diagsession` files are intentionally excluded from Git. In the local working folder they occupy about 2.77 GB across 45 captures, which would make the repository heavy and hard to review. Keep those files as external evidence or attach selected captures to GitHub Releases if needed.

## Reconstructed Milestones

- `v0.1-self-coded` - initial self-coded source reconstructed from `Nuclei 19 Apr Final.rar`.
- `v0.2-cpu-stabilized` - CPU solver behavior stabilized, including wrap/no-wrap and benchmark harness work.
- `v0.3-cpu-preview-optimized` - CPU diffusion and preview cache optimizations.
- `v0.4-gpu-solver` - first meaningful GPU solver and particle preview pipeline.
- `v0.5-current-collaboration` - current reconstructed collaboration state with fast voxel data work and internal voxel-field particle generation.

## Timing Notes

The generated timing file `BenchmarkSuite1/NucleiTiming.csv` is not committed because it is runtime output and its schema evolved during optimization. The small CSV in this folder records representative stable measurements from the later schema.

Representative current GPU run:

- Run: `20260624-165518`
- Solver: `solver_gpu`
- Workload: `300000` particles, `350 x 350 x 350` voxel field (`42875000` voxels)
- Median solver call: about `10.962 ms`
- Average solver call: about `15.474 ms`, with occasional spikes up to `149.81 ms`
- Particle preview median: about `0.019 ms`

These numbers are not a universal benchmark. They capture the specific Grasshopper/Rhino setup and parameters used in that run.
