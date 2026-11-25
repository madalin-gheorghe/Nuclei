# Ant GPU performance probe

Profiles an existing Nuclei D3D11 build directly. It never builds production code or opens Rhino. WARP fallback is rejected. Each measured batch uses GPU timestamps, per-pass markers, and a blocking population readback as a GPU fence. Shader/resource setup, warmup, and timestamp-query allocation occur before measurements.

```powershell
dotnet run --project tools/Nuclei.AntPerformanceProbe -c Release -- --nuclei-output .codex-temp/ant-performance/before --intro-profile --preview off --particle-preview --steps 12 --repeats 3 --warmup 6 --json .codex-temp/ant-performance/baseline250.json
```

The default synthetic fixture uses a 100³ grid and 1,000 ants, 1,000 slime particles, and 60,000 slime particles. `--intro-profile` uses a 250³ grid, 1,436 ants, 1,436 slime particles, and 90,000 slime particles, with settings extracted from the Ants Intro 3D and 3D Intro definitions:

| Setting | Ants | Slime |
|---|---:|---:|
| Speed / sensor distance | 3 / 9 | 3 / 9 |
| Sensor / rotation angle | 45° / 45° | 45° / 45° |
| Deposit | 10 | 1 |
| Base wander / wander | 0.15 | 0 |
| Wrap | False | True |
| Diffusion range | 2 | 1 |
| Diffusion food / base | 0.1 / 0.1 | — |
| Decay food / base | 0.0001 / 0.0005 | — |
| Scalar diffuse / decay / gradual | 0.1 / 0.03 / 1 | 0.15 / 0.01 / 0.5 |

Options include `--grid`, `--particles`, `--slime-particles`, `--steps`, `--repeats`, `--warmup`, `--species ants,slime,slime60`, `--preview off,single,three`, and `--preview-scale`. The `--range` option applies to the default fixture; the intro profile has the ranges above. `--particle-preview` publishes particle preview textures each step. `single` requests the combined ant field or slime field; `three` requests food, base, and combined ant fields (slime still has one field).

`--variants` runs validation modes against the **same loaded backend binary**, creating a new engine and repeating warmup for every sequence entry:

| Variant | Diffusion | Ant decay | Food/base dispatch | Ant-only scalar updates |
|---|---|---|---|---|
| `legacy` | Direct | Separate | Separate | Enabled |
| `direct-fused` | Direct | Fused | Separate | Enabled |
| `tiled-separate` | Backend tiled selection | Separate | Separate | Enabled |
| `prior-optimized` | Backend tiled selection | Fused | Separate | Enabled |
| `paired-only` | Backend tiled selection | Fused | Paired | Enabled |
| `skip-density-only` | Backend tiled selection | Fused | Separate | Skipped |
| `paired-no-sparse` | Backend tiled selection | Fused | Paired | Skipped |
| `current` (default) | Backend tiled selection | Fused | Paired | Skipped |
| `current-particle-deposits` | Backend tiled selection | Fused | Paired | Skipped |

`current` uses production's adaptive sparse-ant deposit selection. `paired-no-sparse` disables that selection using `disableAdaptiveAntDepositsForValidation=true`, retaining paired fields and skipped scalar updates to isolate deposit improvements. All earlier stage modes (`legacy`, `direct-fused`, `tiled-separate`, `prior-optimized`, `paired-only`, and `skip-density-only`) also disable adaptive deposit selection, preserving their previous meaning. Automatic sparse-ant execution appears as `ApplyDeposits.ParticleSparseAnt` in pass timings.

`current-particle-deposits` additionally sets `forceParticleDrivenDepositForValidation=true`, forcing particle-driven deposits independently of production's adaptive threshold. This diagnostic flag does not change production selection. JSON records `ForceParticleDrivenDeposits` and `DisableAdaptiveAntDeposits` for every variant.

The original comparison modes use `forceDirectDiffusionForValidation` and `disableAntDecayFusionForValidation`. Newer backends also expose `disablePairedAntDiffusionForValidation` and `forceScalarDensityUpdatesForValidation`; the probe sets these after construction and before warmup. `prior-optimized`, `paired-only`, `skip-density-only`, and `current` retain tiled diffusion and ant decay fusion while isolating the two newer improvements. The original comparison variants disable both newer improvements to preserve their previous meaning.

`current` can also inspect older archived binaries, where it means that binary's production defaults. Missing newer validation switches appear as `null` in JSON, so an archived run does not claim to enable unsupported improvements. `paired-only` requires the pairing switch; `skip-density-only` requires the scalar-update switch. The prior installed runtime is stored at `.codex-temp/ant-performance/deploy`; use `--nuclei-output .codex-temp/ant-performance/deploy --variants current` to measure it independently.

Scalar decay fusion remains unchanged; the intro profile's scalar range of one already selects direct diffusion whenever scalar updates run. JSON records variant names, sequence positions, and all applied switch values. Repeated variants are preserved, allowing a counterbalanced run such as:

```powershell
dotnet tools/Nuclei.AntPerformanceProbe/bin/Release/net8.0-windows/Nuclei.AntPerformanceProbe.dll --nuclei-output .codex-temp/ant-performance/candidate --intro-profile --species ants --preview off --particle-preview --variants prior-optimized,current,current,prior-optimized --warmup 120 --steps 20 --repeats 5 --json .codex-temp/ant-performance/candidate-abba-warm250.json
```

This is a solver diagnostic, not playback of the Grasshopper definitions. Particles occupy unique, dispersed interior cells with deterministic random headings. All ants have a central home; half start carrying food and their launch-boundary flags are set. Fields have deterministic nonzero initial data except the ants' scalar field, which starts at zero and remains allocated (`ProcessDensity=true`); the backend and selected variant determine whether it is updated. There are no obstacles, food sources, dynamic populations, trails, CPU output synchronization, or Rhino rendering. Preview modes measure shared-texture generation, not viewport drawing. Thus timings locate solver costs but do not predict every interactive document's frame rate. Run GPU benchmarks serially; device clocks and concurrent viewport activity affect timing.
