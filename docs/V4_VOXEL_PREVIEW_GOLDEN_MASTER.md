# V4 Voxel Preview Golden Master

This is the final, user-approved voxel preview appearance. The new architecture
must reproduce it exactly. Do not restyle, simplify, enhance, tune, or otherwise
change its appearance without explicit user approval.

## Authoritative implementation

- Source snapshot: `C:\Nuclei\.codex-backups\component-nicknames-before-20260817-075815\Nuclei-v4\Nuclei4`
- Installed assembly: `Nuclei4, Version=4.1.0.0`
- Installed `Nuclei4.gha` SHA-256 after the approved build:
  `A8A86CFC99C698EC450F4C2016CBC93CD8A65A6A87EBCEDF941A86E323DC8A0E`
- Visual reference: [right-voxel-preview.png](reference/right-voxel-preview.png)
- Visual reference SHA-256:
  `D545F32132E57604EE06237F0A779CE4404F224DF5C5D6C6DBD7DF75E461F0D1`

The assembly hash records this exact build, but source and shader parity are the
primary gates because a managed assembly can contain nondeterministic metadata.

## Locked source files

| File | SHA-256 |
| --- | --- |
| `Preview_Voxel.cs` | `A042B8B39175ECB7DDAAF79B12661F9A051A1904D2A6843F61D9772D38D08387` |
| `GpuDensityFieldD3DRenderer.cs` | `2858877AB75E9DDFBDB1006C988E2CEBA4D73876BC97F7146AB90441C3A61F5E` |
| `GpuFullSlimeSolverEngine.cs` | `2E8A9CB6919272BB28BCAB1DFC0892219FEFCB265DBA052016E3324C6EAF1F40` |
| `GpuDensityFieldPreviewFrame.cs` | `D4B481CF07EB358489612A9B4F1F187229DEC9C507997486D91B6CCF30989FD0` |

These files may be moved into new projects during the architecture split, but
their voxel-preview behavior and values must remain equivalent.

## Locked appearance contract

- 3D slime uses renderer version 2 when its gradient texture is available.
- `FancyRender` remains `false`.
- Volume opacity remains `0.8`.
- Volume contrast and preview scale remain `1.5`.
- Volume sample count remains automatic (`0`); the renderer selects
  `min(256, max(64, ceil(maxResolution * 1.5)))`.
- Preserve the exact shader code and compiled shader behavior, field/channel
  packing, gradient generation, transfer function and LUT, palette, thresholds,
  trilinear sampling, occupancy skipping, lighting, shadows, tone mapping,
  camera-ray construction, depth state, blend state, and compositing.
- Preserve `Preview_Voxel` field routing and custom-color behavior.
- Preserve GPU frame creation, texture formats, atlas layout, constants, and
  synchronization timing; a visually similar rewrite is not sufficient.

## Refactor rule

Architecture work may change assembly boundaries and adapters only. Before any
voxel-preview-related change is accepted, compare the rendered result in Rhino
against the golden image using the same Grasshopper definition, inputs, camera,
viewport mode, resolution, and simulation frame. Any visible difference is a
regression unless the user explicitly requests it.
