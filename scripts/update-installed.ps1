param(
    [string]$Version,
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$propsPath = Join-Path $root "Directory.Build.props"
$publishScript = Join-Path $root "scripts\publish-installer.ps1"
$msiPath = Join-Path $root "artifacts\installer\KeyForge.msi"

function Get-KeyForgeVersion {
    [xml]$props = Get-Content -Raw -LiteralPath $propsPath
    return [string]$props.Project.PropertyGroup.KeyForgeVersion
}

function Set-KeyForgeVersion {
    param([string]$NewVersion)

    if ($NewVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must use major.minor.patch format, for example 0.1.2"
    }

    [xml]$props = Get-Content -Raw -LiteralPath $propsPath
    $props.Project.PropertyGroup.KeyForgeVersion = $NewVersion
    $props.Project.PropertyGroup.KeyForgeFileVersion = "$NewVersion.0"
    $props.Project.PropertyGroup.Version = '$(KeyForgeVersion)'
    $props.Project.PropertyGroup.AssemblyVersion = '$(KeyForgeFileVersion)'
    $props.Project.PropertyGroup.FileVersion = '$(KeyForgeFileVersion)'
    $props.Project.PropertyGroup.InformationalVersion = '$(KeyForgeVersion)'
    $props.Project.PropertyGroup.ProductVersion = '$(KeyForgeVersion)'
    $props.Save($propsPath)
}

function Get-NextPatchVersion {
    param([string]$CurrentVersion)

    $parts = $CurrentVersion.Split('.')
    if ($parts.Count -ne 3) {
        throw "Current version '$CurrentVersion' is not major.minor.patch."
    }

    $patch = [int]$parts[2] + 1
    return "$($parts[0]).$($parts[1]).$patch"
}

$currentVersion = Get-KeyForgeVersion
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-NextPatchVersion -CurrentVersion $currentVersion
}

if ($Version -ne $currentVersion) {
    Write-Host "Updating KeyForge version $currentVersion -> $Version"
    Set-KeyForgeVersion -NewVersion $Version
} else {
    Write-Host "Building KeyForge version $Version"
}

& $publishScript -Configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE"
}

if ($NoInstall) {
    Write-Host "Built update MSI: $msiPath"
    return
}

Get-Process KeyForge.App -ErrorAction SilentlyContinue | Stop-Process -Force

$arguments = "/i `"$msiPath`" /passive /norestart"
Write-Host "Launching in-place MSI update..."
$process = Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Wait -PassThru -Verb RunAs

if ($process.ExitCode -notin 0, 3010) {
    throw "MSI update failed with exit code $($process.ExitCode)"
}

Write-Host "KeyForge updated to $Version."
