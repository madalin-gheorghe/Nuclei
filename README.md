# Nuclei

Nuclei is an original Grasshopper/Rhino plugin for bottom-up generative systems,
behavior-based simulations, and voxel-controlled spatial maps.

The plugin combines particle simulation with highly customizable voxel
environments, allowing movement and behavior to respond to spatial maps. Its
slime-mold logic is inspired by
[Physarum transport-network research](https://uwe-repository.worktribe.com/output/980579/characteristics-of-pattern-formation-and-evolution-in-approximations-of-physarum-transport-networks)
and expanded toward computational design, speculative urban systems, and
generative spatial workflows.

Stable public releases are published on
[Food4Rhino](https://www.food4rhino.com/en/app/nuclei).

## Source Layout

- `Nuclei-v3/Nuclei-v3.sln` - V3.3 CPU implementation for Rhino 8.
- `Nuclei-v4/Nuclei-v4.sln` - V4.1 GPU implementation for Rhino 9 on Windows.
- `docs` - milestone, architecture, behavior-parity, and performance notes.
- `tools` - shared repository verification and maintenance utilities.

The V3 and V4 codebases deliberately retain different assembly and component GUID
families. They can be developed independently without Grasshopper definitions
silently changing component identity.

## Current Checkpoints

### V3.3 CPU

V3.x is the CPU behavioral reference. It includes internal particle generation,
scalar-array solver paths, wrap/no-wrap behavior, balanced diffusion, ant and slime
systems, CPU previews, and the Nuclei-to-Dendro bridge. It contains no GPU solver.

### V4.1 GPU

V4.x targets Rhino 9 on Windows. It includes the Direct3D 11 compute-shader solver,
adaptive large voxel fields, GPU-resident dynamic maps and previews, ordered trails,
dynamic populations, ant food and pheromone behavior, reliable hard resets, live
wrap changes, on-demand paused-state extraction, and GPU volume-to-mesh conversion
with scalar and mesh smoothing.

The CPU V3 implementation remains the reference whenever behavior is translated to
the GPU. Known differences are documented in
[CPU to GPU Behavior Parity](docs/GPU_BEHAVIOR_PARITY.md).
The latest compatibility and feature checkpoint is summarized in
[Development Status](docs/DEVELOPMENT_STATUS.md).

## Performance Evidence

The detailed optimization history is recorded in
[Performance History](docs/performance/performance-history.md). The concise
[Solver Frame Comparison](docs/performance/solver-frame-comparison.md) compares
representative CPU and GPU median milliseconds per frame and speedup ratios.

Raw Visual Studio profiler captures remain outside Git because the diagnostic set
is several gigabytes. Small CSV summaries and representative timings are retained
in the repository.

## Milestones

- `v3.0` - stable self-coded baseline, hand-coded by Madalin Gheorghe.
- `v3.1` - CPU solver stabilization and structured performance measurement.
- `v3.2` - CPU diffusion and preview optimization.
- `v3.3` - current CPU compatibility and behavioral-reference checkpoint.
- `v4.0` - first meaningful GPU solver and GPU preview architecture.
- `v4.1` - current GPU speed and main-functionality checkpoint.

After V3.0, AI augmentation with ChatGPT in Codex was used to accelerate profiling,
performance exploration, GPU translation, testing, and documentation. The plugin's
original behavior-based simulation concepts and voxel-controlled spatial-map
architecture were developed by Madalin Gheorghe.
