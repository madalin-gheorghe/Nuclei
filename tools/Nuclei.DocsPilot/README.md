# Documentation pilot verification

Builds a five-component metadata catalog and verifies the original Slime Intro in an isolated Rhino 9 / Grasshopper host. No installed GHA, original definition, or open user document is edited.

```powershell
dotnet build Nuclei-v4/Nuclei4/Nuclei4.csproj -c Release -f net7.0-windows --no-restore -m:1 -nr:false -p:SkipGrasshopperInstall=true -p:BuildInParallel=false
dotnet build tools/Nuclei.DocsPilot/Nuclei.DocsPilot.csproj -c Release -m:1 -nr:false
dotnet tools/Nuclei.DocsPilot/bin/Release/net8.0-windows/Nuclei.DocsPilot.dll Nuclei-v4/Nuclei4/bin/Release/net7.0-windows/Nuclei4.gha .codex-temp/docs-pilot-original-new --run --original-intro
```

Wait for each command to complete before running the next. The output directory must be new and below `C:/Nuclei/.codex-temp`. The shared bootstrap uses the repository's existing isolated RhinoCore initialization and 180-second watchdog; its usage text refers to the original renderer probe.

`--original-intro` preserves all saved numeric inputs and the six-component graph. The in-memory copy has Reset True and its Trigger paused. An unsaved trail extractor is added only after saving/capturing the copy, to request data for inspection. Runtime checks cover 200 steps, boundary-filtered counts, movement, bounds, trails, reset, and reopening. Separate real Grasshopper fixtures check scalar mapping order and mismatch handling.

Without `--original-intro`, the internal fixture uses a 100 × 100 × 1 field and 1,000 requested particles for fast diagnostics. It is not a new user tutorial and should not be published as one.

Outputs include `component-contracts.json`, `validation.json`, a temporary definition copy, a canvas rendering, and a plot of extracted trail coordinates. The result plot is not a Rhino viewport screenshot. A `passed: true` validation file and exit code 0 are required for success. Failure runs must not be used as evidence.

The component reference explanations include source-reviewed behavior beyond the checks executed here. The validator does not certify every edge case, minimum hardware requirements, interactive Trigger behavior, or viewport rendering.

For walkthrough screenshots, use `--capture-walkthrough` in place of `--original-intro`. This mode initializes a temporary copy at the original settings and captures five crops with the original layout and group colors. It renders actual incoming wires for every receiving component, including recipients outside the crop, so connected output wires remain visible. It writes a connection manifest and verifies the original file and graph are unchanged; it does not run the 200-step validation.

Add `--ant` to capture the original Ants Intro instead. Saved input values are also written to the capture output directory.

Use `--catalog-only` to inspect all public components without running a simulation. It records their exact registered port metadata and checks that the assembly hash stays unchanged during capture. The raw export pairs each port's metadata with its registered description; the documentation catalog uses the metadata and source-reviewed explanations separately.
