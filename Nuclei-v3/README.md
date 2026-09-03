# Nuclei V3.x

Nuclei V3.3 is the CPU implementation for Rhino 8 and the behavioral reference
for GPU translations. Its assembly and component GUID family are intentionally
separate from both the original V3.0 plugin and Nuclei V4.

Current features include CPU particle generation inside voxel fields, scalar-array
solver paths, wrap/no-wrap behavior, balanced diffusion passes, ant and slime
behavior, static and dynamic voxel previews, and the Nuclei-to-Dendro bridge.
With Convert held true, the Dendro bridge rebuilds for every incoming solver
update; when false, it retains the last successful volume.
Particle occupancy is exclusive: initialization is sampled without replacement,
movement and division atomically claim empty voxels, and blocked moves stay in
place without depositing before choosing a new heading.
GPU solver and Direct3D components are not included in V3.x. The V3 source and
deployment are also free of the dormant GPU engines and Vortice dependencies.

## Build

Open `Nuclei-v3.sln`, or validate without installing into Grasshopper:

```powershell
dotnet build .\Nuclei3\Nuclei3.csproj -c Release -f net7.0-windows -p:SkipGrasshopperInstall=true
```
