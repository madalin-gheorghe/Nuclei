[CmdletBinding()]
param(
    [string]$BuildDirectory = "",
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $repositoryRoot "Nuclei-v4\Nuclei4\bin\Release\net7.0-windows"
}
$BuildDirectory = (Resolve-Path -LiteralPath $BuildDirectory).Path

$expected = [ordered]@{
    AssemblyName = "Nuclei4"
    AssemblyVersion = "4.1.0.0"
    AssemblyInfoId = "a4810f34-10b6-480c-a6d0-607aac4e8d2a"
    TypeLibraryGuid = "67e300d2-061e-4987-b6e6-ffbb4810a624"
    ComponentGuidCount = 40
    ComponentGuidHash = "BA5DD56D2DB434E2FEEC0AD489F1DF481FAFC4FA2E3843C3C21E49DED4DCB126"
    ExportedTypeCount = 51
    PublicApiRecordCount = 728
    PublicApiHash = "1ADB075EA91D2B043F890CF2249A57EDBB62A1076BE295736C10DAAA0F0AA433"
    ComponentCount = 38
    ComponentSchemaRecordCount = 215
    ComponentSchemaHash = "83EDE30503D7F16B5EF4788AE0EF7C4E58EA6DFCCB1A1BC0032B5DB08DA2F70F"
    MainResourceCount = 26
    MainResourceNameHash = "5DFD765D509A50F5942F8E0C8758AD2F8DEC6319AAD2B2A24FF9311798FB77C7"
    MainResourceHash = "305C30BCD4E18CC281178DB6F3C01E1266FEBEBFADE6D2C8103D9ED2DBD2AE26"
    ShaderCount = 25
    ShaderHash = "CFE49D9298411644D315E7F7287F639E87A58119E28142D9A9780B3218467592"
    GpuShaderCount = 20
    GpuShaderHash = "5804E55E013B16BEC25ECB90F390C5C8E2916FFB36D252AFAA5C9530F71625E3"
    DisplayShaderCount = 5
    DisplayShaderHash = "7962C5E6C8BCAE08EEB74E649239601E884515546EA8E8C926A809224896C695"
    FullSolverParameterSize = 416
    FullSolverParameterFields = 104
    MeshParameterSize = 48
    MeshParameterFields = 12
}

