<#
.SYNOPSIS
    音声再生と画面点滅でイベント発生を通知するスクリプト
    複数のアラーム発生した場合は、タイムスタンプ付きで 1つのウィンドウの中にメッセージを表示

.DESCRIPTION
    【主な動作・仕様】
    ・引数メッセージ : 指定した文字列をタイムスタンプ付きで一覧に追加表示
    ・引数省略時     : "event happened" をデフォルトで表示
    ・音声ループ再生 : a.wav を繰り返し再生 (ない場合は %SystemRoot%\Media\Alarm01.wav)
    ・音の自動停止   : 最後のアラームから一定時間（初期設定10分）経過すると音のみ自動停止
    ・画面点滅       : 警告色で点滅表示
    ・単一ウィンドウ : 既に起動中の場合は既存ウィンドウへメッセージを追加

.PARAMETER Message
    表示するアラームメッセージ（既定値: "event happened"）

.PARAMETER SoundTimeoutMin
    音が自動停止するまでの時間（分、既定値: 10）

.PARAMETER FlashIntervalMs
    ウィンドウの点滅間隔（ミリ秒、既定値: 700）

.EXAMPLE
    .\alarm.ps1 "黒ヤギさんからお手紙が届きました"
    メッセージを指定してアラームウィンドウを表示

.EXAMPLE
    .\alarm.ps1 "白ヤギさんは読まずにお手紙をたべてしまいました"
    既にウィンドウが開いていればそこへメッセージを追記

.EXAMPLE
    .\alarm.ps1 "白ヤギさんは黒ヤギさんへお手紙を書きました"

.EXAMPLE
    .\alarm.ps1 "先ほどの用件は何でしたでしょうか？"

.EXAMPLE
    .\alarm.ps1
    引数を省略して実行した場合、"event happened" と表示

.EXAMPLE
    powershell.exe -File .\alarm.ps1 "白ヤギさんのお手紙を食べてしまいました"
    外部ツール等からの呼び出しする場合
#>

param(
    [Parameter(Position = 0)]
    [AllowEmptyString()]
    [string]$Message = 'event happened',

    # 音が止まるまでの時間
    [int]$SoundTimeoutMin = 10,

    # ウィンドウの点滅間隔
    [int]$FlashIntervalMs = 700,

    [Parameter(DontShow = $true)]
    [switch]$ServerMode
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# プロセス起動時刻
$Timestamp = [System.DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss')

# ============================================================
# 共通ヘルパー関数
# ============================================================

function Test-Blank {
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return $true
    }

    return ([string]$Value).Trim().Length -eq 0
}

function Format-AlarmMessage {
    param([string]$Text)

    if (Test-Blank $Text) {
        return ''
    }

    return ($Text -replace "[\r\n]+", ' ').Trim()
}

function Show-AlarmError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [string]$Caption = 'ALARM'
    )

    try {
        [void][System.Windows.Forms.MessageBox]::Show(
            $Text,
            $Caption,
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        )
    }
    catch {
        Write-Error $Text
    }
}

# ============================================================
# 設定
# ============================================================

# 通信設定
$MutexName = 'Local\AlarmWindow.PowerShell51.Singleton'
$PipeName = 'AlarmWindow.PowerShell51.MessagePipe'

# スクリプトおよび音声ファイルパス
$ScriptPath = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Path }
$ScriptDirectory = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $ScriptPath }

$WaveFilePath = Join-Path -Path $ScriptDirectory -ChildPath 'a.wav'

# a.wav が存在しない場合は Windows 標準の Alarm01.wav を使用
if (-not (Test-Path -LiteralPath $WaveFilePath -PathType Leaf)) {
    $WaveFilePath = "$env:SystemRoot\Media\Alarm01.wav"
}

$Message = Format-AlarmMessage $Message

# ============================================================
# Named Pipe送受信ヘルパー
# ============================================================

function ConvertTo-AlarmPayload {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text
    )
 
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    return [System.Convert]::ToBase64String($bytes)
}

