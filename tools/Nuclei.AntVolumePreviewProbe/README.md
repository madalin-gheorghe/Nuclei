# Ant volume GPU preview regression

This headless probe constructs the real D3D11 solver, requests voxel preview frames, and reads its GPU density and gradient textures. It requires a hardware GPU and a built v4 output folder; it does not open Rhino or modify Grasshopper documents.

```powershell
dotnet build tools/Nuclei.AntVolumePreviewProbe/Nuclei.AntVolumePreviewProbe.csproj -c Release -m:1 -p:BuildInParallel=false -nr:false
dotnet tools/Nuclei.AntVolumePreviewProbe/bin/Release/net8.0-windows/Nuclei.AntVolumePreviewProbe.dll --nuclei-output Nuclei-v4/Nuclei4/bin/Release/net7.0-windows --json .codex-temp/ant-volume-preview.json
```

The fixtures use different ramps for slime, food pheromone, and base pheromone, plus a separate remaining-food region. Every atlas texel is checked against the source field and the selected displayed ant scalar (the existing slime-only normal behavior is preserved); half-float gradient tolerance is 0.1% (minimum 0.001). Checks cover both ant channels, combined ants, ants plus slime, slime versions, clamped edges, atlas padding, switching fields/scales, repeated cached requests, all three planar orientations, absent ants, and empty ant fields beside a nonempty slime field. Planar RGBA texels remain checked against their original bilinear sampling semantics and must not acquire a volume gradient.

The optional JSON records exact GPU atlas hashes for comparisons. This validates the solver-to-preview texture path; it does not claim a Rhino viewport image comparison.

`--density-only` skips ant-volume gradient checks so the same fixtures can run on a preserved pre-fix assembly. Compare its `DensitySha256` records and slime `GradientSha256` records with a normal run to prove exact unchanged RGBA atlas output (including planar previews) and existing slime gradients.

Retained frame handles are reread after every field change to catch simultaneous previews overwriting each other's normals. The probe requires separate gradient textures per source (7 and 12 share the slime source), verifies disabling previews releases every cached gradient, and advances the real solver once to check gradient refresh for every source.
Ant Food (13) is included in the GPU field/scale/cache matrix. A live depletion check clears its packed quantity without resetting the solver, advances a step, and requires both standalone food and combined previews to refresh to an empty food channel.
