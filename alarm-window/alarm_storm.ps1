# アラームの負荷試験用スクリプト

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
 
$scriptPath = (Resolve-Path .\alarm.ps1).Path
$powerShellExe = Join-Path $PSHOME 'powershell.exe'
 
$totalCount = 1000
$processes = New-Object System.Collections.Generic.List[System.Diagnostics.Process]
 
$startedCount = 0
$startFailedCount = 0
 
Write-Host "Starting $totalCount processes without interval..."
 
$totalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$launchStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
 
for ($i = 1; $i -le $totalCount; $i++) {
    $message = 'LOAD-{0:D4} {1}' -f `
        $i,
    [System.DateTime]::Now.ToString('HH:mm:ss.fff')
 
    $argumentText = (
        '-NoLogo ' +
        '-NoProfile ' +
        '-NonInteractive ' +
        '-STA ' +
        '-WindowStyle Hidden ' +
        '-ExecutionPolicy Bypass ' +
        '-File "' +
        $scriptPath +
        '" "' +
        $message +
        '"'
    )
 
    try {
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $powerShellExe
        $startInfo.Arguments = $argumentText
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
 
        $startInfo.WindowStyle =
        [System.Diagnostics.ProcessWindowStyle]::Hidden
 
        $process = [System.Diagnostics.Process]::Start($startInfo)

        if ($null -eq $process) {
            $startFailedCount++
        }
        else {
            $processes.Add($process)
            $startedCount++
        }

        # 次のPowerShellプロセスを起動する前に80ミリ秒待機
        Start-Sleep -Milliseconds 50
    }
    catch {
        $startFailedCount++
 
        Write-Warning (
            'Start failed: LOAD-{0:D4}: {1}' -f
            $i,
            $_.Exception.Message
        )
    }
 
    if (($i % 100) -eq 0) {
        Write-Host (
            "`rStarted: {0}/{1}" -f
            $i,
            $totalCount
        ) -NoNewline
    }
}
 
$launchStopwatch.Stop()
 
Write-Host ''
Write-Host 'All start requests have been issued.'
Write-Host 'Waiting for sender processes to exit...'
 
# ============================================================
# 全送信プロセスの終了を待つ
#
# 起動間隔は設けない。
# ここでの待機は、全件処理時間を測るための待機。
# ============================================================
 
$timeoutSeconds = 120
$deadline = [System.DateTime]::UtcNow.AddSeconds($timeoutSeconds)
 
do {
    $runningCount = 0
 
    foreach ($process in $processes) {
        try {
            if (-not $process.HasExited) {
                $runningCount++
            }
        }
        catch {
            # すでに破棄または終了済みなら無視
        }
    }
 
    Write-Host (
        "`rRemaining sender processes: {0}   " -f
        $runningCount
    ) -NoNewline
 
    if ($runningCount -eq 0) {
        break
    }
 
    # 負荷投入時には待機なし。
    # この短い待機は終了確認ループのCPU占有防止のみ。
    Start-Sleep -Milliseconds 100
}
while ([System.DateTime]::UtcNow -lt $deadline)
 
$totalStopwatch.Stop()
Write-Host ''
 
# ============================================================
# 終了コード集計
# ============================================================
 
$exitCodeZeroCount = 0
$exitCodeNonZeroCount = 0
$stillRunningCount = 0
 
$exitCodeGroups = @{}
 
foreach ($process in $processes) {
    try {
        if (-not $process.HasExited) {
            $stillRunningCount++
            continue
        }
 
        $exitCode = $process.ExitCode
 
        if ($exitCode -eq 0) {
            $exitCodeZeroCount++
        }
        else {
            $exitCodeNonZeroCount++
        }
 
        $exitCodeKey = [string]$exitCode
 
        if ($exitCodeGroups.ContainsKey($exitCodeKey)) {
            $exitCodeGroups[$exitCodeKey]++
        }
        else {
            $exitCodeGroups[$exitCodeKey] = 1
        }
    }
    catch {
        $exitCodeNonZeroCount++
    }
    finally {
        try {
            $process.Dispose()
        }
        catch {
        }
    }
}
 
$processes.Clear()
 
# ============================================================
# 測定結果
# ============================================================
 
$launchSeconds = $launchStopwatch.Elapsed.TotalSeconds
$totalSeconds = $totalStopwatch.Elapsed.TotalSeconds
 
$launchRate = 0
$totalRate = 0
 
if ($launchSeconds -gt 0) {
    $launchRate = $startedCount / $launchSeconds
}
 
if ($totalSeconds -gt 0) {
    $totalRate = $exitCodeZeroCount / $totalSeconds
}
 
''
'============================================================'
'Load test result'
'============================================================'
'Requested                 : {0}' -f $totalCount
'Started                   : {0}' -f $startedCount
'Start failed              : {0}' -f $startFailedCount
'Exit code 0               : {0}' -f $exitCodeZeroCount
'Exit code non-zero        : {0}' -f $exitCodeNonZeroCount
'Still running             : {0}' -f $stillRunningCount
'Launch elapsed            : {0:N3} sec' -f $launchSeconds
'Launch rate               : {0:N1} starts/sec' -f $launchRate
'Total elapsed             : {0:N3} sec' -f $totalSeconds
'Completed sender rate     : {0:N1} sends/sec' -f $totalRate
'============================================================'
''
'Exit code breakdown:'
 
$exitCodeGroups.GetEnumerator() |
Sort-Object {
    [int]$_.Key
} |
ForEach-Object {
    'Exit code {0}: {1}' -f $_.Key, $_.Value
}
 
if ($stillRunningCount -gt 0) {
    ''
    Write-Warning (
        '{0} sender processes were still running after {1} seconds.' -f
        $stillRunningCount,
        $timeoutSeconds
    )
}