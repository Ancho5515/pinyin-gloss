[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$inputFile = (Resolve-Path -LiteralPath $InputPath).Path
$outputFile = [IO.Path]::GetFullPath($OutputPath)
if ($inputFile -eq $outputFile) { throw '输入与输出路径必须不同' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFile)) | Out-Null
$tempFile = $outputFile + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$records = 0; $rows = 0; $splitRecords = 0
try {
    $reader = [IO.StreamReader]::new($inputFile, $utf8)
    try {
        $writer = [IO.StreamWriter]::new($tempFile, $false, $utf8)
        try {
            $writer.WriteLine('# CC-CEDICT contributors; published by MDBG; CC BY-SA 4.0')
            $writer.WriteLine('# https://www.mdbg.net/chinese/dictionary?page=cedict')
            $writer.WriteLine('# https://creativecommons.org/licenses/by-sa/4.0/')
            $writer.WriteLine('# 转换：简体词头、英文释义、繁体别名；去除拼音；长释义按义项分行，不截断。')
            while ($null -ne ($line = $reader.ReadLine())) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                if ($line.StartsWith('#')) { $writer.WriteLine($line); continue }
                if ($line -notmatch '^(?<traditional>\S+) (?<simplified>\S+) \[[^\]]+\] /(?<gloss>.+)/$') { throw "CEDICT 格式错误，记录 $($records + 1)" }
                $traditional = $Matches.traditional; $simplified = $Matches.simplified
                if ($simplified.Length -gt 128 -or $traditional.Length -gt 128 -or $traditional.Contains('|')) { throw '词头或别名超出 TSV 范围' }
                $glosses = [Collections.Generic.List[string]]::new()
                $chunk = ''
                foreach ($sense in $Matches.gloss.Split('/')) {
                    if ([string]::IsNullOrWhiteSpace($sense) -or $sense.Length -gt 512) { throw "义项为空或超出 512 字符：$simplified" }
                    if ($chunk.Length -gt 0 -and $chunk.Length + 3 + $sense.Length -gt 512) { $glosses.Add($chunk); $chunk = '' }
                    $chunk = if ($chunk.Length -eq 0) { $sense } else { $chunk + ' / ' + $sense }
                }
                $glosses.Add($chunk)
                if ($glosses.Count -gt 1) { $splitRecords++ }
                foreach ($gloss in $glosses) {
                    $row = "$simplified`ten`t$gloss"
                    if ($traditional -ne $simplified) { $row += "`t`t$traditional" }
                    $writer.WriteLine($row); $rows++
                }
                $records++
            }
            if ($records -eq 0) { throw '词典没有记录' }
        } finally { $writer.Dispose() }
    } finally { $reader.Dispose() }
    [IO.File]::Move($tempFile, $outputFile, $true)
    $manifest = [ordered]@{
        name = 'CC-CEDICT'; source = 'https://www.mdbg.net/chinese/dictionary?page=cedict'
        license = 'CC-BY-SA-4.0'; inputSha256 = (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash.ToLowerInvariant()
        tsvSha256 = (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash.ToLowerInvariant()
        originalRecords = $records; tsvRows = $rows; splitRecords = $splitRecords
        changes = '去除拼音，以简体为词头、繁体为别名；超过 512 字符的释义按完整义项分行；未删减义项。'
    }
    $json = $manifest | ConvertTo-Json
    [IO.File]::WriteAllText($outputFile + '.json', $json, $utf8)
    Write-Output $json
} finally { if ([IO.File]::Exists($tempFile)) { [IO.File]::Delete($tempFile) } }
