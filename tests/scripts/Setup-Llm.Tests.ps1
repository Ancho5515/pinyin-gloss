$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactRoot = Join-Path $root 'artifacts/validation'
$testRoot = Join-Path $artifactRoot ('setup-script-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$count = 0
try {
    foreach ($quantization in @('Q8_0','Q6_K','Q5_K_M','Q4_K_M')) { foreach ($custom in @($false,$true)) {
        $install = Join-Path $testRoot ("安装目录 $quantization $custom")
        $modelDir = if ($custom) { Join-Path $testRoot "自定义 模型 $quantization" } else { Join-Path $install 'LLM' }
        New-Item -ItemType Directory -Force -Path $install,$modelDir | Out-Null
        $model = Join-Path $modelDir "LMT-60-1.7B.$quantization.gguf"
        [IO.File]::WriteAllText($model, 'existing invalid model - preserve me')
        $manifest = Join-Path $install 'llm-installation.json'
        [IO.File]::WriteAllText($manifest, 'existing configuration - preserve me')
        $parameters = @{InstallDirectory=$install; Quantization=$quantization}
        if ($custom) { $parameters.ModelDirectory = $modelDir }
        $rejected = $false
        try { & (Join-Path $root 'scripts/Setup-Llm.ps1') @parameters }
        catch { $rejected = $_.Exception.Message -like '*已有模型校验失败*' }
        if (!$rejected) { throw "未拒绝已有错误模型：custom=$custom" }
        if ([IO.File]::ReadAllText($model) -ne 'existing invalid model - preserve me') { throw '已有文件被覆盖' }
        if ([IO.File]::ReadAllText($manifest) -ne 'existing configuration - preserve me') { throw '失败安装覆盖了清单' }
        if (Test-Path -LiteralPath "$model.parts") { throw '已有错误文件不应触发下载' }
        $count++; Write-Output "PASS 默认/自定义含空格路径、错误模型拒绝、已有配置保护：$quantization custom=$custom"
    } }
    function Read-Host { '4' }
    $menuRoot = Join-Path $testRoot '菜单选择'
    $menuModels = Join-Path $menuRoot 'LLM'
    New-Item -ItemType Directory -Force -Path $menuModels | Out-Null
    foreach ($quantization in @('Q8_0','Q6_K','Q5_K_M','Q4_K_M')) {
        [IO.File]::WriteAllText((Join-Path $menuModels "LMT-60-1.7B.$quantization.gguf"), 'invalid fixture')
    }
    $rejected = $false
    try { & (Join-Path $root 'scripts/Setup-Llm.ps1') -InstallDirectory $menuRoot -SelectQuantization }
    catch { $rejected = $_.Exception.Message -like '*已有模型校验失败*Q4_K_M.gguf*' }
    if (!$rejected) { throw '选择菜单未应用 Q4_K_M' }
    $count++; Write-Output 'PASS 量化菜单选择 4 正确应用 Q4_K_M，不联网'
    function Read-Host { '' }
    $rejected = $false
    try { & (Join-Path $root 'scripts/Setup-Llm.ps1') -InstallDirectory $menuRoot -SelectQuantization }
    catch { $rejected = $_.Exception.Message -like '*已有模型校验失败*Q4_K_M.gguf*' }
    if (!$rejected) { throw '量化菜单直接回车未默认选择 Q4_K_M' }
    $count++; Write-Output 'PASS 量化菜单直接回车默认 Q4_K_M，不联网'
    function Read-Host { 'invalid' }
    $invalidRoot = Join-Path $testRoot '无效菜单'
    $rejected = $false
    try { & (Join-Path $root 'scripts/Setup-Llm.ps1') -InstallDirectory $invalidRoot -SelectQuantization }
    catch { $rejected = $_.Exception.Message -like '*无效选项*' }
    if (!$rejected -or (Test-Path -LiteralPath $invalidRoot)) { throw '无效菜单选择未及时拒绝' }
    Remove-Item Function:Read-Host
    $count++; Write-Output 'PASS 无效菜单拒绝，无文件或网络操作'
    $directRoot = Join-Path $testRoot '直接脚本默认路径'
    $directScripts = Join-Path $directRoot 'scripts'; $directModels = Join-Path $directRoot 'LLM'
    New-Item -ItemType Directory -Force -Path $directScripts,$directModels | Out-Null
    $directScript = Join-Path $directScripts 'Setup-Llm.ps1'
    Copy-Item -LiteralPath (Join-Path $root 'scripts/Setup-Llm.ps1') -Destination $directScript
    foreach ($quantization in @('Q8_0','Q6_K','Q5_K_M','Q4_K_M')) { [IO.File]::WriteAllText((Join-Path $directModels "LMT-60-1.7B.$quantization.gguf"), 'invalid fixture') }
    $directLog = Join-Path $directRoot 'result.log'
    $savedPreference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { powershell.exe -NoProfile -ExecutionPolicy Bypass -File $directScript *> $directLog; $directExit = $LASTEXITCODE }
    finally { $ErrorActionPreference = $savedPreference }
    if ($directExit -eq 0 -or (Get-Content -LiteralPath $directLog -Raw) -notlike '*已有模型校验失败*Q4_K_M.gguf*') { throw 'PS5.1 无参数脚本未使用 exe 同级 LLM 默认目录/Q4 模型' }
    $count++; Write-Output 'PASS PS5.1 直接无参数脚本使用默认目录且拒绝错误文件，不联网'
    Write-Output "PASS $count 项脚本用例（不联网、不创建窗口）"
} finally {
    $full = [IO.Path]::GetFullPath($testRoot)
    if (!$full.StartsWith([IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '测试目录越界' }
    Remove-Item -LiteralPath $full -Recurse -Force
}
