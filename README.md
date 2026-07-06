# Nuclei

Nuclei is a Grasshopper/Rhino plugin for particle-based voxel simulations, with CPU and GPU solver work reconstructed into a Git history from the original self-coded project and later optimization milestones.

This repository intentionally tracks source code and small evidence summaries, not local build products or multi-gigabyte profiler captures.

## Milestones

- `v0.1-self-coded` - initial self-coded source reconstructed from `Nuclei 19 Apr Final.rar`.
- `v0.2-cpu-stabilized` - CPU solver behavior stabilized, including wrap/no-wrap fixes and benchmark harness work.
- `v0.3-cpu-preview-optimized` - CPU diffusion and preview cache optimization period.
- `v0.4-gpu-solver` - first meaningful GPU solver and particle preview pipeline.
- `v0.5-current-collaboration` - current reconstructed collaboration state with fast voxel data work and internal voxel-field particle generation.

## Performance Evidence

The detailed performance notes live in [docs/performance/performance-history.md](docs/performance/performance-history.md).

Local Visual Studio `.diagsession` captures are kept outside Git because the current diagnostic set is about 2.77 GB. The repository instead includes small CSV summaries for the diagnostic inventory, decoded CPU hot-frame samples, and representative GPU timing runs.

## Build

The plugin project is `Nuclei/Nuclei3.csproj` in `Nuclei.sln`.

For CI-style validation without installing into Grasshopper:

```powershell
dotnet build .\Nuclei\Nuclei3.csproj -c Debug -f net48 -p:SkipGrasshopperInstall=true
```

