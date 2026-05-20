param(
    [string] $Version = "latest",
    [string] $InstallDir = "$env:LOCALAPPDATA\Programs\azimg",
    [switch] $Force,
    [switch] $DryRun,
    [switch] $NoPathUpdate
)

$ErrorActionPreference = "Stop"
$Repository = "Jcardif/azimg-cli"

function Get-AzImgRid {
    switch ($env:PROCESSOR_ARCHITECTURE) {
        "AMD64" { return "win-x64" }
        "ARM64" { return "win-arm64" }
        default { throw "Unsupported Windows architecture: $env:PROCESSOR_ARCHITECTURE" }
    }
}

function Get-ReleaseBase([string] $RequestedVersion) {
    if ($RequestedVersion -eq "latest") {
        return "https://github.com/$Repository/releases/latest/download"
    }

    if ($RequestedVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $tag = $RequestedVersion
    }
    else {
        $tag = "v$RequestedVersion"
    }

    return "https://github.com/$Repository/releases/download/$tag"
}

$rid = Get-AzImgRid
$releaseBase = Get-ReleaseBase $Version
$manifestUrl = "$releaseBase/azimg-release.json"
$target = Join-Path $InstallDir "azimg.exe"

Write-Host "azimg installer"
Write-Host "  release: $Version"
Write-Host "  rid:     $rid"
Write-Host "  target:  $target"

if ($DryRun) {
    return
}

if ((Test-Path $target) -and -not $Force) {
    throw "azimg already exists at $target. Re-run with -Force to overwrite it."
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "azimg-install-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    $manifest = Invoke-RestMethod -Uri $manifestUrl
    $asset = $manifest.assets | Where-Object { $_.rid -eq $rid } | Select-Object -First 1
    if (-not $asset) {
        throw "Release manifest does not contain an asset for $rid."
    }

    $archivePath = Join-Path $tempRoot $asset.fileName
    $extractDir = Join-Path $tempRoot "extract"
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    $downloadUrl = if ($asset.downloadUrl) { $asset.downloadUrl } else { "$releaseBase/$($asset.fileName)" }
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

    $actualHash = (Get-FileHash -Algorithm SHA256 -Path $archivePath).Hash.ToLowerInvariant()
    if ($actualHash -ne $asset.sha256.ToLowerInvariant()) {
        throw "Checksum mismatch for $($asset.fileName). Expected $($asset.sha256), got $actualHash."
    }

    Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force
    $sourceExe = Get-ChildItem -Path $extractDir -Recurse -File -Filter "azimg.exe" | Select-Object -First 1
    if (-not $sourceExe) {
        throw "The archive did not contain azimg.exe."
    }

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Copy-Item -Path $sourceExe.FullName -Destination $target -Force

    $metadataDir = Join-Path $env:USERPROFILE ".azimg"
    New-Item -ItemType Directory -Force -Path $metadataDir | Out-Null
    [ordered]@{
        schemaVersion = 1
        install = [ordered]@{
            schemaVersion = 1
            installPath = $target
            rid = $rid
            installedVersion = $manifest.version
            sourceRepository = $Repository
            installMethod = "install.ps1"
            updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        }
        update = [ordered]@{
            schemaVersion = 1
        }
    } | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $metadataDir "metadata.json")

    if (-not $NoPathUpdate) {
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        $pathParts = @($userPath -split ';' | Where-Object { $_ })
        if ($pathParts -notcontains $InstallDir) {
            $newPath = if ($userPath) { "$userPath;$InstallDir" } else { $InstallDir }
            [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
            Write-Host "Added $InstallDir to the user PATH. Open a new terminal to use it."
        }
    }

    Write-Host "Installed azimg to $target"
    & $target version
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
