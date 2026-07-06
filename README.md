# Nuclei

Nuclei is an original Grasshopper/Rhino plugin for bottom-up generative systems, behaviour-based simulations, and voxel-controlled spatial maps.

The plugin combines a simulation core with tools for defining particle behaviours and highly customizable voxel environments, allowing particles to adapt their movement through spatial behavior maps. Its slime-mold logic is inspired by [Physarum transport-network research](https://uwe-repository.worktribe.com/output/980579/characteristics-of-pattern-formation-and-evolution-in-approximations-of-physarum-transport-networks) and expanded toward computational design, speculative urban systems, and generative spatial workflows.

Stable public releases will be published on Food4Rhino:

[Nuclei on Food4Rhino](https://www.food4rhino.com/en/app/nuclei)

This repository tracks the source code, reconstructed optimization history, and small performance evidence summaries. It does not track local build products or multi-gigabyte profiler captures.

## Milestones

- `v0.1-self-coded` - initial self-coded source reconstructed from `Nuclei 19 Apr Final.rar`.
- `v0.2-cpu-stabilized` - CPU solver behavior stabilized, including wrap/no-wrap fixes and benchmark harness work.
- `v0.3-cpu-preview-optimized` - CPU diffusion and preview cache optimization period.
- `v0.4-gpu-solver` - first meaningful GPU solver and particle preview pipeline.
- `v0.5-current-collaboration` - current reconstructed collaboration state with fast voxel data work and internal voxel-field particle generation.

## Performance Evidence

The detailed performance notes live in [docs/performance/performance-history.md](docs/performance/performance-history.md).

For the easiest overview, start with [docs/performance/solver-frame-comparison.md](docs/performance/solver-frame-comparison.md). It compares CPU and GPU median ms/frame and speedup ratios.

Local Visual Studio `.diagsession` captures are kept outside Git because the current diagnostic set is about 2.77 GB. The repository instead includes small CSV summaries for the diagnostic inventory, decoded CPU hot-frame samples, and representative GPU timing runs.

## Build

The plugin project is `Nuclei/Nuclei3.csproj` in `Nuclei.sln`.

For CI-style validation without installing into Grasshopper:

```powershell
dotnet build .\Nuclei\Nuclei3.csproj -c Debug -f net48 -p:SkipGrasshopperInstall=true
```
