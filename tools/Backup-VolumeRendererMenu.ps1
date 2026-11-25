param([string]$BackupRoot = 'C:\Nuclei\.codex-backups\before-volume-renderer-menu-20260908')
$ErrorActionPreference = 'Stop'
$backupTarget = [IO.Path]::GetFullPath($BackupRoot)
if (-not $backupTarget.StartsWith('C:\Nuclei\.codex-backups\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must stay within the workspace backup directory.' }
if (Test-Path -LiteralPath $backupTarget) { throw 'Backup already exists; refusing to overwrite it.' }
$backupSources = @(
    @{ Source='C:\Nuclei\Nuclei-v4'; Name='Nuclei-v4' },
    @{ Source='C:\Nuclei\Nuclei Definitions'; Name='Nuclei Definitions' },
    @{ Source='C:\Users\madal\AppData\Roaming\Grasshopper\Libraries\Nuclei4'; Name='Installed-Nuclei4' }
)
$records = @()
foreach ($root in $backupSources) {
    $resolvedSource = (Resolve-Path -LiteralPath $root.Source).Path.TrimEnd('\')
    foreach ($file in Get-ChildItem -LiteralPath $resolvedSource -File -Recurse -Force) {
        $relative = $file.FullName.Substring($resolvedSource.Length + 1)
        $destination = Join-Path (Join-Path $backupTarget $root.Name) $relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        $originalHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $copyHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($originalHash -ne $copyHash) { throw "Backup verification failed: $($file.FullName)" }
        $records += [pscustomobject]@{ Source=$file.FullName; Backup=$destination; SHA256=$originalHash; Length=$file.Length }
    }
}
$manifest = [pscustomobject]@{ CreatedUtc=[DateTime]::UtcNow.ToString('o'); Verified=$true; FileCount=$records.Count; Files=$records }
[IO.File]::WriteAllText((Join-Path $backupTarget 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5))
[pscustomobject]@{ Backup=$backupTarget; Verified=$true; FileCount=$records.Count }
