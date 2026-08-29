[CmdletBinding()]
param(
    [string]$Definitions,
    [Parameter(Mandatory = $true)]
    [string]$V4Gha,
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedV4Sha256,
    [string]$Map,
    [string]$RhinoExe = 'C:\Program Files\Rhino 9 WIP\System\Rhino.exe',
    [string]$GrasshopperDll = 'C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper\Grasshopper.dll',
    [switch]$UseNormalGrasshopperProfile,
    [switch]$AutoloadIsolatedProfile,
    [string]$OnlyFile,
    [string[]]$SkipExtra,
    [ValidateRange(30, 3600)]
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($Definitions)) {
    $Definitions = Join-Path $repositoryRoot 'Nuclei Definitions\v4_updated'
}
if ([string]::IsNullOrWhiteSpace($Map)) {
    $Map = Join-Path $repositoryRoot 'tools\Nuclei.DefinitionConverter\v3.3-to-v4.json'
}

$Definitions = [IO.Path]::GetFullPath($Definitions)
$V4Gha = [IO.Path]::GetFullPath($V4Gha)
$Map = [IO.Path]::GetFullPath($Map)
$RhinoExe = [IO.Path]::GetFullPath($RhinoExe)
$GrasshopperDll = [IO.Path]::GetFullPath($GrasshopperDll)
$validator = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'ValidateInRhino.py'))
$manifestPath = Join-Path $Definitions '_conversion_manifest.json'
$reportPath = Join-Path $Definitions '_rhino9_validation.json'
$progressPath = Join-Path $Definitions '_rhino9_validation.progress.json'

foreach ($required in @($Definitions, $V4Gha, $Map, $RhinoExe, $GrasshopperDll, $validator, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required validation path was not found: $required"
    }
}

$actualV4Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $V4Gha).Hash
if (-not $ExpectedV4Sha256) {
    $ExpectedV4Sha256 = $actualV4Sha256
}
if ($actualV4Sha256 -ne $ExpectedV4Sha256.ToUpperInvariant()) {
    throw "Requested V4 GHA hash is $actualV4Sha256; expected $ExpectedV4Sha256."
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$isolatedGrasshopper = Join-Path $temporaryRoot ('NucleiDefinitionValidation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolatedGrasshopper | Out-Null

$env:NUCLEI_VALIDATION_DEFINITIONS = $Definitions
$env:NUCLEI_VALIDATION_V4_GHA = $V4Gha
$env:NUCLEI_VALIDATION_MAP = $Map
$env:NUCLEI_VALIDATION_NORMALIZE = '0'
$env:NUCLEI_VALIDATION_SOLVE_DENDRO = '1'
$env:NUCLEI_VALIDATION_ORIGINAL_APPDATA = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
$env:NUCLEI_VALIDATION_GRASSHOPPER_DLL = $GrasshopperDll
$env:NUCLEI_VALIDATION_ISOLATED_GH_APPDATA = $isolatedGrasshopper
$env:NUCLEI_VALIDATION_AUTOLOAD = if ($AutoloadIsolatedProfile) { '1' } else { '0' }
$env:NUCLEI_VALIDATION_USE_NORMAL_PROFILE = if ($UseNormalGrasshopperProfile) { '1' } else { '0' }
$env:NUCLEI_VALIDATION_EXPECTED_V4_SHA256 = $ExpectedV4Sha256.ToUpperInvariant()
Remove-Item Env:NUCLEI_VALIDATION_START_AT -ErrorAction SilentlyContinue
if ($OnlyFile) {
    $env:NUCLEI_VALIDATION_ONLY_FILE = $OnlyFile
}
else {
    Remove-Item Env:NUCLEI_VALIDATION_ONLY_FILE -ErrorAction SilentlyContinue
}
if ($SkipExtra) {
    $env:NUCLEI_VALIDATION_SKIP_EXTRAS = $SkipExtra -join ','
}
else {
    Remove-Item Env:NUCLEI_VALIDATION_SKIP_EXTRAS -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath $progressPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

if ($UseNormalGrasshopperProfile) {
    if ($AutoloadIsolatedProfile) {
        throw '-UseNormalGrasshopperProfile and -AutoloadIsolatedProfile are mutually exclusive.'
    }
    $installedV4 = Join-Path $env:NUCLEI_VALIDATION_ORIGINAL_APPDATA 'Grasshopper\Libraries\Nuclei4.gha'
    if (Test-Path -LiteralPath $installedV4) {
        $installedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installedV4).Hash
        $requestedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $V4Gha).Hash
        if ($installedHash -ne $requestedHash) {
            throw "Normal Grasshopper profile contains a different Nuclei4.gha ($installedHash). Use the isolated default or install the requested build."
        }
    }
}

$macro = '-_RunPythonScript ' + $validator + ' _Enter _-Exit _Enter'
$runArgument = '/runscript="' + $macro + '"'
$rhinoProcess = $null

try {
    $rhinoProcess = Start-Process -FilePath $RhinoExe -ArgumentList @('/nosplash', '/notemplate', $runArgument) -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastStatus = ''
    while (-not $rhinoProcess.HasExited) {
        if ([DateTime]::UtcNow -ge $deadline) {
            Stop-Process -Id $rhinoProcess.Id -Force
            throw "Rhino 9 validation timed out after $TimeoutSeconds seconds."
        }
        if (Test-Path -LiteralPath $progressPath) {
            try {
                $status = (Get-Content -Raw -LiteralPath $progressPath | ConvertFrom-Json).status
                if ($status -and $status -ne $lastStatus) {
                    Write-Host "Rhino 9: $status"
                    $lastStatus = $status
                }
            }
            catch {
                # An atomic rename normally prevents partial reads; retry if an
                # antivirus/indexer briefly has the progress file locked.
            }
        }
        Start-Sleep -Milliseconds 500
        $rhinoProcess.Refresh()
    }

    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw "Rhino 9 exited without writing a validation report (exit code $($rhinoProcess.ExitCode))."
    }
    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    if (-not $report.success) {
        throw "Rhino 9 validation failed: $($report.error)"
    }
    $expectedFileCount = if ($OnlyFile) { 1 } else { (Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json).fileCount }
    if ($report.fileCount -ne $expectedFileCount) {
        throw "Rhino 9 validated $($report.fileCount) files; expected $expectedFileCount."
    }
    $expectedGhaHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $V4Gha).Hash
    if ($report.v4GhaSha256 -ne $expectedGhaHash) {
        throw 'The validation report does not identify the requested V4 GHA binary.'
    }
    Write-Host "Rhino 9 validation passed for $($report.fileCount) definitions with V4 GHA $expectedGhaHash."
}
finally {
    if ($rhinoProcess -and -not $rhinoProcess.HasExited) {
        Stop-Process -Id $rhinoProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $isolatedGrasshopper) {
        $resolvedCleanup = [IO.Path]::GetFullPath($isolatedGrasshopper)
        if (-not $resolvedCleanup.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected temporary path: $resolvedCleanup"
        }
        Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force -ErrorAction SilentlyContinue
    }
}
