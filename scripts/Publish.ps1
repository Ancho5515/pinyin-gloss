[CmdletBinding()]
param([bool]$SelfContained = $true, [switch]$BackgroundOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts/pinyin-gloss-win-x64'
$zip = Join-Path $root 'artifacts/pinyin-gloss-win-x64.zip'
Push-Location $root
try {
    dotnet test tests/PinyinGloss.Tests -c Release -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    if ($IsWindows) {
        $testOptions = @()
        if ($BackgroundOnly) {
            $testOptions = @('--filter', 'Category!=InteractiveDesktop&Category!=DesktopCapture')
            Write-Output '后台验证：不运行可见窗口或桌面采集测试；这些项目仍需另行验收。'
        }
        dotnet test tests/PinyinGloss.Windows.Tests -c Release -m:1 -p:UseSharedCompilation=false @testOptions
        if ($LASTEXITCODE -ne 0) { throw 'Windows tests failed' }
    }
    $expectedOut = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/pinyin-gloss-win-x64'))
    $workspacePrefix = [IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar
    if ([IO.Path]::GetFullPath($out) -ne $expectedOut -or !$expectedOut.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected publish directory' }
    $artifactDirectory = Split-Path -Parent $expectedOut
    if ((Test-Path -LiteralPath $artifactDirectory) -and ((Get-Item -LiteralPath $artifactDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Artifact directory must not be a link' }
    if (Test-Path -LiteralPath $out) {
        if ((Test-Path -LiteralPath (Join-Path $out 'LLM')) -or (Test-Path -LiteralPath (Join-Path $out 'llm-installation.json'))) {
            throw '发布目录已有模型安装。请先移动整个旧发布目录，再重新发布，避免删除已下载权重或配置。'
        }
        $item = Get-Item -LiteralPath $out
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish directory must not be a link' }
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }
    dotnet publish src/PinyinGloss.Desktop -c Release -r win-x64 --self-contained $SelfContained.ToString().ToLowerInvariant() -o $out -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
    dotnet publish src/PinyinGloss.CandidateReader -c Release -r win-x64 --self-contained $SelfContained.ToString().ToLowerInvariant() -o (Join-Path $out 'reader') -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Reader publish failed' }
    foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item $file $out }
    Copy-Item 'third-party' $out -Recurse
    Copy-Item 'docs' $out -Recurse
    $setupScripts = Join-Path $out 'scripts'
    New-Item -ItemType Directory -Path $setupScripts -Force | Out-Null
    Copy-Item 'scripts/Setup-Llm.ps1','scripts/Get-LlamaRuntime.ps1' $setupScripts
    Copy-Item 'Setup-LLM.cmd' $out
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
