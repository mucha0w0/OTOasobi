# Stage the acoustic runtime into a dotnet publish folder, and the precision
# engine (CPU torch, torchaudio 2.8, Praat, MMS weights) beside it.
#   dotnet publish prototype/host/Otoasobi.Probe.csproj -c Release -r win-x64 --self-contained true -o publish
#   powershell -File scripts/stage_release.ps1 -PublishDir publish -OutputDir engine-dist

param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,
    [string]$OutputDir = "",
    [string]$PythonVersion = "3.12.10",
    [switch]$ReuseEngine,
    [switch]$SkipEngine
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root "engine-dist"
}
$PublishDir = (Resolve-Path $PublishDir).Path
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$CacheDir = Join-Path ([IO.Path]::GetTempPath()) "otoasobi-stage"
$ModelUrl = "https://dl.fbaipublicfiles.com/mms/torchaudio/ctc_alignment_mling_uroman/model.pt"
$AcousticRequirements = Join-Path $root "requirements-acoustic.txt"
$EngineRequirements = Join-Path $root "requirements-release.txt"

function Invoke-Native {
    param([scriptblock]$Command, [string]$Failure)
    & $Command | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw $Failure
    }
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

function Get-TextSha256([string]$Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Save-Utf8([string]$Path, [string]$Text) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Remove-Pycache([string]$Path) {
    if (-not (Test-Path $Path)) {
        return
    }
    Get-ChildItem $Path -Recurse -Directory -Filter __pycache__ | Remove-Item -Recurse -Force
}

function Copy-Otoasobi([string]$Destination) {
    $target = Join-Path $Destination "otoasobi"
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
    }
    New-Item -ItemType Directory -Path $target | Out-Null
    $skip = @("gui.py", "cli.py", "__main__.py")
    Get-ChildItem (Join-Path $root "otoasobi") -Filter *.py |
        Where-Object { $skip -notcontains $_.Name } |
        Copy-Item -Destination $target
}

