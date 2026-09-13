#requires -Version 7.0
<#
.SYNOPSIS
Builds a Rhino 8/9 Yak candidate from Nuclei V3 source.
.DESCRIPTION
Creates a fresh ignored .publish-work directory. The net48 build retains its
runtime dependencies; the portable net7.0 build contains one GHA with direct PNG
resources. Does not install, publish, or change the working source tree.
Defaults to committed source; -UseWorkingTree snapshots current source instead.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^3\.\d+\.\d+$')]
    [string]$Version,
    [switch]$UseWorkingTree,
    [string]$YakPath = 'C:\Program Files\Rhino 8\System\Yak.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$projectRelative = 'Nuclei-v3/Nuclei3'
$iconRelative = 'tools/assets/nuclei-yak-icon.png'
$iconSha256 = 'B2723987CC1B3F1B916072FA668077170095565635B8C49A8E0C505D8429A16A'
$rhinoSdkVersion = '8.0.23304.9001'
$assemblyVersion = "$Version.0"
if (@(([version]$assemblyVersion).Major, ([version]$assemblyVersion).Minor,
        ([version]$assemblyVersion).Build) | Where-Object { $_ -gt 65535 }) {
    throw 'Version components must fit an assembly version (0-65535).'
}
if (-not (Test-Path -LiteralPath $YakPath -PathType Leaf)) {
    throw "Rhino 8 Yak executable not found: $YakPath"
}
Get-Command git, dotnet -ErrorAction Stop | Out-Null

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

