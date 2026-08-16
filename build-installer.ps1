param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $root "nextdns-doh.csproj"
$publishDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"
$iss = Join-Path $root "installer\nextdns-doh.iss"
$toolsDir = Join-Path $root "tools\innosetup"

function Get-AppVersion {
    $text = Get-Content -Raw -Path $csproj
    if ($text -match "<Version>([^<]+)</Version>") {
        return $Matches[1].Trim()
    }
    return "1.0.0"
}

function Get-MsBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
            Select-Object -First 1
        if ($found -and (Test-Path $found)) {
            return $found
        }
    }
    return $null
}

function Publish-App {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $sdks = @( & dotnet --list-sdks 2>$null )
        if ($sdks.Count -gt 0) {
            & dotnet publish $csproj -c $Configuration -o $publishDir --nologo
            if ($LASTEXITCODE -eq 0) {
                return
            }
        }
    }

    $msbuild = Get-MsBuild
    if ($msbuild) {
        $sdkFolder = Join-Path (Split-Path (Split-Path $msbuild)) "Sdks\Microsoft.NET.Sdk\Sdk"
        if (Test-Path $sdkFolder) {
            & $msbuild $csproj /t:Restore /t:Publish /p:Configuration=$Configuration /p:PublishDir="$publishDir\" /nologo
            if ($LASTEXITCODE -eq 0) {
                return
            }
        }
    }

    Write-Host "No .NET SDK publish is available; using existing files in $publishDir"
}

function Get-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $existing = Get-ChildItem -Path $toolsDir -Filter "ISCC.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($existing) {
        return $existing.FullName
    }

    Write-Host "Downloading Inno Setup compiler..."
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $nupkg = Join-Path $toolsDir "Tools.InnoSetup.nupkg"
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Tools.InnoSetup/6.4.3" -OutFile $nupkg
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $toolsDir)

    $downloaded = Get-ChildItem -Path $toolsDir -Filter "ISCC.exe" -Recurse |
        Select-Object -First 1
    if (-not $downloaded) {
        throw "ISCC.exe was not found after downloading Inno Setup."
    }
    return $downloaded.FullName
}

$version = Get-AppVersion
Write-Host "Publishing NextDNS DoH $version..."
Publish-App

$exe = Join-Path $publishDir "nextdns-doh.exe"
$config = Join-Path $publishDir "nextdns-doh.exe.config"
if (-not (Test-Path $exe) -or -not (Test-Path $config)) {
    throw "Publish output is incomplete. Expected nextdns-doh.exe and nextdns-doh.exe.config in $publishDir"
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$iscc = Get-Iscc
Write-Host "Building installer with $iscc ..."
& $iscc /Q /DMyAppVersion=$version $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

$legacySetup = Join-Path $distDir "NextDNS-DoH.exe"
if (Test-Path $legacySetup) {
    Remove-Item $legacySetup -Force
}

$setup = Join-Path $distDir "NextDNS-DoH-$version.exe"
if (-not (Test-Path $setup)) {
    throw "Installer was not created at $setup"
}

Write-Host "Installer ready: $setup"
