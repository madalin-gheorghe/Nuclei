param([string]$OutputDirectory = 'docs/performance/latest-cpu-gpu-20260913')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$cases = @()
foreach ($shape in @(@{n=500;count=25000;planar=$true},@{n=64;count=1572;planar=$false},@{n=128;count=12582;planar=$false})) {
    foreach ($scale in @(1,2)) { $cases += @{n=$shape.n;count=$shape.count;planar=$shape.planar;scale=$scale;ant=$false} }
}
$cases += @{n=500;count=25000;planar=$true;scale=1;ant=$true}
$cases += @{n=64;count=1572;planar=$false;scale=1;ant=$true}
foreach ($round in @(1,2)) {
    foreach ($case in $cases) {
        $species = if ($case.ant) {'ants'} else {'slime'}
        $dimension = if ($case.planar) {'2d'} else {'3d'}
        $backends = if ($round -eq 1) {@('cpu','gpu')} else {@('gpu','cpu')}
        foreach ($backend in $backends) {
            $name = "$species-$dimension-$($case.n)-scale$($case.scale)-$backend-round$round"
            $arguments = @('tools/Nuclei.CpuGpuBenchmark/bin/Release/net8.0-windows/Nuclei.CpuGpuBenchmark.dll','--grid',"$($case.n)",'--particles',"$($case.count)",'--scale',"$($case.scale)",'--warmup','300','--steps','30','--repeats','5','--json',(Join-Path $OutputDirectory "$name.json"))
            if ($case.planar) {$arguments += '--2d'}
            if ($case.ant) {$arguments += '--ants'}
            if ($backend -eq 'gpu') {$arguments += '--gpu'}
            Write-Output "START $name"
            & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory "$name.log")
            if ($LASTEXITCODE -ne 0) {throw "Benchmark failed: $name"}
        }
    }
}
