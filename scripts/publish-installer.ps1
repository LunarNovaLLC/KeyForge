param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishDir = Join-Path $root "artifacts\publish\KeyForge"
$installerOutDir = Join-Path $root "artifacts\installer"
$appProject = Join-Path $root "src\KeyForge.App\KeyForge.App.csproj"
$installerProject = Join-Path $root "installer\KeyForge.Installer\KeyForge.Installer.wixproj"

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path $installerOutDir) {
    Remove-Item -LiteralPath $installerOutDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerOutDir | Out-Null

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

dotnet build $installerProject `
    --configuration $Configuration `
    -p:PublishDir="$publishDir\" `
    -p:OutputPath="$installerOutDir\"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet build for the installer failed with exit code $LASTEXITCODE"
}

Write-Host "Installer artifacts written to $installerOutDir"
