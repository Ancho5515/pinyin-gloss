[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModelDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Revision,
    [Parameter(Mandatory)][string]$LlamaCppDirectory,
    [Parameter(Mandatory)][string]$QuantizeExecutable,
    [string]$OutputDirectory = 'models/LMT-60-1.7B-Q8_0'
)
$ErrorActionPreference = 'Stop'
$actual = git -C $LlamaCppDirectory rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -ne '982a3329af8401772087d67c54d5948a5b270943') { throw 'Use the pinned llama.cpp b11077 checkout' }
if (-not (Test-Path (Join-Path $ModelDirectory 'config.json'))) { throw 'Missing official translation checkpoint' }
# Download/checkpoint identity is supplied and must be independently verified; this script does not contact a model service.
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
$f16 = Join-Path $OutputDirectory 'LMT-60-1.7B-f16.gguf'
$q8 = Join-Path $OutputDirectory 'LMT-60-1.7B-Q8_0.gguf'
python (Join-Path $LlamaCppDirectory 'convert_hf_to_gguf.py') $ModelDirectory --outfile $f16 --outtype f16
if ($LASTEXITCODE -ne 0) { throw 'GGUF conversion failed' }
& $QuantizeExecutable $f16 $q8 Q8_0
if ($LASTEXITCODE -ne 0) { throw 'Q8_0 quantization failed' }
@{
    checkpoint='NiuTrans/LMT-60-1.7B'; revision=$Revision; quantization='Q8_0';
    llamaCppRevision=$actual; sha256=(Get-FileHash $q8 -Algorithm SHA256).Hash.ToLowerInvariant();
    validationStatus='unverified: compare short-word translations before use'; license='Apache-2.0 (verify downloaded revision)'
} | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'manifest.json') -Encoding utf8
Write-Output "Converted $q8. Keep checkpoint license/tokenizer/template and manifest; quality and latency require validation."
