#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes Jyro CLI and builds the MSI installer.
.PARAMETER Runtime
    Target runtime identifier. Default: win-x64
.PARAMETER Configuration
    Build configuration. Default: Release
.PARAMETER Major
    Bump the major version (X.0.0)
.PARAMETER Minor
    Bump the minor version (0.X.0)
.PARAMETER Patch
    Bump the patch version (0.0.X). This is the default.
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$Major,
    [switch]$Minor,
    [switch]$Patch,
    [string]$HomebrewTapPath = "C:\GitHub\homebrew-tap",
    [string]$GitHubRepo = "realtree-systems/jyro-cli"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Update-HomebrewFormula {
    param(
        [string]$FormulaPath,
        [string]$Version,
        [string]$Runtime,
        [string]$Url,
        [string]$Sha256
    )

    if (-not (Test-Path $FormulaPath)) {
        Write-Warning "Homebrew formula not found at $FormulaPath, skipping update"
        return
    }

    $cpuCheck = switch ($Runtime) {
        "osx-arm64" { "arm" }
        "osx-x64"   { "intel" }
        default     { throw "Unsupported macOS runtime for Homebrew update: $Runtime" }
    }

    $content = Get-Content $FormulaPath -Raw

    # Update top-level version string
    $content = [regex]::Replace($content, 'version\s+"[^"]*"', "version `"$Version`"")

    # Update url and sha256 inside the matching CPU block
    $blockPattern = "(?s)(Hardware::CPU\.$cpuCheck\?\s*\r?\n\s*)url\s+""[^""]*""(\s*\r?\n\s*)sha256\s+""[^""]*"""
    $blockRegex = [regex]::new($blockPattern)

    if (-not $blockRegex.IsMatch($content)) {
        Write-Warning "Could not find '$cpuCheck' CPU block in $FormulaPath."
        Write-Warning "Add a stub block for Hardware::CPU.${cpuCheck}? manually, then re-run publish."
        return
    }

    $newContent = $blockRegex.Replace($content, {
        param($m)
        "$($m.Groups[1].Value)url `"$Url`"$($m.Groups[2].Value)sha256 `"$Sha256`""
    })

    # Write without adding a trailing newline that wasn't already there
    [System.IO.File]::WriteAllText($FormulaPath, $newContent)

    Write-Host "Updated Homebrew formula:" -ForegroundColor Green
    Write-Host "  File:    $FormulaPath"
    Write-Host "  Version: $Version"
    Write-Host "  Runtime: $Runtime ($cpuCheck block)"
    Write-Host "  URL:     $Url"
    Write-Host "  SHA256:  $Sha256"
}

$RepoRoot = $PSScriptRoot
$ProjectFile = Join-Path $RepoRoot "src\Jyro.Cli\Jyro.Cli.fsproj"
$InstallerDir = Join-Path $RepoRoot "Installer"
$WxsFile = Join-Path $InstallerDir "Jyro.wxs"
$OutputDir = Join-Path $RepoRoot "pub"

# Read current version from the project file
[xml]$proj = Get-Content $ProjectFile
$OldVersion = $proj.Project.PropertyGroup.Version
if (-not $OldVersion) {
    Write-Error "Could not read Version from $ProjectFile"
}

# Parse and bump version. A prerelease version (1.0.0-rc.1) is published as
# written unless a bump is asked for, which drops the suffix.
$Core, $Suffix = $OldVersion.Split('-', 2)
$Parts = $Core.Split('.')
$MajorNum = [int]$Parts[0]
$MinorNum = [int]$Parts[1]
$PatchNum = [int]$Parts[2]

if ($Major) {
    $MajorNum++; $MinorNum = 0; $PatchNum = 0
} elseif ($Minor) {
    $MinorNum++; $PatchNum = 0
} elseif ($Patch -or -not $Suffix) {
    # Default to patch
    $PatchNum++
}

$NumericVersion = "$MajorNum.$MinorNum.$PatchNum"
$Version = if ($Suffix -and -not ($Major -or $Minor -or $Patch)) { "$NumericVersion-$Suffix" } else { $NumericVersion }

# Map .NET RID to friendly platform name for output filenames
$FriendlyRuntime = $Runtime -replace '^osx-', 'macos-'

$TargetFramework = $proj.Project.PropertyGroup.TargetFramework
$PublishDir = Join-Path $RepoRoot "src\Jyro.Cli\bin\$Configuration\$TargetFramework\$Runtime\publish"
if ($Runtime.StartsWith("win")) {
    $OutputFile = Join-Path $OutputDir "jyro-cli-$Version-$FriendlyRuntime.msi"
} else {
    $OutputFile = Join-Path $OutputDir "jyro-cli-$Version-$FriendlyRuntime.tar.gz"
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Write-Host "=== Jyro CLI Publish ===" -ForegroundColor Cyan
Write-Host "Version:       $Version"
Write-Host "Runtime:       $Runtime"
Write-Host "Configuration: $Configuration"
Write-Host "Publish dir:   $PublishDir"
Write-Host "Output:        $OutputFile"
Write-Host ""

# Step 1: Publish
Write-Host "Publishing..." -ForegroundColor Yellow
dotnet publish $ProjectFile -c $Configuration -r $Runtime --self-contained true /p:Version=$Version
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed" }

# Step 2: Verify publish output
$ExeName = if ($Runtime.StartsWith("win")) { "Jyro.exe" } else { "Jyro" }
if (-not (Test-Path (Join-Path $PublishDir $ExeName))) {
    Write-Error "$ExeName not found in publish output"
}

if ($Runtime.StartsWith("win")) {
    # Step 3: Ensure WiX is installed
    if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
        Write-Host "Installing WiX..." -ForegroundColor Yellow
        dotnet tool install --global wix
        if ($LASTEXITCODE -ne 0) { Write-Error "Failed to install WiX" }
    }

    # Step 4: Build MSI
    Write-Host "Building MSI..." -ForegroundColor Yellow
    # An MSI version is numbers only
    wix build -ext WixToolset.Util.wixext $WxsFile -d Version=$NumericVersion -d PublishDir=$PublishDir -o $OutputFile
    if ($LASTEXITCODE -ne 0) { Write-Error "WiX build failed" }
} else {
    # Package as tar.gz for non-Windows
    Write-Host "Packaging tar.gz..." -ForegroundColor Yellow
    tar -czf $OutputFile -C $PublishDir .
}

$Size = [math]::Round((Get-Item $OutputFile).Length / 1MB, 1)

# Step 5: Persist the bumped version to the project file (only after a successful build)
$proj.Project.PropertyGroup.Version = $Version
$proj.Save($ProjectFile)
Write-Host "Updated project version: $OldVersion -> $Version" -ForegroundColor Green

# Step 6: Update Homebrew tap formula (macOS only)
if ($Runtime.StartsWith("osx-")) {
    Write-Host ""
    Write-Host "Updating Homebrew formula..." -ForegroundColor Yellow

    $FormulaFile = Join-Path $HomebrewTapPath "Formula\jyro.rb"
    $FileName = Split-Path $OutputFile -Leaf
    $ReleaseUrl = "https://github.com/$GitHubRepo/releases/download/v$Version/$FileName"
    $Sha256 = (Get-FileHash -Path $OutputFile -Algorithm SHA256).Hash.ToLower()

    Update-HomebrewFormula `
        -FormulaPath $FormulaFile `
        -Version $Version `
        -Runtime $Runtime `
        -Url $ReleaseUrl `
        -Sha256 $Sha256
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Output: $OutputFile ($Size MB)"
