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
    PublicApiRecordCount = 741
    PublicApiHash = "24B466B99A06FFEB9F24730E411EA53549639C70C8C4BEDAA17100905F8DE037"
    ComponentCount = 38
    ComponentSchemaRecordCount = 214
    ComponentSchemaHash = "2C82F4DDC84E50A154F6F48DAB9C1E1C82E3A4700F99146FC2DFF128E8518DDB"
    MainResourceCount = 28
    MainResourceNameHash = "5A304C5A9EE3117A8D33B999B27677FC0B5B98CA527657FC0A02E44DBE8993A7"
    MainResourceHash = "A76B6974498D3430B6140BE010AF04D570364F3725866B4DD455734F7253D602"
    ShaderCount = 27
    ShaderHash = "C005808A049C53BB021251D0D1C0FD16538FA945153E121AE6D7FECF4740BF83"
    GpuShaderCount = 22
    GpuShaderHash = "76A587C8C31492F0E597DD47A6A0B55A537F6368F7E460B17D1AFF0D1A7CBA9F"
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
    "Nuclei4.GpuShaders.AdvanceParticleAges.cso" = "2CB04C974ACC57DBD36C642AAEF613E7F527A9E87B7FDB28A83A65069B645F55"
    "Nuclei4.GpuShaders.ApplyBoundaryModeTransition.cso" = "38B4EB0C603DEE40DF7F699232DA28C1FE5D5FBBBAA58B54BA1158EA8AD0FE1A"
    "Nuclei4.GpuShaders.ApplyDecay.cso" = "B240272B2F8B628CAB4773110EED4569577ABE9F017C3A4049D9798FB4288A44"
    "Nuclei4.GpuShaders.ApplyDeposits.cso" = "4F74E47D92D520CF4039BE62A88C88E905656745A2A82E229571B87706EBFCB2"
    "Nuclei4.GpuShaders.ApplyParticleDeath.cso" = "497185664BCE555F07B856D4BA42C26ECF0EF04E933E3E0505244210A17CF0EC"
    "Nuclei4.GpuShaders.ApplyParticleDivision.cso" = "41A46FC86062D15C6D9B3088CC4DBCDFC0C98F9CD27FA39E859775818D2AB0C0"
    "Nuclei4.GpuShaders.BuildCombinedDensityPreview.cso" = "51ADE67292000D4D6F14C2959948E86F9D7EE14024D55378D15A380890A46AB1"
    "Nuclei4.GpuShaders.BuildDensityGradientPreview.cso" = "337A3E2D32791C157C5CFC4DA265F440EBD16C91A5257B5D47AACDE88DB04361"
    "Nuclei4.GpuShaders.BuildDensityPreview.cso" = "4E174B493379C39D29BD0D6E92B54BEC8285E0626AB4F4DEBBA988318C850769"
    "Nuclei4.GpuShaders.BuildParticlePreview.cso" = "62694E8D1874B8DB950EE1EB368BCCEB67FB1CA226D91B3935E3818712ED6FA1"
    "Nuclei4.GpuShaders.BuildParticleTrailPreview.cso" = "CE12342F1A54C9EADCC9A4659C37FAC9E8D355D75B149A629803FFB3B57A977C"
    "Nuclei4.GpuShaders.ClassifyVolumeCells.cso" = "A03DA35D1E6AE35110B61B001C6B5A3F9F4006F14B9488E5649157DC46CBEB83"
    "Nuclei4.GpuShaders.ClearParticleCounts.cso" = "B897D598443626092911F3C6ED565C153D2F492CAB5423E1C4125888A997D3B0"
    "Nuclei4.GpuShaders.CountParticles.cso" = "815E7A1AF6EC98A66FECFEC14B7E0026D79E2E3D90308F896B87AAAA5D56B7B9"
    "Nuclei4.GpuShaders.DensityPreviewComposite.cso" = "D335CF53593BDFFBA2BEAA5E50A2D9A8F44ACF78FC29551B8EBFE6F544D3C689"
    "Nuclei4.GpuShaders.DensityPreviewOccupancy.cso" = "A8BC8583F7881E43B9FE48CD9C4546E102E0578782F708F85AC9F2ACE37C3146"
    "Nuclei4.GpuShaders.DensityPreviewPS.cso" = "8A0990EC6CA082044E898F8204F3AFE04FC7ED7891A278E0DE25F545B4DE2E4C"
    "Nuclei4.GpuShaders.DensityPreviewShadow.cso" = "B3F61209F2E463FF89D3E00311CDC257DD1A46E933689A0D1BCADF0EB334FA88"
    "Nuclei4.GpuShaders.DensityPreviewVS.cso" = "23EBF36C6D05E654B6DF3FCCE422997E7D611448D44225FBEFC6EBDC822B99FC"
    "Nuclei4.GpuShaders.DiffuseAxis.cso" = "D1E283E2C4CB0F7ABAF60D5A06F3B71CDB9151B8D8700250FCE4571CF3C60A04"
    "Nuclei4.GpuShaders.EmitVolumeTriangles.cso" = "4D03AFDDB6C0DDD2BDC285DE397E9196DEDA4D7442798B9D0E322A5DE2C0DBC9"
    "Nuclei4.GpuShaders.MoveAntParticlesAndDeposit.cso" = "55F5A9AB23D458E9A5B4012E7B9B267C17FF1FFC7BE328862A53BE756DF994EE"
    "Nuclei4.GpuShaders.MoveParticlesAndDeposit.cso" = "83D9B6B5EF977163ED72A5BA6A128A0A662D8009540370FA09DC600F3E343E42"
    "Nuclei4.GpuShaders.ProjectFoodSources.cso" = "EBA2BFE646728DB5A6B3D0027A98DDF361738EC9C09B483A183C3A6788531600"
    "Nuclei4.GpuShaders.SeedNeighbourCounts.cso" = "93DC8E10D67B82AFC24313810ECFAADC25921EFBB1ECF4BDDCF6AC12D772D510"
    "Nuclei4.GpuShaders.SmoothVolumeForMesh.cso" = "34129D9DB7C789296F2A55945C58CE580F1A3D8E572DE1065390965C695F5595"
    "Nuclei4.GpuShaders.SumNeighbourAxis.cso" = "F06E0C0D6F9687B836A4806B3C60B6A55D0722CCB58015E8547111228C9F906E"
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

$targetFrameworkAttribute = $mainAssembly.GetCustomAttributes(
    [Runtime.Versioning.TargetFrameworkAttribute],
    $false) | Select-Object -First 1
if ($null -eq $targetFrameworkAttribute) {
    $failures.Add("assembly target framework metadata missing")
}
elseif ($targetFrameworkAttribute.FrameworkName.StartsWith(".NETCoreApp,", [StringComparison]::Ordinal)) {
    if (Test-Path -LiteralPath (Join-Path $BuildDirectory "Nuclei4.deps.json")) {
        $passes.Add("deployment file present: Nuclei4.deps.json")
    }
    else {
        $failures.Add("deployment file missing: Nuclei4.deps.json")
    }
}
elseif ($targetFrameworkAttribute.FrameworkName.StartsWith(".NETFramework,", [StringComparison]::Ordinal)) {
    # SDK-style .NET Framework class-library builds do not emit deps.json. Their
    # deployed managed closure is still verified below from assembly references.
    $passes.Add("framework-appropriate deployment metadata: $($targetFrameworkAttribute.FrameworkName)")
}
else {
    $failures.Add("unsupported assembly target framework: $($targetFrameworkAttribute.FrameworkName)")
}

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
