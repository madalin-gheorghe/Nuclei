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
    ComponentSchemaHash = "5D674B2C4231A47404527DA721A6E7B8C14BF256F5FA199E846ACF38CDF09841"
    MainResourceCount = 33
    MainResourceNameHash = "3DD5871F8952055EC8C9E3AFB170EBA23CAA67B37561D371756DB29B74875506"
    MainResourceHash = "D8C079CEB0B1EEF2A7BB2B9859A0DF9569BC9BB620F159A4BC8DD2635CE61A54"
    ShaderCount = 32
    ShaderHash = "DC10E229A6D90B2667B3674B93B9343D224099F7C172DE7FF4C46D6787C87E03"
    GpuShaderCount = 27
    GpuShaderHash = "A8E7FC0EA823EB56789E8BA5CA70B015B5B3854E14D76F7849ABDE1D23CCED8B"
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
    "Nuclei4.GpuShaders.AdvanceParticleAges.cso" = "DF134CA4CCEBBBA2C01AB4EC5428FAA598CD7896A1ED585430E06CFE0F96CC67"
    "Nuclei4.GpuShaders.ApplyBoundaryModeTransition.cso" = "CC6787DEA5BF769401BA92480959A37E414BF698B69AFC895B9F06787A4FC80F"
    "Nuclei4.GpuShaders.ApplyDecay.cso" = "56F5575B57195C21C5D4E86F6C20718B5E07853654599216FF100EF42983B2E0"
    "Nuclei4.GpuShaders.ApplyDeposits.cso" = "DF968D3D3DCF58CD94C056B7082F183D73720EED94A1C9B0842F8270BC6845B1"
    "Nuclei4.GpuShaders.ApplyParticleDeath.cso" = "715579700349D10590C0F9DB3E93D68A799D7869A211B21A8F379D4F16A99C7F"
    "Nuclei4.GpuShaders.ApplyParticleDivision.cso" = "B72610AC94DA0A906A05D6C272F77A560E2C86BA3A79639B5D17F42CEE741AB7"
    "Nuclei4.GpuShaders.BuildCombinedDensityPreview.cso" = "A6000957F8FFF219CC0C3CDEEB960EE56DBB92414EAE981CF900D3E69C7AC968"
    "Nuclei4.GpuShaders.BuildDensityGradientPreview.cso" = "FA8D41AE29B1272ADB41EFEBB8C029F612723C73CE84573E72CA88F1AB65DA7E"
    "Nuclei4.GpuShaders.BuildDensityPreview.cso" = "D2B15A698DF1C9920FE644D7D4AB1D67F970E2A69EC312256DEA5BC7C391FBC0"
    "Nuclei4.GpuShaders.BuildParticlePreview.cso" = "8D730481938400B833F828A06AF96F3FDABAA6EA4E1543A24F8B39906CB99BE1"
    "Nuclei4.GpuShaders.BuildParticleTrailPreview.cso" = "219286A88CFBB60C1097F93AE20A5DD1CDFDD8341E7AFAFF5D49BF8068A9CF6E"
    "Nuclei4.GpuShaders.ClaimParticleOwners.cso" = "85654593D1FB5219009BEA77F5AA9D3638B26A9671BA795CE1A78E7B93E03C9F"
    "Nuclei4.GpuShaders.ClassifyVolumeCells.cso" = "A03DA35D1E6AE35110B61B001C6B5A3F9F4006F14B9488E5649157DC46CBEB83"
    "Nuclei4.GpuShaders.ClearParticleCounts.cso" = "422D876612A899CB9ABE3C4E57FEDB2CA03D6CE7A79066E7D2FD27BAE3A8942A"
    "Nuclei4.GpuShaders.CountParticles.cso" = "BA941187632B00E1D036BF0D1FABFE41CD3E20CF46ACA9E53E9A02B5DDFBA2EA"
    "Nuclei4.GpuShaders.CullParticleOwnerConflicts.cso" = "427EEB5EA0B7AE47A503AC86CF724D6CE68601CEE4FE1873E2E275C7A9CF98B0"
    "Nuclei4.GpuShaders.DensityPreviewComposite.cso" = "D335CF53593BDFFBA2BEAA5E50A2D9A8F44ACF78FC29551B8EBFE6F544D3C689"
    "Nuclei4.GpuShaders.DensityPreviewOccupancy.cso" = "A8BC8583F7881E43B9FE48CD9C4546E102E0578782F708F85AC9F2ACE37C3146"
    "Nuclei4.GpuShaders.DensityPreviewPS.cso" = "8A0990EC6CA082044E898F8204F3AFE04FC7ED7891A278E0DE25F545B4DE2E4C"
    "Nuclei4.GpuShaders.DensityPreviewShadow.cso" = "B3F61209F2E463FF89D3E00311CDC257DD1A46E933689A0D1BCADF0EB334FA88"
    "Nuclei4.GpuShaders.DensityPreviewVS.cso" = "23EBF36C6D05E654B6DF3FCCE422997E7D611448D44225FBEFC6EBDC822B99FC"
    "Nuclei4.GpuShaders.DiffuseAxis.cso" = "A3DA03621BDBC95BA2D777D60D83B2A1F46BD44DA9CCC0F8A4132467E8593D83"
    "Nuclei4.GpuShaders.DiffuseAxisXTiled.cso" = "579C7FA5D4F574F4C5B5CBEB9FDA884CB61AB82C549CBEF6FB7B7FECDB4BF817"
    "Nuclei4.GpuShaders.DiffuseAxisYTiled.cso" = "6B99D92AE3A4B5D32473A67108B2B3F8ECD1FA1AFA3A6631A3EC5062908BC2F4"
    "Nuclei4.GpuShaders.DiffuseAxisZTiled.cso" = "87ABDD0662D60D47AE1F6D47BECDA2B51C91D29C7B3E3162A432E87AAD02DA38"
    "Nuclei4.GpuShaders.EmitVolumeTriangles.cso" = "4D03AFDDB6C0DDD2BDC285DE397E9196DEDA4D7442798B9D0E322A5DE2C0DBC9"
    "Nuclei4.GpuShaders.MoveAntParticlesAndDeposit.cso" = "F348D9D1E2C6D442ED525C4FBFB650C62C94E61E92859D320958BEFB3FFF0DE2"
    "Nuclei4.GpuShaders.MoveParticlesAndDeposit.cso" = "C459C7FAE5ACDB147AFE6BFA70FB2169EC16B5F9DD9D6253320BA38D447BB7A6"
    "Nuclei4.GpuShaders.ProjectFoodSources.cso" = "7DF1712704B04BA6F2E38B189877A379FB6FFF4CED1108E2A1092328E8C5AAC6"
    "Nuclei4.GpuShaders.SeedNeighbourCounts.cso" = "93F22AE8B8CC5C76DECCFEB013EF06A4E220FC425F95371F61B985FB7BA72616"
    "Nuclei4.GpuShaders.SmoothVolumeForMesh.cso" = "34129D9DB7C789296F2A55945C58CE580F1A3D8E572DE1065390965C695F5595"
    "Nuclei4.GpuShaders.SumNeighbourAxis.cso" = "BE03289EAC81C34112D9B77360DEA45543A76362B9379E58796DD57AAC1E568C"
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
