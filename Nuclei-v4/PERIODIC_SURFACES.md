# Function Attractor

Nuclei4 > Environment > Function Attractor selects voxels by approximate physical distance to `f(x,y,z) = isoValue` on the GPU.

Inputs, in order: voxels, surface, custom, scale, isoValue, minRange, maxRange. Custom sits directly after Surface. Existing five- and seven-input definitions migrate their actual parameters, keeping input identities, connected wires, formulas and numeric values. The component GUID is unchanged; old five-input components acquire range defaults when reopened.

- `minRange`: 0 by default, in model units, measured outward from the surface on either side.
- `maxRange`: 2 by default, in model units, measured outward on either side. Supplied reversed bounds are sorted. If the maximum is left unconnected at its default below the minimum, the minimum is preserved and the maximum grows automatically. Negative or nonfinite values remain invalid.
- With `minRange = 0`, the effective maximum is at least one voxel size. Both endpoints of sampled face-neighbor surface crossings are also retained. Thus a resolved crossing with voxel size 1 and maxRange 0.25 retains at least one voxel layer on each side. A surface exactly on a center can select that center plus a layer on each side.
- With `minRange > 0`, thin bands also retain both neighboring voxel centers that bracket the band midpoint in the sampled distance field. This keeps at least two neighboring layers across resolved offset walls too. Rasterization may include centers outside the requested interval by up to the neighboring sample; diagonals, corners and grid-aligned walls can be thicker.
- The numeric fallback is `maxRange >= minRange + voxelSize`, not `2 * voxelSize` on both sides. The width alone is not a two-layer guarantee: the discrete crossing rule provides the minimum. This is a minimum of two neighboring layers total, not exactly two everywhere, nor two on each side. The default range of 2 can intentionally produce wider walls.

Selection is always restricted to the input voxels. No method can create a layer outside the input domain, or guarantee features that the grid cannot resolve (for example, several oscillations inside one voxel). The minimum-layer rule concerns sampled crossings; it is not a watertight meshing guarantee.

Distance is estimated as `abs(f - isoValue) / length(gradient f)`. Central differences use 1% of a voxel, with each axis converted to model units using scale, grid resolution and voxel size. The estimate is accurate for planes and near regular surfaces, but is not exact closest-point distance for arbitrary functions, wide ranges or flat gradients. Nonfinite samples are excluded. At a zero gradient, nonzero residuals are excluded unless the crossing rule applies. Exact level samples have zero distance; a constant function equal to isoValue therefore selects the entire input for minRange 0.

The dropdown contains only predefined functions. Connecting a panel to Custom overrides Surface automatically; disconnecting it restores the selected preset. An empty or invalid connected formula produces an error instead of silently falling back. Surface may be empty while Custom is connected. Unconnected persistent Custom text is ignored.

The 15 presets keep their original numeric IDs, including the Schwarz P sign convention. Existing generated eight- and seventeen-item lists upgrade automatically, preserving surviving choices. Chirped Labyrinth (13) and Blended Field (17) have been removed; their IDs remain reserved. If a removed choice was selected in an old generated list, that list defaults to Gyroid. The retired Custom ID 8 is reserved; it falls back to Gyroid when Custom is disconnected. User-authored lists are preserved.

| Value | Preset | Form |
| --- | --- | --- |
| 1–7 | Gyroid, Schwarz D, Schwarz G, Schwarz P, Neovius, Diamond, P W Hybrid | Original periodic fields |
| 9 | IWP | Interconnected periodic passages |
| 10 | Fischer-Koch S | Branched periodic channels |
| 11 | Lidinoid | Periodic lobes and openings |
| 12 | Twisted Sheets | Helicoid-like membrane with twist increasing away from the center |
| 14 | Interference Field | Intersecting, curved wave sheets |
| 15 | Tanglecube | Rounded cubic form with interconnected handles |
| 16 | Trefoil Knot | Tube around an algebraic trefoil knot |
| 18 | Warped Caves | Nested sine waves producing irregular chambers; deterministic, not noise |

All presets produce surfaces at the default isoValue 0 and scale `2*pi`. Changing isoValue can alter topology or remove the surface; it does not always translate the shape. Fractals and true noise fields require additional evaluator support and are not included as formula presets.

For Custom, connect a panel such as:

```csharp
Math.Cos(x) * Math.Sin(y) + Math.Cos(y) * Math.Sin(z) + Math.Cos(z) * Math.Sin(x)
```

