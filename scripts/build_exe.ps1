# Build the Windows exe from the 3.12 venv.
#   py -3.12 -m venv .venv
#   .\.venv\Scripts\python.exe -m pip install -r requirements.txt pyinstaller pillow
#   powershell -File scripts\build_exe.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$python = Join-Path $root ".venv\Scripts\python.exe"
if (-not (Test-Path $python)) {
    Write-Error "Create .venv with Python 3.12 first: py -3.12 -m venv .venv"
}

& $python -m PyInstaller (Join-Path $root "packaging\otoasobi.spec") --noconfirm
Write-Host "exe: $root\dist\OtoasobiLabel\OtoasobiLabel.exe"
