param(
    [Parameter(Mandatory = $true)]
    [string]$OriginalGha,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$inputPath = (Resolve-Path -LiteralPath $OriginalGha).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    throw "Use a new output directory: $outputPath"
}

$bannerProject = Join-Path $repoRoot 'Nuclei-v2-old/Nuclei2.OldBanner/Nuclei2.OldBanner.csproj'
& dotnet build $bannerProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Banner build failed.' }

$bannerDll = Join-Path $repoRoot 'Nuclei-v2-old/Nuclei2.OldBanner/bin/Release/net48/Nuclei2.OldBanner.dll'
& dotnet run --project (Join-Path $repoRoot 'tools/Nuclei2VersionGuard') -c Release -- $inputPath (Join-Path $outputPath 'nuclei2.gha') $bannerDll
if ($LASTEXITCODE -ne 0) { throw 'Nuclei2 version-guard build failed.' }

@'
Nuclei 2 - Rhino 6/7/8/9 compatibility build (Windows)

Install only nuclei2.gha in Grasshopper's Libraries folder, replacing the
existing Nuclei2 installation, then restart Rhino. Remove Nuclei2.OldBanner.gha
if you installed the previous two-file build; the banner is now embedded.
Every Nuclei2 component displays "old v2" above its body in Rhino 8 and 9 only.
Rhino 6/7 need only nuclei2.gha and retain their original appearance.
Component GUIDs and solver code are preserved.
The compact Grasshopper category tab reads N2.
In Rhino 8/9, installed Nuclei tabs are grouped together in N2, N3, N4 order.

This is a local build, not a published Yak release.
'@ | Set-Content -LiteralPath (Join-Path $outputPath 'INSTALL.txt') -Encoding utf8
Get-ChildItem -LiteralPath $outputPath -Filter '*.gha' | Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='File'; Expression={Split-Path $_.Path -Leaf}}, Hash |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'SHA256.json') -Encoding utf8
Write-Host "Built: $outputPath"
