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
- On-demand particle and voxel extraction while the solver is paused.
- GPU volume-to-mesh conversion with scalar-field and mesh smoothing controls.
- V4 is gated from loading as a normal plugin in Rhino 8.

## Validation

Both separated projects build for `net7.0-windows` without errors. V4's build
also compiles and embeds all 24 GPU shaders. Existing framework/package warnings
remain unchanged from the pre-separation projects.
