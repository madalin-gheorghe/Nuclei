# Nuclei V4.x

Nuclei V4.1 is the GPU implementation for Rhino 9 on Windows. It keeps the V3
behavioral model while moving the solver, dynamic voxel fields, particle display,
trail display, and volumetric display toward GPU-resident execution.

The current checkpoint includes adaptive voxel-field storage for large grids,
hard solver resets, live wrap updates, scattered particle initialization, ant and
slime behavior, on-demand paused-state extraction, GPU volume-to-mesh conversion,
and configurable scalar and mesh smoothing.

## Build

Open `Nuclei-v4.sln`, or validate without installing into Grasshopper:

```powershell
dotnet build .\Nuclei4\Nuclei4.csproj -c Release -f net7.0-windows -p:SkipGrasshopperInstall=true
```

The source namespace remains `Nuclei3` for embedded-resource compatibility. The
assembly output is `Nuclei4.gha`; this is intentional and does not affect the V4
component identity.
