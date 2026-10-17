<#
.SYNOPSIS
Checks that Llampec's Windows App SDK component packages form one official release.

.DESCRIPTION
Llampec references the WinUI, Foundation and InteractiveExperiences components rather than the
Microsoft.WindowsAppSDK metapackage, which would add about 40 MB of unused AI, ML, Search and Widgets
binaries. The metapackage still defines which component versions make up a release, so this script
reads <WindowsAppSdkRelease> from src/Llampec.App/Llampec.App.csproj, downloads that release's
metapackage manifest from NuGet and requires every referenced Microsoft.WindowsAppSDK.* package to
have the version the release declares.

With -RequireLatest it also fails when a newer stable release exists, so the weekly dependencies
workflow reports it. Without it, a newer release is only a warning.
#>
param([switch]$RequireLatest)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Llampec.App/Llampec.App.csproj'
[xml]$xml = Get-Content -LiteralPath $project -Raw
$release = ($xml.Project.PropertyGroup | ForEach-Object { $_.WindowsAppSdkRelease } | Where-Object { $_ }) | Select-Object -First 1
if (-not $release) { throw "No <WindowsAppSdkRelease> in $project." }
$references = @{}
foreach ($reference in $xml.Project.ItemGroup.PackageReference) {
    if ($reference.Include -like 'Microsoft.WindowsAppSDK.*') { $references[$reference.Include] = $reference.Version }
}

function Get-Manifest([string]$version) {
    $uri = "https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk/$version/microsoft.windowsappsdk.nuspec"
    [xml](Invoke-WebRequest -Uri $uri -UseBasicParsing).Content
}

$manifest = Get-Manifest $release
$declared = @{}
foreach ($dependency in $manifest.package.metadata.dependencies.SelectNodes('//*[local-name()="dependency"]')) {
    $declared[$dependency.id] = $dependency.version.Trim('[', ']')
}

$problems = @()
foreach ($name in $references.Keys | Sort-Object) {
    if (-not $declared.ContainsKey($name)) { $problems += "$name is not part of Windows App SDK $release."; continue }
    if ($references[$name] -ne $declared[$name]) {
        $problems += "$name is $($references[$name]); Windows App SDK $release declares $($declared[$name])."
    }
}
if ($problems) {
    $problems | ForEach-Object { Write-Host "::error::$_" }
    throw 'The Windows App SDK components do not match one release. Update them together with <WindowsAppSdkRelease>.'
}
Write-Host "Windows App SDK ${release}: $(($references.Keys | Sort-Object | ForEach-Object { "$_ $($references[$_])" }) -join ', ')"

$index = (Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk/index.json' -UseBasicParsing).Content | ConvertFrom-Json
$latest = $index.versions | Where-Object { $_ -notmatch '-' } | Sort-Object { [version]$_ } | Select-Object -Last 1
if ([version]$latest -gt [version]$release) {
    $next = Get-Manifest $latest
    $versions = foreach ($name in $references.Keys | Sort-Object) {
        $dependency = $next.SelectNodes('//*[local-name()="dependency"]') | Where-Object { $_.id -eq $name }
        "$name $($dependency.version.Trim('[', ']'))"
    }
    $message = "Windows App SDK $latest is available (Llampec uses $release): $($versions -join ', ')."
    if ($RequireLatest) { Write-Host "::error::$message"; throw $message }
    Write-Host "::warning::$message"
}
else { Write-Host "Windows App SDK $release is the latest stable release." }
