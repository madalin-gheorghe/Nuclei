param(
    [string]$BackupRoot = 'C:\Nuclei\.codex-backups\before-volume-renderer-menu-20260908',
    [string]$ValidationReport = 'C:\Nuclei\.codex-temp\volume-renderer-mode-20260908-run1\results.json'
)
$ErrorActionPreference = 'Stop'
$sourceGha = 'C:\Nuclei\Nuclei-v4\Nuclei4\bin\Release\net7.0-windows\Nuclei4.gha'
$targetDirectory = 'C:\Users\madal\AppData\Roaming\Grasshopper\Libraries\Nuclei4'
$targetGha = Join-Path $targetDirectory 'Nuclei4.gha'
$backupGha = Join-Path $BackupRoot 'Installed-Nuclei4\Nuclei4.gha'
$deploymentPath = 'C:\Nuclei\.codex-temp\volume-renderer-menu-20260908\deployment.json'
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) { throw 'Close Rhino first; this installer will not close applications.' }
if (Test-Path -LiteralPath $deploymentPath) { throw 'Deployment evidence already exists; refusing a duplicate install.' }
$manifest = Get-Content -LiteralPath (Join-Path $BackupRoot 'manifest.json') -Raw | ConvertFrom-Json
$validation = Get-Content -LiteralPath $ValidationReport -Raw | ConvertFrom-Json
$sourceHash = (Get-FileHash -LiteralPath $sourceGha).Hash
if (-not $manifest.Verified -or -not $validation.Passed -or $validation.GhaSha256 -ne $sourceHash) { throw 'Verified backup and tests for this exact build are required.' }
$installedRecords = @($manifest.Files | Where-Object { $_.Source.StartsWith($targetDirectory + '\', [StringComparison]::OrdinalIgnoreCase) })
if ($installedRecords.Count -lt 3) { throw 'Installed-file backup is incomplete.' }
foreach ($record in $installedRecords) {
    if ((Get-FileHash -LiteralPath $record.Source).Hash -ne $record.SHA256 -or (Get-FileHash -LiteralPath $record.Backup).Hash -ne $record.SHA256) { throw "File or backup changed: $($record.Source)" }
}
$beforeHash = (Get-FileHash -LiteralPath $targetGha).Hash
try {
    Copy-Item -LiteralPath $sourceGha -Destination $targetGha -Force
    if ((Get-FileHash -LiteralPath $targetGha).Hash -ne $sourceHash) { throw 'Installed GHA hash mismatch.' }
    foreach ($record in $installedRecords | Where-Object { $_.Source -ne $targetGha }) {
        if ((Get-FileHash -LiteralPath $record.Source).Hash -ne $record.SHA256) { throw "Dependency changed unexpectedly: $($record.Source)" }
    }
    $deployment = [pscustomobject]@{
        InstalledUtc=[DateTime]::UtcNow.ToString('o'); Target=$targetGha; Backup=$backupGha;
        BeforeSHA256=$beforeHash; AfterSHA256=$sourceHash; ValidationReport=$ValidationReport;
        ChangedFiles=@('Nuclei4.gha'); UnchangedInstalledFiles=$installedRecords.Count-1;
        Default='Fast half-width/half-height renderer, 128 samples per ray';
        HighResolution3D='Original full-resolution renderer, 256 samples per ray';
        RendererInputRemoved=$true; SolverChanged=$false; RendererShadersChanged=$false
    }
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $deploymentPath) -Force
    [IO.File]::WriteAllText($deploymentPath, ($deployment | ConvertTo-Json -Depth 5))
}
catch {
    $failure = $_.Exception.Message
    Copy-Item -LiteralPath $backupGha -Destination $targetGha -Force
    if ((Get-FileHash -LiteralPath $targetGha).Hash -ne $beforeHash) { throw "Install failed and original GHA restoration needs attention: $failure" }
    throw "Install failed; original GHA restored and verified: $failure"
}
$deployment | ConvertTo-Json -Depth 5
