# Image mapper verification

The isolated Rhino 9 host loads the specified build without installing it. It checks
GPU results against an independent CPU reference, all three planes, grayscale,
bilinear interpolation, ascending/descending/constant remaps, density limits,
transparency, cache invalidation, sparse fields, warnings, GH archive roundtrips,
removal/re-add resource cleanup, and canvas renders. It prints a warm one-million-voxel
GPU mapping/readback median and cached Grasshopper solve timing.
Resize checks cover the corner grip at multiple zoom levels, minimum size,
unchanged mapping during dragging, undo/redo, saved dimensions, and old definitions.

```powershell
dotnet build tools/Nuclei.ImageMapperProbe -c Release -m:1
dotnet tools/Nuclei.ImageMapperProbe/bin/Release/net8.0-windows/Nuclei.ImageMapperProbe.dll C:/Nuclei/Nuclei-v4/Nuclei4/bin/Release/net7.0-windows/Nuclei4.gha C:/Nuclei/.codex-temp/image-mapper-check --run
```

Use a new scratch output directory for every run. The host creates only synthetic
documents and never opens user definitions. `passed.txt`, embedded GH fixtures,
and PNG canvas captures are written under that directory.

Add `--image-fidelity <image-path>` to compare every pixel of an image with a
matching-resolution mapped voxel field and verify full-resolution Voxel Preview texture tiles.
This mode writes `image-fidelity.json`, including mapping error and actual preview
dimensions and the component message. It also checks resolution updates across
cached solves, all planar orientations, 3D, disconnected input and GPU texture sizes.
This mode starts a hidden Rhino graphics host and captures the real textured preview.
Use `--texture-detail` for a close-up instead of the full field. Tile upload dimensions,
complete voxel coverage and disposal of replaced texture cache entries are checked.
It only reads the source image and does not edit user definitions.
