# Locates the Inno Setup compiler, installing it if the runner does not have one.
# Dot-source this, then call Find-Iscc.

function Find-Iscc {
    $candidates = @(
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) { return $path }
    }

    $onPath = Get-Command iscc -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    Write-Host "Inno Setup not found; installing it."
    choco install innosetup -y --no-progress | Out-Null

    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) { return $path }
    }

    throw "Inno Setup is not installed and could not be installed automatically."
}