function ConvertFrom-AlarmPayload {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Payload
    )
 
    try {
        $bytes = [System.Convert]::FromBase64String($Payload)
        return [System.Text.Encoding]::UTF8.GetString($bytes)
    }
    catch {
        return $null
    }
}

function Send-AlarmMessage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PipeName,

        [Parameter(Mandatory = $true)]
        [string]$Text,

        [int]$TimeoutMilliseconds = 500
    )

    $client = $null
    $writer = $null

    try {
        $client = New-Object System.IO.Pipes.NamedPipeClientStream(
            '.',
            $PipeName,
            [System.IO.Pipes.PipeDirection]::Out,
            [System.IO.Pipes.PipeOptions]::Asynchronous
        )

        $client.Connect($TimeoutMilliseconds)

        if (-not $client.IsConnected) {
            return $false
        }

        $writer = New-Object System.IO.StreamWriter(
            $client,
            (New-Object System.Text.UTF8Encoding($false))
        )
        $writer.AutoFlush = $true

        $payload = ConvertTo-AlarmPayload -Text $Text
        $writer.WriteLine($payload)
        $writer.Flush()

        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $writer) {
            try { $writer.Dispose() } catch { }
        }

        if ($null -ne $client) {
            try { $client.Dispose() } catch { }
        }
    }
}

function Start-AlarmServer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AlarmMessage,

        [int]$SoundTimeout = 10,

        [int]$FlashInterval = 700
    )

    if (Test-Blank $ScriptPath) {
        throw 'alarm.ps1のパスを取得できませんでした。'
    }

    $encodedMessage = [System.Convert]::ToBase64String(
        [System.Text.Encoding]::Unicode.GetBytes($AlarmMessage)
    )

    $serverCommand = @"
`$decodedMessage = [System.Text.Encoding]::Unicode.GetString(
    [System.Convert]::FromBase64String('$encodedMessage')
)
& '$($ScriptPath.Replace("'", "''"))' -ServerMode -Message `$decodedMessage -SoundTimeoutMin $SoundTimeout -FlashIntervalMs $FlashInterval
"@

    $encodedCommand = [System.Convert]::ToBase64String(
        [System.Text.Encoding]::Unicode.GetBytes($serverCommand)
    )

    $powerShellPath = Join-Path -Path $PSHOME -ChildPath 'powershell.exe'

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $powerShellPath
    $startInfo.Arguments = (
        '-NoProfile -NonInteractive -STA -WindowStyle Hidden ' +
        '-EncodedCommand ' +
        $encodedCommand
    )
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden

    $process = [System.Diagnostics.Process]::Start($startInfo)

    if ($null -eq $process) {
        throw 'アラームウィンドウ用プロセスを起動できませんでした。'
    }

    $process.Dispose()
}

# ============================================================
# 通常呼び出し側
#
# 呼び出しをブロックしないように、初回起動はサーバープロセスを
# 起動してすぐに終了する
# サーバープロセスがあれば、そのプロセスに送信する
# ============================================================

if (-not $ServerMode) {
    # 送信メッセージ（起動時タイムスタンプを連結）
    $outMessage = '{0}  {1}' -f $Timestamp, $Message

    # 既存サーバーがあれば、まず直接送信する。
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $sent = Send-AlarmMessage `
            -PipeName $PipeName `
            -Text $outMessage `
            -TimeoutMilliseconds 100

        if ($sent) {
            exit 0
        }

        Start-Sleep -Milliseconds 100
    }

    # 既存サーバーがなければ、非表示のサーバーモードを起動する。
    try {
        Start-AlarmServer `
            -AlarmMessage $outMessage `
            -SoundTimeout $SoundTimeoutMin `
            -FlashInterval $FlashIntervalMs
    }
    catch {
        Show-AlarmError -Text @"
アラームウィンドウを起動できませんでした。

$($_.Exception.Message)
"@
        exit 1
    }

    # サーバープロセスの起動完了を待たず、
    # 呼び出し元のPowerShellへ制御を戻す。
    exit 0
}

# ============================================================
# サーバープロセス初期化 (ここから先はサーバーモードのみ実行)
# ============================================================

