$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "未找到 .NET Framework 4.x 编译器: $csc"
}

$distDir = Join-Path $root 'dist'
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$icoPath = Join-Path $root 'app.ico'
if (-not (Test-Path $icoPath)) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'make_icon.ps1')
}

$outExe = Join-Path $distDir 'Antigravity一键汉化.exe'
$srcFile = Join-Path $root 'Program.cs'

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/codepage:65001',
    "/win32icon:$icoPath",
    "/out:$outExe",
    '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll',
    '/r:System.Net.Http.dll',
    '/r:System.Web.Extensions.dll',
    $srcFile
)

Write-Host "Compiling $outExe ..."
& $csc @args
if ($LASTEXITCODE -ne 0) {
    throw "csc.exe 编译失败，退出码: $LASTEXITCODE"
}

$fi = Get-Item $outExe
Write-Host "Build succeeded: $($fi.FullName) ($($fi.Length) bytes)"