function Install-EmbedPython {
    param([string]$Destination, [string]$Requirements)
    if (Test-Path $Destination) {
        Remove-Item $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination | Out-Null
    New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null

    $zip = Join-Path $CacheDir "python-$PythonVersion-embed-amd64.zip"
    if (-not (Test-Path $zip)) {
        $url = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
        Invoke-Native { curl.exe -L --fail --retry 5 --retry-delay 2 -o $zip $url } "Python をダウンロードできませんでした。"
    }
    Expand-Archive -Path $zip -DestinationPath $Destination -Force

    $pth = Get-ChildItem $Destination -Filter "python*._pth" | Select-Object -First 1
    $stdlib = Get-ChildItem $Destination -Filter "python*.zip" | Select-Object -First 1
    if (-not $pth -or -not $stdlib) {
        throw "埋め込み Python の形が想定と違います。"
    }
    $lines = @($stdlib.Name, ".", "", "Lib\site-packages", "import site")
    Save-Utf8 $pth.FullName (($lines -join "`n") + "`n")

    $python = Join-Path $Destination "python.exe"
    $getPip = Join-Path $CacheDir "get-pip.py"
    if (-not (Test-Path $getPip)) {
        Invoke-Native { curl.exe -L --fail --retry 5 --retry-delay 2 -o $getPip "https://bootstrap.pypa.io/get-pip.py" } "pip をダウンロードできませんでした。"
    }

    $savedPath = $env:PYTHONPATH
    $savedHome = $env:PYTHONHOME
    $env:PYTHONPATH = $null
    $env:PYTHONHOME = $null
    $env:PYTHONUTF8 = "1"
    $env:PYTHONNOUSERSITE = "1"
    try {
        Invoke-Native { & $python $getPip } "pip を入れられませんでした。"
        Invoke-Native { & $python -m pip install --disable-pip-version-check -r $Requirements } "Python の依存を入れられませんでした。"
    }
    finally {
        $env:PYTHONPATH = $savedPath
        $env:PYTHONHOME = $savedHome
    }

    # ._pth があると PYTHONPATH は無視される。site-packages の .pth で足し直す。
    $hook = Join-Path $Destination "Lib\site-packages\otoasobi-pythonpath.pth"
    $hookLine = 'import os, sys; extra = os.environ.get("PYTHONPATH") or ""; extra and [sys.path.append(item) for item in extra.split(os.pathsep) if item and item not in sys.path]'
    Save-Utf8 $hook ($hookLine + "`n")
    return $python
}

function Test-ReusedEngine([string]$Directory) {
    $manifestPath = Join-Path $Directory "engine.json"
    $zip = Join-Path $Directory "OtoasobiEngine.zip"
    $model = Join-Path $Directory "mms-fa-model.pt"
    if (-not (Test-Path $manifestPath) -or -not (Test-Path $zip) -or -not (Test-Path $model)) {
        return $false
    }
    $meta = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $zipHash = Get-Sha256 $zip
    $modelHash = Get-Sha256 $model
    return ($zipHash -eq ([string]$meta.pythonSha256).ToLowerInvariant()) -and
        ($modelHash -eq ([string]$meta.modelSha256).ToLowerInvariant())
}

function Write-EngineManifest {
    param(
        [string]$Directory,
        [string]$Version,
        [string]$ZipPath,
        [string]$ModelPath
    )
    $pythonHash = Get-Sha256 $ZipPath
    $modelHash = Get-Sha256 $ModelPath
    $pythonSize = (Get-Item $ZipPath).Length
    $modelSize = (Get-Item $ModelPath).Length
    $meta = [ordered]@{
        version = $Version
        python = "OtoasobiEngine.zip"
        pythonSha256 = $pythonHash
        pythonSize = $pythonSize
        model = "mms-fa-model.pt"
        modelSha256 = $modelHash
        modelSize = $modelSize
    }
    Save-Utf8 (Join-Path $Directory "engine.json") (($meta | ConvertTo-Json) + "`n")
}

function Write-EnginePack([string]$Directory) {
    $pack = Join-Path $Directory "OtoasobiEnginePack.zip"
    $names = @("engine.json", "OtoasobiEngine.zip", "mms-fa-model.pt")
    if (Test-Path $pack) {
        Remove-Item $pack -Force
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($pack, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $names) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                (Join-Path $Directory $name),
                $name,
                [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
    Assert-GitHubSize $pack
}

function Assert-GitHubSize([string]$Path) {
    $limit = 2GB - 1
    $length = (Get-Item $Path).Length
    if ($length -ge $limit) {
        throw "$(Split-Path -Leaf $Path) が GitHub の 1 ファイル上限を超えています。"
    }
}

if (-not (Test-Path $PublishDir)) {
    throw "publish フォルダがありません。"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Copy-Otoasobi $PublishDir
$acousticPython = Install-EmbedPython (Join-Path $PublishDir "python") $AcousticRequirements
$savedPath = $env:PYTHONPATH
$env:PYTHONPATH = $PublishDir
try {
    Invoke-Native { & $acousticPython -c "import otoasobi.worker" } "音響エンジンの確認に失敗しました。"
}
finally {
    $env:PYTHONPATH = $savedPath
}
Remove-Pycache $PublishDir

if ($SkipEngine) {
    return
}

$zipPath = Join-Path $OutputDir "OtoasobiEngine.zip"
$modelPath = Join-Path $OutputDir "mms-fa-model.pt"
if ($ReuseEngine -and (Test-ReusedEngine $OutputDir)) {
    Assert-GitHubSize $zipPath
    Assert-GitHubSize $modelPath
    Write-EnginePack $OutputDir
    return
}

$engineRoot = Join-Path $CacheDir "engine-python"
$enginePython = Install-EmbedPython $engineRoot $EngineRequirements
$savedPath = $env:PYTHONPATH
try {
    $env:PYTHONPATH = $null
    Invoke-Native { & $enginePython -c "import torch, torchaudio, parselmouth; from torchaudio.pipelines import MMS_FA" } "高精度エンジンの確認に失敗しました。"
    $env:PYTHONPATH = $PublishDir
    Invoke-Native { & $enginePython -c "import otoasobi.worker" } "高精度エンジンから解析を読み込めませんでした。"
}
finally {
    $env:PYTHONPATH = $savedPath
}
Remove-Pycache $engineRoot

if (Test-Path $modelPath) {
    Remove-Item $modelPath -Force
}
Invoke-Native { curl.exe -L --fail --retry 5 --retry-delay 2 -o $modelPath $ModelUrl } "音声モデルをダウンロードできませんでした。"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $engineRoot,
    $zipPath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$req = Get-Content $EngineRequirements -Raw
$version = Get-TextSha256 ($req + "`npython=$PythonVersion`n")
Write-EngineManifest $OutputDir $version $zipPath $modelPath
Assert-GitHubSize $zipPath
Assert-GitHubSize $modelPath
Write-EnginePack $OutputDir
Remove-Item $engineRoot -Recurse -Force