# Per-shader hashes. The full authorization history for every intentional
# change lives in docs/V4_PRESERVATION_CONTRACT.md.
$expectedShaderHashes = [ordered]@{
    "Nuclei4.GpuShaders.ApplyBoundaryModeTransition.cso" = "A8EEEA7FDB19BE7C956CD3616985046271E35F3BE6B6AE6B961CF43D218C6E66"
    "Nuclei4.GpuShaders.ApplyDecay.cso" = "95CF08F35F24B5301BD0E7691834E1987F869F7D14BD197FDA97FDB1EECB6A6C"
    "Nuclei4.GpuShaders.ApplyDeposits.cso" = "D57C35725B5C2D43336B3E91417519C57557CD83C4E17D593A37FD47A6B9F174"
    "Nuclei4.GpuShaders.ApplyParticleDeath.cso" = "2C93605F0DFB4EEFF56DAA8F79D213C369999221D49910E0E5E2C32FFD5C82AF"
    "Nuclei4.GpuShaders.ApplyParticleDivision.cso" = "97D8910455F44BA29BDA13A1F2277CE87D582C17EE0692B44275F6736782B113"
    "Nuclei4.GpuShaders.BuildCombinedDensityPreview.cso" = "14AEF84D5173D234A0A9AB71F443A4FB64353FB3690869BC8228B9E4B616235B"
    "Nuclei4.GpuShaders.BuildDensityGradientPreview.cso" = "F8578CFDC34B499080FB20C56F9D51CA887089FED6112F2E0F24E6A2B4E042DA"
    "Nuclei4.GpuShaders.BuildDensityPreview.cso" = "8ECF654EB3C1963DA529819DD47B56D18124DC0BF20329D21B662169BEB18E6E"
    "Nuclei4.GpuShaders.BuildParticlePreview.cso" = "409005CB547E04EE204EA04A313EEE1FC355435F6AC72EDD56F6C56C6AD813AC"
    "Nuclei4.GpuShaders.BuildParticleTrailPreview.cso" = "9F1542D6ADC14EC6BCA8181B5D1BF9CEDF01626518DD8A618DAC623F5BEBF878"
    "Nuclei4.GpuShaders.ClassifyVolumeCells.cso" = "A03DA35D1E6AE35110B61B001C6B5A3F9F4006F14B9488E5649157DC46CBEB83"
    "Nuclei4.GpuShaders.ClearParticleCounts.cso" = "93708D7F9151EE659C3FDB718854C482F9F1628683DECE73F6B5504A7385D1ED"
    "Nuclei4.GpuShaders.CountParticles.cso" = "878DE2D2911DE86E4A5C5E6BB63F9492821C9ED02A30D3704679A121D1FAF0AF"
    "Nuclei4.GpuShaders.DensityPreviewComposite.cso" = "D335CF53593BDFFBA2BEAA5E50A2D9A8F44ACF78FC29551B8EBFE6F544D3C689"
    "Nuclei4.GpuShaders.DensityPreviewOccupancy.cso" = "A8BC8583F7881E43B9FE48CD9C4546E102E0578782F708F85AC9F2ACE37C3146"
    "Nuclei4.GpuShaders.DensityPreviewPS.cso" = "8A0990EC6CA082044E898F8204F3AFE04FC7ED7891A278E0DE25F545B4DE2E4C"
    "Nuclei4.GpuShaders.DensityPreviewShadow.cso" = "B3F61209F2E463FF89D3E00311CDC257DD1A46E933689A0D1BCADF0EB334FA88"
    "Nuclei4.GpuShaders.DensityPreviewVS.cso" = "23EBF36C6D05E654B6DF3FCCE422997E7D611448D44225FBEFC6EBDC822B99FC"
    "Nuclei4.GpuShaders.DiffuseAxis.cso" = "8C94DE636B272334CACEA3B0EEE6222402AD27525E246E2BB09396A8ADDCD334"
    "Nuclei4.GpuShaders.EmitVolumeTriangles.cso" = "4D03AFDDB6C0DDD2BDC285DE397E9196DEDA4D7442798B9D0E322A5DE2C0DBC9"
    "Nuclei4.GpuShaders.MoveParticlesAndDeposit.cso" = "3F235146E7103B594DB69F6D2E247CE70D9D61705BAA1E8499C1C8116833D68C"
    "Nuclei4.GpuShaders.ProjectFoodSources.cso" = "CCB9D425A2904D1BDFB1CC61DF59E74DBDE5C923463A0700DB9EA2C6D071E786"
    "Nuclei4.GpuShaders.SeedNeighbourCounts.cso" = "BB8E58AD51A26088A50B34F1E9B72A02BC94AF171587BB670B045C1203D15AC1"
    "Nuclei4.GpuShaders.SmoothVolumeForMesh.cso" = "34129D9DB7C789296F2A55945C58CE580F1A3D8E572DE1065390965C695F5595"
    "Nuclei4.GpuShaders.SumNeighbourAxis.cso" = "A0E276F6A6508CA9BD7A1EFD857714F8057C8E1117AF10D3CD5B8FE29DC968C8"
}

$failures = [System.Collections.Generic.List[string]]::new()
$passes = [System.Collections.Generic.List[string]]::new()

function Get-TextHash([string]$text) {
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}

function Test-Equal([string]$label, $actual, $wanted) {
    if ($actual -eq $wanted) {
        $passes.Add("$label = $actual")
    }
    else {
        $failures.Add("${label}: expected '$wanted', got '$actual'")
    }
}

function Load-AssemblyPath([Runtime.Loader.AssemblyLoadContext]$context, [string]$path) {
    try {
        return $context.LoadFromAssemblyPath((Resolve-Path -LiteralPath $path).Path)
    }
    catch [System.IO.FileLoadException] {
        $name = [Reflection.AssemblyName]::GetAssemblyName($path).Name
        return $context.Assemblies | Where-Object { $_.GetName().Name -eq $name } | Select-Object -First 1
    }
}

