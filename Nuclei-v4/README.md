# Nuclei V4.2

Nuclei V4.2 is the GPU implementation for Rhino 9 on Windows. It keeps the V3
behavioral model while moving the solver, dynamic voxel fields, particle display,
trail display, and volumetric display toward GPU-resident execution.

V4.2 is a major speed and feature milestone. It adds the GPU Image Mapper,
periodic-surface voxel generation, expanded voxel attractor controls, scalable 2D
texture previews, a new volumetric renderer, improved volume-to-mesh conversion,
and faster slime, ant, preview, diffusion, and deposit paths. Matched tests found
V4 faster than V3 across all eight recorded CPU/GPU workloads; see the
[performance summary](../docs/performance/README.md).

The current checkpoint includes adaptive voxel-field storage for large grids,
hard solver resets, live wrap updates, scattered particle initialization, ant and
slime behavior, on-demand paused-state extraction, GPU volume-to-mesh conversion,
and configurable scalar and mesh smoothing.
With Update held true, the Dendro bridge rebuilds for every incoming solver
update; when false, it retains the last successful output and stops solver ticks
from recomputing the bridge or its downstream components.
The GPU particle field enforces one live particle per voxel through atomic owner
claims. Occupied-target moves stay in place without depositing, then select a new
heading; initialization and division also respect the same exclusive occupancy.

At Max Iterations, the solver pauses any automatic Grasshopper Timer/Trigger
targeting only that solver, preventing continued downstream preview and extraction
updates. The result stays available. Reset or raise the limit and re-enable the
trigger to run again. Shared and manual triggers keep their existing behavior.
Deleted target references saved inside a trigger do not prevent it from pausing.

## Image Mapper for Voxels

2D **Voxel Preview** displays one texture pixel per voxel, including 3000 × 3000
fields. Larger fields use seamless texture tiles instead of a reduced vertex-colour
mesh. Its message shows the actual texture resolution. Textures are cached between
unchanged solves and released when replaced, disconnected or the definition closes.

Double-click **Image Mapper for Voxels** to choose a PNG, JPEG, BMP, GIF or TIFF.
The image is embedded in the Grasshopper document and displayed on the component.
Drag the bottom-right corner grip to resize the component. The image keeps its
aspect ratio, and the size is saved with the definition. Resizing supports undo/redo
and redraws the canvas without recomputing voxel values.
Connect a planar voxel field, choose the same **Type** used by Define Voxel Values,
and set **targetStart** and **targetEnd**. Black maps to the start, white to the end;
color images use luminance (0.2126 R + 0.7152 G + 0.0722 B). Transparency is
composited on white, matching the canvas preview. Animated images use their first frame.

Both ascending and descending ranges work: `0.2 → 0.4` and `0.4 → 0.2`.
Equal endpoints produce a constant map. Minimum/Maximum Density retain the
existing Define Voxel Values limits (0–1, plus the -1 sentinel).
The output is a voxel field with the selected property defined; other maps and
the active voxel selection are preserved.

Mapping uses the full voxel domain normalized to 0–1, bilinear sampling at voxel
centers, and a bottom-left origin. XY, XZ and YZ fields are supported. Different
aspect ratios stretch the mapping and show a bottom message while the component
continues running normally. 3D fields turn the component red, report
**Only 2D Voxel Field Allowed**, and produce no output.

The D3D11 backend computes sampling, grayscale conversion and remapping together.
It reuses image/buffer resources and caches unchanged results. A single float-array
readback per changed map supplies the existing voxel-field API, avoiding per-voxel
Grasshopper numbers. If hardware initialization fails, software fallback is explicitly
reported. The canvas draws a cached thumbnail rather than resampling the full image.

## Build

Open `Nuclei-v4.sln`, or validate without installing into Grasshopper:

```powershell
dotnet build .\Nuclei4\Nuclei4.csproj -c Release -f net7.0-windows -p:SkipGrasshopperInstall=true
```

The source namespace and assembly identity are both `Nuclei4`. The assembly
output remains `Nuclei4.gha`, and the V4 component identity is unchanged.