Original periodic presets, IWP, Fischer-Koch S, Lidinoid and Custom preserve the example coordinates: `x = scale * indexX / resX`, similarly for y/z. The remaining presets (12, 14, 15, 16, 18) use `x = scale * (indexX - (resX-1)/2) / resX`, centered on the grid. Scale changes the coordinate span; increasing it fits more of a compact shape in the grid and makes that shape smaller in model units. Centering is a GPU uniform, so changing Scale does not recompile the shader. Scale defaults to `2*pi`. The old fixed +/-0.1 formula-value band has been replaced by physical ranges, so existing selections will change when upgraded.

Preset expressions are in `Nuclei4/Voxel_PeriodicSurface.cs`. The trefoil evaluates `|u^3-v^2|^2 - 0.02`, with `u=2(x+iy)/(1+r²)` and `v=(2z+i(r²-1))/(1+r²)`, expanded into real arithmetic. The constant provides a useful default zero level; the range inputs still control voxel selection around that level. Copying a centered preset into Custom requires shifting x/y/z to reproduce its centered coordinates.

The added periodic approximations follow published equations for [IWP](https://pmc.ncbi.nlm.nih.gov/articles/PMC12436426/), [Fischer-Koch S](https://www.mdpi.com/2076-3417/14/9/3790), and [Lidinoid](https://pmc.ncbi.nlm.nih.gov/articles/PMC12477579/).

Outputs: voxels, voxelPosition, voxelIndex. Positions are hidden by default; positions and indices are calculated only when connected. Indices are zero-based output-selection ordinals, matching Curve Attractor. Input selection and settings are preserved.

Point, Curve, Mesh and Function Attractors display `Voxels: x` beneath the component. The count uses the final active output selection, including inversion, and totals output fields across Grasshopper iterations. It reuses cached counts without enumerating voxels or computing lazy outputs. Cleared or empty results show `Voxels: 0`.

The shader and buffers are reused. A packed one-bit-per-voxel mask is read back from the GPU. Hardware Direct3D feature level 11 is required; invalid formulas and GPU failures produce component errors. Custom input supports the arithmetic and Math functions listed in its tooltip, not arbitrary C# execution.

Mesh Inclusion also exposes voxelIndex, grouped by first containing mesh, with inverted selections in branch 0. Its auxiliary outputs are computed only when connected. Mesh containment remains on the CPU.

Point, Curve and Mesh Attractors use the same range normalization and thin offset-band voxel-pair rule. Their position, distance and index trees are independently demand-driven. With all three disconnected, there is no auxiliary geometry-distance pass or tree allocation. Selection still computes the distances it needs; position/index branching can also require nearest-attractor queries. Inverted position/index-only outputs skip those queries. The average distance output now uses an unbiased sum starting at zero.

Connecting the first consumer to an empty lazy output automatically expires its source before the normal Grasshopper solution starts collecting data. The component recomputes and supplies that output in the same solution. Additional consumers reuse populated data. Removing the last consumer stops auxiliary output generation and clears that output on the next solution. This applies to Function, Point, Curve and Mesh Attractors and Mesh Inclusion. No manual recompute is required; disabled Grasshopper solutions still wait until solutions are enabled. The demand watcher detaches when a component leaves its document.

## Validation

`pwsh -NoProfile -File tools/Nuclei.PeriodicSurfaceProbe/Verify.ps1` compares all 15 presets with independent CPU physical-gradient estimates (including complex-number trefoil evaluation), verifies usable default selections and both shader variants, and tests the custom gyroid, partial domains, tail bits, empty selections, reversed ranges, plane distances, varied scale/voxel size, all axes, diagonal crossing pairs, sub-voxel surface positions and positive-minimum offset walls. It also reports warm GPU evaluation/readback timing; this excludes Grasshopper outputs and cold initialization, and depends on current GPU load. Narrow offset bands require additional distance samples; ordinary bands compile out this extra shader path.

The C# probe in the same folder verifies defaults, hidden positions, and five/seven-input archives through open/save/reopen in an isolated Rhino host, retaining all original input identities and source connections. It solves every dropdown choice, checks automatic Custom override and disconnect recovery, invalid/empty Custom errors, ignored empty/invalid Surface during override, and migration of old generated lists. It also solves real point/curve/mesh attractors to verify omitted and reversed ranges, disconnected tree allocation, output connect/disconnect behavior, thin-wall output alignment and inverted index-only output. Its Bootstrap and RhinoStage are shared with the existing volume-renderer probe. Build its csproj, then invoke the executable with the built GHA path, a new scratch directory under `C:/Nuclei/.codex-temp`, and `--run`.

Add `--demand-only` to run the connection regression without archive roundtrips. Wiring-only checks do not explicitly expire the source: they start with solved components, connect/disconnect consumers, and request a normal solution. They verify delivery in the same solution, unchanged voxel selection, reuse by a second consumer, last-disconnect clearing and watcher detachment.

Use `--skip-archives` for all functional checks, including the new presets and Custom override, without Grasshopper archive loading.
