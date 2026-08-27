# Development Status

## Compatibility

| Version | Runtime | Solver | Plugin output | Solution |
| --- | --- | --- | --- | --- |
| V3.3 | Rhino 8 | CPU | `Nuclei3.gha` | `Nuclei-v3/Nuclei-v3.sln` |
| V4.1 | Rhino 9 on Windows | Direct3D 11 GPU | `Nuclei4.gha` | `Nuclei-v4/Nuclei-v4.sln` |

V3 and V4 use separate library and component GUID families. Moving the projects
into separate folders did not change any source GUID literal.

## V3.3 CPU Checkpoint

- CPU behavioral reference for GPU feature translations.
- Internal CPU particle generation from voxel fields.
- Scalar-array solver paths and balanced diffusion pass ordering.
- Slime and ant behavior, including food and pheromone fields.
- CPU particle and voxel previews.
- Nuclei-to-Dendro volume bridge.
- No GPU solver or Direct3D trail renderer is exposed.

## V4.1 GPU Checkpoint

- Direct3D 11 compute-shader particle solver and dynamic voxel fields.
- Adaptive voxel architecture tested with substantially larger 3D grids.
- Live wrap changes, reliable hard reset, and scattered particle initialization.
- Dynamic populations and GPU ant food/pheromone behavior.
- GPU particle, trail, and volumetric voxel previews.
- On-demand particle and voxel extraction and preview refresh while the solver is paused.
- Hidden trail previews retain an ordered two-sample GPU history without CPU readback.
- Particle, trail, and planar voxel preview layers preserve independent display state.
- GPU volume-to-mesh conversion with scalar-field and mesh smoothing controls.
- Component names and nicknames are normalized across the V3 and V4 toolsets.
- V4 is gated from loading as a normal plugin in Rhino 8.

## Current Working Checkpoint

### Shared behaviour work

- **Gradual diffusion.** Voxel Settings Slime gains a `Gradual` input (0 to 1,
  default 1). 1 is the original raised-cosine kernel; 0 is V2-style immediate
  averaging. Retention is applied only on the final diffusion axis so multi-axis
  passes do not compound it.
- **Slime and ant food are separate maps.** `Voxel.antFood` is a new scalar field at
  index 13. Value lists offer "Slime Food" and "Ant Food", and existing definitions
  are retrofitted in place by `VoxelFoodValueList`.
- **Slime food is a persistent source.** It is projected into the chemoattractant
  field before diffusion each step, so it diffuses and decays normally. Ant food
  keeps the consumable behaviour.
- **Random population.** Particle Settings Population gains Random Division, Random
  Death and Frequency: independent per-particle probabilities applied alongside the
  neighbour rules, budget-clamped by the minimum and maximum population.

### V3.3 CPU

Also rewritten: particle seeding now scatters within voxels and skips blocked
regions, and boundary recovery no longer kills particles at the grid edge under
dynamic population.

### V4.1 GPU

- The legacy CPU `Solver` component was removed; V4 is GPU-only and `SolverGPU` is
  the sole solver. This is a breaking identity change, recorded in the preservation
  contract.
- The V3 behaviour work above is ported to the D3D11 backend.
- Volumetric preview samples 128 rays per pixel by default, with a **High
  Resolution** right-click toggle on Voxel Preview raising it to 256.

### Known open

Division rate parity between V3 and V4 is not yet closed. Death agrees exactly;
division diverges when the neighbour band is narrow and converges to within 0.3%
when the band is wide. See `GPU_BEHAVIOR_PARITY.md`.

### Tooling

`tools/Nuclei.ArchitectureProbe` gained two headless modes: `--benchmark` for GPU
solver timing and `--parity` for running the V3 CPU solver and the V4 GPU solver side
by side on identical settings strings. Both toolsets' particle preview caches are now
allocated lazily so neither requires a Rhino host to construct.
See `docs/performance/gpu-benchmark.md`.

## Validation

Both separated projects build for `net7.0-windows` without errors. V4's build
also compiles and embeds all 24 GPU shaders. Existing framework/package warnings
remain unchanged from the pre-separation projects.
