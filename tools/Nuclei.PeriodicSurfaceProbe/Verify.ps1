$ErrorActionPreference = 'Stop'
$bin = Join-Path $PSScriptRoot '../../Nuclei-v4/Nuclei4/bin/Release/net7.0-windows'
[Reflection.Assembly]::LoadFrom('C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll') | Out-Null
[Reflection.Assembly]::LoadFrom('C:\Program Files\Rhino 8\System\RhinoCommon.dll') | Out-Null
Get-ChildItem $bin -Filter '*.dll' | ForEach-Object { try { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch {} }
$a = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'Nuclei4.gha'))
$grid = $a.GetType('Nuclei4.VoxelGridData')
$engine = $a.GetType('Nuclei4.GpuPeriodicSurface')
$component = $a.GetType('Nuclei4.Voxel_PeriodicSurface')
$formulas = $component.GetField('Formulas', [Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$names = $component.GetField('Names', [Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$values = $component.GetField('Values', [Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
if($formulas.Length -ne 15 -or $names.Length -ne $formulas.Length -or $values.Length -ne $names.Length -or 'Custom' -in $names -or 8 -in $values -or 13 -in $values -or 17 -in $values -or @($values | Select-Object -Unique).Count -ne $values.Length){throw 'Invalid preset catalog'}
$gpu = [Activator]::CreateInstance($engine, $true)
try {
    $data = $grid.GetMethod('CreateFullDomain').Invoke($null, @([int]37,[int]29,[int]23,[double]1))
    $scale = [single]7.3; $iso = [single]0.21
    $evaluators = @(
        {param($x,$y,$z) [Math]::Cos($x)*[Math]::Sin($y)+[Math]::Cos($y)*[Math]::Sin($z)+[Math]::Cos($z)*[Math]::Sin($x)},
        {param($x,$y,$z) [Math]::Cos($x)*[Math]::Cos($y)*[Math]::Cos($z)-[Math]::Sin($x)*[Math]::Sin($y)*[Math]::Sin($z)},
        {param($x,$y,$z) [Math]::Sin($x)*[Math]::Cos($y)+[Math]::Sin($z)*[Math]::Cos($x)+[Math]::Sin($y)*[Math]::Cos($z)},
        {param($x,$y,$z) -[Math]::Cos($x)+[Math]::Cos($y)+[Math]::Cos($z)},
        {param($x,$y,$z) 3*([Math]::Cos($x)+[Math]::Cos($y)+[Math]::Cos($z))+4*[Math]::Cos($x)*[Math]::Cos($y)*[Math]::Cos($z)},
        {param($x,$y,$z) [Math]::Sin($x)*[Math]::Sin($y)*[Math]::Sin($z)+[Math]::Sin($x)*[Math]::Cos($y)*[Math]::Cos($z)+[Math]::Cos($x)*[Math]::Sin($y)*[Math]::Cos($z)+[Math]::Cos($x)*[Math]::Cos($y)*[Math]::Sin($z)},
        {param($x,$y,$z) [Math]::Cos($x)+[Math]::Cos($y)+[Math]::Cos($z)+4*[Math]::Cos($x)*[Math]::Cos($y)*[Math]::Cos($z)},
        {param($x,$y,$z) 2*([Math]::Cos($x)*[Math]::Cos($y)+[Math]::Cos($y)*[Math]::Cos($z)+[Math]::Cos($z)*[Math]::Cos($x))-[Math]::Cos(2*$x)-[Math]::Cos(2*$y)-[Math]::Cos(2*$z)},
        {param($x,$y,$z) [Math]::Cos(2*$x)*[Math]::Sin($y)*[Math]::Cos($z)+[Math]::Cos(2*$y)*[Math]::Sin($z)*[Math]::Cos($x)+[Math]::Cos(2*$z)*[Math]::Sin($x)*[Math]::Cos($y)},
        {param($x,$y,$z) [Math]::Sin(2*$x)*[Math]::Cos($y)*[Math]::Sin($z)+[Math]::Sin(2*$y)*[Math]::Cos($z)*[Math]::Sin($x)+[Math]::Sin(2*$z)*[Math]::Cos($x)*[Math]::Sin($y)-[Math]::Cos(2*$x)*[Math]::Cos(2*$y)-[Math]::Cos(2*$y)*[Math]::Cos(2*$z)-[Math]::Cos(2*$z)*[Math]::Cos(2*$x)+0.3},
        {param($x,$y,$z) $angle=$z+0.12*$z*$z*$z; $y*[Math]::Cos($angle)-$x*[Math]::Sin($angle)},
        {param($x,$y,$z) [Math]::Sin($x*$y)+[Math]::Sin($y*$z)+[Math]::Sin($z*$x)},
        {param($x,$y,$z) [Math]::Pow($x,4)+[Math]::Pow($y,4)+[Math]::Pow($z,4)-5*($x*$x+$y*$y+$z*$z)+11.8},
        {param($x,$y,$z)
            $r2=$x*$x+$y*$y+$z*$z; $d=1+$r2
            $u=[System.Numerics.Complex]::new(2*$x/$d,2*$y/$d)
            $v=[System.Numerics.Complex]::new(2*$z/$d,($r2-1)/$d)
            [Math]::Pow(($u*$u*$u-$v*$v).Magnitude,2)-0.02
        },
        {param($x,$y,$z) [Math]::Sin($x+0.8*[Math]::Sin(1.7*$y+0.6*[Math]::Sin($z)))+[Math]::Sin($y+0.8*[Math]::Sin(1.3*$z+0.6*[Math]::Sin($x)))+[Math]::Sin($z+0.8*[Math]::Sin(1.5*$x+0.6*[Math]::Sin($y)))}
    )
    for ($s=0;$s -lt $formulas.Length;$s++) {
        $formula=$engine.GetMethod('Translate').Invoke($null,@($formulas[$s]))
        $centered=$component.GetMethod('Centered',[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,@([int]$s))
        $select=$engine.GetMethod($(if($centered){'SelectCentered'}else{'Select'}))
        $bits = $select.Invoke($gpu, @($data,$formula,$scale,$iso,[single]0.3,[single]2.8))
        for ($i=0;$i -lt 24679;$i+=17) {
            $x=$scale*[Math]::Floor($i/(29*23))/37; $y=$scale*([Math]::Floor($i/23)%29)/29; $z=$scale*($i%23)/23
            if($centered){$x-=$scale*18/37; $y-=$scale*14/29; $z-=$scale*11/23}
            $v = & $evaluators[$s] $x $y $z
            $h=0.01
            $hx=$scale*$h/37; $hy=$scale*$h/29; $hz=$scale*$h/23
            $dx=((& $evaluators[$s] ($x+$hx) $y $z) - (& $evaluators[$s] ($x-$hx) $y $z))/(2*$h)
            $dy=((& $evaluators[$s] $x ($y+$hy) $z) - (& $evaluators[$s] $x ($y-$hy) $z))/(2*$h)
            $dz=((& $evaluators[$s] $x $y ($z+$hz)) - (& $evaluators[$s] $x $y ($z-$hz)))/(2*$h)
            $gradient=[Math]::Sqrt($dx*$dx+$dy*$dy+$dz*$dz)
            $distance=if($gradient -gt 0){[Math]::Abs($v-$iso)/$gradient}else{-1}
            $expected = $distance -ge 0.3 -and $distance -le 2.8
            $actual = ($bits[$i -shr 5] -band (1 -shl ($i -band 31))) -ne 0
            if ($actual -ne $expected -and [Math]::Min([Math]::Abs($distance-0.3),[Math]::Abs($distance-2.8)) -gt 0.005) { throw "Preset $s mismatch at $i" }
        }
        $defaultBits=$select.Invoke($gpu,@($data,$formula,[single](2*[Math]::PI),[single]0,[single]0,[single]2))
        $selected=$grid.GetMethod('WithActiveWords').Invoke($data,@(,$defaultBits))
        $count=$grid.GetField('ActiveCount').GetValue($selected)
        if($count -le 0 -or $count -ge 24679){throw "Preset $($names[$s]) has no usable default selection"}
        # Compile and exercise the more expensive narrow offset-band variant too.
        $select.Invoke($gpu,@($data,$formula,[single](2*[Math]::PI),[single]0,[single]0.2,[single]0.3)) | Out-Null
        Write-Output "$($names[$s]): CPU/GPU agreement; $count default voxels; offset shader passed"
    }
    $custom = 'Math.Cos(x) * Math.Sin(y) + Math.Cos(y) * Math.Sin(z) + Math.Cos(z) * Math.Sin(x)'
    $translated = $engine.GetMethod('Translate').Invoke($null,@($custom))
    $customBits = $engine.GetMethod('Select').Invoke($gpu,@($data,$translated,$scale,$iso,[single]0,[single]2))
    $presetBits = $engine.GetMethod('Select').Invoke($gpu,@($data,$formulas[0],$scale,$iso,[single]0,[single]2))
    if (Compare-Object $customBits $presetBits) { throw 'Custom gyroid differs' }
    foreach ($bad in @('System.IO.File.ReadAllText(x)','x; return 1','x{y}','unknown(x)')) {
        $rejected=$false
        try { $engine.GetMethod('Translate').Invoke($null,@($bad)) | Out-Null } catch { $rejected=$true }
        if (!$rejected) { throw "Accepted invalid formula: $bad" }
    }
    # Exercise input selection intersection, tail bits, resize and all/none selection.
    $small=$grid.GetMethod('CreateFullDomain').Invoke($null,@([int]3,[int]3,[int]5,[double]1))
    $mask=[int[]]@(5,1)
    $partial=$grid.GetMethod('WithActiveWords').Invoke($small,@(,$mask))
    $bits=$engine.GetMethod('Select').Invoke($gpu,@($partial,'0',[single]0,[single]0,[single]0,[single]2))
    if ($bits[0] -ne 5 -or $bits[1] -ne 1) { throw 'Input selection was not preserved' }
    $bits=$engine.GetMethod('Select').Invoke($gpu,@($small,'0',[single]0,[single]0,[single]0,[single]2))
    if ($bits[0] -ne -1 -or $bits[1] -ne 8191) { throw 'Tail bits incorrect' }
    $bits=$engine.GetMethod('Select').Invoke($gpu,@($small,'0',[single]0,[single]1,[single]0,[single]2))
    if ($bits[0] -ne 0 -or $bits[1] -ne 0) { throw 'Empty selection incorrect' }
    Write-Output 'Partial input, tail bits, resize, zero scale and empty selection passed.'
    # Exact planar distance tests: scale, voxel size, non-cubic axes, and inner gaps.
    $planeGrid=$grid.GetMethod('CreateFullDomain').Invoke($null,@([int]8,[int]6,[int]4,[double]1))
    foreach($axis in 0..2) {
        $expr=@('x','y','z')[$axis]
        $res=@(8,6,4)[$axis]
        foreach($location in @(1.0,1.15,1.5,1.95)) {
            $level=[single]($location*8/$res)
            $bits=$engine.GetMethod('Select').Invoke($gpu,@($planeGrid,$expr,[single]8,$level,[single]0,[single]0.25))
            $negative=0; $positive=0
            for($i=0;$i -lt 192;$i++) {
                $coord=@([Math]::Floor($i/24),([Math]::Floor($i/4)%6),($i%4))[$axis]
                $distance=[Math]::Abs($coord-$location)
                $selected=($bits[$i -shr 5] -band (1 -shl ($i -band 31))) -ne 0
                if($selected -ne ($distance -le 1.0001)) { throw "One-layer plane mismatch: axis $axis, offset $location, index $i" }
                if($selected -and $coord -lt $location){$negative++}
                if($selected -and $coord -gt $location){$positive++}
            }
            if(!$negative -or !$positive){throw 'Missing voxel layer on one side'}
        }
    }
    foreach($size in @(0.5,1.0,2.0)) {
        $planeGrid=$grid.GetMethod('CreateFullDomain').Invoke($null,@([int]8,[int]6,[int]4,[double]$size))
        foreach($scaleCase in @(4.0,8.0,-8.0)) {
            $bits=$engine.GetMethod('Select').Invoke($gpu,@($planeGrid,'x',[single]$scaleCase,[single]($scaleCase*3.5/8),[single]0.6,[single]2.2))
            for($i=0;$i -lt 192;$i++) {
                $distance=[Math]::Abs([Math]::Floor($i/24)-3.5)*$size
                $selected=($bits[$i -shr 5] -band (1 -shl ($i -band 31))) -ne 0
                $effectiveMax=[Math]::Max(2.2,0.6+$size)
                $expected=$distance -ge 0.6 -and $distance -le $effectiveMax
                if($effectiveMax-0.6 -lt 2*$size) {
                    $middle=(0.6+$effectiveMax)/2
                    $cx=[Math]::Floor($i/24)
                    foreach($nx in @(($cx-1),($cx+1))) {
                        if($nx -lt 0 -or $nx -ge 8){continue}
                        $other=[Math]::Abs($nx-3.5)*$size
                        if(($distance-$middle)*($other-$middle) -le 0){$expected=$true}
                    }
                }
                if($selected -ne $expected){throw 'Physical range or voxel-pair mismatch'}
            }
        }
    }
    foreach($ranges in @(@(-1,2),@(0,[single]::NaN),@(0,[single]::PositiveInfinity))) {
        $rejected=$false
        try {$engine.GetMethod('Select').Invoke($gpu,@($planeGrid,'x',[single]8,[single]3.5,[single]$ranges[0],[single]$ranges[1])) | Out-Null} catch {$rejected=$true}
        if(!$rejected){throw 'Invalid range accepted'}
    }
    # x^3 has a flat derivative at its level: crossings must still keep both sides.
    $bits=$engine.GetMethod('Select').Invoke($gpu,@($planeGrid,'pow(x-3.01,3)',[single]8,[single]0,[single]0,[single]0.25))
    foreach($x in @(3,4)) { $i=$x*24; if(($bits[$i -shr 5] -band (1 -shl ($i -band 31))) -eq 0){throw 'Flat-gradient crossing lost a side'} }
    Write-Output 'Physical ranges, inner gaps, all axes, sub-voxel offsets, scale/size invariance and range validation passed.'
    # Axis-aligned and diagonal walls: check both endpoints of every sampled
    # crossing, including narrow positive-minimum offset walls and grid phase shifts.
    $cube=$grid.GetMethod('CreateFullDomain').Invoke($null,@([int]8,[int]8,[int]8,[double]1))
    foreach($diagonal in @($false,$true)) {
        $expr=if($diagonal){'x+y+z'}else{'x'}
        $normal=if($diagonal){[Math]::Sqrt(3)}else{1}
        foreach($level in @(3.1,3.5,4.0)) {
            foreach($minimum in @(0,1.2)) {
                $bits=$engine.GetMethod('Select').Invoke($gpu,@($cube,$expr,[single]8,[single]$level,[single]$minimum,[single]($minimum+0.25)))
                $crossings=0
                for($i=0;$i -lt 512;$i++) {
                    $x=[int][Math]::Floor($i/64); $y=[int]([Math]::Floor($i/8)%8); $z=$i%8
                    $v=if($diagonal){$x+$y+$z-$level}else{$x-$level}
                    $a=if($minimum -eq 0){$v}else{[Math]::Abs($v)/$normal-($minimum+0.5)}
                    foreach($axis in 0..2) {
                        if(@($x,$y,$z)[$axis] -eq 7){continue}
                        $neighbor=$i+@(64,8,1)[$axis]
                        $delta=if($diagonal -or $axis -eq 0){1}else{0}
                        $v2=$v+$delta
                        $b=if($minimum -eq 0){$v2}else{[Math]::Abs($v2)/$normal-($minimum+0.5)}
                        if($a*$b -gt 0 -or $a -eq $b){continue}
                        $crossings++
                        foreach($id in @($i,$neighbor)) { if(($bits[$id -shr 5] -band (1 -shl ($id -band 31))) -eq 0){throw 'Wall crossing lost an endpoint'} }
                    }
                }
                if(!$crossings){throw 'Pair test did not exercise a crossing'}
            }
        }
    }
    $normalBits=$engine.GetMethod('Select').Invoke($gpu,@($cube,'x',[single]8,[single]3.5,[single]1.2,[single]3.7))
    $reverseBits=$engine.GetMethod('Select').Invoke($gpu,@($cube,'x',[single]8,[single]3.5,[single]3.7,[single]1.2))
    if(Compare-Object $normalBits $reverseBits){throw 'Reversed GPU ranges differ'}
    Write-Output 'Axis/diagonal voxel-pair crossings, positive-minimum walls and reversed GPU ranges passed.'
    $large=$grid.GetMethod('CreateFullDomain').Invoke($null,@([int]128,[int]128,[int]128,[double]1))
    $engine.GetMethod('Select').Invoke($gpu,@($large,$formulas[0],$scale,$iso,[single]0,[single]2)) | Out-Null
    $timer=[Diagnostics.Stopwatch]::StartNew()
    for($r=0;$r -lt 5;$r++) { $engine.GetMethod('Select').Invoke($gpu,@($large,$formulas[0],$scale,$iso,[single]0,[single]2)) | Out-Null }
    $timer.Stop()
    Write-Output "Custom formula and rejection checks passed. Warm 2,097,152 voxel dispatch + readback mean: $([Math]::Round($timer.Elapsed.TotalMilliseconds/5,2)) ms"
} finally { $gpu.Dispose() }





