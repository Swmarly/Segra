[CmdletBinding()]
param(
    [string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) "Obs/OBS 32.1.2.zip")
)

$ErrorActionPreference = "Stop"

$releaseTag = "obs-bundle-32.1.2"
$assetName = "OBS-32.1.2.zip"
$expectedSize = 65260810
$expectedSha256 = "a7c7e8ee1770df3138179e49ba9080b771ff1dfdbe48d5b3b10a5ddf41bf7c0b"
$assetUrl = "https://github.com/Swmarly/Segra/releases/download/$releaseTag/$assetName"

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-ObsBundle {
    param([Parameter(Mandatory = $true)][string]$Path)

    $file = Get-Item -LiteralPath $Path
    if ($file.Length -ne $expectedSize) {
        throw "OBS bundle has size $($file.Length) bytes; expected $expectedSize bytes."
    }

    $actualSha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        throw "OBS bundle SHA-256 is $actualSha256; expected $expectedSha256."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $hasObsDll = $null -ne ($archive.Entries | Where-Object { $_.FullName -eq "obs.dll" } | Select-Object -First 1)
        $has64BitPlugin = $null -ne ($archive.Entries | Where-Object { $_.FullName -like "obs-plugins/64bit/*" -and -not [string]::IsNullOrEmpty($_.Name) } | Select-Object -First 1)

        if (-not $hasObsDll -or -not $has64BitPlugin) {
            throw "OBS bundle does not contain obs.dll and files under obs-plugins/64bit/."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$destinationDirectory = Split-Path $destinationPath -Parent

if (Test-Path -LiteralPath $destinationPath) {
    try {
        Assert-ObsBundle -Path $destinationPath
        Write-Host "OBS bundle is already present and verified: $destinationPath"
        exit 0
    }
    catch {
        Write-Warning "Existing OBS bundle failed verification and will be replaced: $($_.Exception.Message)"
    }
}

New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
$temporaryPath = "$destinationPath.download-$PID"

try {
    Write-Host "Downloading $assetUrl"
    Invoke-WebRequest -Uri $assetUrl -OutFile $temporaryPath
    Assert-ObsBundle -Path $temporaryPath
    Move-Item -LiteralPath $temporaryPath -Destination $destinationPath -Force
    Write-Host "OBS bundle downloaded and verified: $destinationPath"
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
