<#
.SYNOPSIS
    Build HourChime and produce two release-ready ZIP packages.

.DESCRIPTION
    Shared by local development and GitHub Actions (see .github/workflows/build.yml).
    Every package contains HourChime.exe and the MIT LICENSE file.
    A SHA256SUMS.txt is generated next to the packages.

.EXAMPLE
    ./scripts/build.ps1 -Version 2.1.0

.EXAMPLE
    ./scripts/build.ps1 -Version 0.0.0-ci.42 -OutputDir artifacts
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Version = '2.1.0',
    [string] $OutputDir = 'artifacts',
    [string] $Runtime = 'win-x64',
    [switch] $SkipSelfContained
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # keep CI logs readable

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'HourChimeWpf/HourChimeWpf.csproj'
$license = Join-Path $root 'LICENSE'
$out = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
} else {
    Join-Path $root $OutputDir
}
$stage = Join-Path $out '_stage'

if (-not (Test-Path $project)) { throw "Project not found: $project" }
if (-not (Test-Path $license)) { throw "LICENSE not found: $license" }

# _stage keeps the raw publish output. It is intentionally NOT deleted:
# the folder is git-ignored, and avoiding recursive deletes keeps this script
# friendly to locked-down environments (and to CI sandboxes).
New-Item -ItemType Directory -Force -Path $out | Out-Null
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Invoke-Publish {
    param(
        [Parameter(Mandatory)] [string] $Destination,
        [Parameter(Mandatory)] [bool] $SelfContained
    )

    $publishArgs = @(
        'publish', $project,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', $SelfContained.ToString().ToLowerInvariant(),
        '-p:PublishSingleFile=true',
        '-p:DebugType=none',
        "-p:Version=$Version",
        '-o', $Destination,
        '--nologo'
    )
    if ($SelfContained) {
        # Required to pack the native WPF/DirectWrite DLLs into one executable.
        $publishArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'
        $publishArgs += '-p:EnableCompressionInSingleFile=true'
    }

    Write-Host ">> dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code $LASTEXITCODE) for $Destination"
    }

    $exe = Join-Path $Destination 'HourChime.exe'
    if (-not (Test-Path $exe)) { throw "Expected output not found: $exe" }
}

function New-Package {
    param(
        [Parameter(Mandatory)] [string] $Destination,
        [Parameter(Mandatory)] [string] $ZipPath
    )

    Copy-Item -Path $license -Destination (Join-Path $Destination 'LICENSE') -Force
    $files = @(
        (Join-Path $Destination 'HourChime.exe'),
        (Join-Path $Destination 'LICENSE')
    )
    if (Test-Path $ZipPath) { [System.IO.File]::Delete($ZipPath) }
    Compress-Archive -Path $files -DestinationPath $ZipPath -CompressionLevel Optimal
    Write-Host ">> package: $ZipPath"
    return $ZipPath
}

$packages = @()

if (-not $SkipSelfContained) {
    $dir = Join-Path $stage 'self-contained'
    Invoke-Publish -Destination $dir -SelfContained $true
    $zip = Join-Path $out "HourChime-$Version-$Runtime.zip"
    $packages += New-Package -Destination $dir -ZipPath $zip
}

$liteDir = Join-Path $stage 'framework-dependent'
Invoke-Publish -Destination $liteDir -SelfContained $false
$liteZip = Join-Path $out "HourChime-$Version-$Runtime-framework-dependent.zip"
$packages += New-Package -Destination $liteDir -ZipPath $liteZip

# One standalone checksum file per artifact, then aggregate them into
# SHA256SUMS.txt from the same source of truth.
$sumLines = foreach ($p in $packages) {
    $name = [System.IO.Path]::GetFileName($p)
    $hash = (Get-FileHash -Path $p -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $name"
    $sidecar = "${p}.sha256"
    [System.IO.File]::WriteAllText(
        $sidecar,
        $line + "`n",
        (New-Object System.Text.UTF8Encoding($false))
    )
    Write-Host ">> checksum: $sidecar"
    $line
}

# LF endings keep both files usable with `sha256sum -c` on Linux runners.
[System.IO.File]::WriteAllText(
    (Join-Path $out 'SHA256SUMS.txt'),
    (($sumLines -join "`n") + "`n"),
    (New-Object System.Text.UTF8Encoding($false))
)

Write-Host ''
Write-Host "Version : $Version"
Write-Host "Output  : $out"
Get-ChildItem -Path $out -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-64} {1,12:N0} bytes" -f $_.Name, $_.Length)
}
