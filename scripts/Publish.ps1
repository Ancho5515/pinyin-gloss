[CmdletBinding()]
param([bool]$SelfContained = $true)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts/pinyin-gloss-win-x64'
$zip = Join-Path $root 'artifacts/pinyin-gloss-win-x64.zip'
Push-Location $root
try {
    dotnet test tests/PinyinGloss.Tests -c Release -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    dotnet publish src/PinyinGloss.Desktop -c Release -r win-x64 --self-contained $SelfContained.ToString().ToLowerInvariant() -o $out -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
    dotnet publish src/PinyinGloss.CandidateReader -c Release -r win-x64 --self-contained $SelfContained.ToString().ToLowerInvariant() -o (Join-Path $out 'reader') -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Reader publish failed' }
    foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item $file $out }
    Copy-Item 'third-party' $out -Recurse
    Copy-Item 'docs' $out -Recurse
    # Include exact redistributable runtime-pack notices, when exposed by the restored packs.
    $assets = Get-Content 'src/PinyinGloss.Desktop/obj/project.assets.json' -Raw | ConvertFrom-Json
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        foreach ($package in $assets.libraries.PSObject.Properties.Name) {
            if ($package -notmatch '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.') { continue }
            $path = Join-Path $folder $package.ToLowerInvariant()
            if (Test-Path $path) {
                $dest = Join-Path $out ('third-party/' + ($package -replace '/', '-'))
                New-Item $dest -ItemType Directory -Force | Out-Null
                Get-ChildItem $path -File -Recurse | Where-Object { $_.Name -match 'license|notice' } | ForEach-Object { Copy-Item $_.FullName $dest -Force }
            }
        }
    }
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    Write-Output "Published $zip"
} finally { Pop-Location }
