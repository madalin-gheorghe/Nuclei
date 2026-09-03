# Preview isolation validator

This Windows/Rhino 9 regression test verifies that V3 particle previews and V4
particle, trail, and GPU voxel previews expose only objects owned by the active
Grasshopper document.

Close Rhino, build the net7 V3 and V4 plugins, then run from the repository root:

```powershell
pwsh -NoProfile -File .\tools\Nuclei.PreviewIsolationValidator\RunValidation.ps1
```

The runner starts one hidden Rhino process with an isolated Grasshopper profile,
checks eight active-document contracts, and removes its temporary files. It
refuses to start while another Rhino process is open.