function Write-Utf8 {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

Push-Location $repo
try {
    $status = @(Invoke-Checked git @('status', '--porcelain', '--', $projectRelative, 'global.json', $iconRelative))
    if ($status.Count -gt 0 -and -not $UseWorkingTree) {
        throw "Commit the V3 source, SDK selection, and Yak icon before packaging:`n$($status -join "`n")"
    }
    $commit = (Invoke-Checked git @('rev-parse', 'HEAD')).Trim()
    $stage = Join-Path $repo ('.publish-work/yak-v3-' + $Version + '-' +
        (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $stage | Out-Null
    $source = Join-Path $stage 'source'
    if ($UseWorkingTree) {
        # Snapshot only source-controlled and non-ignored source files, never bin/obj.
        $files = @(Invoke-Checked git @('ls-files', '--cached', '--others', '--exclude-standard', '--',
            $projectRelative, 'global.json', $iconRelative)) | Sort-Object -Unique
        foreach ($file in $files) {
            $inputPath = Join-Path $repo $file
            if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { continue }
            $destination = Join-Path $source $file
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $inputPath -Destination $destination
        }
    }
    else {
        $archive = Join-Path $stage 'committed-source.zip'
        Invoke-Checked git @('archive', '--format=zip', "--output=$archive", $commit,
            $projectRelative, 'global.json', $iconRelative)
        Expand-Archive -LiteralPath $archive -DestinationPath $source
    }
    $legacyProject = Join-Path $source $projectRelative
    $portableProject = Join-Path $source 'portable/Nuclei3'
    New-Item -ItemType Directory -Path (Split-Path $portableProject -Parent) | Out-Null
    Copy-Item -LiteralPath $legacyProject -Destination $portableProject -Recurse

    $sourceFiles = @(Get-ChildItem -LiteralPath $legacyProject -File -Recurse |
        Sort-Object FullName | ForEach-Object {
            [ordered]@{ Path = [IO.Path]::GetRelativePath($legacyProject, $_.FullName).Replace('\', '/');
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    $icon = Join-Path $source $iconRelative
    if ((Get-FileHash -LiteralPath $icon -Algorithm SHA256).Hash -ne $iconSha256) {
        throw 'Yak icon differs from the approved Nuclei 3.0.0 package icon.'
    }

    # Apply package version only to exported copies, preserving component IDs.
    foreach ($project in @($legacyProject, $portableProject)) {
        $assemblyInfo = Join-Path $project 'Properties/AssemblyInfo.cs'
        $text = Get-Content -LiteralPath $assemblyInfo -Raw
        foreach ($attribute in @('AssemblyVersion', 'AssemblyFileVersion')) {
            $pattern = '(?m)^\[assembly: ' + $attribute + '\("[^"]+"\)\]'
            if ([regex]::Matches($text, $pattern).Count -ne 1) {
                throw "Expected exactly one $attribute in $assemblyInfo."
            }
            $text = [regex]::Replace($text, $pattern, "[assembly: $attribute(`"$assemblyVersion`")]")
        }
        Write-Utf8 $assemblyInfo $text
    }

    # Pin the otherwise floating Rhino 8 references for repeatable packaging.
    $legacyCsproj = Join-Path $legacyProject 'Nuclei3.csproj'
    [xml]$legacyXml = Get-Content -LiteralPath $legacyCsproj -Raw
    foreach ($reference in $legacyXml.SelectNodes('/Project/ItemGroup/PackageReference')) {
        if ($reference -and $reference.Include -in @('Grasshopper', 'RhinoCommon')) {
            $reference.Version = $rhinoSdkVersion
        }
    }
    $legacyXml.Save($legacyCsproj)

    # Generate the portable resource wrapper from the committed resx mapping.
    # Aliases share the same cached bitmap and its retained source stream.
    $resxPath = Join-Path $portableProject 'Properties/Resources.resx'
    [xml]$resx = Get-Content -LiteralPath $resxPath -Raw
    $iconMappings = @($resx.root.data | ForEach-Object {
        if ($_.name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or
            $_.type -notlike 'System.Resources.ResXFileRef,*') {
            throw "Unsupported portable resource: $($_.name)"
        }
        $relative = ([string]$_.value).Split(';')[0]
        $path = [IO.Path]::GetFullPath((Join-Path (Split-Path $resxPath -Parent) $relative))
        $resourceRoot = [IO.Path]::GetFullPath((Join-Path $portableProject 'Resources')) + [IO.Path]::DirectorySeparatorChar
        if (-not $path.StartsWith($resourceRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetExtension($path) -ne '.png' -or -not (Test-Path -LiteralPath $path)) {
            throw "Expected an existing PNG under Resources: $relative"
        }
        $fileName = [IO.Path]::GetFileName($path)
        if ($fileName -notmatch '^[A-Za-z0-9_]+\.png$') {
            throw "Unsupported resource filename: $fileName"
        }
        [pscustomobject]@{ Property = [string]$_.name; FileName = $fileName }
    })
    $properties = ($iconMappings | ForEach-Object {
        "        internal static Bitmap $($_.Property) => Load(`"$($_.FileName)`");"
    }) -join "`n"
    $resourceItems = ($iconMappings.FileName | Sort-Object -Unique | ForEach-Object {
        "    <EmbeddedResource Include=`"Resources\$_`" LogicalName=`"Nuclei3.Icons.$_`" />"
    }) -join "`n"
    $embeddedIcons = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Nuclei3.Properties
{
    // Generated from the original resx; raw PNGs avoid System.Resources.Extensions.
    internal static class Resources
    {
        private static readonly Dictionary<string, IconResource> Cache =
            new Dictionary<string, IconResource>(StringComparer.Ordinal);

        private static Bitmap Load(string fileName)
        {
            lock (Cache)
            {
                IconResource icon;
                if (!Cache.TryGetValue(fileName, out icon))
                {
                    icon = new IconResource(fileName);
                    Cache.Add(fileName, icon);
                }
                return icon.Image;
            }
        }

        private sealed class IconResource
        {
            // Bitmap(Stream) requires an open stream for the bitmap's lifetime.
            private readonly Stream source;
            internal Bitmap Image { get; }

            internal IconResource(string fileName)
            {
                source = typeof(Resources).Assembly.GetManifestResourceStream("Nuclei3.Icons." + fileName);
                if (source == null)
                    throw new InvalidOperationException("Missing embedded Nuclei icon: " + fileName);
                try { Image = new Bitmap(source); }
                catch { source.Dispose(); throw; }
            }
        }
__PROPERTIES__
    }
}
'@
    Write-Utf8 (Join-Path $portableProject 'Properties/EmbeddedIcons.cs') $embeddedIcons.Replace('__PROPERTIES__', $properties)
    $portableCsproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <OutputType>Library</OutputType>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <AssemblyName>Nuclei3</AssemblyName>
    <RootNamespace>Nuclei3</RootNamespace>
    <TargetExt>.gha</TargetExt>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateDependencyFile>false</GenerateDependencyFile>
    <AssemblyIcon>Nuclei.ico</AssemblyIcon>
    <Version>__VERSION__</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Grasshopper" Version="__RHINO_SDK__" ExcludeAssets="runtime" />
    <PackageReference Include="RhinoCommon" Version="__RHINO_SDK__" ExcludeAssets="runtime" />
    <!-- Rhino supplies its own Drawing and Windows Forms implementations on Mac. -->
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" ExcludeAssets="all" GeneratePathProperty="true" />
    <Reference Include="$(PkgMicrosoft_NETFramework_ReferenceAssemblies_net48)\build\.NETFramework\v4.8\System.Windows.Forms.dll" Private="False" />
    <PackageReference Include="System.Drawing.Common" Version="7.0.0" ExcludeAssets="runtime" />
  </ItemGroup>
  <ItemGroup>
    <Compile Remove="Properties\Settings.Designer.cs" />
    <Compile Remove="Properties\Resources.Designer.cs" />
    <EmbeddedResource Remove="Properties\Resources.resx" />
__RESOURCE_ITEMS__
  </ItemGroup>
</Project>
'@
    Write-Utf8 (Join-Path $portableProject 'Nuclei3.csproj') $portableCsproj.Replace('__VERSION__', $Version).Replace('__RHINO_SDK__', $rhinoSdkVersion).Replace('__RESOURCE_ITEMS__', $resourceItems)

    # The exported global.json controls both builds. Always suppress installation.
    Push-Location $source
    try {
        Invoke-Checked dotnet @('build', $legacyCsproj, '-c', 'Release', '-f', 'net48', '-m:1',
            '/nodeReuse:false', '-p:SkipGrasshopperInstall=true')
        Invoke-Checked dotnet @('build', (Join-Path $portableProject 'Nuclei3.csproj'), '-c', 'Release',
            '-f', 'net7.0', '-m:1', '/nodeReuse:false', '-p:SkipGrasshopperInstall=true')
    }
    finally { Pop-Location }

    $package = Join-Path $stage 'package'
    $legacyPackage = Join-Path $package 'net48'
    $modernPackage = Join-Path $package 'net7.0'
    New-Item -ItemType Directory -Path $legacyPackage, $modernPackage | Out-Null
    $legacyOutput = Join-Path $legacyProject 'bin/Release/net48'
    $modernOutput = Join-Path $portableProject 'bin/Release/net7.0'
    Copy-Item -LiteralPath (Join-Path $legacyOutput 'Nuclei3.gha') -Destination $legacyPackage
    # These are the legacy dependencies declared by the project's install target.
    foreach ($name in @('Microsoft.Bcl.HashCode.dll', 'System.Buffers.dll', 'System.Memory.dll',
        'System.Numerics.Vectors.dll', 'System.Resources.Extensions.dll', 'System.Runtime.CompilerServices.Unsafe.dll')) {
        $dependency = Join-Path $legacyOutput $name
        if (Test-Path -LiteralPath $dependency) {
            Copy-Item -LiteralPath $dependency -Destination $legacyPackage
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $legacyPackage 'System.Resources.Extensions.dll'))) {
        throw 'Legacy resource dependency is missing.'
    }
    if (@(Get-ChildItem -LiteralPath $modernOutput -Filter '*.dll').Count -ne 0) {
        throw 'Portable build unexpectedly produced companion DLLs; inspect before packaging.'
    }
    Copy-Item -LiteralPath (Join-Path $modernOutput 'Nuclei3.gha') -Destination $modernPackage
    foreach ($folder in @($legacyPackage, $modernPackage)) {
        $actualVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $folder 'Nuclei3.gha')).Version.ToString()
        if ($actualVersion -ne $assemblyVersion) { throw "Unexpected assembly version: $actualVersion" }
    }
    Copy-Item -LiteralPath $icon -Destination (Join-Path $package 'icon.png')
    $manifest = @'
