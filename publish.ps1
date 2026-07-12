#Requires -Version 5.1

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts'),

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.1'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectPath = Join-Path $PSScriptRoot 'VpyAudioCutter.csproj'
$selfContained = Join-Path $OutputRoot 'self-contained'
$frameworkDependent = Join-Path $OutputRoot 'framework-dependent'

New-Item -ItemType Directory -Force -Path $selfContained | Out-Null
New-Item -ItemType Directory -Force -Path $frameworkDependent | Out-Null

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $selfContained
if ($LASTEXITCODE -ne 0) {
    throw "Self-contained publish failed with exit code $LASTEXITCODE."
}

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $frameworkDependent
if ($LASTEXITCODE -ne 0) {
    throw "Framework-dependent publish failed with exit code $LASTEXITCODE."
}

foreach ($directory in @($selfContained, $frameworkDependent)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $directory -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $directory -Force

    $pdbPath = Join-Path $directory 'VpyAudioCutter.pdb'
    if (Test-Path -LiteralPath $pdbPath) {
        Remove-Item -LiteralPath $pdbPath -Force
    }
}

$packages = @(
    @{
        Directory = $selfContained
        Archive = Join-Path $OutputRoot "VpyAudioCutter-v$Version-win-x64-self-contained.zip"
    },
    @{
        Directory = $frameworkDependent
        Archive = Join-Path $OutputRoot "VpyAudioCutter-v$Version-win-x64-framework-dependent.zip"
    }
)

foreach ($package in $packages) {
    if (Test-Path -LiteralPath $package.Archive) {
        Remove-Item -LiteralPath $package.Archive -Force
    }

    $packageFiles = @(
        (Join-Path $package.Directory 'VpyAudioCutter.exe'),
        (Join-Path $package.Directory 'README.md'),
        (Join-Path $package.Directory 'LICENSE')
    )
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $package.Archive -CompressionLevel Optimal
    Write-Output $package.Archive
}
