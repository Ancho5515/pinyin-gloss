#requires -Version 7.0
[CmdletBinding()]
param([bool]$SelfContained = $true, [switch]$BackgroundOnly, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifactDirectory = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$out = if (!$OutputDirectory) { Join-Path $artifactDirectory 'pinyin-gloss-win-x64' }
    elseif ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
$savedEnvironment = @{}
foreach ($name in @('DOTNET_ROOT','PATH','DOTNET_CLI_HOME','NUGET_PACKAGES')) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
function Invoke-Dotnet {
    param([string[]]$Arguments)
    $PSNativeCommandUseErrorActionPreference = $false
    $savedEncoding = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        for ($attempt = 0; $attempt -lt 2; $attempt++) {
            $output = @(); $missingCommand = $false
            try { dotnet @Arguments 2>&1 | Tee-Object -Variable output | Out-Host; $exitCode = $LASTEXITCODE }
            catch [Management.Automation.CommandNotFoundException] { $output = @($_.Exception.Message); $missingCommand = $true; $exitCode = 1; Write-Host $output[0] }
            if ($exitCode -eq 0) { return }
            $sdkError = $missingCommand -or ($output -join "`n") -match 'NETSDK1045|MSB4236|A compatible \.NET SDK was not found|No \.NET SDKs were found|You must install or update \.NET'
            if ($attempt -ne 0 -or !$sdkError) { throw "dotnet 执行失败（退出码 $exitCode），已停止。" }
            $localSdk = Join-Path $root 'artifacts/toolchain/dotnet'
            if (!(Test-Path -LiteralPath (Join-Path $localSdk 'dotnet.exe'))) { throw '当前 dotnet 环境不可用，仓库内也没有可供切换的 .NET SDK。' }
            Write-Output '检测到 dotnet 环境错误，临时切换到仓库 SDK 后重试一次。'
            $env:DOTNET_ROOT = $localSdk
            $env:PATH = $localSdk + [IO.Path]::PathSeparator + $env:PATH
        }
    } finally { [Console]::OutputEncoding = $savedEncoding }
}
Push-Location $root
try {
    Invoke-Dotnet -Arguments @('test','tests/PinyinGloss.Tests','-c','Release','-m:1','-p:UseSharedCompilation=false')
    if ($env:OS -eq 'Windows_NT') {
        $testOptions = @()
        if ($BackgroundOnly) {
            $testOptions = @('--filter', 'Category!=InteractiveDesktop&Category!=DesktopCapture')
            Write-Output '后台验证：不运行可见窗口或桌面采集测试；这些项目仍需另行验收。'
        }
        Invoke-Dotnet -Arguments (@('test','tests/PinyinGloss.Windows.Tests','-c','Release','-m:1','-p:UseSharedCompilation=false') + $testOptions)
    }
    $out = $out.TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (!$out.StartsWith($artifactDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录必须位于仓库 artifacts 内' }
    $parent = Split-Path -Parent $out
    while ($parent) {
        if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '发布目录的父目录不能是链接' }
        if ($parent -eq $artifactDirectory) { break }
        $parent = Split-Path -Parent $parent
    }
    if (Test-Path -LiteralPath $out) {
        $item = Get-Item -LiteralPath $out
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '发布目录不能是链接' }
        if (Get-ChildItem -LiteralPath $out -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) { throw '发布目录内不能包含链接' }
    }
    $running = Get-Process -Name 'PinyinGloss.Desktop','PinyinGloss.CandidateReader' -ErrorAction SilentlyContinue |
        Where-Object { !$_.Path -or $_.Path.StartsWith($out + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw '发布目录中的程序仍在运行，请先退出 Pinyin Gloss 再发布。' }
    # 原位更新程序文件，保留 LLM、安装清单及其他已有数据。
    Invoke-Dotnet -Arguments @('publish','src/PinyinGloss.Desktop','-c','Release','-r','win-x64','--self-contained',$SelfContained.ToString().ToLowerInvariant(),'-o',$out,'-m:1','-p:UseSharedCompilation=false')
    Invoke-Dotnet -Arguments @('publish','src/PinyinGloss.CandidateReader','-c','Release','-r','win-x64','--self-contained',$SelfContained.ToString().ToLowerInvariant(),'-o',(Join-Path $out 'reader'),'-m:1','-p:UseSharedCompilation=false')
    foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item $file $out -Force }
    Copy-Item 'third-party' $out -Recurse -Force
    Copy-Item 'docs' $out -Recurse -Force
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
    Write-Output "已发布到目录：$out"
} finally {
    Pop-Location
    foreach ($name in $savedEnvironment.Keys) {
        if ($null -eq $savedEnvironment[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    }
}
