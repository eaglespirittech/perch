# Builds the Windows release artifacts:
#
#   dist\Perch-<version>-setup.exe               installer: app + perch-cli, one shared runtime
#   dist\Perch-<version>-win-x64.exe             portable app, single file
#   dist\perch-cli-<version>-win-x64.exe         portable CLI, single file
#
#   packaging\windows\build.ps1 -Version 1.2.3 [-SkipPortable] [-Step all|publish|package]
#
# The two steps exist so the release workflow can code sign in between:
#   publish   the installer payload into dist\app, the portable exes into dist\portable
#   package   the installer from dist\app, and the portable exes under their release names
#
# Needs the .NET 9 SDK; installs Inno Setup through Chocolatey if it is missing.
param(
    [Parameter(Mandatory)] [string] $Version,
    [switch] $SkipPortable,
    [ValidateSet('all', 'publish', 'package')] [string] $Step = 'all'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\..\.."
$dist = Join-Path $root 'dist'
$payload = Join-Path $dist 'app'
$portable = Join-Path $dist 'portable'
$tfm = 'net9.0-windows10.0.19041.0'

function Publish([string] $project, [string] $output, [string[]] $extra) {
    dotnet publish (Join-Path $root "src\$project") -c Release -f $tfm -r win-x64 --self-contained true `
        "-p:Version=$Version" @extra -o $output
    if ($LASTEXITCODE) { throw "dotnet publish $project failed" }
}

if ($Step -in 'all', 'publish') {
    Remove-Item -Recurse -Force $payload, $portable -ErrorAction SilentlyContinue

    # Not single-file here: both programs share one copy of the runtime, which roughly
    # halves what the installer has to carry.
    Publish 'Perch.App' $payload @('-p:PublishSingleFile=false')
    Publish 'Perch.Cli' $payload @('-p:PublishSingleFile=false')

    if (-not $SkipPortable) {
        $single = @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
                    '-p:EnableCompressionInSingleFile=true')
        Publish 'Perch.App' $portable $single
        Publish 'Perch.Cli' $portable $single
    }
}

if ($Step -in 'all', 'package') {
    . (Join-Path $PSScriptRoot 'find-iscc.ps1')
    & (Find-Iscc) "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'perch.iss')
    if ($LASTEXITCODE) { throw 'Inno Setup failed' }

    if (Test-Path (Join-Path $portable 'Perch.exe')) {
        Move-Item -Force (Join-Path $portable 'Perch.exe') (Join-Path $dist "Perch-$Version-win-x64.exe")
        Move-Item -Force (Join-Path $portable 'perch-cli.exe') (Join-Path $dist "perch-cli-$Version-win-x64.exe")
        Remove-Item -Recurse -Force $portable
    }

    Get-ChildItem $dist -File | Format-Table Name, Length
}
