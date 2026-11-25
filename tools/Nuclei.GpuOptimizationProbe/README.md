# GPU optimization correctness probe

Headless hardware-D3D11 checks using actual solver buffers, shared fixtures from `Nuclei.AntDiffusionProbe`, and the selected backend assembly. No Rhino document or installation is changed.

```powershell
dotnet build tools/Nuclei.GpuOptimizationProbe/Nuclei.GpuOptimizationProbe.csproj -c Release
dotnet tools/Nuclei.GpuOptimizationProbe/bin/Release/net8.0-windows/Nuclei.GpuOptimizationProbe.dll --nuclei-output .codex-temp/gpu-optimization/candidate-bin --json movement.json
```

- Default: 32 collision-free slime movement fixtures, 32 steps each, full-state bit comparisons. Covers full 3D, all planar orientations, boundary switching, voxel masks/limits/behavior/vectors, and jointly scaled movement/sensors (0.5, 1, 2, 4).
- `--diffusion`: 24 radius-one fixtures, 12 steps each. Direct reference versus forced tiled paths on small/masked/planar fields; also tests automatic dispatch on unmasked 128 cubed fields. Includes ant paired fields, slime, independent decay and boundary switching.
- `--networks`: 800 iterations on 64 cubed/3,000 particles and 128 cubed/24,000 particles, scales 0.5 and 2. Two original-path replays and one optimized run per fixture. Checks finite state, retained population and aggregate field/mass guardrails relative to baseline replay variation. Writes maximum-projection float32 arrays alongside the JSON. This is a regression screen, not statistical proof of equivalent morphology; occupancy races can make repeated original runs diverge substantially.
- `--shader-hashes --json shaders.json`: dump embedded bytecode hashes without running a simulation; also supports the backed-up backend.
- `--networks --ensemble 10`: alternate ten original and ten optimized runs per domain/scale. Saves per-run mass, occupied fraction and squared field sum incrementally to `.runs.json`. This diagnostic mode records distributions instead of applying the original three-run mass/L1 guard; finite-state and retained-population assertions still apply. Use at least two repeats. A final three-run repeat failed the mass guard, motivating this investigation; do not label ensemble completion as proof of equivalent behavior.

Pass `--json` to save evidence. Validation switches are private reflection hooks, not user-facing simulation settings. Runtime checks use .NET 8 to load the net7 backend; net48 is build-checked separately.

Results and limitations: [GPU optimization report](../../docs/performance/gpu-optimization/README.md).
