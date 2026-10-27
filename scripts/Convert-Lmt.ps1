#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModelDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Revision,
    [Parameter(Mandatory)][string]$LlamaCppDirectory,
    [Parameter(Mandatory)][string]$QuantizeExecutable,
    [string]$OutputDirectory = 'models/LMT-60-1.7B-Q8_0'
)
$ErrorActionPreference = 'Stop'
function Invoke-PythonConversion {
    param([string[]]$Arguments)
    $PSNativeCommandUseErrorActionPreference = $false
    $savedEncoding = [Console]::OutputEncoding
    $savedPythonEncoding = [Environment]::GetEnvironmentVariable('PYTHONIOENCODING', 'Process')
    try {
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        $env:PYTHONIOENCODING = 'utf-8'
        $pythonCommand = 'python'; $prefix = @()
        for ($attempt = 0; $attempt -lt 2; $attempt++) {
            $output = @(); $missingCommand = $false
            try { & $pythonCommand @prefix @Arguments 2>&1 | Tee-Object -Variable output | Out-Host; $exitCode = $LASTEXITCODE }
            catch [Management.Automation.CommandNotFoundException] { $output = @($_.Exception.Message); $missingCommand = $true; $exitCode = 1; Write-Host $output[0] }
            if ($exitCode -eq 0) { return }
            $errorText = $output -join "`n"
            $environmentError = $missingCommand -or $errorText -match 'ModuleNotFoundError|No module named|DLL load failed|Fatal Python error: (init_fs_encoding|init_import_site|init_importlib|init_stdio)|Python was not found'
            if ($attempt -ne 0 -or !$environmentError) { throw "Python 转换失败（退出码 $exitCode），已停止。" }
            if ($env:PYTHONHOME -and $errorText -match 'encodings|init_fs_encoding|PYTHONHOME') {
                Remove-Item Env:PYTHONHOME
                Write-Output '检测到 Python 标准库路径错误，临时清除 PYTHONHOME 后重试一次。'
            } else {
                $pythonCommand = @(Join-Path $LlamaCppDirectory '.venv/Scripts/python.exe'; Join-Path (Split-Path -Parent $PSScriptRoot) '.venv/Scripts/python.exe') |
                    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
                if (!$pythonCommand -and (Get-Command py -ErrorAction SilentlyContinue)) { $pythonCommand = 'py'; $prefix = @('-3') }
                if (!$pythonCommand) { throw 'Python 环境不可用，未找到已有虚拟环境或 py 启动器；请根据上方错误补齐解释器或依赖。' }
                Write-Output "检测到 Python 环境错误，改用已有解释器 $pythonCommand 后重试一次。"
            }
        }
    } finally {
        [Console]::OutputEncoding = $savedEncoding
        if ($null -eq $savedPythonEncoding) { Remove-Item Env:PYTHONIOENCODING -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable('PYTHONIOENCODING', $savedPythonEncoding, 'Process') }
    }
}
$actual = git -C $LlamaCppDirectory rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -ne '982a3329af8401772087d67c54d5948a5b270943') { throw 'Use the pinned llama.cpp b11077 checkout' }
if (-not (Test-Path (Join-Path $ModelDirectory 'config.json'))) { throw 'Missing official translation checkpoint' }
# Download/checkpoint identity is supplied and must be independently verified; this script does not contact a model service.
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
$f16 = Join-Path $OutputDirectory 'LMT-60-1.7B-f16.gguf'
$q8 = Join-Path $OutputDirectory 'LMT-60-1.7B-Q8_0.gguf'
$savedPythonHome = [Environment]::GetEnvironmentVariable('PYTHONHOME', 'Process')
try { Invoke-PythonConversion -Arguments @((Join-Path $LlamaCppDirectory 'convert_hf_to_gguf.py'),$ModelDirectory,'--outfile',$f16,'--outtype','f16') }
finally {
    if ($null -eq $savedPythonHome) { Remove-Item Env:PYTHONHOME -ErrorAction SilentlyContinue }
    else { [Environment]::SetEnvironmentVariable('PYTHONHOME', $savedPythonHome, 'Process') }
}
& $QuantizeExecutable $f16 $q8 Q8_0
if ($LASTEXITCODE -ne 0) { throw 'Q8_0 quantization failed' }
@{
    checkpoint='NiuTrans/LMT-60-1.7B'; revision=$Revision; quantization='Q8_0';
    llamaCppRevision=$actual; sha256=(Get-FileHash $q8 -Algorithm SHA256).Hash.ToLowerInvariant();
    validationStatus='unverified: compare short-word translations before use'; license='Apache-2.0 (verify downloaded revision)'
} | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'manifest.json') -Encoding utf8
Write-Output "Converted $q8. Keep checkpoint license/tokenizer/template and manifest; quality and latency require validation."
