# Voxel obstacle and food overlay probe

Runs geometry and component cache checks against a built Nuclei4 assembly in the existing isolated Rhino 9 host. Synthetic inputs cover XY/XZ/YZ, lines/points, active masks, zero and near-zero obstacles, unset and positive walkable values, uniform maps, 3D point sampling, field switching, and disposal. The probe checks that live density readback is unnecessary. It does not edit installed files or user definitions.

```powershell
dotnet build C:\Nuclei\tools\Nuclei.VoxelObstaclePreviewProbe\Nuclei.VoxelObstaclePreviewProbe.csproj -c Release -m:1 -nr:false
dotnet C:\Nuclei\tools\Nuclei.VoxelObstaclePreviewProbe\bin\Release\net8.0-windows\Nuclei.VoxelObstaclePreviewProbe.dll C:\Nuclei\Nuclei-v4\Nuclei4\bin\Release\net7.0-windows\Nuclei4.gha C:\Nuclei\.codex-temp\voxel-obstacles-new-run --run
```

Use a new scratch output directory; the shared bootstrap rejects existing directories and requires `--run`. The output contains assertion counts, case names, and the tested GHA SHA256. Rhino license initialization must succeed before the checks can run.

Food checks cover all planar orientations and 3D, active cells, positive amounts below signal thresholds, white material on both sides, CPU food depletion, GPU authored-food cache reuse without readback, selected preview types, and disposal.

Append `--capture` to use hidden viewport rendering and capture food deliberately placed behind a purple obstacle mesh and black voxel texture. This saves `cpu-planar-food-obstacles.png` and checks that the food remains white on top while the surrounding obstacle and field remain visible.
