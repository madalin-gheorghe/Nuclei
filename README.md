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
- `Nuclei Definitions/v3` - canonical V3.3 Grasshopper examples.
- `Nuclei Definitions/v4_updated` - structurally verified V4 conversions of those examples.
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
Slime groups support persisted Classic and Probabilistic steering, including the
connected weighted-sensor behavior used as the V4 parity reference. Trail Settings
now exposes only Trail Size while safely reading the retired frequency schema, and
Voxel Settings Slime migrates legacy input layouts to Diffuse Rate, Decay Rate,
Falloff, and Diffuse Range.

### V4.1 GPU

V4.x targets Rhino 9 on Windows. It includes the Direct3D 11 compute-shader solver,
adaptive large voxel fields, GPU-resident dynamic maps and previews, ordered trails,
dynamic populations, ant food and pheromone behavior, reliable hard resets, live
wrap changes, on-demand paused-state extraction and preview refresh, and GPU
volume-to-mesh conversion with scalar and mesh smoothing. Its trail preview keeps
the latest ordered GPU segment current while hidden, so enabling the preview on a
paused solver does not connect stale particle positions.

The final V3 parity pass adds matching connected steering, non-wrapped boundary
and blocked-parent behavior, species-aware density processing, ant home and launch
state, V3-compatible ageing and dynamic-population ordering, nonblocking population
readback, and a dedicated ant movement dispatch. Dendro output is published on an
Update rising edge and caches the last successful volume between updates.

V4 source types now use the `Nuclei4` namespace while preserving the existing
`Nuclei4.gha` assembly name and all Grasshopper component identities.

V4.1 is now separated into a GH1 compatibility adapter, platform-neutral Core,
GPU and display contracts, and concrete Direct3D 11 compute/display backends.
This keeps the current Windows implementation and hot GPU path intact while
leaving explicit extension points for a future GPU-only Grasshopper 2 adapter
and macOS Metal backends. GH2 and Metal are design targets only and are not
shipped yet. See [V4 Architecture](docs/V4_ARCHITECTURE.md) and the executable
[V4 Preservation Contract](docs/V4_PRESERVATION_CONTRACT.md).

The CPU V3 implementation remains the reference whenever behavior is translated to
the GPU. Known differences are documented in
[CPU to GPU Behavior Parity](docs/GPU_BEHAVIOR_PARITY.md).
The latest compatibility and feature checkpoint is summarized in
[Development Status](docs/DEVELOPMENT_STATUS.md).

## Definition Conversion and Validation

The fail-closed definition converter under `tools/Nuclei.DefinitionConverter`
maps the canonical V3.3 examples to V4 component identities while preserving
object IDs, wire endpoints, and persistent data outside documented schema
adapters. It also migrates the retired Trail Frequency input without editing the
source definitions.

The converted 14-definition set in `Nuclei Definitions/v4_updated` includes its
conversion manifest. The Rhino 9 validation workflow loads and reopens every
definition against an exact V4 binary hash, rejects missing objects or V3 residue,
and exercises the saved GPU-to-Dendro-to-mesh path. Machine-specific validation
reports remain local rather than becoming repository inputs.
The isolated net8 and net48 hosts and validator scripts live under
`tools/Nuclei.DefinitionValidationHost` and `tools/Nuclei.DefinitionValidator`.

The repository-level `global.json` pins the verification toolchain to .NET SDK
8.0.418 with latest-patch roll-forward.

## Performance Evidence

The detailed optimization history is recorded in
[Performance History](docs/performance/performance-history.md). The concise
[Solver Frame Comparison](docs/performance/solver-frame-comparison.md) compares
representative CPU and GPU median milliseconds per frame and speedup ratios.

Raw Visual Studio profiler captures remain outside Git because the diagnostic set
is several gigabytes. Small CSV summaries and representative timings are retained
in the repository.

The V4 architecture split was also benchmarked against its immutable pre-split
build on the same machine and sustained synchronized GPU workload (262,144
particles and 262,144 voxels). The median-of-medians changed from 3.530 ms to
3.562 ms per step (+0.91%), well inside the 5% preservation gate.

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
