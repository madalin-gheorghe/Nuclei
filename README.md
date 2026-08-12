# Nuclei

Nuclei is an original Grasshopper/Rhino plugin for bottom-up generative systems, behaviour-based simulations, and voxel-controlled spatial maps.

The plugin combines a simulation core with tools for defining particle behaviours and highly customizable voxel environments, allowing particles to adapt their movement through spatial behavior maps. Its slime-mold logic is inspired by [Physarum transport-network research](https://uwe-repository.worktribe.com/output/980579/characteristics-of-pattern-formation-and-evolution-in-approximations-of-physarum-transport-networks) and expanded toward computational design, speculative urban systems, and generative spatial workflows.

Stable public releases will be published on Food4Rhino:

[Nuclei on Food4Rhino](https://www.food4rhino.com/en/app/nuclei)

This repository tracks the source code, reconstructed optimization history, and small performance evidence summaries. It does not track local build products or multi-gigabyte profiler captures.

## Milestones

- `v3.0` - self-coded baseline hand-coded by Madalin Gheorghe; the initial stable Grasshopper/Rhino plugin before AI-assisted optimization work.
- `v3.1` - CPU solver stabilization, wrap/no-wrap behavior fixes, and first structured benchmark workflow.
- `v3.2` - CPU preview and diffusion optimization, separating solver and preview costs and clarifying the limits of CPU-side speedups.
- `v4.0` - first meaningful GPU solver prototype using compute shader based execution and GPU-resident preview work.
- `v4.1` - collaboration checkpoint for speed and main-functionality testing, with fast voxel data work, GPU solver progress, and internal voxel field particle generation.

## Current Development Backups

- [`main`](../../tree/main) and [`v4.x`](../../tree/v4.x) contain the current V4 GPU development checkpoint for Rhino 9 on Windows.
- [`v3.x`](../../tree/v3.x) contains the current V3.3 CPU checkpoint for Rhino 8 compatibility and behavior comparison.

The current V4 checkpoint includes the Direct3D 11 compute-shader solver, GPU-resident particle and voxel previews, ordered GPU particle trails, live voxel behavior maps, internal particle generation, dynamic particle division and death, and ant food/pheromone behavior. The V3.x CPU solver remains the behavioral reference when features are translated to the GPU. Known implementation differences are documented in [CPU to GPU Behavior Parity](docs/GPU_BEHAVIOR_PARITY.md).

## Performance Evidence

The detailed performance notes live in [docs/performance/performance-history.md](docs/performance/performance-history.md).

For the easiest overview, start with [docs/performance/solver-frame-comparison.md](docs/performance/solver-frame-comparison.md). It compares CPU and GPU median ms/frame and speedup ratios.

Local Visual Studio `.diagsession` captures are kept outside Git because the current diagnostic set is about 2.77 GB. The repository instead includes small CSV summaries for the diagnostic inventory, decoded CPU hot-frame samples, and representative GPU timing runs.

## Build

The plugin project is `Nuclei/Nuclei3.csproj` in `Nuclei.sln`. On `main`, it builds the V4 plugin for Rhino 9 on Windows.

For CI-style validation without installing into Grasshopper:

```powershell
dotnet build .\Nuclei\Nuclei3.csproj -c Debug -f net48 -p:SkipGrasshopperInstall=true
```
