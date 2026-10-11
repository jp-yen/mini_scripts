#requires -Version 5.1
 
Add-Type -AssemblyName System.Windows.Forms
 
$ErrorActionPreference = 'Stop'
 
try {
# Install.ps1と同じフォルダーにある本体を取得
    $sourceScript = Join-Path `
        -Path $PSScriptRoot `
        -ChildPath 'EditorRenameTool.ps1'
 
    if (-not (Test-Path -LiteralPath $sourceScript -PathType Leaf)) {
        throw @"
EditorRenameTool.ps1が見つかりません。
 
Install.ps1とEditorRenameTool.ps1を
同じフォルダーへ保存してください。
 
検索した場所:
$sourceScript
"@
    }
 
# ユーザーごとのインストール先
    $applicationDirectory = Join-Path `
        -Path $env:LOCALAPPDATA `
        -ChildPath 'EditorRenameTool'
 
    New-Item `
        -ItemType Directory `
        -Path $applicationDirectory `
        -Force |
        Out-Null
 
    $installedScript = Join-Path `
        -Path $applicationDirectory `
        -ChildPath 'EditorRenameTool.ps1'
 
# 本体をインストール先へコピー
    Copy-Item `
        -LiteralPath $sourceScript `
        -Destination $installedScript `
        -Force
 
# Windows PowerShell 5.1の実行ファイル
    $powerShellPath = Join-Path `
        -Path $env:SystemRoot `
        -ChildPath 'System32\WindowsPowerShell\v1.0\powershell.exe'
 
    if (-not (Test-Path -LiteralPath $powerShellPath -PathType Leaf)) {
        throw "Windows PowerShellが見つかりません。`r`n$powerShellPath"
    }
 
# ショートカット作成用COMオブジェクト
    $shell = New-Object -ComObject WScript.Shell
 
# 「送る」メニューへのショートカット登録
    $sendToDirectory = [System.Environment]::GetFolderPath(
        [System.Environment+SpecialFolder]::SendTo
    )
 
    if (
        $null -eq $sendToDirectory -or
        $sendToDirectory.Trim().Length -eq 0
    ) {
        throw '「送る」フォルダーの場所を取得できませんでした。'
    }
 
    $sendToShortcutPath = Join-Path `
        -Path $sendToDirectory `
        -ChildPath 'エディターで名前変更または移動.lnk'
 
    $sendToShortcut = $shell.CreateShortcut(
        $sendToShortcutPath
    )
 
    $sendToShortcut.TargetPath = $powerShellPath
 
    $sendToShortcut.Arguments = (
        '-NoProfile ' +
        '-ExecutionPolicy Bypass ' +
        '-STA ' +
        '-File ' +
        '"' + $installedScript + '"'
    )
 
    $sendToShortcut.WorkingDirectory = $applicationDirectory
    $sendToShortcut.IconLocation = "$powerShellPath,0"
 
    $sendToShortcut.Description = (
        '選択したファイルの名前や移動先を' +
        'テキストエディターで一括編集します'
    )
 
    $sendToShortcut.Save()
 
# スタートメニューへの管理画面ショートカット登録
    $programsDirectory = [System.Environment]::GetFolderPath(
        [System.Environment+SpecialFolder]::Programs
    )
 
    if (
        $null -eq $programsDirectory -or
        $programsDirectory.Trim().Length -eq 0
    ) {
        throw 'スタートメニューフォルダーの場所を取得できませんでした。'
    }
 
    $startMenuShortcutPath = Join-Path `
        -Path $programsDirectory `
        -ChildPath 'Editor Rename Tool.lnk'
 
    $startMenuShortcut = $shell.CreateShortcut(
        $startMenuShortcutPath
    )
 
    $startMenuShortcut.TargetPath = $powerShellPath
 
    $startMenuShortcut.Arguments = (
        '-NoProfile ' +
        '-ExecutionPolicy Bypass ' +
        '-STA ' +
        '-File ' +
        '"' + $installedScript + '"'
    )
 
    $startMenuShortcut.WorkingDirectory = $applicationDirectory
    $startMenuShortcut.IconLocation = "$powerShellPath,0"
 
    $startMenuShortcut.Description = (
        'Editor Rename ToolのUndo履歴と' +
        'エディター設定を開きます'
    )
 
    $startMenuShortcut.Save()
 
# COMオブジェクトを解放
    if ($null -ne $sendToShortcut) {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject(
            $sendToShortcut
        )
    }
 
    if ($null -ne $startMenuShortcut) {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject(
            $startMenuShortcut
        )
    }
 
    if ($null -ne $shell) {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject(
            $shell
        )
    }
 
    $sendToShortcut = $null
    $startMenuShortcut = $null
    $shell = $null
 
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
 
# インストール完了表示
    $message = @"
インストールが完了しました。
 
【ファイル名の変更または移動】
 
エクスプローラーで対象ファイルを選択し、
 
右クリック
  → 送る
    → エディターで名前変更または移動
 
を選択してください。
 
【Undo履歴とエディター設定】
 
スタートメニューから
 
Editor Rename Tool
 
を起動してください。
 
インストール先:
 
$applicationDirectory
 
このあと、Editor Rename Toolの管理画面を起動します。
"@
 
    [System.Windows.Forms.MessageBox]::Show(
        $message,
        'Editor Rename Tool',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
 
# インストール後に管理画面を起動
    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName = $powerShellPath
 
    $processInfo.Arguments = (
        '-NoProfile ' +
        '-ExecutionPolicy Bypass ' +
        '-STA ' +
        '-File ' +
        '"' + $installedScript + '"'
    )
 
    $processInfo.WorkingDirectory = $applicationDirectory
    $processInfo.UseShellExecute = $true
 
    [System.Diagnostics.Process]::Start(
        $processInfo
    ) | Out-Null
}
catch {
    $errorMessage = @"
インストールに失敗しました。
 
$($_.Exception.Message)
 
エラーが発生した行:
$($_.InvocationInfo.ScriptLineNumber)
 
対象:
$($_.InvocationInfo.Line)
"@
 
    [System.Windows.Forms.MessageBox]::Show(
        $errorMessage,
        'Editor Rename Tool - インストール失敗',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
 
    exit 1
}
