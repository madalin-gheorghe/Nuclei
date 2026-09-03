[CmdletBinding()]
param(
    [string]$V3Gha = '.\Nuclei-v3\Nuclei3\bin\Release\net7.0-windows\Nuclei3.gha',
    [string]$V4Gha = '.\Nuclei-v4\Nuclei4\bin\Release\net7.0-windows\Nuclei4.gha',
    [string]$RhinoExe = 'C:\Program Files\Rhino 9 WIP\System\Rhino.exe',
    [string]$GrasshopperDll = 'C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper\Grasshopper.dll',
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
function Resolve-RepositoryPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

$V3Gha = Resolve-RepositoryPath $V3Gha
$V4Gha = Resolve-RepositoryPath $V4Gha
$RhinoExe = [IO.Path]::GetFullPath($RhinoExe)
$GrasshopperDll = [IO.Path]::GetFullPath($GrasshopperDll)
$validator = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'ValidateInRhino.py'))

foreach ($required in @($V3Gha, $V4Gha, $RhinoExe, $GrasshopperDll, $validator)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required validation path was not found: $required"
    }
}

$existingRhino = @(Get-Process Rhino -ErrorAction SilentlyContinue)
if ($existingRhino.Count -ne 0) {
    throw 'Close Rhino before running preview-isolation validation; the validator owns its isolated Rhino process.'
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$validationRoot = Join-Path $temporaryRoot ('NucleiPreviewIsolation-' + [Guid]::NewGuid().ToString('N'))
$grasshopperRoot = Join-Path $validationRoot 'Grasshopper'
$reportPath = Join-Path $validationRoot 'report.json'
New-Item -ItemType Directory -Path $grasshopperRoot | Out-Null

$env:NUCLEI_PREVIEW_VALIDATION_V3_GHA = $V3Gha
$env:NUCLEI_PREVIEW_VALIDATION_V4_GHA = $V4Gha
$env:NUCLEI_PREVIEW_VALIDATION_GRASSHOPPER_DLL = $GrasshopperDll
$env:NUCLEI_PREVIEW_VALIDATION_GH_APPDATA = $grasshopperRoot
$env:NUCLEI_PREVIEW_VALIDATION_REPORT = $reportPath

$macro = '-_RunPythonScript ' + $validator + ' _Enter _-Exit _Enter'
$runArgument = '/runscript="' + $macro + '"'
$process = $null

try {
    $process = Start-Process -FilePath $RhinoExe -ArgumentList @('/nosplash', '/notemplate', $runArgument) -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited) {
        if ([DateTime]::UtcNow -ge $deadline) {
            Stop-Process -Id $process.Id -Force
            throw "Preview isolation validation timed out after $TimeoutSeconds seconds."
        }
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }

    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw "Rhino exited without writing a validation report (exit code $($process.ExitCode))."
    }
    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    if (-not $report.success) {
        throw "Preview isolation validation failed: $($report.error)"
    }
    Write-Host "Preview document isolation passed ($($report.contracts) active-document contracts)."
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    Get-Process Rhino -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $RhinoExe } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $validationRoot) {
        $resolvedCleanup = [IO.Path]::GetFullPath($validationRoot)
        if (-not $resolvedCleanup.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected temporary path: $resolvedCleanup"
        }
        Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force -ErrorAction SilentlyContinue
    }
}
