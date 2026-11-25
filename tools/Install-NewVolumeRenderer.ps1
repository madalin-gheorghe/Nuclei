param(
    [string]$BackupRoot = 'C:\Nuclei\.codex-backups\before-new-volume-renderer-20260908'
)
$ErrorActionPreference = 'Stop'
$installSource = 'C:\Nuclei\Nuclei-v4\Nuclei4\bin\Release\net7.0-windows'
$installTarget = 'C:\Users\madal\AppData\Roaming\Grasshopper\Libraries\Nuclei4'
$installBackup = Join-Path ([IO.Path]::GetFullPath($BackupRoot)) 'Installed-Nuclei4'
$installReport = 'C:\Nuclei\.codex-temp\new-volume-renderer-20260908\deployment.json'
$installFiles = @('Nuclei4.gha', 'Nuclei4.Display.Abstractions.dll', 'Nuclei4.Display.D3D11.dll')
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
    throw 'Close Rhino before installing; this script will not close any application.'
}
if (Test-Path -LiteralPath $installReport) { throw 'A deployment record already exists. Inspect before installing again.' }
$manifest = Get-Content -LiteralPath (Join-Path $BackupRoot 'manifest.json') -Raw | ConvertFrom-Json
if (-not $manifest.Verified) { throw 'The full backup has not been verified.' }
$records = foreach ($name in $installFiles) {
    $source = Join-Path $installSource $name
    $target = Join-Path $installTarget $name
    $backup = Join-Path $installBackup $name
    $beforeHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($beforeHash -ne (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash) {
        throw "Installed file changed since backup: $target"
    }
    [pscustomobject]@{ Name=$name; Source=$source; Target=$target; Backup=$backup; BeforeSHA256=$beforeHash; AfterSHA256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
}
$null = New-Item -ItemType Directory -Path (Split-Path -Parent $installReport) -Force
try {
    foreach ($record in $records) {
        Copy-Item -LiteralPath $record.Source -Destination $record.Target -Force
        if ((Get-FileHash -LiteralPath $record.Target -Algorithm SHA256).Hash -ne $record.AfterSHA256) {
            throw "Installed hash mismatch: $($record.Target)"
        }
    }
    $report = [pscustomobject]@{
        InstalledUtc=[DateTime]::UtcNow.ToString('o'); Files=$records;
        Feature='Voxel Preview > Renderer > new renderer'; ScreenScale=0.5;
        DefaultRendererChanged=$false; SolverChanged=$false; ThinStrandRefinement=$false
    }
    [IO.File]::WriteAllText($installReport, ($report | ConvertTo-Json -Depth 6))
}
catch {
    $failure = $_.Exception.Message
    $restoreErrors = @()
    foreach ($record in $records) {
        try {
            Copy-Item -LiteralPath $record.Backup -Destination $record.Target -Force
            if ((Get-FileHash -LiteralPath $record.Target -Algorithm SHA256).Hash -ne $record.BeforeSHA256) {
                throw 'Restored hash mismatch.'
            }
        }
        catch { $restoreErrors += "$($record.Name): $($_.Exception.Message)" }
    }
    if ($restoreErrors.Count) { throw "Installation failed: $failure; restore needs attention: $($restoreErrors -join ' | ')" }
    throw "Installation failed; all three originals restored and hash-verified: $failure"
}
$report | ConvertTo-Json -Depth 6
