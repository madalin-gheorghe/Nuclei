param(
    [string]$LibrariesPath = "$env:APPDATA\Grasshopper\Libraries",
    [string]$IlspyPath = "C:\Nuclei\.diag-work\tools\ilspycmd.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $LibrariesPath)) {
    throw "Grasshopper Libraries folder was not found: $LibrariesPath"
}

if (-not (Test-Path -LiteralPath $IlspyPath)) {
    throw "ILSpy command line tool was not found: $IlspyPath"
}

$expected = @{
    "Nuclei30Legacy.gha" = @{
        Version = "3.0.0.0"
        LibraryId = "e3867b7b-1e11-45c4-9544-6f30e27d2730"
        ConstructVoxelsId = "6526b596-0bf5-405d-9dcb-2d9db924652b"
    }
    "Nuclei3.gha" = @{
        Version = "3.3.0.0"
        LibraryId = "fe53d2b8-e56d-da70-cde9-0b078f8bc65d"
        ConstructVoxelsId = "feb0993f-6d5f-bfcf-76ae-1377559f335a"
    }
    "Nuclei4.gha" = @{
        Version = "4.1.0.0"
        LibraryId = "a4810f34-10b6-480c-a6d0-607aac4e8d2a"
        ConstructVoxelsId = "a3940a4d-9015-411c-9ffa-e38ecc90d394"
    }
}

$assemblies = Get-ChildItem -LiteralPath $LibrariesPath -Filter "Nuclei*.gha" |
    Where-Object { $_.Name -notlike "*.bak" } |
    Sort-Object Name

$unexpected = $assemblies | Where-Object { -not $expected.ContainsKey($_.Name) }
if ($unexpected) {
    $names = ($unexpected | Select-Object -ExpandProperty Name) -join ", "
    throw "Unexpected loadable Nuclei .gha files found: $names"
}

foreach ($name in $expected.Keys) {
    $path = Join-Path $LibrariesPath $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Expected plugin is missing: $path"
    }
}

$tempRoot = Join-Path $env:TEMP ("nuclei-guid-check-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($assembly in $assemblies) {
        $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($assembly.FullName)
        $expectedAssembly = $expected[$assembly.Name]

        if ($assemblyName.Version.ToString() -ne $expectedAssembly.Version) {
            throw "$($assembly.Name) version is $($assemblyName.Version), expected $($expectedAssembly.Version)"
        }

        $outDir = Join-Path $tempRoot ([IO.Path]::GetFileNameWithoutExtension($assembly.Name))
        & $IlspyPath -p -o $outDir $assembly.FullName | Out-Null

        Get-ChildItem -LiteralPath $outDir -Filter "*.cs" -Recurse | ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            $classMatch = [regex]::Match($text, 'class\s+(\w+)')
            $className = if ($classMatch.Success) { $classMatch.Groups[1].Value } else { $_.BaseName }

            $libraryMatch = [regex]::Match($text, 'override\s+Guid\s+Id\s*=>\s*new\s+Guid\("([0-9a-fA-F-]+)"\)')
            if ($libraryMatch.Success) {
                $rows.Add([pscustomobject]@{
                    Assembly = $assembly.Name
                    Kind = "Library"
                    Class = $className
                    Guid = $libraryMatch.Groups[1].Value.ToLowerInvariant()
                })
            }

            $componentMatch = [regex]::Match($text, 'ComponentGuid\s*=>\s*new\s+Guid\("([0-9a-fA-F-]+)"\)')
            if ($componentMatch.Success) {
                $rows.Add([pscustomobject]@{
                    Assembly = $assembly.Name
                    Kind = "Component"
                    Class = $className
                    Guid = $componentMatch.Groups[1].Value.ToLowerInvariant()
                })
            }
        }
    }

    foreach ($assembly in $assemblies) {
        $expectedAssembly = $expected[$assembly.Name]
        $libraryId = ($rows | Where-Object { $_.Assembly -eq $assembly.Name -and $_.Kind -eq "Library" } | Select-Object -First 1).Guid
        if ($libraryId -ne $expectedAssembly.LibraryId) {
            throw "$($assembly.Name) library GUID is $libraryId, expected $($expectedAssembly.LibraryId)"
        }

        $constructVoxelsId = ($rows | Where-Object { $_.Assembly -eq $assembly.Name -and $_.Class -match 'VoxelConstructor' } | Select-Object -First 1).Guid
        if ($constructVoxelsId -ne $expectedAssembly.ConstructVoxelsId) {
            throw "$($assembly.Name) Construct Voxels GUID is $constructVoxelsId, expected $($expectedAssembly.ConstructVoxelsId)"
        }
    }

    $duplicates = $rows | Group-Object Kind, Guid | Where-Object { $_.Count -gt 1 }
    if ($duplicates) {
        $message = $duplicates | ForEach-Object {
            $items = $_.Group | ForEach-Object { "$($_.Assembly):$($_.Class)" }
            "$($_.Name) -> $($items -join ', ')"
        }
        throw "Duplicate Nuclei GUIDs found:`n$($message -join "`n")"
    }

    Write-Host "Nuclei install GUID check passed."
    $rows | Group-Object Assembly, Kind | Select-Object Name, Count | Sort-Object Name | Format-Table -AutoSize
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