---
name: Nuclei3
version: __VERSION__
authors:
  - Madalin Gheorghe
description: >-
  Nuclei is a generative-design plugin for Grasshopper that combines behavior-based particle simulations with highly customizable voxel environments. Inspired by slime-mold transport networks and ant foraging systems, it allows particles to respond to spatial fields, producing branching networks, evolving patterns, and volumetric structures.
  Nuclei 3 supports Rhino 8 and 9. Only in Rhino 9, its components display an "old v3" banner.
  For migration, uninstall the old shared Nuclei package, then install Nuclei2 and Nuclei3 separately to use both together.
url: https://www.food4rhino.com/en/app/nuclei
keywords:
  - guid:fe53d2b8-e56d-da70-cde9-0b078f8bc65d
  - grasshopper
  - generative-design
  - particles
  - voxels
  - slime-mold
  - ants
icon: icon.png
'@
    Write-Utf8 (Join-Path $package 'manifest.yml') $manifest.Replace('__VERSION__', $Version)
    Push-Location $package
    try { Invoke-Checked $YakPath @('build', '--platform', 'any') }
    finally { Pop-Location }
    $yakFiles = @(Get-ChildItem -LiteralPath $package -Filter '*.yak')
    if ($yakFiles.Count -ne 1 -or $yakFiles[0].Name -ine "nuclei3-$Version-rh8_0-any.yak") {
        throw 'Yak did not produce the expected Rhino 8 any-platform package.'
    }
    $builtFiles = @(Get-ChildItem -LiteralPath $package -File -Recurse | Sort-Object FullName |
        ForEach-Object {
            [ordered]@{ Path = [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/');
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    $provenance = [ordered]@{
        Version = $Version
        GitCommit = $commit
        SourceKind = $(if ($UseWorkingTree) { 'WorkingTree' } else { 'Commit' })
        RhinoSdkVersion = $rhinoSdkVersion
        IconOrigin = 'Unmodified approved icon from the Nuclei 3.0.0 Yak package'
        IconSha256 = $iconSha256
        SourceFiles = $sourceFiles
        PackagingTransforms = @('Version attributes in staged copies', 'Pinned Rhino SDK references',
            'Portable net7.0 project with host-provided APIs', 'Direct PNG resources generated from original resx')
        IconMappings = $iconMappings
        PackageFiles = $builtFiles
        YakPath = $yakFiles[0].FullName
    }
    Write-Utf8 (Join-Path $stage 'build-provenance.json') ($provenance | ConvertTo-Json -Depth 6)
    Write-Host "Built: $($yakFiles[0].FullName)"
    Write-Host "Provenance: $(Join-Path $stage 'build-provenance.json')"
    Write-Host 'Package is ready for runtime validation; it has not been installed or published.'
}
finally { Pop-Location }