try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
}
catch {
    Write-Error "Windows Formsを読み込めませんでした。$($_.Exception.Message)"
    exit 1
}

[System.Windows.Forms.Application]::EnableVisualStyles()

# 点滅タイマー初期設定
$flashTimer = New-Object System.Windows.Forms.Timer
$flashTimer.Interval = $FlashIntervalMs

# 実行条件確認
if (
    [System.Threading.Thread]::CurrentThread.ApartmentState -ne
    [System.Threading.ApartmentState]::STA
) {
    Show-AlarmError -Text @'
アラームウィンドウを起動できませんでした。
'@
    exit 1
}

if (-not (Test-Path -LiteralPath $WaveFilePath -PathType Leaf)) {
    Show-AlarmError -Text @"
WAVファイルが見つかりません。

必要なファイル:
$WaveFilePath

alarm.ps1と同じディレクトリに a.wav を配置するか、
$env:SystemRoot\Media\Alarm01.wav を用意してください。
"@
    exit 1
}

# ============================================================
# Mutexによる単一起動
# ============================================================

$mutex = $null
$ownsMutex = $false

try {
    $mutex = New-Object System.Threading.Mutex($false, $MutexName)

    try {
        $ownsMutex = $mutex.WaitOne(0, $false)
    }
    catch [System.Threading.AbandonedMutexException] {
        $ownsMutex = $true
    }
}
catch {
    Show-AlarmError -Text @"
単一起動制御用のMutexを作成できませんでした。

$($_.Exception.Message)
"@
    exit 1
}

function Release-AlarmMutex {
    param(
        $MutexInstance,
        [bool]$Owns
    )

    if ($null -eq $MutexInstance) {
        return
    }

    if ($Owns) {
        try {
            $MutexInstance.ReleaseMutex()
        }
        catch {
        }
    }

    try {
        $MutexInstance.Dispose()
    }
    catch {
    }
}

# ============================================================
# サーバープロセスの競合処理
#
# 同時に複数のアラームが発生すると、複数の呼び出し元が
# サーバープロセスを起動する可能性がある。
#
# Mutexを取得できた1個だけがアラーム画面になる。
# 取得できなかった候補は、勝者のNamed Pipeへメッセージを
# 転送して終了する。
# ============================================================

