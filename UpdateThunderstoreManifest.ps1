# Updates Thunderstore package manifest version_number.
# Called from MSBuild after package files are prepared and before the archive is created.

param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Manifest file was not found: $ManifestPath"
}

$RawVersion = $Version.Trim()
$ParsedVersion = $null
if (-not [System.Version]::TryParse($RawVersion, [ref]$ParsedVersion)) {
    throw "Invalid assembly version: $Version"
}

if ($ParsedVersion.Major -lt 0 -or $ParsedVersion.Minor -lt 0 -or $ParsedVersion.Build -lt 0) {
    throw "Thunderstore version must contain major, minor and patch components: $Version"
}

$PackageVersion = "{0}.{1}.{2}" -f $ParsedVersion.Major, $ParsedVersion.Minor, $ParsedVersion.Build
$ManifestText = [System.IO.File]::ReadAllText($ManifestPath)
$Manifest = $ManifestText | ConvertFrom-Json
$OldVersion = [string]$Manifest.version_number
$Manifest.version_number = $PackageVersion

$Json = $Manifest | ConvertTo-Json -Depth 20
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($ManifestPath, $Json + [Environment]::NewLine, $Utf8NoBom)

if ($OldVersion -eq $PackageVersion) {
    Write-Host "Thunderstore manifest version is already $PackageVersion"
}
else {
    Write-Host "Thunderstore manifest version updated: $OldVersion -> $PackageVersion"
}
