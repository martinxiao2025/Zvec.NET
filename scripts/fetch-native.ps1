# 从 zvec GitHub Releases 下载原生 SDK 并提取 win-x64 库与 C API 头文件。
# 用法: powershell -File scripts/fetch-native.ps1 [-Version v0.7.0]
param(
    [string]$Version = "v0.7.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $root "build"
$zipPath = Join-Path $buildDir "zvec-sdk-windows-amd64.zip"
$nativeDir = Join-Path $root "src/Zvec.NET/runtimes/win-x64/native"
$referenceDir = Join-Path $root "src/Zvec.NET/Interop/reference"

New-Item -ItemType Directory -Force -Path $buildDir, $nativeDir, $referenceDir | Out-Null

$url = "https://github.com/alibaba/zvec/releases/download/$Version/zvec-sdk-windows-amd64.zip"
Write-Host "下载 $url ..."
Invoke-WebRequest -Uri $url -OutFile $zipPath

Write-Host "解压 ..."
$extractDir = Join-Path $buildDir "sdk-extract"
if (Test-Path $extractDir) { Remove-Item -Recurse -Force $extractDir }
Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

Write-Host "复制 DLL -> $nativeDir"
Copy-Item -Path (Join-Path $extractDir "lib/*.dll") -Destination $nativeDir -Force

Write-Host "复制头文件 -> $referenceDir"
Copy-Item -Path (Join-Path $extractDir "include/zvec/c_api.h") -Destination $referenceDir -Force
Copy-Item -Path (Join-Path $extractDir "include/zvec/export.h") -Destination $referenceDir -Force -ErrorAction SilentlyContinue

Write-Host "完成。当前版本：$Version"
Write-Host "验证：dotnet test"
