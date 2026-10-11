#requires -Version 5.1

Add-Type -AssemblyName System.Windows.Forms

$ErrorActionPreference = 'Stop'

try {
    $applicationDirectory = Join-Path `
        -Path $env:LOCALAPPDATA `
        -ChildPath 'EditorRenameTool'

# 「送る」メニューのショートカット
    $sendToDirectory = [System.Environment]::GetFolderPath(
        [System.Environment+SpecialFolder]::SendTo
    )

    $sendToShortcutPath = Join-Path `
        -Path $sendToDirectory `
        -ChildPath 'エディターで名前変更または移動.lnk'

# スタートメニューのショートカット
    $programsDirectory = [System.Environment]::GetFolderPath(
        [System.Environment+SpecialFolder]::Programs
    )

    $startMenuShortcutPath = Join-Path `
        -Path $programsDirectory `
        -ChildPath 'Editor Rename Tool.lnk'

# 設定と履歴を削除するか確認
    $message = @"
Editor Rename Toolをアンインストールします。

エディター設定、作業用一覧、およびUndo履歴も
すべて削除しますか？

［はい］
  ショートカット、本体、設定、作業用一覧、
  Undo履歴、ログをすべて削除します。

［いいえ］
  ショートカットと本体だけを削除し、
  設定とUndo履歴は残します。

［キャンセル］
  アンインストールを中止します。
"@

    $answer = [System.Windows.Forms.MessageBox]::Show(
        $message,
        'Editor Rename Tool - アンインストール',
        [System.Windows.Forms.MessageBoxButtons]::YesNoCancel,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )

    if (
        $answer -eq
        [System.Windows.Forms.DialogResult]::Cancel
    ) {
        return
    }

# 「送る」メニューのショートカットを削除
    if (Test-Path -LiteralPath $sendToShortcutPath) {
        Remove-Item `
            -LiteralPath $sendToShortcutPath `
            -Force
    }

# スタートメニューのショートカットを削除
    if (Test-Path -LiteralPath $startMenuShortcutPath) {
        Remove-Item `
            -LiteralPath $startMenuShortcutPath `
            -Force
    }

    if (
        $answer -eq
        [System.Windows.Forms.DialogResult]::Yes
    ) {
# 設定、履歴、ログを含めてすべて削除
        if (Test-Path -LiteralPath $applicationDirectory) {
            Remove-Item `
                -LiteralPath $applicationDirectory `
                -Recurse `
                -Force
        }

        $completionMessage = @"
アンインストールが完了しました。

次のデータをすべて削除しました。

・「送る」メニューのショートカット
・スタートメニューのショートカット
・EditorRenameTool.ps1
・エディター設定
・Undo履歴
・編集用の作業ファイル
・アプリケーションログ
"@
    }
    else {
# 本体だけを削除し、設定と履歴は残す
        $installedScript = Join-Path `
            -Path $applicationDirectory `
            -ChildPath 'EditorRenameTool.ps1'

        if (Test-Path -LiteralPath $installedScript) {
            Remove-Item `
                -LiteralPath $installedScript `
                -Force
        }

        $completionMessage = @"
アンインストールが完了しました。

次の項目を削除しました。

・「送る」メニューのショートカット
・スタートメニューのショートカット
・EditorRenameTool.ps1

エディター設定、Undo履歴、作業用ファイル、
およびログは次の場所に残しています。

$applicationDirectory
"@
    }

    [System.Windows.Forms.MessageBox]::Show(
        $completionMessage,
        'Editor Rename Tool',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
}
catch {
    $errorMessage = @"
アンインストール処理中にエラーが発生しました。

$($_.Exception.Message)

エラーが発生した行:
$($_.InvocationInfo.ScriptLineNumber)

対象:
$($_.InvocationInfo.Line)
"@

    [System.Windows.Forms.MessageBox]::Show(
        $errorMessage,
        'Editor Rename Tool - アンインストール失敗',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null

    exit 1
}