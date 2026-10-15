# Packages Dizzy Clothesline for GitHub Releases.
# Usage: .\scripts\package-release.ps1
# The version comes from PluginVersion in Plugin.cs.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$pluginSource = Get-Content "src\Dizzy.Clothesline\Plugin.cs" -Raw
if ($pluginSource -notmatch 'PluginVersion\s*=\s*"([^"]+)"') {
    throw "Could not find PluginVersion in Plugin.cs."
}
$Version = $Matches[1]

$dll = "src\Dizzy.Clothesline\bin\Release\Dizzy.Clothesline.dll"
# Always rebuild so an old DLL is never zipped.
Write-Host "Building Dizzy.Clothesline $Version Release..."
dotnet build src\Dizzy.Clothesline\Dizzy.Clothesline.csproj -c Release -p:DeployOnBuild=false
if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}
if (-not (Test-Path $dll)) {
    throw "Missing $dll - build failed."
}

$staging = "dist\Dizzy.Clothesline-$Version"
$pluginDir = "$staging\Dizzy.Clothesline"
$zipPath = "dist\Dizzy.Clothesline-$Version.zip"

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item $dll $pluginDir -Force
Copy-Item "LICENSE" $pluginDir -Force

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path $pluginDir -DestinationPath $zipPath -Force
Write-Host "Packed $zipPath"
