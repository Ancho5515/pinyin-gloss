$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$directory = Join-Path $root ('artifacts/validation/converter-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$inputFile = Join-Path $directory 'fixture.txt'; $outputFile = Join-Path $directory 'result.tsv'
$utf8 = [Text.UTF8Encoding]::new($false)
$first = 'a' * 500; $second = 'b' * 20
[IO.File]::WriteAllText($inputFile, "# 原创测试数据，MIT`n繁體 繁体 [fan2 ti3] /$first/$second/`n苹果 苹果 [ping2 guo3] /apple/`n", $utf8)
& (Join-Path $root 'scripts/Convert-Cedict.ps1') -InputPath $inputFile -OutputPath $outputFile | Out-Null
$rows = @(Get-Content -LiteralPath $outputFile | Where-Object { -not $_.StartsWith('#') })
if ($rows.Count -ne 3 -or $rows[0] -ne "繁体`ten`t$first`t`t繁體" -or $rows[1] -ne "繁体`ten`t$second`t`t繁體" -or $rows[2] -ne "苹果`ten`tapple") { throw '繁简别名或长释义分行不符合预期' }
Write-Output 'PASS 繁简别名、长释义不截断、普通记录'
$before = (Get-FileHash -LiteralPath $outputFile).Hash
[IO.File]::WriteAllText($inputFile, "invalid`n", $utf8)
$rejected = $false
try { & (Join-Path $root 'scripts/Convert-Cedict.ps1') -InputPath $inputFile -OutputPath $outputFile | Out-Null } catch { $rejected = $true }
if (-not $rejected -or (Get-FileHash -LiteralPath $outputFile).Hash -ne $before) { throw '格式错误未拒绝或覆盖了原 TSV' }
Write-Output 'PASS 损坏源文件不覆盖原 TSV'
$rejected = $false
try { & (Join-Path $root 'scripts/Convert-Cedict.ps1') -InputPath $inputFile -OutputPath $inputFile | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw '未拒绝输入输出同路径' }
Write-Output 'PASS 拒绝输入输出同路径'