function Get-PackageFile([string]$packageRoot, [string]$packageName, [string]$fileName) {
    $packageDirectory = Join-Path $packageRoot $packageName
    $candidate = Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Filter $fileName -ErrorAction Stop |
        Where-Object { $_.FullName -match "[\\/]lib[\\/]net48[\\/]" } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "Could not locate $fileName in NuGet package $packageName."
    }
    return $candidate.FullName
}

function Get-ResourceRows([Reflection.Assembly]$assembly) {
    $rows = [System.Collections.Generic.List[string]]::new()
    foreach ($name in ($assembly.GetManifestResourceNames() | Sort-Object)) {
        $stream = $assembly.GetManifestResourceStream($name)
        try {
            $hasher = [Security.Cryptography.SHA256]::Create()
            try {
                $hash = [Convert]::ToHexString($hasher.ComputeHash($stream))
            }
            finally {
                $hasher.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
        $rows.Add("$name $hash")
    }
    return $rows
}

function Get-VisibleApiRecords([Reflection.Assembly]$assembly) {
    $flags = [Reflection.BindingFlags]"Instance,Static,Public,NonPublic,DeclaredOnly"
    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($type in ($assembly.GetExportedTypes() | Sort-Object FullName)) {
        $records.Add("T|$($type.FullName)|base=$($type.BaseType.FullName)")
        $members = $type.GetMembers($flags) | Where-Object {
            if ($_ -is [Reflection.MethodBase]) {
                $_.IsPublic -or $_.IsFamily -or $_.IsFamilyOrAssembly
            }
            elseif ($_ -is [Reflection.FieldInfo]) {
                $_.IsPublic -or $_.IsFamily -or $_.IsFamilyOrAssembly
            }
            elseif ($_ -is [Reflection.PropertyInfo]) {
                ($null -ne $_.GetMethod -and ($_.GetMethod.IsPublic -or $_.GetMethod.IsFamily -or $_.GetMethod.IsFamilyOrAssembly)) -or
                ($null -ne $_.SetMethod -and ($_.SetMethod.IsPublic -or $_.SetMethod.IsFamily -or $_.SetMethod.IsFamilyOrAssembly))
            }
            elseif ($_ -is [Reflection.EventInfo]) {
                $null -ne $_.AddMethod -and ($_.AddMethod.IsPublic -or $_.AddMethod.IsFamily -or $_.AddMethod.IsFamilyOrAssembly)
            }
            else {
                $false
            }
        } | Sort-Object MemberType, Name, @{ Expression = { $_.ToString() } }

        foreach ($member in $members) {
            $records.Add("M|$($type.FullName)|$($member.MemberType)|$($member.ToString())")
        }
    }
    return $records
}

function Get-ComponentGuidRecords([Reflection.Assembly]$assembly) {
    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($type in $assembly.GetExportedTypes()) {
        $property = $type.GetProperty("ComponentGuid", [Reflection.BindingFlags]"Instance,Public")
        if ($null -eq $property -or $type.IsAbstract) {
            continue
        }
        $instance = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($type)
        $guid = [Guid]$property.GetValue($instance)
        $records.Add("$($type.Name) $($guid.ToString().ToLowerInvariant())")
    }
    return @($records | Sort-Object)
}

function Get-ComponentSchemaRecords(
    [Reflection.Assembly]$assembly,
    [Type]$componentBaseType) {
    $records = [System.Collections.Generic.List[string]]::new()
    $componentTypes = $assembly.GetExportedTypes() |
        Where-Object { $_.IsSubclassOf($componentBaseType) -and -not $_.IsAbstract } |
        Sort-Object FullName

    foreach ($type in $componentTypes) {
        # This constructor touches Rhino native geometry outside Rhino. Its static
        # catalogue data is intentionally represented exactly as in the baseline.
        if ($type.FullName -eq "Nuclei4.Preview_Particle") {
            $records.Add("C|Nuclei4.Preview_Particle|60649521-0784-4a2e-8dfa-27e4a04600ac|Particle Preview|Particle Preview|Displays particles in the Rhino viewport|Nuclei4|Preview|secondary")
            $records.Add("I|0|Grasshopper.Kernel.Parameters.Param_GenericObject|Particles|particles|Input Particles|item|optional=False|mapping=None|hidden=False|reverse=False|simplify=False|defaults=")
            $records.Add("I|1|Grasshopper.Kernel.Parameters.Param_Number|Point Size|size|Point Display Size|item|optional=True|mapping=None|hidden=False|reverse=False|simplify=False|defaults=Grasshopper.Kernel.Types.GH_Number:2")
            continue
        }

        $component = [Activator]::CreateInstance($type)
        $records.Add("C|$($type.FullName)|$($component.ComponentGuid.ToString().ToLowerInvariant())|$($component.Name)|$($component.NickName)|$($component.Description)|$($component.Category)|$($component.SubCategory)|$($component.Exposure)")

        foreach ($direction in @("Input", "Output")) {
            $parameters = @($component.Params.$direction)
            for ($index = 0; $index -lt $parameters.Count; $index++) {
                $parameter = $parameters[$index]
                $defaults = ""
                $persistentDataProperty = $parameter.GetType().GetProperty("PersistentData")
                if ($null -ne $persistentDataProperty) {
                    $persistentData = $persistentDataProperty.GetValue($parameter)
                    if ($null -ne $persistentData) {
                        $defaults = (@($persistentData.AllData($true)) | ForEach-Object {
                            "$($_.GetType().FullName):$($_.ToString())"
                        }) -join ","
                    }
                }
                $prefix = if ($direction -eq "Input") { "I" } else { "O" }
                $optional = Get-OptionalPropertyValue $parameter "Optional"
                $mapping = Get-OptionalPropertyValue $parameter "DataMapping"
                $hidden = Get-OptionalPropertyValue $parameter "Hidden"
                $reverse = Get-OptionalPropertyValue $parameter "Reverse"
                $simplify = Get-OptionalPropertyValue $parameter "Simplify"
                $records.Add("$prefix|$index|$($parameter.GetType().FullName)|$($parameter.Name)|$($parameter.NickName)|$($parameter.Description)|$($parameter.Access)|optional=$optional|mapping=$mapping|hidden=$hidden|reverse=$reverse|simplify=$simplify|defaults=$defaults")
            }
        }
    }
    return [pscustomobject]@{
        Components = @($componentTypes).Count
        Records = $records
    }
}

function Get-UnmanagedSize([Type]$type) {
    $method = [Runtime.InteropServices.Marshal].GetMethod("SizeOf", [Type[]]@([Type]))
    return [int]$method.Invoke($null, @($type))
}

function Get-OptionalPropertyValue($target, [string]$name) {
    $property = $target.PSObject.Properties[$name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

$mainPath = Join-Path $BuildDirectory "Nuclei4.gha"
foreach ($requiredFile in @(
    "Nuclei4.gha",
    "Nuclei4.deps.json",
    "Nuclei4.Core.dll",
    "Nuclei4.Gpu.Abstractions.dll",
    "Nuclei4.Gpu.D3D11.dll",
    "Nuclei4.Display.Abstractions.dll",
    "Nuclei4.Display.D3D11.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $BuildDirectory $requiredFile))) {
        $failures.Add("deployment file missing: $requiredFile")
    }
}

$packageRoot = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    $env:NUGET_PACKAGES
}
else {
    Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ".nuget\packages"
}

$context = [Runtime.Loader.AssemblyLoadContext]::Default
$rhinoPath = Get-PackageFile $packageRoot "rhinocommon" "RhinoCommon.dll"
$grasshopperPath = Get-PackageFile $packageRoot "grasshopper" "Grasshopper.dll"
$ghIoPath = Join-Path (Split-Path -Parent $grasshopperPath) "GH_IO.dll"
foreach ($dependency in @($rhinoPath, $ghIoPath, $grasshopperPath)) {
    [void](Load-AssemblyPath $context $dependency)
}
foreach ($dependency in (Get-ChildItem -LiteralPath $BuildDirectory -File -Filter "*.dll" | Sort-Object Name)) {
    [void](Load-AssemblyPath $context $dependency.FullName)
}

$mainAssembly = Load-AssemblyPath $context $mainPath
$gpuAssembly = $context.Assemblies | Where-Object { $_.GetName().Name -eq "Nuclei4.Gpu.D3D11" } | Select-Object -First 1
$displayAssembly = $context.Assemblies | Where-Object { $_.GetName().Name -eq "Nuclei4.Display.D3D11" } | Select-Object -First 1
$grasshopperAssembly = $context.Assemblies | Where-Object { $_.GetName().Name -eq "Grasshopper" } | Select-Object -First 1

Test-Equal "assembly name" $mainAssembly.GetName().Name $expected.AssemblyName
Test-Equal "assembly version" $mainAssembly.GetName().Version.ToString() $expected.AssemblyVersion

$infoType = $mainAssembly.GetType("Nuclei4.Nuclei4Info", $true)
$info = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($infoType)
Test-Equal "GH assembly ID" $infoType.GetProperty("Id").GetValue($info).ToString().ToLowerInvariant() $expected.AssemblyInfoId

$guidAttribute = $mainAssembly.GetCustomAttributes([Runtime.InteropServices.GuidAttribute], $false) | Select-Object -First 1
Test-Equal "type-library GUID" $guidAttribute.Value.ToLowerInvariant() $expected.TypeLibraryGuid

$guidRecords = Get-ComponentGuidRecords $mainAssembly
$guidText = ($guidRecords -join "`n") + "`n"
Test-Equal "component/parameter GUID count" @($guidRecords).Count $expected.ComponentGuidCount
Test-Equal "component/parameter GUID hash" (Get-TextHash $guidText) $expected.ComponentGuidHash

$apiRecords = Get-VisibleApiRecords $mainAssembly
$apiText = ($apiRecords -join "`n") + "`n"
Test-Equal "exported public type count" $mainAssembly.GetExportedTypes().Count $expected.ExportedTypeCount
Test-Equal "public API record count" $apiRecords.Count $expected.PublicApiRecordCount
Test-Equal "public API hash" (Get-TextHash $apiText) $expected.PublicApiHash

$componentBaseType = $grasshopperAssembly.GetType("Grasshopper.Kernel.GH_Component", $true)
$schema = Get-ComponentSchemaRecords $mainAssembly $componentBaseType
$schemaText = ($schema.Records -join "`n") + "`n"
Test-Equal "GH component count" $schema.Components $expected.ComponentCount
Test-Equal "GH component schema record count" $schema.Records.Count $expected.ComponentSchemaRecordCount
Test-Equal "GH component schema hash" (Get-TextHash $schemaText) $expected.ComponentSchemaHash

$mainRows = @(Get-ResourceRows $mainAssembly)
$mainNames = @($mainRows | ForEach-Object { $_.Substring(0, $_.LastIndexOf(" ")) })
$mainShaderRows = @($mainRows | Where-Object { $_ -like "*.cso *" })
Test-Equal "main resource count" $mainRows.Count $expected.MainResourceCount
Test-Equal "main resource-name hash" (Get-TextHash (($mainNames -join "`n") + "`n")) $expected.MainResourceNameHash
Test-Equal "main resource-content hash" (Get-TextHash (($mainRows -join "`n") + "`n")) $expected.MainResourceHash
Test-Equal "main shader count" $mainShaderRows.Count $expected.ShaderCount
Test-Equal "main shader hash" (Get-TextHash (($mainShaderRows -join "`n") + "`n")) $expected.ShaderHash

$mainShaderMap = @{}
foreach ($row in $mainShaderRows) {
    $separator = $row.LastIndexOf(" ")
    $mainShaderMap[$row.Substring(0, $separator)] = $row.Substring($separator + 1)
}
foreach ($shaderName in $expectedShaderHashes.Keys) {
    if (-not $mainShaderMap.ContainsKey($shaderName)) {
        $failures.Add("main shader missing: $shaderName")
    }
    elseif ($mainShaderMap[$shaderName] -ne $expectedShaderHashes[$shaderName]) {
        $failures.Add("main shader changed: $shaderName expected $($expectedShaderHashes[$shaderName]), got $($mainShaderMap[$shaderName])")
    }
}

$gpuRows = @(Get-ResourceRows $gpuAssembly)
$displayRows = @(Get-ResourceRows $displayAssembly)
Test-Equal "D3D11 compute support shader count" $gpuRows.Count $expected.GpuShaderCount
Test-Equal "D3D11 compute support shader hash" (Get-TextHash (($gpuRows -join "`n") + "`n")) $expected.GpuShaderHash
Test-Equal "D3D11 display support shader count" $displayRows.Count $expected.DisplayShaderCount
Test-Equal "D3D11 display support shader hash" (Get-TextHash (($displayRows -join "`n") + "`n")) $expected.DisplayShaderHash

$supportRows = @($gpuRows + $displayRows | Sort-Object)
foreach ($row in $supportRows) {
    $separator = $row.LastIndexOf(" ")
    $name = $row.Substring(0, $separator)
    $hash = $row.Substring($separator + 1)
    if (-not $expectedShaderHashes.Contains($name)) {
        $failures.Add("unexpected support shader: $name")
    }
    elseif ($expectedShaderHashes[$name] -ne $hash) {
        $failures.Add("support shader changed: $name expected $($expectedShaderHashes[$name]), got $hash")
    }
}
Test-Equal "support shader union count" $supportRows.Count $expected.ShaderCount
Test-Equal "support shader union hash" (Get-TextHash (($supportRows -join "`n") + "`n")) $expected.ShaderHash

$solverParameters = $gpuAssembly.GetType("Nuclei4.GpuFullSlimeSolverEngine+FullSolverParameters", $true)
$meshParameters = $gpuAssembly.GetType("Nuclei4.GpuFullSlimeSolverEngine+GpuMeshParameters", $true)
$instanceFields = [Reflection.BindingFlags]"Instance,Public,NonPublic"
Test-Equal "FullSolverParameters size" (Get-UnmanagedSize $solverParameters) $expected.FullSolverParameterSize
Test-Equal "FullSolverParameters field count" $solverParameters.GetFields($instanceFields).Count $expected.FullSolverParameterFields
Test-Equal "GpuMeshParameters size" (Get-UnmanagedSize $meshParameters) $expected.MeshParameterSize
Test-Equal "GpuMeshParameters field count" $meshParameters.GetFields($instanceFields).Count $expected.MeshParameterFields

$deployedNames = @{}
$deployedNames["Nuclei4"] = "Nuclei4.gha"
foreach ($file in (Get-ChildItem -LiteralPath $BuildDirectory -File -Filter "*.dll")) {
    try {
        $deployedNames[[Reflection.AssemblyName]::GetAssemblyName($file.FullName).Name] = $file.Name
    }
    catch {
        # Native or invalid assemblies are not managed dependency candidates.
    }
}
$requiredDeploymentPrefixes = @("Nuclei4.", "Vortice.", "SharpGen.")
$managedDeploymentAssemblies = @($mainAssembly) + @($context.Assemblies | Where-Object {
    $_.GetName().Name -like "Nuclei4.*"
})
$missingReferences = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($assembly in $managedDeploymentAssemblies) {
    foreach ($reference in $assembly.GetReferencedAssemblies()) {
        $requiresDeployment = $false
        foreach ($prefix in $requiredDeploymentPrefixes) {
            if ($reference.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                $requiresDeployment = $true
                break
            }
        }
        if ($requiresDeployment -and -not $deployedNames.ContainsKey($reference.Name)) {
            [void]$missingReferences.Add("$($assembly.GetName().Name) -> $($reference.Name)")
        }
    }
}
if ($missingReferences.Count -eq 0) {
    $passes.Add("clean-folder managed dependency closure")
}
else {
    foreach ($missingReference in ($missingReferences | Sort-Object)) {
        $failures.Add("deployment dependency missing: $missingReference")
    }
}

if (-not $Quiet) {
    foreach ($pass in $passes) {
        Write-Host "PASS $pass" -ForegroundColor Green
    }
}
foreach ($failure in $failures) {
    Write-Host "FAIL $failure" -ForegroundColor Red
}

if ($failures.Count -gt 0) {
    Write-Host "V4 preservation verification failed with $($failures.Count) delta(s)." -ForegroundColor Red
    exit 1
}

Write-Host "V4 preservation verification passed ($($passes.Count) contracts)." -ForegroundColor Green
exit 0
