[CmdletBinding()]
param([ValidateSet('Cuda','Cpu')][string]$Backend = 'Cuda', [string]$OutputDirectory = 'runtime/llama-b11077')
$ErrorActionPreference = 'Stop'
$tag = 'b11077'
$packages = if ($Backend -eq 'Cuda') {
    @(
        @{Name='llama-b11077-bin-win-cuda-12.4-x64.zip'; Hash='7b59daa973b90167bf8659c514c515ebabd5b2068aa22425216bcc64ab31f807'},
        @{Name='cudart-llama-bin-win-cuda-12.4-x64.zip'; Hash='8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6'}
    )
} else { @(@{Name='llama-b11077-bin-win-cpu-x64.zip'; Hash='59a6d523d47f9ca02ffee2d817694774aa6b1410e465f9a34a68d430de040154'}) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'third-party/llama.cpp-LICENSE.txt') -Destination (Join-Path $OutputDirectory 'LICENSE-llama.cpp.txt')
$record = Join-Path $OutputDirectory 'pinyin-gloss-runtime.json'
if (Test-Path -LiteralPath $record) {
    try {
        $installed = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
        $valid = $installed.tag -eq $tag -and $installed.backend -eq $Backend -and @($installed.files).Count -gt 0
        foreach ($entry in $installed.files) {
            $path = Join-Path $OutputDirectory $entry.path
            if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { $valid = $false; break }
        }
        if ($valid -and @(Get-ChildItem -LiteralPath $OutputDirectory -Filter 'llama-server.exe' -File -Recurse).Count -eq 1) {
            Write-Output 'llama.cpp 已安装且文件校验通过，跳过下载。'; return
        }
    } catch { Write-Output '已有运行时清单无效，重新下载固定版本。' }
}
foreach ($package in $packages) {
    $cache = Join-Path (Split-Path -Parent $OutputDirectory) '.runtime-downloads'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $archive = Join-Path $cache $package.Name
    $validArchive = (Test-Path -LiteralPath $archive) -and (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $package.Hash
    if (!$validArchive) {
        Write-Output "下载/续传固定运行时：$($package.Name)"
        & curl.exe --silent --show-error --fail --location --retry 3 --connect-timeout 20 --speed-limit 1024 --speed-time 90 --continue-at - --output $archive "https://github.com/ggml-org/llama.cpp/releases/download/$tag/$($package.Name)"
        if ($LASTEXITCODE -ne 0) { throw "运行时下载失败：$($package.Name)" }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.Hash) {
            Remove-Item -LiteralPath $archive
            throw '运行时 SHA256 不匹配，已删除损坏缓存；重跑重新下载。'
        }
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $OutputDirectory -Force
}
$prefix = $OutputDirectory.TrimEnd('\') + '\'
Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'third-party/llama.cpp-LICENSE.txt') -Destination (Join-Path $OutputDirectory 'LICENSE-llama.cpp.txt')
$files = @(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse | Where-Object FullName -ne $record | ForEach-Object {
    @{path=$_.FullName.Substring($prefix.Length); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
@{tag=$tag; revision='982a3329af8401772087d67c54d5948a5b270943'; backend=$Backend; packages=$packages; files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $record -Encoding utf8
Write-Output 'llama.cpp 已下载并校验；保留运行时内上游与 NVIDIA 许可文件。'
