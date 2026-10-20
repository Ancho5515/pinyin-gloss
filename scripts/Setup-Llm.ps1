[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [string]$ModelDirectory,
    [ValidateSet('Auto','Cpu','Cuda')][string]$Backend = 'Auto',
    [ValidateSet('Q8_0','Q6_K','Q5_K_M','Q4_K_M')][string]$Quantization = 'Q4_K_M',
    [switch]$SelectQuantization,
    [ValidateRange(1,8)][int]$Connections = 4
)
$ErrorActionPreference = 'Stop'
if (!$InstallDirectory) { $InstallDirectory = Split-Path -Parent $PSScriptRoot }
if ($SelectQuantization) {
    Write-Host '选择 LMT-60-1.7B 量化：1 Q8_0 2.17GB；2 Q6_K 1.67GB；3 Q5_K_M 1.47GB；4 Q4_K_M 1.28GB（默认，更省内存）。'
    Write-Host '较小量化可能降低译文质量，尚未完成系统质量评估；文件大小不等于总显存需求。不会自动换精度。'
    $answer = Read-Host '输入 1-4，直接回车使用 Q4_K_M'
    $Quantization = switch ($answer) { '' {'Q4_K_M'} '1' {'Q8_0'} '2' {'Q6_K'} '3' {'Q5_K_M'} '4' {'Q4_K_M'} default { throw '无效选项，未开始下载。' } }
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
if (!$ModelDirectory) { $ModelDirectory = Join-Path $InstallDirectory 'LLM' }
$ModelDirectory = [IO.Path]::GetFullPath($ModelDirectory)
New-Item -ItemType Directory -Force -Path $InstallDirectory,$ModelDirectory | Out-Null
$revision = 'f03d51b43463d8a457eee1cd765220de69e9bac8'
$models = @{
    Q8_0 = @{Size=[long]2165039200; Hash='18ff357223b2ee8e43f965d92cc72d37287575660d134aaff2835a5fe5697ac1'}
    Q6_K = @{Size=[long]1673007200; Hash='2e6446baba3a62141ecedf0a869afda01fe3b26551389d1d2cbfe6efe4095815'}
    Q5_K_M = @{Size=[long]1471805536; Hash='f7291217a413cf8e159173fd0ba8b45154ca67285cd5fa1feb8f601f0f02703b'}
    Q4_K_M = @{Size=[long]1282439264; Hash='54c6244f100330264cef607188413ee82cb8a254f0cf950011aaa9cee74f7eb5'}
}
$sha = $models[$Quantization].Hash; $modelSize = $models[$Quantization].Size
$file = "LMT-60-1.7B.$Quantization.gguf"
$model = Join-Path $ModelDirectory $file
$url = "https://huggingface.co/mradermacher/LMT-60-1.7B-GGUF/resolve/$revision/$file"
Write-Host "准备 LMT-60-1.7B $Quantization（社区量化，$modelSize 字节），后端 $Backend；不更改系统或在线来源。"
if (!(Test-Path -LiteralPath $model)) {
    $partial = "$model.part"
    # 固定大小分块，重跑复用已完成块；只从固定上游下载，最后仍校验整个文件。
    $parts = "$model.parts"
    New-Item -ItemType Directory -Force -Path $parts | Out-Null
    $jobs = @()
    try {
        for ($worker = 0; $worker -lt $Connections; $worker++) {
            $jobs += Start-Job -ArgumentList $url,$parts,$worker,$Connections,$modelSize -ScriptBlock {
                param($uri,$directory,$index,$workers,$total)
                $ErrorActionPreference = 'Stop'; $total = [long]$total; $size = [long]67108864
                $count = [int][Math]::Ceiling($total / $size)
                for ($i = $index; $i -lt $count; $i += $workers) {
                    $start = $i * $size; $end = [Math]::Min($total - 1, $start + $size - 1)
                    $piece = Join-Path $directory ($i.ToString('D3') + '.part')
                    if ((Test-Path -LiteralPath $piece) -and (Get-Item -LiteralPath $piece).Length -eq ($end - $start + 1)) { continue }
                    & curl.exe --silent --show-error --fail --location --retry 3 --retry-delay 3 --connect-timeout 20 --speed-limit 1024 --speed-time 90 --range "$start-$end" --output $piece $uri
                    if ($LASTEXITCODE -ne 0 -or (Get-Item -LiteralPath $piece).Length -ne ($end - $start + 1)) { throw "下载块 $i 失败；重跑可复用完整块。" }
                    Write-Output "已下载块 $($i + 1)/$count"
                }
            }
        }
        while (@($jobs | Where-Object State -eq 'Running').Count) {
            $jobs | Wait-Job -Timeout 2 | Out-Null
            $jobs | Receive-Job
            $downloaded = [long]0
            foreach ($piece in Get-ChildItem -LiteralPath $parts -Filter '*.part' -File) {
                # NTFS 目录项的长度可能直到句柄关闭才更新，用共享读句柄取实时长度。
                $stream = [IO.File]::Open($piece.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
                try { $downloaded += $stream.Length } finally { $stream.Dispose() }
            }
            Write-Progress -Activity "下载 LMT $Quantization" -Status ("{0:N0} / {1} 字节" -f $downloaded,$modelSize) -PercentComplete ([Math]::Min(100,100 * $downloaded / $modelSize))
            if (@($jobs | Where-Object State -eq 'Failed').Count) { throw '模型分块下载失败；保留完整块，下次重跑继续。' }
        }
        $jobs | Receive-Job
        Write-Progress -Activity "下载 LMT $Quantization" -Completed
        if (@($jobs | Where-Object State -ne 'Completed').Count) { throw '模型下载未完成。' }
        $output = [IO.File]::Create($partial)
        try {
            for ($i = 0; $i -lt [Math]::Ceiling($modelSize / 67108864); $i++) {
                $inputFile = [IO.File]::OpenRead((Join-Path $parts ($i.ToString('D3') + '.part')))
                try { $inputFile.CopyTo($output) } finally { $inputFile.Dispose() }
            }
        } finally { $output.Dispose() }
    } finally { $jobs | Stop-Job; $jobs | Remove-Job -Force }
    if ((Get-Item -LiteralPath $partial).Length -ne $modelSize -or (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $sha) {
        Remove-Item -LiteralPath $partial
        Get-ChildItem -LiteralPath $parts -Filter '*.part' -File | Remove-Item -Force
        throw '模型大小或 SHA256 不匹配，已删除损坏下载，未生成安装配置。'
    }
    Move-Item -LiteralPath $partial -Destination $model
    Get-ChildItem -LiteralPath $parts -Filter '*.part' -File | Remove-Item -Force
    Remove-Item -LiteralPath $parts
}
if ((Get-Item -LiteralPath $model).Length -ne $modelSize -or (Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash -ne $sha) {
    throw "已有模型校验失败，未覆盖文件：$model"
}
Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'third-party/LMT-APACHE-2.0.txt') -Destination (Join-Path $ModelDirectory 'LICENSE-LMT.txt')
# GPU 初始化失败必须能启动独立 CPU 包，不能假定 CUDA DLL 可以加载。
$backends = if ($Backend -eq 'Cpu') { @('Cpu') } else { @('Cuda','Cpu') }
$servers = @{}
foreach ($runtimeBackend in $backends) {
    $runtime = Join-Path $ModelDirectory "llama-b11077-$runtimeBackend"
    & (Join-Path $PSScriptRoot 'Get-LlamaRuntime.ps1') -Backend $runtimeBackend -OutputDirectory $runtime
    $executables = @(Get-ChildItem -LiteralPath $runtime -Recurse -Filter 'llama-server.exe' -File)
    if ($executables.Count -ne 1) { throw '运行时缺少唯一的 llama-server.exe，未生成安装配置。' }
    $servers[$runtimeBackend] = $executables[0].FullName
}
$primary = if ($Backend -eq 'Cpu') { $servers['Cpu'] } else { $servers['Cuda'] }
# 使用相对路径使默认安装可整体移动；自定义目录用绝对路径。
$prefix = $InstallDirectory.TrimEnd('\') + '\'
function InstallationPath([string]$path) {
    if ($path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $path.Substring($prefix.Length) }
    return $path
}
$manifest = [ordered]@{
    SchemaVersion = 1; Model = 'NiuTrans/LMT-60-1.7B'; Quantization = $Quantization
    ModelRepository = 'mradermacher/LMT-60-1.7B-GGUF'; ModelRevision = $revision; ModelSha256 = $sha
    ModelPath = (InstallationPath $model); ExecutablePath = (InstallationPath $primary)
    CpuExecutablePath = (InstallationPath $servers['Cpu'])
    Backend = $Backend; RuntimeTag = 'b11077'; RuntimeRevision = '982a3329af8401772087d67c54d5948a5b270943'
}
@'
LMT-60-1.7B：NiuTrans，Apache-2.0，https://huggingface.co/NiuTrans/LMT-60-1.7B
GGUF 量化：mradermacher，Apache-2.0，https://huggingface.co/mradermacher/LMT-60-1.7B-GGUF
GGUF revision 固定于安装配置；社区模型卡未说明原始 safetensors 的具体 revision，不作此保证。
llama.cpp：MIT，https://github.com/ggml-org/llama.cpp/tree/b11077
运行时目录内的上游及 NVIDIA 许可必须保留。程序发布包不包含模型权重。
'@ | Set-Content -LiteralPath (Join-Path $ModelDirectory 'SOURCES.txt') -Encoding UTF8
$destination = Join-Path $InstallDirectory 'llm-installation.json'
$temp = "$destination.$([guid]::NewGuid().ToString('N')).tmp"
try {
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $destination -Force
} finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
Write-Host "下载与校验完成：$destination"
Write-Host '重新启动 PinyinGloss.Desktop.exe 即可识别。已有手工模型配置和显式禁用设置保持不变。'
