param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Version,

    [string]$RepositoryUrl,

    [string]$ReleaseNotesPath,

    [string]$SignTemplate,

    [string]$SignParams,

    [string]$AzureTrustedSignFile,

    [switch]$SkipPreviousDownload
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$propsPath = Join-Path $root "Directory.Build.props"
$publishDir = Join-Path $root "artifacts\publish\KeyForge"
$releaseDir = Join-Path $root "artifacts\velopack"
$defaultReleaseNotesPath = Join-Path $root "artifacts\release-notes.md"
$appProject = Join-Path $root "src\KeyForge.App\KeyForge.App.csproj"
$iconPath = Join-Path $root "Icon\KFIcon.ico"

function Get-KeyForgeVersion {
    [xml]$props = Get-Content -Raw -LiteralPath $propsPath
    return [string]$props.Project.PropertyGroup.KeyForgeVersion
}

function Get-DefaultRepositoryUrl {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
        $serverUrl = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SERVER_URL)) { "https://github.com" } else { $env:GITHUB_SERVER_URL }
        return "$serverUrl/$env:GITHUB_REPOSITORY"
    }

    return "https://github.com/LunarNovaLLC/KeyForge"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-KeyForgeVersion
}

if ([string]::IsNullOrWhiteSpace($RepositoryUrl)) {
    $RepositoryUrl = Get-DefaultRepositoryUrl
}

if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $ReleaseNotesPath = $defaultReleaseNotesPath
}

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReleaseNotesPath) | Out-Null

if (-not (Test-Path $ReleaseNotesPath)) {
    Set-Content -LiteralPath $ReleaseNotesPath -Encoding UTF8 -Value @"
# KeyForge $Version

Official KeyForge release $Version.
"@
}

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:KeyForgeReleaseRepositoryUrl="$RepositoryUrl" `
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet tool restore failed with exit code $LASTEXITCODE"
}

if (-not $SkipPreviousDownload) {
    $downloadArgs = @(
        "vpk",
        "download",
        "github",
        "--repoUrl", $RepositoryUrl,
        "--outputDir", $releaseDir,
        "--channel", "win"
    )

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $downloadArgs += @("--token", $env:GITHUB_TOKEN)
    }

    & dotnet @downloadArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not download previous Velopack release. Continuing without delta base packages."
    }
}

$packArgs = @(
    "vpk",
    "pack",
    "--packId", "KeyForge",
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", "KeyForge.App.exe",
    "--packAuthors", "KeyForge",
    "--packTitle", "KeyForge",
    "--outputDir", $releaseDir,
    "--channel", "win",
    "--runtime", "win-x64",
    "--icon", $iconPath,
    "--releaseNotes", $ReleaseNotesPath,
    "--instLocation", "PerUser",
    "--noPortable"
)

if (-not [string]::IsNullOrWhiteSpace($SignTemplate)) {
    $packArgs += @("--signTemplate", $SignTemplate)
}

if (-not [string]::IsNullOrWhiteSpace($SignParams)) {
    $packArgs += @("--signParams", $SignParams)
}

if (-not [string]::IsNullOrWhiteSpace($AzureTrustedSignFile)) {
    $packArgs += @("--azureTrustedSignFile", $AzureTrustedSignFile)
}

& dotnet @packArgs
if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed with exit code $LASTEXITCODE"
}

$channelSetupPath = Join-Path $releaseDir "KeyForge-win-Setup.exe"
$publicSetupPath = Join-Path $releaseDir "KeyForge-Setup.exe"
if (Test-Path $channelSetupPath) {
    Move-Item -LiteralPath $channelSetupPath -Destination $publicSetupPath -Force
}

Write-Host "Velopack artifacts written to $releaseDir"