if (-not $ownsMutex) {
    $messageSent = $false

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $messageSent = Send-AlarmMessage `
            -PipeName $PipeName `
            -Text $Message `
            -TimeoutMilliseconds 1000

        if ($messageSent) {
            break
        }

        Start-Sleep -Milliseconds 100
    }

    Release-AlarmMutex -MutexInstance $mutex -Owns $false

    if ($messageSent) {
        exit 0
    }

    # 別のサーバー候補がMutexを取得したものの、
    # Pipeを開始できなかった場合でも、この候補は残さない。
    exit 2
}

# ============================================================
# 共有状態
# ============================================================

$shared = [System.Collections.Hashtable]::Synchronized(@{
        Shutdown          = New-Object System.Threading.ManualResetEvent($false)
        MessageQueue      = New-Object 'System.Collections.Concurrent.ConcurrentQueue[string]'
        SoundWanted       = $false
        LastAlarmUtcTicks = [long]0
    })

# スクリプトレベル状態
$soundPlayer = $null
$pipeWorker = $null

$form = $null
$alarmTextBox = $null
$closeButton = $null

$controlTimer = $null

$script:isClosing = $false
$script:flashIsRed = $true

# ============================================================
# Named Pipe受信ワーカー
# ============================================================

$pipeReceiverScript = {
    param(
        $SharedState,
        [string]$NamedPipeName
    )

    while (-not $SharedState.Shutdown.WaitOne(0)) {
        $server = $null
        $reader = $null

        try {
            $server = New-Object System.IO.Pipes.NamedPipeServerStream(
                $NamedPipeName,
                [System.IO.Pipes.PipeDirection]::In,
                1,
                [System.IO.Pipes.PipeTransmissionMode]::Byte,
                [System.IO.Pipes.PipeOptions]::Asynchronous
            )

            $connectionResult = $server.BeginWaitForConnection($null, $null)

            $connectionHandles = New-Object System.Threading.WaitHandle[] 2
            $connectionHandles[0] = $connectionResult.AsyncWaitHandle
            $connectionHandles[1] = $SharedState.Shutdown

            $waitResult = [System.Threading.WaitHandle]::WaitAny($connectionHandles, 1000)

            if ($SharedState.Shutdown.WaitOne(0)) {
                break
            }

            if ($waitResult -eq [System.Threading.WaitHandle]::WaitTimeout) {
                continue
            }

            if ($waitResult -ne 0) {
                break
            }

            $server.EndWaitForConnection($connectionResult)

            if (-not $server.IsConnected) {
                continue
            }

            $reader = New-Object System.IO.StreamReader(
                $server,
                [System.Text.Encoding]::UTF8,
                $false,
                1024,
                $true
            )

            $readTask = $reader.ReadLineAsync()
            $readAsyncResult = [System.IAsyncResult]$readTask

            $readHandles = New-Object System.Threading.WaitHandle[] 2
            $readHandles[0] = $readAsyncResult.AsyncWaitHandle
            $readHandles[1] = $SharedState.Shutdown

            $readWaitResult = [System.Threading.WaitHandle]::WaitAny($readHandles, 2000)

            if ($SharedState.Shutdown.WaitOne(0)) {
                break
            }

            if ($readWaitResult -eq 0) {
                $receivedLine = $readTask.Result

                if ($null -ne $receivedLine) {
                    $SharedState.MessageQueue.Enqueue($receivedLine)
                }
            }
        }
        catch {
            if (-not $SharedState.Shutdown.WaitOne(0)) {
                Start-Sleep -Milliseconds 500
            }
        }
        finally {
            if ($null -ne $reader) {
                try { $reader.Dispose() } catch { }
            }

            if ($null -ne $server) {
                try { $server.Dispose() } catch { }
            }
        }
    }
}

# ============================================================
# 音声再生制御関数
# ============================================================

function Start-SoundPlayback {
    if ($null -ne $script:soundPlayer) {
        try {
            if (-not [bool]$script:shared.SoundWanted) {
                $script:soundPlayer.PlayLooping()
            }
            $script:shared.SoundWanted = $true
        }
        catch {
            $script:shared.SoundWanted = $false
        }
    }
}

function Stop-SoundPlayback {
    if ($null -ne $script:soundPlayer) {
        try {
            $script:soundPlayer.Stop()
        }
        catch {
        }
    }

    if ($null -ne $script:shared) {
        $script:shared.SoundWanted = $false
    }
}

# ============================================================
# Runspace管理ヘルパー
# ============================================================

function Start-WorkerRunspace {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$ScriptBlock,

        [object[]]$Arguments = @()
    )

    $runspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
    $runspace.ApartmentState = [System.Threading.ApartmentState]::MTA
    $runspace.ThreadOptions = [System.Management.Automation.Runspaces.PSThreadOptions]::ReuseThread
    $runspace.Open()

    $powerShell = [System.Management.Automation.PowerShell]::Create()
    $powerShell.Runspace = $runspace

    [void]$powerShell.AddScript($ScriptBlock.ToString())

    foreach ($arg in $Arguments) {
        [void]$powerShell.AddArgument($arg)
    }

    $asyncResult = $powerShell.BeginInvoke()

    return @{
        Runspace    = $runspace
        PowerShell  = $powerShell
        AsyncResult = $asyncResult
    }
}

function Stop-WorkerRunspace {
    param($Worker)

    if ($null -eq $Worker) {
        return
    }

    if ($null -ne $Worker.PowerShell) {
        try {
            if ($null -ne $Worker.AsyncResult) {
                if ($Worker.AsyncResult.IsCompleted) {
                    try {
                        [void]$Worker.PowerShell.EndInvoke($Worker.AsyncResult)
                    }
                    catch {
                    }
                }
                else {
                    try {
                        $Worker.PowerShell.Stop()
                    }
                    catch {
                    }
                }
            }
        }
        catch {
        }

        try {
            $Worker.PowerShell.Dispose()
        }
        catch {
        }
    }

    if ($null -ne $Worker.Runspace) {
        try {
            $Worker.Runspace.Close()
        }
        catch {
        }

        try {
            $Worker.Runspace.Dispose()
        }
        catch {
        }
    }
}

# ============================================================
# 後始末用関数
# ============================================================

function Stop-AlarmBackgroundWork {
    if ($script:isClosing) {
        return
    }

    $script:isClosing = $true

    if ($null -ne $script:flashTimer) {
        try { $script:flashTimer.Stop() } catch { }
    }

    if ($null -ne $script:controlTimer) {
        try { $script:controlTimer.Stop() } catch { }
    }

    Stop-SoundPlayback

    if ($null -ne $script:shared) {
        try { [void]$script:shared.Shutdown.Set() } catch { }
    }
}

# ============================================================
# GUI補助関数
# ============================================================



function Add-AlarmToWindow {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$AlarmMessage
    )

    $line = if ($AlarmMessage -match '^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(  |$)') {
        '{0}{1}' -f $AlarmMessage, [System.Environment]::NewLine
    }
    else {
        $timestamp = [System.DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss')
        '{0}  {1}{2}' -f $timestamp, (Format-AlarmMessage $AlarmMessage), [System.Environment]::NewLine
    }

    $script:alarmTextBox.AppendText($line)
    $script:alarmTextBox.SelectionStart = $script:alarmTextBox.TextLength
    $script:alarmTextBox.SelectionLength = 0
    $script:alarmTextBox.ScrollToCaret()

    # 最後にアラームを受信した時刻を更新
    $script:shared.LastAlarmUtcTicks = [System.DateTime]::UtcNow.Ticks

    # 音声ループ再生を開始
    Start-SoundPlayback
}

# ============================================================
# フォーム作成
# ============================================================

$form = New-Object System.Windows.Forms.Form
$form.Text = 'ALARM'
$form.ClientSize = New-Object System.Drawing.Size(640, 480)
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.TopMost = $true
$form.ShowInTaskbar = $true
$form.BackColor = [System.Drawing.Color]::Red

# アラーム一覧
$alarmTextBox = New-Object System.Windows.Forms.TextBox
$alarmTextBox.Location = New-Object System.Drawing.Point(15, 20)
$alarmTextBox.Size = New-Object System.Drawing.Size(610, 385)
$alarmTextBox.Multiline = $true
$alarmTextBox.ReadOnly = $true
$alarmTextBox.WordWrap = $false
$alarmTextBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Both
$alarmTextBox.BackColor = [System.Drawing.Color]::White
$alarmTextBox.ForeColor = [System.Drawing.Color]::Black
$alarmTextBox.AcceptsReturn = $false
$alarmTextBox.AcceptsTab = $false
$alarmTextBox.TabStop = $false
$alarmTextBox.HideSelection = $false
$alarmTextBox.Font = New-Object System.Drawing.Font(
    'Consolas',
    14,
    [System.Drawing.FontStyle]::Regular,
    [System.Drawing.GraphicsUnit]::Point
)

# 閉じるボタン
$closeButton = New-Object System.Windows.Forms.Button
$closeButton.Location = New-Object System.Drawing.Point(250, 420)
$closeButton.Size = New-Object System.Drawing.Size(140, 45)
$closeButton.Text = '閉じる'
$closeButton.UseVisualStyleBackColor = $true
$closeButton.Font = New-Object System.Drawing.Font(
    'Segoe UI',
    18,
    [System.Drawing.FontStyle]::Regular,
    [System.Drawing.GraphicsUnit]::Point
)

$form.Controls.Add($alarmTextBox)
$form.Controls.Add($closeButton)
$form.CancelButton = $closeButton

# ============================================================
# タイマー作成
# ============================================================

# 点滅タイマー (イベント登録)
$flashTimer.Add_Tick({
        if ($script:flashIsRed) {
            $script:form.BackColor = [System.Drawing.Color]::Yellow
            $script:flashIsRed = $false
        }
        else {
            $script:form.BackColor = [System.Drawing.Color]::Red
            $script:flashIsRed = $true
        }
    })

# メッセージ処理およびタイマー監視
$controlTimer = New-Object System.Windows.Forms.Timer
$controlTimer.Interval = 200
$controlTimer.Add_Tick({
        try {
            # Named Pipeから受信したすべてのメッセージを処理
            $payload = $null

            while ($script:shared.MessageQueue.TryDequeue([ref]$payload)) {
                $receivedMessage = ConvertFrom-AlarmPayload -Payload $payload

                if ($null -ne $receivedMessage) {
                    Add-AlarmToWindow -AlarmMessage $receivedMessage
                }

                $payload = $null
            }

            # 最後のアラームから10分経過した場合は音だけ停止
            $lastTicks = [long]$script:shared.LastAlarmUtcTicks

            if (
                [bool]$script:shared.SoundWanted -and
                $lastTicks -gt 0
            ) {
                $lastAlarmUtc = New-Object System.DateTime(
                    $lastTicks,
                    [System.DateTimeKind]::Utc
                )

                $elapsed = [System.DateTime]::UtcNow - $lastAlarmUtc

                if ($elapsed.TotalMinutes -ge $SoundTimeoutMin) {
                    Stop-SoundPlayback
                }
            }
        }
        catch {
        }
    })

# ============================================================
# フォームイベント
# ============================================================

$closeButton.Add_Click({
        try {
            $script:form.Close()
        }
        catch {
            Stop-AlarmBackgroundWork
        }
    })

$form.Add_FormClosing({
        Stop-AlarmBackgroundWork
    })

$form.Add_Shown({
        try {
            $script:form.Activate()
            $script:form.BringToFront()
        }
        catch {
        }
    })

# ============================================================
# ウィンドウ表示およびライフサイクル
# ============================================================

try {
    # WAVファイル読み込み
    try {
        $soundPlayer = New-Object System.Media.SoundPlayer
        $soundPlayer.SoundLocation = $WaveFilePath
        $soundPlayer.Load()
    }
    catch {
        Show-AlarmError -Text @"
WAVファイルを読み込めませんでした。

ファイル:
$WaveFilePath

エラー:
$($_.Exception.Message)
"@
        exit 1
    }

    # Named Pipe受信Runspace開始
    try {
        $pipeWorker = Start-WorkerRunspace `
            -ScriptBlock $pipeReceiverScript `
            -Arguments @($shared, $PipeName)
    }
    catch {
        Show-AlarmError -Text @"
Named Pipe受信ワーカーを開始できませんでした。

$($_.Exception.Message)
"@
        exit 1
    }

    # 1件目のアラームを直接表示
    Add-AlarmToWindow -AlarmMessage $Message

    $flashTimer.Start()
    $controlTimer.Start()

    [void]$form.ShowDialog()
}
catch {
    Show-AlarmError -Text @"
アラームウィンドウでエラーが発生しました。

$($_.Exception.Message)
"@
}
finally {
    # バックグラウンド処理へ終了通知
    Stop-AlarmBackgroundWork

    # Runspace終了
    Stop-WorkerRunspace $pipeWorker

    # SoundPlayer破棄
    if ($null -ne $soundPlayer) {
        try { $soundPlayer.Dispose() } catch { }
    }

    # タイマー破棄
    if ($null -ne $flashTimer) {
        try { $flashTimer.Dispose() } catch { }
    }

    if ($null -ne $controlTimer) {
        try { $controlTimer.Dispose() } catch { }
    }

    # フォーム破棄
    if ($null -ne $form) {
        try { $form.Dispose() } catch { }
    }

    # メモリ内の受信待ちメッセージ破棄
    if ($null -ne $shared) {
        try {
            $discardedPayload = $null
            while ($shared.MessageQueue.TryDequeue([ref]$discardedPayload)) {
                $discardedPayload = $null
            }
        }
        catch {
        }

        try { $shared.Shutdown.Dispose() } catch { }
    }

    # Mutex解放
    Release-AlarmMutex -MutexInstance $mutex -Owns $ownsMutex
}

exit 0