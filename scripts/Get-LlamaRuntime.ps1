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
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
foreach ($package in $packages) {
    $archive = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString() + '.zip')
    try {
        Invoke-WebRequest "https://github.com/ggml-org/llama.cpp/releases/download/$tag/$($package.Name)" -OutFile $archive
        if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.Hash) { throw 'Runtime SHA256 mismatch' }
        Expand-Archive $archive $OutputDirectory -Force
    } finally { if (Test-Path $archive) { Remove-Item $archive } }
}
@{tag=$tag; revision='982a3329af8401772087d67c54d5948a5b270943'; backend=$Backend; packages=$packages} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'pinyin-gloss-runtime.json') -Encoding utf8
Write-Output 'Runtime downloaded and verified. Preserve upstream/NVIDIA license files; select llama-server.exe in settings.'
