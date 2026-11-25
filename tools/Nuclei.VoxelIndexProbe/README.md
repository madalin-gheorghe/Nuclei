# Voxel index regression probe

Runs the actual D3D11 solver headlessly on a 350³ grid, with full and selected
domains. Eight particles cover small indices, crossing 2²⁴ during movement,
and odd indices above 2²⁵. Checks movement, exact parent indices, voxel ownership,
population and an inactive slot's sentinel. Requires the hardware GPU backend.

```powershell
dotnet run --project tools/Nuclei.VoxelIndexProbe -c Release -- --nuclei-output Nuclei-v4/Nuclei4/bin/Release/net7.0-windows
```

Pass `--expect-freeze` against an unpatched deployment to verify the old precision
failure. Optional `--selection full|selected|both` and `--steps 1..8` limit cases.
The default runs both domains for four movement steps. Timings include readback
and zero diffusion, so they are regression timings, not a workload benchmark.

Verified behavior: the numeric-float baseline freezes odd high-index seeds
immediately, freezes an even high-index seed after its first move, and freezes
a low-index seed after it crosses 2²⁴. The exact integer-bit payload passes all
four steps in both domains, including small indices, ownership and the sentinel.
The fixed `net48` and `net7.0-windows` deployment assemblies both passed on the
hardware D3D11 backend (the probe itself runs under .NET 8).

The existing architecture probe also passed exclusive occupancy, population
ordering, ant reset and ant state checks. Its blocked-parent parity case still
fails the same first movement assertion on both the original and fixed builds
(expected X=3.1, actual X=2.5); that failure predates the index encoding change.
