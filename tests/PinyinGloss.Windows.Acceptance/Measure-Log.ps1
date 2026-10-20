[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$LogPath)
$ErrorActionPreference = 'Stop'
$samples = [Collections.Generic.List[double]]::new()
$published = 0
foreach ($path in $LogPath) {
    $rows = @(Get-Content -LiteralPath $path | ConvertFrom-Json)
    $pages = @{}; $starts = @{}; $measured = @{}; $current = $null
    foreach ($row in $rows) {
        if ($row.kind -eq 'snapshot' -and $row.value.SnapshotId) {
            $pages[$row.value.SnapshotId] = $row.value
            $published++
        }
    }
    foreach ($row in $rows) {
        if ($row.kind -eq 'reader' -and $row.value.Kind -eq 0 -and !$starts.ContainsKey($row.value.SnapshotId)) {
            $starts[$row.value.SnapshotId] = $row.ms
        }
        if ($row.kind -eq 'snapshot') { $current = $row.value.SnapshotId }
        if ($row.kind -eq 'cell') {
            $page = $pages[$row.value.SnapshotId]
            if (!$page) { throw "缺少单元格对应快照：$($row.value.SnapshotId)" }
            $candidate = @($page.candidates | Where-Object Id -eq $row.value.CandidateId)
            if ($candidate.Count -ne 1 -or $candidate[0].text -ne $row.value.text) { throw '候选与释义原文不匹配' }
        }
        if ($row.kind -eq 'render') {
            if ($row.value.IsActive) { throw '释义窗发生激活' }
            if ($current -and $row.value.IsVisible -and $row.value.texts -and
                $row.value.texts -notcontains '等待…' -and !$measured.ContainsKey($current) -and $starts.ContainsKey($current)) {
                $samples.Add($row.ms - $starts[$current]); $measured[$current] = $true
            }
        }
    }
}
if ($samples.Count -eq 0) { throw '没有有效测量样本（需要 --include-text 日志）' }
$ordered = @($samples | Sort-Object)
[pscustomobject]@{
    scope = '主进程收到快照至自身 WPF 文本树无等待单元格；20ms 轮询，含日志开销，不是按键到屏幕延迟'
    publishedPages = $published; measuredPages = $samples.Count
    p50Ms = $ordered[[Math]::Ceiling($ordered.Count * 0.50) - 1]
    p95Ms = $ordered[[Math]::Ceiling($ordered.Count * 0.95) - 1]
    maxMs = $ordered[-1]
} | ConvertTo-Json
