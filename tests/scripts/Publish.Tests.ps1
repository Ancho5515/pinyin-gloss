$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $root ('artifacts/validation/publish-script-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $testRoot 'scripts'),(Join-Path $testRoot 'docs'),(Join-Path $testRoot 'third-party'),(Join-Path $testRoot 'src/PinyinGloss.Desktop/obj') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'scripts/Publish.ps1') -Destination (Join-Path $testRoot 'scripts/Publish.ps1')
foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','Setup-LLM.cmd','docs/validation.md','third-party/notice.txt','scripts/Setup-Llm.ps1','scripts/Get-LlamaRuntime.ps1')) {
    Set-Content -LiteralPath (Join-Path $testRoot $file) -Value '验收文件'
}
Set-Content -LiteralPath (Join-Path $testRoot 'src/PinyinGloss.Desktop/obj/project.assets.json') -Value '{"packageFolders":{},"libraries":{}}'
$global:PublishTestFailBuild = $false
$global:PublishTestSdkMode = 'healthy'
$global:PublishTestSdkRoot = Join-Path $testRoot 'artifacts/toolchain/dotnet'
$global:PublishTestRuntimeLog = Join-Path $testRoot 'runtime-calls.jsonl'
$global:PublishTestRunning = $false
function Get-Process {
    if ($global:PublishTestRunning) { [pscustomobject]@{ Path = Join-Path $testRoot 'artifacts/pinyin-gloss-win-x64/PinyinGloss.Desktop.exe' } }
}
function dotnet {
    Add-Content -LiteralPath $global:PublishTestRuntimeLog -Value (ConvertTo-Json -Compress @{Command=$args[0];DotnetRoot=$env:DOTNET_ROOT;Path=$env:PATH;CliHome=$env:DOTNET_CLI_HOME;Packages=$env:NUGET_PACKAGES})
    if ($global:PublishTestFailBuild) { $global:LASTEXITCODE = 7; return }
    if ($global:PublishTestSdkMode -eq 'missing' -and $env:DOTNET_ROOT -ne $global:PublishTestSdkRoot) { Write-Output 'A compatible .NET SDK was not found.'; $global:LASTEXITCODE = 1; return }
    if ($global:PublishTestSdkMode -eq 'command-missing' -and $env:DOTNET_ROOT -ne $global:PublishTestSdkRoot) { throw [Management.Automation.CommandNotFoundException]::new('dotnet command not found') }
    if ($global:PublishTestSdkMode -eq 'broken') { Write-Output 'A compatible .NET SDK was not found.'; $global:LASTEXITCODE = 1; return }
    if ($global:PublishTestSdkMode -eq 'code-error') { Write-Output 'error CS1002: ; expected'; $global:LASTEXITCODE = 1; return }
    if ($global:PublishTestSdkMode -eq 'network-error') { Write-Output 'error NU1301: Unable to load the service index'; $global:LASTEXITCODE = 1; return }
    if ($args[0] -eq 'publish') {
        $output = $args[[Array]::IndexOf($args, '-o') + 1]
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        $exe = if ($args[1] -like '*Desktop') { 'PinyinGloss.Desktop.exe' } else { 'PinyinGloss.CandidateReader.exe' }
        Set-Content -LiteralPath (Join-Path $output $exe) -Value '验收用外部构建器产物'
    }
    $global:LASTEXITCODE = 0
}
$savedEnvironment = @{}
foreach ($name in @('DOTNET_ROOT','PATH','DOTNET_CLI_HOME','NUGET_PACKAGES')) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$savedLocation = (Get-Location).Path
$count = 0
try {
    $publish = Join-Path $testRoot 'scripts/Publish.ps1'
    New-Item -ItemType Directory -Path $global:PublishTestSdkRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $global:PublishTestSdkRoot 'dotnet.exe') -Value '本地 SDK 文件占位，外部命令由替身执行'
    & $publish -BackgroundOnly
    $output = Join-Path $testRoot 'artifacts/pinyin-gloss-win-x64'
    foreach ($file in @('PinyinGloss.Desktop.exe','reader/PinyinGloss.CandidateReader.exe','README.md','docs/validation.md','scripts/Setup-Llm.ps1')) {
        if (!(Test-Path -LiteralPath (Join-Path $output $file))) { throw "目录发布缺少文件：$file" }
    }
    if (Test-Path -LiteralPath ($output + '.zip')) { throw '目录发布仍生成 ZIP' }
    $count++; Write-Output 'PASS 测试后发布配套程序和文档，不生成 ZIP'
    $firstCall = Get-Content -LiteralPath $global:PublishTestRuntimeLog -First 1 | ConvertFrom-Json
    if ($firstCall.DotnetRoot -cne $savedEnvironment.DOTNET_ROOT -or $firstCall.Path -cne $savedEnvironment.PATH -or $firstCall.CliHome -cne $savedEnvironment.DOTNET_CLI_HOME -or $firstCall.Packages -cne $savedEnvironment.NUGET_PACKAGES) { throw '未先使用现有环境直接运行' }
    $count++; Write-Output 'PASS 当前工具可用时不修改环境，即使仓库已有 SDK'

    Set-Content -LiteralPath ($output + '.zip') -Value '已有 ZIP，不能修改'
    & $publish -BackgroundOnly
    if ((Get-Content -LiteralPath ($output + '.zip') -Raw).Trim() -ne '已有 ZIP，不能修改') { throw '目录发布触碰已有 ZIP' }
    $count++; Write-Output 'PASS 再次发布不修改已有 ZIP'

    $custom = Join-Path $testRoot 'artifacts/自定义 发布目录'
    & $publish -BackgroundOnly -OutputDirectory $custom
    if (!(Test-Path -LiteralPath (Join-Path $custom 'reader/PinyinGloss.CandidateReader.exe'))) { throw '自定义输出缺少读取器' }
    $count++; Write-Output 'PASS 含空格的自定义输出目录'

    Set-Content -LiteralPath (Join-Path $output 'llm-installation.json') -Value '已有模型清单'
    New-Item -ItemType Directory -Path (Join-Path $output 'LLM') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $output 'LLM/model.gguf') -Value '已有权重，不能改动'
    Set-Content -LiteralPath (Join-Path $output 'PinyinGloss.Desktop.exe') -Value '旧程序'
    $manifestHash = (Get-FileHash -LiteralPath (Join-Path $output 'llm-installation.json')).Hash
    $modelHash = (Get-FileHash -LiteralPath (Join-Path $output 'LLM/model.gguf')).Hash
    & $publish -BackgroundOnly
    if ((Get-FileHash -LiteralPath (Join-Path $output 'llm-installation.json')).Hash -ne $manifestHash -or (Get-FileHash -LiteralPath (Join-Path $output 'LLM/model.gguf')).Hash -ne $modelHash -or (Get-Content -LiteralPath (Join-Path $output 'PinyinGloss.Desktop.exe') -Raw).Trim() -ne '验收用外部构建器产物') { throw '更新程序时未保留已有模型和清单' }
    if (!(Test-Path -LiteralPath (Join-Path $output 'docs/validation.md')) -or (Test-Path -LiteralPath (Join-Path $output 'docs/docs'))) { throw '重复发布的文档目录错误' }
    $count++; Write-Output 'PASS 已有模型目录可重复发布，更新程序并保留模型和清单'

    $global:PublishTestRunning = $true
    Clear-Content -LiteralPath $global:PublishTestRuntimeLog
    $rejected = $false
    try { & $publish -BackgroundOnly } catch { $rejected = $_.Exception.Message -like '*请先退出*' }
    if (!$rejected -or (Get-Content -LiteralPath $global:PublishTestRuntimeLog | Select-String '"Command":"publish"') -or (Get-FileHash -LiteralPath (Join-Path $output 'LLM/model.gguf')).Hash -ne $modelHash) { throw '程序运行中仍尝试覆盖发布目录' }
    $global:PublishTestRunning = $false
    $count++; Write-Output 'PASS 正式程序运行时停止发布，不覆盖使用中的文件'

    $linkTarget = Join-Path $testRoot 'link-target'
    New-Item -ItemType Directory -Path $linkTarget -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $linkTarget 'untouched.txt') -Value '链接目标必须保留'
    New-Item -ItemType Junction -Path (Join-Path $custom 'reader-link') -Target $linkTarget | Out-Null
    $rejected = $false
    try { & $publish -BackgroundOnly -OutputDirectory $custom } catch { $rejected = $_.Exception.Message -like '*链接*' }
    if (!$rejected -or (Get-Content -LiteralPath (Join-Path $linkTarget 'untouched.txt') -Raw).Trim() -ne '链接目标必须保留') { throw '未拒绝输出目录内的链接' }
    Remove-Item -LiteralPath (Join-Path $custom 'reader-link') -Force
    $count++; Write-Output 'PASS 发布目录内的链接拒绝，链接目标保持不变'

    $rejected = $false
    try { & $publish -BackgroundOnly -OutputDirectory $testRoot } catch { $rejected = $true }
    if (!$rejected -or !(Test-Path -LiteralPath $publish)) { throw '拒绝越出 artifacts 的输出目录失败' }
    $count++; Write-Output 'PASS 输出目录越界拒绝，不删除工程'

    $rejected = $false
    try { & $publish -BackgroundOnly -OutputDirectory ((Join-Path $testRoot 'artifacts') + [IO.Path]::DirectorySeparatorChar) } catch { $rejected = $true }
    if (!$rejected -or !(Test-Path -LiteralPath (Join-Path $output 'llm-installation.json'))) { throw '拒绝 artifacts 根目录失败' }
    $count++; Write-Output 'PASS 带尾分隔符的 artifacts 根目录拒绝，已有模型清单保留'

    foreach ($mode in @('missing','command-missing')) {
        $global:PublishTestSdkMode = $mode
        Clear-Content -LiteralPath $global:PublishTestRuntimeLog
        & $publish -BackgroundOnly -OutputDirectory $custom
        $runtimeCalls = @(Get-Content -LiteralPath $global:PublishTestRuntimeLog | ForEach-Object { $_ | ConvertFrom-Json })
        if ($runtimeCalls.Count -ne 5 -or $runtimeCalls[0].DotnetRoot -cne $savedEnvironment.DOTNET_ROOT -or $runtimeCalls[1].DotnetRoot -ne $global:PublishTestSdkRoot) { throw 'dotnet 环境错误后未切换本地 SDK 重试' }
        $count++; Write-Output "PASS $mode 后修复并重试，后续命令复用修复结果"
    }

    foreach ($mode in @('code-error','network-error','broken')) {
        $global:PublishTestSdkMode = $mode
        Clear-Content -LiteralPath $global:PublishTestRuntimeLog
        $rejected = $false
        try { & $publish -BackgroundOnly -OutputDirectory $custom } catch { $rejected = $true }
        $runtimeCalls = @(Get-Content -LiteralPath $global:PublishTestRuntimeLog | ForEach-Object { $_ | ConvertFrom-Json })
        $expectedCalls = if ($mode -eq 'broken') { 2 } else { 1 }
        if (!$rejected -or $runtimeCalls.Count -ne $expectedCalls -or !(Test-Path -LiteralPath (Join-Path $custom 'PinyinGloss.Desktop.exe'))) { throw "错误分类或重试上限失效：$mode" }
        $count++; Write-Output "PASS $mode 保留原有输出并按分类停止，最多重试一次"
    }
    $global:PublishTestSdkMode = 'healthy'
    $global:PublishTestFailBuild = $true
    $rejected = $false
    try { & $publish -BackgroundOnly -OutputDirectory $custom } catch { $rejected = $true }
    if (!$rejected -or !(Test-Path -LiteralPath (Join-Path $custom 'PinyinGloss.Desktop.exe'))) { throw '测试失败后仍清理或发布' }
    $count++; Write-Output 'PASS 测试失败即停止，保留已有输出'

    foreach ($name in $savedEnvironment.Keys) {
        if ([Environment]::GetEnvironmentVariable($name, 'Process') -cne $savedEnvironment[$name]) { throw "未恢复调用者环境：$name" }
    }
    if ((Get-Location).Path -ne $savedLocation) { throw '未恢复调用者工作目录' }
    $count++; Write-Output 'PASS 成功和失败均恢复调用者工具链环境及工作目录'
    Write-Output "PASS $count 项发布脚本用例（隔离文件与外部构建器，不联网、不运行模型）"
} finally {
    Remove-Item Function:dotnet,Function:Get-Process
    Remove-Variable -Name PublishTestFailBuild -Scope Global
    Remove-Variable -Name PublishTestSdkMode,PublishTestSdkRoot,PublishTestRuntimeLog,PublishTestRunning -Scope Global
    foreach ($name in $savedEnvironment.Keys) {
        if ($null -eq $savedEnvironment[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    }
}
