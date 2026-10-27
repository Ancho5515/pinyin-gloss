$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $root ('artifacts/validation/convert-runtime-' + [guid]::NewGuid().ToString('N'))
$model = Join-Path $testRoot 'model'; $llama = Join-Path $testRoot 'llama'
New-Item -ItemType Directory -Path $model,$llama,(Join-Path $testRoot 'scripts') -Force | Out-Null
$converter = Join-Path $testRoot 'scripts/Convert-Lmt.ps1'
Copy-Item -LiteralPath (Join-Path $root 'scripts/Convert-Lmt.ps1') -Destination $converter
Set-Content -LiteralPath (Join-Path $model 'config.json') -Value '{}'
$quantizer = Join-Path $testRoot 'Quantize.ps1'
Set-Content -LiteralPath $quantizer -Value 'Set-Content -LiteralPath $args[1] -Value "quantized fixture"; $global:LASTEXITCODE = 0'
$savedHome = [Environment]::GetEnvironmentVariable('PYTHONHOME', 'Process')
$savedPath = $env:PATH
$global:ConvertRuntimeLog = Join-Path $testRoot 'calls.jsonl'
$global:ConvertRuntimeMode = 'healthy'
function git { Write-Output '982a3329af8401772087d67c54d5948a5b270943'; $global:LASTEXITCODE = 0 }
function Write-TestConversion($arguments) {
    $output = $arguments[[Array]::IndexOf($arguments, '--outfile') + 1]
    Set-Content -LiteralPath $output -Value 'f16 fixture'
    $global:LASTEXITCODE = 0
}
function python {
    Add-Content -LiteralPath $global:ConvertRuntimeLog -Value 'python'
    if ($global:ConvertRuntimeMode -eq 'missing') { throw [Management.Automation.CommandNotFoundException]::new('python command not found') }
    if ($global:ConvertRuntimeMode -in @('module','broken')) { Write-Output "ModuleNotFoundError: No module named 'torch'"; $global:LASTEXITCODE = 1; return }
    if ($global:ConvertRuntimeMode -eq 'home' -and $env:PYTHONHOME) { Write-Output "Fatal Python error: init_fs_encoding; No module named 'encodings'"; $global:LASTEXITCODE = 1; return }
    if ($global:ConvertRuntimeMode -eq 'code-error') { Write-Output 'ValueError: invalid model data'; $global:LASTEXITCODE = 1; return }
    Write-TestConversion $args
}
function py {
    Add-Content -LiteralPath $global:ConvertRuntimeLog -Value 'py'
    if ($global:ConvertRuntimeMode -eq 'broken') { Write-Output "ModuleNotFoundError: No module named 'torch'"; $global:LASTEXITCODE = 1; return }
    Write-TestConversion $args
}
$count = 0
try {
    foreach ($mode in @('healthy','missing','module','home','code-error','broken')) {
        $global:ConvertRuntimeMode = $mode
        $output = Join-Path $testRoot $mode
        if (Test-Path -LiteralPath $global:ConvertRuntimeLog) { Clear-Content -LiteralPath $global:ConvertRuntimeLog }
        if ($mode -eq 'home') { $env:PYTHONHOME = Join-Path $testRoot 'invalid-python-home' }
        $homeBefore = [Environment]::GetEnvironmentVariable('PYTHONHOME', 'Process')
        $failed = $false
        try { & $converter -ModelDirectory $model -Revision ('a' * 40) -LlamaCppDirectory $llama -QuantizeExecutable $quantizer -OutputDirectory $output }
        catch { $failed = $true; if ($mode -notin @('code-error','broken')) { throw } }
        $calls = @(Get-Content -LiteralPath $global:ConvertRuntimeLog)
        $expectedCalls = if ($mode -in @('healthy','code-error')) { 1 } else { 2 }
        if ($calls.Count -ne $expectedCalls -or $calls[0] -ne 'python') { throw "首次执行或重试数量错误：$mode" }
        if ([Environment]::GetEnvironmentVariable('PYTHONHOME', 'Process') -cne $homeBefore) { throw '未恢复 PYTHONHOME' }
        if ($mode -in @('code-error','broken')) {
            if (!$failed -or (Test-Path -LiteralPath (Join-Path $output 'manifest.json'))) { throw '业务错误被当成环境错误继续运行' }
        } elseif (!(Test-Path -LiteralPath (Join-Path $output 'manifest.json'))) { throw "未恢复转换：$mode" }
        $count++; Write-Output "PASS Python $mode：现有命令先运行，按错误分类处理，保留调用者环境"
    }
    Remove-Item Function:py
    $env:PATH = ''
    $global:ConvertRuntimeMode = 'missing'
    Clear-Content -LiteralPath $global:ConvertRuntimeLog
    $failed = $false
    try { & $converter -ModelDirectory $model -Revision ('a' * 40) -LlamaCppDirectory $llama -QuantizeExecutable $quantizer -OutputDirectory (Join-Path $testRoot 'unavailable') }
    catch { $failed = $_.Exception.Message -like '*未找到已有虚拟环境或 py 启动器*' }
    if (!$failed -or @(Get-Content -LiteralPath $global:ConvertRuntimeLog).Count -ne 1) { throw '没有可用 Python 时未明确停止' }
    $count++; Write-Output 'PASS 未找到可用 Python 时保留原错误并停止，不自动安装'
    Write-Output "PASS $count 项 Python 转换脚本用例（不运行模型、不下载）"
} finally {
    Remove-Item Function:git,Function:python,Function:py,Function:Write-TestConversion -ErrorAction SilentlyContinue
    Remove-Variable -Name ConvertRuntimeLog,ConvertRuntimeMode -Scope Global
    if ($null -eq $savedHome) { Remove-Item Env:PYTHONHOME -ErrorAction SilentlyContinue }
    else { [Environment]::SetEnvironmentVariable('PYTHONHOME', $savedHome, 'Process') }
    $env:PATH = $savedPath
}
