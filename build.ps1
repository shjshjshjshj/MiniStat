# MiniStat 빌드 스크립트.
# Roslyn C# 컴파일러가 필요합니다. Visual Studio Build Tools에 들어 있거나,
# $env:MiniStatCsc 에 csc.exe 경로를 지정하면 그걸 씁니다.
param([switch]$TestBuild)

$ErrorActionPreference = 'Stop'
$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$src    = Join-Path $root 'src'
$lib    = Join-Path $root 'lib'
$outDir = if ($TestBuild) { Join-Path $root 'build\test' } else { Join-Path $root 'build\MiniStat' }
$fw     = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

$csc = @(
    $env:MiniStatCsc,
    'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe'
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $csc) {
    throw 'csc.exe를 찾지 못했습니다. Visual Studio Build Tools를 설치하거나 $env:MiniStatCsc 에 경로를 지정하세요.'
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$refs = 'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Management.dll'
$refArgs = $refs | ForEach-Object { '/r:' + (Join-Path $fw $_) }
$files = Get-ChildItem -LiteralPath $src -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }

$cscArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/langversion:7.3') +
    $refArgs + $files + @('/out:' + (Join-Path $outDir 'MiniStat.exe'))

$icon = Join-Path $src 'app.ico'
if (Test-Path -LiteralPath $icon) { $cscArgs += '/win32icon:' + $icon }
$manifest = Join-Path $src 'app.manifest'
if ((Test-Path -LiteralPath $manifest) -and -not $TestBuild) { $cscArgs += '/win32manifest:' + $manifest }

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

# LibreHardwareMonitorLib 은 런타임에 이름으로 로드하므로 실행 파일 옆에 있어야 합니다.
Copy-Item -Path (Join-Path $lib '*.dll') -Destination $outDir -Force

Write-Output ('OK ' + (Join-Path $outDir 'MiniStat.exe'))

