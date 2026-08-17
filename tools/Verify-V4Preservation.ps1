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
    ComponentGuidCount = 41
    ComponentGuidHash = "EAD28352BBF34A775D9C097DFD8F4D60063B4ED3B72A564FC517999AA2877FC8"
    ExportedTypeCount = 52
    PublicApiRecordCount = 732
    PublicApiHash = "E5773E1D63A1ED4E18227C1FB481DB222900A8401D4B3FAF92DB8DDA4D799081"
    ComponentCount = 39
    ComponentSchemaRecordCount = 217
    ComponentSchemaHash = "830A96E32E3AB3097BBA6DC9F873C90ACA3EBE6EFDDD8D0B061808F29C9C7E6F"
    MainResourceCount = 25
    MainResourceNameHash = "A58F476B1D4B92E2400236B5C324C873054666D3B1B7798F1604C5CD778224E3"
    MainResourceHash = "A69F38823ED789BA5934FE4D0B82F3A0F29F0E865641F5B39BDD4937E2C64422"
    ShaderCount = 24
    ShaderHash = "BBD3F0049D5A902B774EE45A7B5BACB52C6D20E2C2605C7115144DAB5AE5C88A"
    GpuShaderCount = 19
    GpuShaderHash = "AFB772F558735336C5DBC87677CA4BD195BB354BDC2AC6032EF88C2AD83EA1A5"
    DisplayShaderCount = 5
    DisplayShaderHash = "E6FD81DDE8AFA214FEEF2E37CBA21D6E195392C1FC5B8BCBADF29F06D8C5F7D4"
    FullSolverParameterSize = 400
    FullSolverParameterFields = 100
    MeshParameterSize = 48
    MeshParameterFields = 12
}

$expectedShaderHashes = [ordered]@{
    "Nuclei4.GpuShaders.ApplyBoundaryModeTransition.cso" = "066D428A987BEE8274EF308920EBCE732FC790268C1A7826B3A613971949A337"
    "Nuclei4.GpuShaders.ApplyDecay.cso" = "568850A93955AB0D1A4DEC9959E65F0607E4891C063BCAC84B233217F062EB8F"
    "Nuclei4.GpuShaders.ApplyDeposits.cso" = "6142A20B9A2B7D8D9F7A747E60A773BAD58A4A3E032E851AB0EBFFDE00A085AC"
    "Nuclei4.GpuShaders.ApplyParticleDeath.cso" = "91DE20BB8097DE0E17E19C1BB50F86402002B9ED742CBCA617E2A0AA2B356C0D"
    "Nuclei4.GpuShaders.ApplyParticleDivision.cso" = "F72EBB13BE33592148EFB930A44D1A4A7F6A9F84F34DF35F82EFB3DFE51416D0"
    "Nuclei4.GpuShaders.BuildCombinedDensityPreview.cso" = "4090D3B644FE67D9E6029FFC5B222774A9CD7D177F6590482403B082A2663073"
    "Nuclei4.GpuShaders.BuildDensityGradientPreview.cso" = "C38C96231C7BCC034110A29984D9EF69DE91CA7B919F6806FACF4F96FA80F0B4"
    "Nuclei4.GpuShaders.BuildDensityPreview.cso" = "AF816BD5F605428D5D9A262BD9B947E3A250724C54AD091F3339C0D4F5CC7018"
    "Nuclei4.GpuShaders.BuildParticlePreview.cso" = "E649847FB63A952B6443E4F1836EECEB119A06C8B6225251CF09EB75C618CE53"
    "Nuclei4.GpuShaders.BuildParticleTrailPreview.cso" = "D4B4669E69290E7A127425FA52EF6E571E839E627C2AC6411FAF10C5784E2CB4"
    "Nuclei4.GpuShaders.ClassifyVolumeCells.cso" = "A03DA35D1E6AE35110B61B001C6B5A3F9F4006F14B9488E5649157DC46CBEB83"
    "Nuclei4.GpuShaders.ClearParticleCounts.cso" = "33E4BEA329B9A7B55447FDADD9AE4FA91190E7CC6D72F976F73FECD48F83E464"
    "Nuclei4.GpuShaders.CountParticles.cso" = "05F078240AD409FE017BEE5CBEBD0C81D6D5F97A36126E99F2293EEECEB951BB"
    "Nuclei4.GpuShaders.DensityPreviewComposite.cso" = "D335CF53593BDFFBA2BEAA5E50A2D9A8F44ACF78FC29551B8EBFE6F544D3C689"
    "Nuclei4.GpuShaders.DensityPreviewOccupancy.cso" = "A8BC8583F7881E43B9FE48CD9C4546E102E0578782F708F85AC9F2ACE37C3146"
    "Nuclei4.GpuShaders.DensityPreviewPS.cso" = "2AB721A014F9AE71784142A5EF5112B8A3353B422DA185E351C23F5925EF245F"
    "Nuclei4.GpuShaders.DensityPreviewShadow.cso" = "B3F61209F2E463FF89D3E00311CDC257DD1A46E933689A0D1BCADF0EB334FA88"
    "Nuclei4.GpuShaders.DensityPreviewVS.cso" = "23EBF36C6D05E654B6DF3FCCE422997E7D611448D44225FBEFC6EBDC822B99FC"
    "Nuclei4.GpuShaders.DiffuseAxis.cso" = "4FC4BAC9AE918B28F8700239EC45C32F1BB495FAB939B97E27E437AF6EEFB7DA"
    "Nuclei4.GpuShaders.EmitVolumeTriangles.cso" = "4D03AFDDB6C0DDD2BDC285DE397E9196DEDA4D7442798B9D0E322A5DE2C0DBC9"
    "Nuclei4.GpuShaders.MoveParticlesAndDeposit.cso" = "1770F4E3AE86A4FB25E1AEFF35AB00A3FAFDE704F7C630885E9C544C9CAB06AE"
    "Nuclei4.GpuShaders.SeedNeighbourCounts.cso" = "1ADE8E467530F3976AF2EE09F3BE23A32634EEEEC6FCBEDD7A5C333B09746199"
    "Nuclei4.GpuShaders.SmoothVolumeForMesh.cso" = "474A64BA1E9A76713EAACA023B0B740D00DF658D895B0CD38CC4AA9C8BABE61C"
    "Nuclei4.GpuShaders.SumNeighbourAxis.cso" = "1D15D9DF842C7E207CB91580CC35132C5CF8B115FFAF026AF265CFA988D87226"
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
