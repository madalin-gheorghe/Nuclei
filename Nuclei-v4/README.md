# Nuclei V4.x

Nuclei V4.1 is the GPU implementation for Rhino 9 on Windows. It keeps the V3
behavioral model while moving the solver, dynamic voxel fields, particle display,
trail display, and volumetric display toward GPU-resident execution.

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

## Build

Open `Nuclei-v4.sln`, or validate without installing into Grasshopper:

```powershell
dotnet build .\Nuclei4\Nuclei4.csproj -c Release -f net7.0-windows -p:SkipGrasshopperInstall=true
```

The source namespace and assembly identity are both `Nuclei4`. The assembly
output remains `Nuclei4.gha`, and the V4 component identity is unchanged.
