# Volume renderer menu and archive regression probe

Tests the real built Voxel Preview component inside an isolated, no-window Rhino 9 host. This tool does not run a solver, render GPU images, open user definitions, or modify installed files.

The entry point, Rhino stage, and Grasshopper stage are separated deliberately. Installed Rhino/Grasshopper references use `Private=false`, and the installed RhinoCommon assembly is loaded before either later stage is reflected into existence.

## Checks

- Exactly five existing inputs, no Renderer input.
- High Resolution (3D) unchecked by default: fast GPU volume renderer and 128 ray samples.
- Clicking the actual right-click menu item enables the original renderer and 256 samples; clicking again returns to the fast mode.
- Planar frame style never enables the new volume renderer.
- Ant food/base, combined pheromones, and mixed ants/slime select gradient-based volume shading in both quality modes; thresholds, single-field custom colours, combined palettes, missing-gradient fallback, and planar style are checked.
- Full synthetic Grasshopper documents preserve original input 0-4 GUIDs, actual resolved source objects/wires, and stored Type/minimum/maximum/colour settings through save/open/save/reopen.
- Legacy five-input archives preserve the quality setting, including the default when the setting is missing.
- Six-input archives with the retired Renderer set to either 0 or 1 migrate to five inputs and retain HighResolutionPreview. Retired Renderer choices do not override quality.
- Source value-list objects are not deleted during migration.

These are UI, style-selection and serialization tests, not image-quality or speed benchmarks.

## Run

```powershell
dotnet build C:\Nuclei\tools\Nuclei.VolumeRendererModeProbe\Nuclei.VolumeRendererModeProbe.csproj -c Release -m:1 -nr:false
dotnet C:\Nuclei\tools\Nuclei.VolumeRendererModeProbe\bin\Release\net8.0-windows\Nuclei.VolumeRendererModeProbe.dll C:\Nuclei\Nuclei-v4\Nuclei4\bin\Release\net7.0-windows\Nuclei4.gha C:\Nuclei\.codex-temp\volume-renderer-mode-new-run --run
```

Use a new output directory below `C:/Nuclei/.codex-temp`; existing output is rejected. The directory receives synthetic `.gh` fixtures, an isolated Grasshopper profile, and `results.json` containing assertions, archive hashes and the exact tested GHA hash. No Rhino host starts without `--run`.

Rhino needs access to its existing license services. If a sandboxed run exits at RhinoCore initialization, do not interpret that as a passing or failing component test; rerun with authorized license access. A 180-second watchdog ends only this isolated process if it stalls.

On 2026-09-08, the repository net7 build passed **614 assertions across 8 cases** against Rhino `9.0.26244.12303` after an authorized unsandboxed licensing retry. Evidence: `C:/Nuclei/.codex-temp/volume-renderer-mode-20260908-run1/results.json`.

The installed GHA and its actual installed dependency set then passed the same **614 assertions across 8 cases**. Evidence, loaded assembly locations and dependency hashes: `C:/Nuclei/.codex-temp/volume-renderer-mode-20260908-installed/results.json`. Tested GHA SHA256: `F527B8F47C4A90FC17A4F300DEEB9179B32B95783AC9B3B69F488FB094665E46`.
