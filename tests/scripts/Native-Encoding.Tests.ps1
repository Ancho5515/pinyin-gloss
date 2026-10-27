#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = Join-Path $root ('artifacts/validation/native-encoding-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$pythonExe = (Get-Command python -CommandType Application | Select-Object -First 1).Source
$savedEncoding = [Console]::OutputEncoding
$savedPythonEncoding = [Environment]::GetEnvironmentVariable('PYTHONIOENCODING', 'Process')
$fixture = 'import sys; sys.stdout.buffer.write("\u6b63\u5728\u786e\u5b9a\u8981\u8fd8\u539f\u7684\u9879\u76ee\n".encode("utf-8")); sys.stderr.buffer.write("\u9519\u8bef\u8f93\u51fa\u4e2d\u6587\n".encode("utf-8")); sys.exit(int(sys.argv[1]))'
$pythonFixture = 'import sys; print("\u6b63\u5728\u786e\u5b9a\u8981\u8fd8\u539f\u7684\u9879\u76ee"); print("\u9519\u8bef\u8f93\u51fa\u4e2d\u6587", file=sys.stderr); sys.exit(int(sys.argv[1]))'
function dotnet { & $pythonExe -c $fixture $nativeExitCode; $global:LASTEXITCODE = $LASTEXITCODE }
$count = 0
try {
    foreach ($script in @('Publish','Convert-Lmt')) {
        $functionName = if ($script -eq 'Publish') { 'Invoke-Dotnet' } else { 'Invoke-PythonConversion' }
        $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $root "scripts/$script.ps1"), [ref]$null, [ref]$null)
        $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $true)
        . ([scriptblock]::Create($definition.Extent.Text))
        foreach ($nativeExitCode in @(0,7)) {
            $log = Join-Path $evidence "$script-$nativeExitCode.log"
            $failed = $false
            Start-Transcript -LiteralPath $log | Out-Null
            try {
                [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(936)
                $callerPythonEncoding = if ($nativeExitCode -eq 0) { 'gbk' } else { $null }
                [Environment]::SetEnvironmentVariable('PYTHONIOENCODING', $callerPythonEncoding, 'Process')
                $callerPythonEncoding = [Environment]::GetEnvironmentVariable('PYTHONIOENCODING', 'Process')
                try {
                    if ($script -eq 'Publish') { Invoke-Dotnet -Arguments @('fixture') }
                    else { Invoke-PythonConversion -Arguments @('-c',$pythonFixture,$nativeExitCode.ToString()) }
                } catch { $failed = $true; if ($nativeExitCode -eq 0) { throw } }
                if ($failed -ne ($nativeExitCode -ne 0)) { throw '原生命令退出码未保留' }
                if ([Console]::OutputEncoding.CodePage -ne 936 -or [Environment]::GetEnvironmentVariable('PYTHONIOENCODING', 'Process') -cne $callerPythonEncoding) { throw '未恢复调用者编码环境' }
            } finally {
                [Console]::OutputEncoding = $savedEncoding
                Stop-Transcript | Out-Null
            }
            $transcript = [IO.File]::ReadAllText($log)
            if (!$transcript.Contains('正在确定要还原的项目') -or !$transcript.Contains('错误输出中文')) { throw "$script 的 UTF-8 原生输出被按 GBK 解码，详见 $log" }
            $count++; Write-Output "PASS $script 退出码 $nativeExitCode：GBK 调用环境下正确读取中文 stdout/stderr，恢复编码环境"
        }
    }
    Write-Output "PASS $count 项原生输出编码回归（真实 Python 输出，不运行模型）"
} finally {
    [Console]::OutputEncoding = $savedEncoding
    if ($null -eq $savedPythonEncoding) { Remove-Item Env:PYTHONIOENCODING -ErrorAction SilentlyContinue }
    else { [Environment]::SetEnvironmentVariable('PYTHONIOENCODING', $savedPythonEncoding, 'Process') }
    Remove-Item Function:dotnet
}
