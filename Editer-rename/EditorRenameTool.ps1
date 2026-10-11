#requires -Version 5.1
 
<#
EditorRenameTool.ps1
 
機能:
- エクスプローラーの「送る」から複数ファイルを受け取る
- ID + タブ + フルパス形式の一覧を任意のエディターで編集
- ファイル名変更および別ディレクトリへの移動
- 移動先ディレクトリの自動作成
- 実行前プレビュー
- 実行履歴の保存
- 履歴選択画面からUndo
- 失敗項目の再編集と再試行
- 使用するエディターと起動引数の登録
#>
 
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
 
[System.Windows.Forms.Application]::EnableVisualStyles()
 
$ErrorActionPreference = 'Stop'
 
$AppName = 'Editor Rename Tool'
 
$AppDirectory = Join-Path `
    -Path $env:LOCALAPPDATA `
    -ChildPath 'EditorRenameTool'
 
$WorkDirectory = Join-Path `
    -Path $AppDirectory `
    -ChildPath 'Work'
 
$HistoryDirectory = Join-Path `
    -Path $AppDirectory `
    -ChildPath 'History'
 
$ConfigPath = Join-Path `
    -Path $AppDirectory `
    -ChildPath 'config.json'
 
$LogPath = Join-Path `
    -Path $AppDirectory `
    -ChildPath 'application.log'
 
New-Item `
    -ItemType Directory `
    -Path @(
    $AppDirectory
    $WorkDirectory
    $HistoryDirectory
) `
    -Force |
Out-Null
 
 
function Write-AppLog {
    param(
        [Parameter(Mandatory)]
        [string]$Message
    )
 
    try {
        $logLine = '{0:yyyy-MM-dd HH:mm:ss} {1}' -f (
            Get-Date
        ), $Message
 
        Add-Content `
            -LiteralPath $LogPath `
            -Value $logLine `
            -Encoding UTF8
    }
    catch {
        # ログの書き込み失敗は無視する
    }
}
 
 
function Show-Info {
    param(
        [Parameter(Mandatory)]
        [string]$Text,
 
        [string]$Title = $AppName
    )
 
    [System.Windows.Forms.MessageBox]::Show(
        $Text,
        $Title,
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
}
 
 
function Show-Warning {
    param(
        [Parameter(Mandatory)]
        [string]$Text,
 
        [string]$Title = $AppName
    )
 
    [System.Windows.Forms.MessageBox]::Show(
        $Text,
        $Title,
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    ) | Out-Null
}
 
 
function Show-ErrorMessage {
    param(
        [Parameter(Mandatory)]
        [string]$Text,
 
        [string]$Title = $AppName
    )
 
    [System.Windows.Forms.MessageBox]::Show(
        $Text,
        $Title,
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
}
 
 
function Confirm-Operation {
    param(
        [Parameter(Mandatory)]
        [string]$Text,
 
        [string]$Title = $AppName
    )
 
    $result = [System.Windows.Forms.MessageBox]::Show(
        $Text,
        $Title,
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
 
    return (
        $result -eq
        [System.Windows.Forms.DialogResult]::Yes
    )
}
 
 
function Test-TextIsEmpty {
    param(
        [AllowNull()]
        [string]$Text
    )
 
    if ($null -eq $Text) {
        return $true
    }
 
    return $Text.Trim().Length -eq 0
}

 
function Get-ApplicationConfig {
    if (
        Test-Path `
            -LiteralPath $ConfigPath `
            -PathType Leaf
    ) {
        try {
            return Get-Content `
                -LiteralPath $ConfigPath `
                -Raw `
                -Encoding UTF8 |
            ConvertFrom-Json
        }
        catch {
            Write-AppLog (
                '設定ファイルの読み込みに失敗しました: ' +
                $_.Exception.Message
            )
        }
    }
 
    return [pscustomobject]@{
        EditorPath      = ''
        EditorArguments = '"{file}"'
    }
}
 
 
function Save-ApplicationConfig {
    param(
        [Parameter(Mandatory)]
        $Config
    )
 
    $Config |
    ConvertTo-Json -Depth 5 |
    Set-Content `
        -LiteralPath $ConfigPath `
        -Encoding UTF8
}
 
 
function Show-EditorSettings {
    $config = Get-ApplicationConfig
 
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName - エディター設定"
    $form.Size = New-Object System.Drawing.Size(700, 250)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.ShowInTaskbar = $true
 
    $editorLabel = New-Object System.Windows.Forms.Label
    $editorLabel.Text = 'エディターの実行ファイル:'
    $editorLabel.Location = New-Object System.Drawing.Point(15, 20)
    $editorLabel.AutoSize = $true
 
    $editorTextBox = New-Object System.Windows.Forms.TextBox
    $editorTextBox.Location = New-Object System.Drawing.Point(15, 45)
    $editorTextBox.Size = New-Object System.Drawing.Size(555, 25)
    $editorTextBox.Text = [string]$config.EditorPath
 
    $browseButton = New-Object System.Windows.Forms.Button
    $browseButton.Text = '参照...'
    $browseButton.Location = New-Object System.Drawing.Point(580, 42)
    $browseButton.Size = New-Object System.Drawing.Size(85, 30)
 
    $argumentLabel = New-Object System.Windows.Forms.Label
    $argumentLabel.Text = (
        '起動引数。{file}が編集一覧ファイルのパスに置換されます:'
    )
    $argumentLabel.Location = New-Object System.Drawing.Point(15, 85)
    $argumentLabel.AutoSize = $true
 
    $argumentTextBox = New-Object System.Windows.Forms.TextBox
    $argumentTextBox.Location = New-Object System.Drawing.Point(15, 110)
    $argumentTextBox.Size = New-Object System.Drawing.Size(650, 25)
 
    if (
        -not (
            Test-TextIsEmpty `
                -Text ([string]$config.EditorArguments)
        )
    ) {
        $argumentTextBox.Text = [string]$config.EditorArguments
    }
    else {
        $argumentTextBox.Text = '"{file}"'
    }
 
    $saveButton = New-Object System.Windows.Forms.Button
    $saveButton.Text = '保存'
    $saveButton.Location = New-Object System.Drawing.Point(490, 160)
    $saveButton.Size = New-Object System.Drawing.Size(80, 32)
    $saveButton.DialogResult = (
        [System.Windows.Forms.DialogResult]::OK
    )
 
    $cancelButton = New-Object System.Windows.Forms.Button
    $cancelButton.Text = 'キャンセル'
    $cancelButton.Location = New-Object System.Drawing.Point(580, 160)
    $cancelButton.Size = New-Object System.Drawing.Size(85, 32)
    $cancelButton.DialogResult = (
        [System.Windows.Forms.DialogResult]::Cancel
    )
 
    $browseButton.Add_Click({
            $dialog = New-Object System.Windows.Forms.OpenFileDialog
 
            $dialog.Filter = (
                '実行ファイル (*.exe)|*.exe|' +
                'すべてのファイル (*.*)|*.*'
            )
 
            $dialog.Title = '使用するエディターを選択してください'
            $dialog.CheckFileExists = $true
            $dialog.Multiselect = $false
 
            if (
                -not (
                    Test-TextIsEmpty `
                        -Text $editorTextBox.Text
                ) -and
                (
                    Test-Path `
                        -LiteralPath $editorTextBox.Text `
                        -PathType Leaf
                )
            ) {
                $dialog.InitialDirectory = [System.IO.Path]::GetDirectoryName($editorTextBox.Text)
                $dialog.FileName = [System.IO.Path]::GetFileName($editorTextBox.Text)
            }
 
            if (
                $dialog.ShowDialog() -eq
                [System.Windows.Forms.DialogResult]::OK
            ) {
                $editorTextBox.Text = $dialog.FileName
            }
 
            $dialog.Dispose()
        })
 
    $form.Controls.AddRange(@(
            $editorLabel
            $editorTextBox
            $browseButton
            $argumentLabel
            $argumentTextBox
            $saveButton
            $cancelButton
        ))
 
    $form.AcceptButton = $saveButton
    $form.CancelButton = $cancelButton
 
    $dialogResult = $form.ShowDialog()
 
    if (
        $dialogResult -eq
        [System.Windows.Forms.DialogResult]::OK
    ) {
        $editorPathIsEmpty = Test-TextIsEmpty `
            -Text $editorTextBox.Text
 
        $editorPathExists = Test-Path `
            -LiteralPath $editorTextBox.Text `
            -PathType Leaf
 
        if (
            $editorPathIsEmpty -or
            -not $editorPathExists
        ) {
            Show-Warning (
                '有効なエディターの実行ファイルを選択してください。'
            )
 
            $form.Dispose()
            return $false
        }
 
        $newConfig = [pscustomobject]@{
            EditorPath      = $editorTextBox.Text
            EditorArguments = $argumentTextBox.Text
        }
 
        Save-ApplicationConfig `
            -Config $newConfig
 
        $form.Dispose()
        return $true
    }
 
    $form.Dispose()
    return $false
}
 
 
function Get-ConfiguredEditor {
    $config = Get-ApplicationConfig
 
    $editorPathIsEmpty = Test-TextIsEmpty `
        -Text ([string]$config.EditorPath)
 
    $editorPathExists = $false
 
    if (-not $editorPathIsEmpty) {
        $editorPathExists = Test-Path `
            -LiteralPath ([string]$config.EditorPath) `
            -PathType Leaf
    }
 
    $editorIsMissing = (
        $editorPathIsEmpty -or
        -not $editorPathExists
    )
 
    if ($editorIsMissing) {
        Show-Info (
            '最初に使用するエディターを選択してください。'
        )
 
        if (-not (Show-EditorSettings)) {
            return $null
        }
 
        $config = Get-ApplicationConfig
    }
 
    return $config
}
 
 
function Open-FileInConfiguredEditor {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath
    )
 
    $config = Get-ConfiguredEditor
 
    if ($null -eq $config) {
        return $false
    }
 
    $editorArguments = [string]$config.EditorArguments
 
    if (
        Test-TextIsEmpty `
            -Text $editorArguments
    ) {
        $editorArguments = '"{file}"'
    }
 
    if (-not $editorArguments.Contains('{file}')) {
        Show-Warning @"
エディターの起動引数に {file} がありません。
 
エディター設定を開き、
編集対象ファイルを渡す位置へ {file} を指定してください。
 
標準設定:
"{file}"
"@
 
        return $false
    }
 
    $escapedFilePath = $FilePath.Replace(
        '"',
        '\"'
    )
 
    $editorArguments = $editorArguments.Replace(
        '{file}',
        $escapedFilePath
    )
 
    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName = [string]$config.EditorPath
    $processInfo.Arguments = $editorArguments
    $processInfo.WorkingDirectory = [System.IO.Path]::GetDirectoryName($FilePath)
 
    $processInfo.UseShellExecute = $true
 
    try {
        $process = [System.Diagnostics.Process]::Start(
            $processInfo
        )
 
        if ($null -eq $process) {
            throw 'エディターのプロセスを開始できませんでした。'
        }
 
 
        return $true
    }
    catch {
        Show-ErrorMessage (
            "エディターを起動できませんでした。`r`n`r`n" +
            $_.Exception.Message
        )
 
        return $false
    }
}
 
 

function Show-EditorWaitDialog {
    param(
        [Parameter(Mandatory)]
        [string]$ListFilePath,

        [Parameter(Mandatory)]
        [int]$ItemCount,

        [string]$Caption = '編集待機中',

        [scriptblock]$OnFilesDropped = $null
    )

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName - $Caption"
    $form.Size = New-Object System.Drawing.Size(620, 290)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.AllowDrop = $true

    $form.Add_DragEnter({
        param($sender, $e)
        if ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)) {
            $e.Effect = [System.Windows.Forms.DragDropEffects]::Copy
        }
        else {
            $e.Effect = [System.Windows.Forms.DragDropEffects]::None
        }
    })

    $form.Add_DragDrop({
        param($sender, $e)
        if ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop) -and $null -ne $OnFilesDropped) {
            $dropped = $e.Data.GetData([System.Windows.Forms.DataFormats]::FileDrop)
            if ($null -ne $dropped -and $dropped.Length -gt 0) {
                & $OnFilesDropped ([string[]]$dropped)
            }
        }
    })

    $titleLabel = New-Object System.Windows.Forms.Label
    $titleLabel.Text = 'エディターで一覧を編集してください'
    $titleLabel.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
    $titleLabel.Location = New-Object System.Drawing.Point(25, 18)
    $titleLabel.AutoSize = $true

    $descLabel = New-Object System.Windows.Forms.Label
    $descLabel.Text = "エディターで右側のファイルパスを編集・保存してください。`r`nこの画面にファイルをドロップして、対象ファイルを後から追加することもできます。`r`n保存完了後、下の［編集完了（プレビューへ進む）］ボタンを押してください。"
    $descLabel.Location = New-Object System.Drawing.Point(27, 50)
    $descLabel.Size = New-Object System.Drawing.Size(555, 50)

    $infoLabel = New-Object System.Windows.Forms.Label
    $fileName = [System.IO.Path]::GetFileName($ListFilePath)
    $infoLabel.Text = "対象件数: $ItemCount 件   ファイル: $fileName"
    $infoLabel.ForeColor = [System.Drawing.SystemColors]::GrayText
    $infoLabel.Location = New-Object System.Drawing.Point(27, 108)
    $infoLabel.Size = New-Object System.Drawing.Size(555, 22)

    $btnComplete = New-Object System.Windows.Forms.Button
    $btnComplete.Text = '編集完了（プレビューへ進む）'
    $btnComplete.Font = New-Object System.Drawing.Font('Segoe UI', 9.5, [System.Drawing.FontStyle]::Bold)
    $btnComplete.Location = New-Object System.Drawing.Point(25, 150)
    $btnComplete.Size = New-Object System.Drawing.Size(230, 42)
    $btnComplete.DialogResult = [System.Windows.Forms.DialogResult]::OK

    $btnReopen = New-Object System.Windows.Forms.Button
    $btnReopen.Text = 'エディターを再度開く'
    $btnReopen.Location = New-Object System.Drawing.Point(270, 150)
    $btnReopen.Size = New-Object System.Drawing.Size(160, 42)
    $btnReopen.Add_Click({
        Open-FileInConfiguredEditor -FilePath $ListFilePath | Out-Null
    })

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = 'キャンセル'
    $btnCancel.Location = New-Object System.Drawing.Point(445, 150)
    $btnCancel.Size = New-Object System.Drawing.Size(95, 42)
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel

    $form.Controls.AddRange(@(
        $titleLabel
        $descLabel
        $infoLabel
        $btnComplete
        $btnReopen
        $btnCancel
    ))

    $form.AcceptButton = $btnComplete
    $form.CancelButton = $btnCancel

    $dialogResult = $form.ShowDialog()
    return ($dialogResult -eq [System.Windows.Forms.DialogResult]::OK)
}

function ConvertTo-NormalPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )
 
    if (
        Test-TextIsEmpty `
            -Text $Path
    ) {
        throw 'パスが空です。'
    }
 
    $fullPath = [System.IO.Path]::GetFullPath(
        $Path
    )
 
    $rootPath = [System.IO.Path]::GetPathRoot(
        $fullPath
    )
 
    # C:\ のようなルートパスでは末尾の区切り文字を保持する
    if ($fullPath -ieq $rootPath) {
        return $fullPath
    }
 
    return $fullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
}
 
 
function Test-PathIsSame {
    param(
        [Parameter(Mandatory)]
        [string]$PathA,
 
        [Parameter(Mandatory)]
        [string]$PathB
    )
 
    $normalPathA = ConvertTo-NormalPath `
        -Path $PathA
 
    $normalPathB = ConvertTo-NormalPath `
        -Path $PathB
 
    # -ceq を使用し、大文字・小文字だけの変更も検出する
    return $normalPathA -ceq $normalPathB
}
 
 
function New-EditorListFile {
    param(
        [Parameter(Mandatory)]
        $Items,
 
        [string]$Prefix = 'rename'
    )
 
    $timestamp = Get-Date `
        -Format 'yyyyMMdd-HHmmss-fff'
 
    $listFilePath = Join-Path `
        -Path $WorkDirectory `
        -ChildPath "$Prefix-$timestamp.txt"
 
    $lines = New-Object `
        System.Collections.Generic.List[string]
 
    $lines.Add(
        '# 左側のIDとタブは変更しないでください。右側のフルパスだけを編集します。'
    )
 
    $lines.Add(
        '# 変更しないファイルは行ごと削除できます。行の並べ替えも可能です。'
    )
 
    foreach ($item in $Items) {
        $lines.Add(
            (
                "{0}`t{1}" -f
                $item.Id,
                $item.DestinationPath
            )
        )
    }
 
    $utf8WithBom = New-Object `
        System.Text.UTF8Encoding($true)
 
    [System.IO.File]::WriteAllLines(
        $listFilePath,
        $lines,
        $utf8WithBom
    )
 
    return $listFilePath
}


function Read-EditorListFile {
    param(
        [Parameter(Mandatory)]
        [string]$ListFilePath,
 
        [Parameter(Mandatory)]
        $SourcePathMap
    )
 
    $validItems = New-Object `
        System.Collections.Generic.List[object]
 
    $errors = New-Object `
        System.Collections.Generic.List[string]
 
    $seenIds = @{}
 
    $utf8 = New-Object `
        System.Text.UTF8Encoding($true)
 
    $lines = [System.IO.File]::ReadAllLines(
        $ListFilePath,
        $utf8
    )
 
    $lineNumber = 0
 
    foreach ($line in $lines) {
        $lineNumber++
 
        if (
            Test-TextIsEmpty `
                -Text $line
        ) {
            continue
        }
 
        if ($line.TrimStart().StartsWith('#')) {
            continue
        }
 
        $parts = $line -split "`t", 2
 
        if ($parts.Count -ne 2) {
            $errors.Add(
                (
                    '行 {0}: IDとパスがタブで区切られていません。' -f
                    $lineNumber
                )
            )
 
            continue
        }
 
        $id = $parts[0].Trim()
        $destinationPath = $parts[1].Trim()
 
        if (
            Test-TextIsEmpty `
                -Text $id
        ) {
            $errors.Add(
                (
                    '行 {0}: IDが空です。' -f
                    $lineNumber
                )
            )
 
            continue
        }
 
        if (-not $SourcePathMap.ContainsKey($id)) {
            $errors.Add(
                (
                    '行 {0}: IDが変更されたか、不明なIDです: {1}' -f
                    $lineNumber,
                    $id
                )
            )
 
            continue
        }
 
        if ($seenIds.ContainsKey($id)) {
            $errors.Add(
                (
                    '行 {0}: IDが重複しています: {1}' -f
                    $lineNumber,
                    $id
                )
            )
 
            continue
        }
 
        if (
            Test-TextIsEmpty `
                -Text $destinationPath
        ) {
            $errors.Add(
                (
                    '行 {0}: 移動先のパスが空です: {1}' -f
                    $lineNumber,
                    $id
                )
            )
 
            continue
        }
 
        try {
            $destinationPath = ConvertTo-NormalPath `
                -Path $destinationPath
        }
        catch {
            $errors.Add(
                (
                    '行 {0}: 移動先のパスが不正です: {1}' -f
                    $lineNumber,
                    $_.Exception.Message
                )
            )
 
            continue
        }
 
        $seenIds[$id] = $true
 
        $validItems.Add(
            [pscustomobject]@{
                Id          = $id
                SourcePath      = [string]$SourcePathMap[$id]
                DestinationPath = $destinationPath
            }
        )
    }
 
    return [pscustomobject]@{
        Items  = $validItems
        Errors = $errors
    }
}
 
 
function Show-OperationPreview {
    param(
        [Parameter(Mandatory)]
        $Items,

        [string]$Caption = '実行予定'
    )

    $changedItems = New-Object System.Collections.Generic.List[object]

    foreach ($item in $Items) {
        if (
            -not (
                Test-PathIsSame `
                    -PathA $item.SourcePath `
                    -PathB $item.DestinationPath
            )
        ) {
            $changedItems.Add($item)
        }
    }

    if ($changedItems.Count -eq 0) {
        Show-Info '実行する変更はありません。'
        return $false
    }

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName - $Caption"
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 10)
    $form.ClientSize = New-Object System.Drawing.Size(960, 600)
    $form.MinimumSize = New-Object System.Drawing.Size(760, 480)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'Sizable'

    $fontPath = New-Object System.Drawing.Font('Segoe UI', 11.5, [System.Drawing.FontStyle]::Regular)
    $fontTag = New-Object System.Drawing.Font('Segoe UI', 10.5, [System.Drawing.FontStyle]::Bold)
    $fontId = New-Object System.Drawing.Font('Consolas', 10, [System.Drawing.FontStyle]::Regular)

    $clientW = $form.ClientSize.Width
    $clientH = $form.ClientSize.Height

    $btnHeight = 36
    $bottomMargin = 16
    $btnY = $clientH - $bottomMargin - $btnHeight
    $listTop = 44
    $listGap = 12
    $listHeight = $btnY - $listGap - $listTop

    $infoLabel = New-Object System.Windows.Forms.Label
    $infoLabel.Text = "変更予定件数: $($changedItems.Count) 件 （変更前と変更後を確認し、よろしければ［実行］を押してください）"
    $infoLabel.Font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
    $infoLabel.Location = New-Object System.Drawing.Point(16, 14)
    $infoLabel.AutoSize = $true

    $previewListBox = New-Object System.Windows.Forms.ListBox
    $previewListBox.Location = New-Object System.Drawing.Point(16, $listTop)
    $previewListBox.Size = New-Object System.Drawing.Size(($clientW - 32), $listHeight)
    $previewListBox.Anchor = ([System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right)
    $previewListBox.DrawMode = [System.Windows.Forms.DrawMode]::OwnerDrawFixed
    $previewListBox.ItemHeight = 58
    $previewListBox.IntegralHeight = $false
    $previewListBox.HorizontalScrollbar = $true

    $maxPixelWidth = 0
    $g = $form.CreateGraphics()
    try {
        foreach ($item in $changedItems) {
            $previewListBox.Items.Add($item) | Out-Null
            $w1 = [int]($g.MeasureString([string]$item.SourcePath, $fontPath).Width)
            $w2 = [int]($g.MeasureString([string]$item.DestinationPath, $fontPath).Width)
            $w = [System.Math]::Max($w1, $w2)
            if ($w -gt $maxPixelWidth) { $maxPixelWidth = $w }
        }
    }
    finally {
        $g.Dispose()
    }
    $previewListBox.HorizontalExtent = [System.Math]::Max($previewListBox.Width, $maxPixelWidth + 240)

    $previewListBox.Add_DrawItem({
        param($sender, $e)
        if ($e.Index -lt 0 -or $e.Index -ge $changedItems.Count) { return }

        $item = $changedItems[$e.Index]
        $isSelected = (($e.State -band [System.Windows.Forms.DrawItemState]::Selected) -eq [System.Windows.Forms.DrawItemState]::Selected)

        $backColor = if ($isSelected) {
            [System.Drawing.Color]::FromArgb(220, 238, 255)
        } elseif ($e.Index % 2 -eq 1) {
            [System.Drawing.Color]::FromArgb(250, 251, 253)
        } else {
            [System.Drawing.Color]::White
        }

        $backBrush = New-Object System.Drawing.SolidBrush($backColor)
        $e.Graphics.FillRectangle($backBrush, $e.Bounds)
        $backBrush.Dispose()

        $y1 = $e.Bounds.Y + 6
        $y2 = $e.Bounds.Y + 31

        $xId = $e.Bounds.X + 10
        $xTag = $xId + 92
        $xPath = $xTag + 68

        $idColor = if ($isSelected) { [System.Drawing.Color]::FromArgb(30, 60, 110) } else { [System.Drawing.Color]::FromArgb(100, 105, 115) }
        $idBrush = New-Object System.Drawing.SolidBrush($idColor)
        $e.Graphics.DrawString("[$($item.Id)]", $fontId, $idBrush, [float]$xId, [float]($y1 + 2))
        $idBrush.Dispose()

        $beforeTagBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(195, 30, 30))
        $beforePathBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(55, 65, 81))
        $e.Graphics.DrawString('変更前:', $fontTag, $beforeTagBrush, [float]$xTag, [float]$y1)
        $e.Graphics.DrawString([string]$item.SourcePath, $fontPath, $beforePathBrush, [float]$xPath, [float]$y1)
        $beforeTagBrush.Dispose()
        $beforePathBrush.Dispose()

        $afterTagBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(16, 135, 60))
        $afterPathBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(15, 23, 42))
        $e.Graphics.DrawString('変更後:', $fontTag, $afterTagBrush, [float]$xTag, [float]$y2)
        $e.Graphics.DrawString([string]$item.DestinationPath, $fontPath, $afterPathBrush, [float]$xPath, [float]$y2)
        $afterTagBrush.Dispose()
        $afterPathBrush.Dispose()

        $linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(226, 232, 240))
        $e.Graphics.DrawLine($linePen, $e.Bounds.Left, $e.Bounds.Bottom - 1, $e.Bounds.Right, $e.Bounds.Bottom - 1)
        $linePen.Dispose()

        if ($isSelected) {
            $selPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 130, 230))
            $e.Graphics.DrawRectangle($selPen, $e.Bounds.X, $e.Bounds.Y, $e.Bounds.Width - 1, $e.Bounds.Height - 1)
            $selPen.Dispose()
        }
    })

    $copySelected = {
        $idx = $previewListBox.SelectedIndex
        if ($idx -lt 0 -or $idx -ge $changedItems.Count) { return }
        $it = $changedItems[$idx]
        $txt = "[$($it.Id)]`r`n変更前: $($it.SourcePath)`r`n変更後: $($it.DestinationPath)"
        try { [System.Windows.Forms.Clipboard]::SetText($txt) } catch {}
    }

    $copyAll = {
        if ($changedItems.Count -eq 0) { return }
        $lines = New-Object System.Collections.Generic.List[string]
        foreach ($it in $changedItems) {
            $lines.Add("[$($it.Id)]")
            $lines.Add("変更前: $($it.SourcePath)")
            $lines.Add("変更後: $($it.DestinationPath)")
            $lines.Add('')
        }
        try { [System.Windows.Forms.Clipboard]::SetText(($lines -join "`r`n")) } catch {}
    }

    $contextMenu = New-Object System.Windows.Forms.ContextMenuStrip
    $menuItemSel = New-Object System.Windows.Forms.ToolStripMenuItem('選択項目のパスをコピー (Ctrl+C)')
    $menuItemSel.Add_Click({ & $copySelected })
    $menuItemAll = New-Object System.Windows.Forms.ToolStripMenuItem('全件の変更予定をコピー')
    $menuItemAll.Add_Click({ & $copyAll })
    $contextMenu.Items.Add($menuItemSel) | Out-Null
    $contextMenu.Items.Add($menuItemAll) | Out-Null
    $previewListBox.ContextMenuStrip = $contextMenu

    $previewListBox.Add_KeyDown({
        param($sender, $e)
        if ($e.Control -and $e.KeyCode -eq [System.Windows.Forms.Keys]::C) {
            & $copySelected
            $e.Handled = $true
        }
    })

    $btnCopy = New-Object System.Windows.Forms.Button
    $btnCopy.Text = 'パスをコピー'
    $btnCopy.Location = New-Object System.Drawing.Point(16, $btnY)
    $btnCopy.Size = New-Object System.Drawing.Size(130, $btnHeight)
    $btnCopy.Anchor = ([System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left)
    $btnCopy.Add_Click({ & $copySelected })

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = 'キャンセル'
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $btnCancel.Size = New-Object System.Drawing.Size(100, $btnHeight)
    $btnCancel.Location = New-Object System.Drawing.Point(($clientW - $bottomMargin - $btnCancel.Width), $btnY)
    $btnCancel.Anchor = ([System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Right)

    $btnExecute = New-Object System.Windows.Forms.Button
    $btnExecute.Text = '実行'
    $btnExecute.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
    $btnExecute.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $btnExecute.Size = New-Object System.Drawing.Size(115, $btnHeight)
    $btnExecute.Location = New-Object System.Drawing.Point(($btnCancel.Left - 10 - $btnExecute.Width), $btnY)
    $btnExecute.Anchor = ([System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Right)

    $form.Controls.AddRange(@(
        $infoLabel
        $previewListBox
        $btnCopy
        $btnExecute
        $btnCancel
    ))

    $form.AcceptButton = $btnExecute
    $form.CancelButton = $btnCancel

    $dialogResult = $form.ShowDialog()

    $fontPath.Dispose()
    $fontTag.Dispose()
    $fontId.Dispose()
    $form.Dispose()

    return ($dialogResult -eq [System.Windows.Forms.DialogResult]::OK)
}

function New-TemporaryPath {
    param(
        [Parameter(Mandatory)]
        [string]$SourcePath
    )
 
    $sourceDirectory = [System.IO.Path]::GetDirectoryName($SourcePath)
    $sourceFileName = [System.IO.Path]::GetFileName($SourcePath)
 
    do {
        $guidText = [System.Guid]::NewGuid().ToString('N')
 
        $temporaryFileName = (
            '.__EditorRenameTool_' +
            $guidText +
            '_' +
            $sourceFileName
        )
 
        $temporaryPath = Join-Path `
            -Path $sourceDirectory `
            -ChildPath $temporaryFileName
    }
    while (
        Test-Path `
            -LiteralPath $temporaryPath
    )
 
    return $temporaryPath
}
 
 
function Invoke-RenameMoveBatch {
    param(
        [Parameter(Mandatory)]
        $Items
    )
 
    $successfulItems = New-Object `
        System.Collections.Generic.List[object]
 
    $failedItems = New-Object `
        System.Collections.Generic.List[object]
 
    $changedItems = New-Object `
        System.Collections.Generic.List[object]
 
    foreach ($item in $Items) {
        if (
            -not (
                Test-PathIsSame `
                    -PathA $item.SourcePath `
                    -PathB $item.DestinationPath
            )
        ) {
            $changedItems.Add($item)
        }
    }
 
    $sourcePathMap = @{}
    $destinationPathMap = @{}
    $readyItems = New-Object `
        System.Collections.Generic.List[object]
 
    foreach ($item in $changedItems) {
        try {
            $sourcePath = ConvertTo-NormalPath `
                -Path $item.SourcePath
 
            $destinationPath = ConvertTo-NormalPath `
                -Path $item.DestinationPath
 
            $sourceKey = $sourcePath.ToLowerInvariant()
 
            if ($sourcePathMap.ContainsKey($sourceKey)) {
                throw '同じ元ファイルが複数回指定されています。'
            }
 
            $sourcePathMap[$sourceKey] = $true
 
            $readyItems.Add(
                [pscustomobject]@{
                    Id          = [string]$item.Id
                    SourcePath      = $sourcePath
                    DestinationPath = $destinationPath
                    TemporaryPath   = $null
                    IsStaged        = $false
                }
            )
        }
        catch {
            $failedItems.Add(
                [pscustomobject]@{
                    Id           = [string]$item.Id
                    SourcePath      = [string]$item.SourcePath
                    DestinationPath = [string]$item.DestinationPath
                    ErrorMessage    = $_.Exception.Message
                }
            )
        }
    }
 
    $validatedItems = New-Object `
        System.Collections.Generic.List[object]
 
    foreach ($item in $readyItems) {
        try {
            $destinationKey = (
                $item.DestinationPath.ToLowerInvariant()
            )
 
            if (
                $destinationPathMap.ContainsKey(
                    $destinationKey
                )
            ) {
                throw (
                    '複数のファイルに同じ移動先が' +
                    '指定されています。'
                )
            }
 
            $destinationPathMap[$destinationKey] = $true
 
            if (
                -not (
                    Test-Path `
                        -LiteralPath $item.SourcePath `
                        -PathType Leaf
                )
            ) {
                throw '元のファイルが存在しません。'
            }
 
            $destinationExists = Test-Path `
                -LiteralPath $item.DestinationPath
 
            if (
                $destinationExists -and
                -not (
                    $sourcePathMap.ContainsKey(
                        $destinationKey
                    )
                )
            ) {
                throw (
                    '移動先に同名のファイルまたは' +
                    'フォルダーが存在します。'
                )
            }
 
            $validatedItems.Add($item)
        }
        catch {
            $failedItems.Add(
                [pscustomobject]@{
                    Id              = $item.Id
                    SourcePath      = $item.SourcePath
                    DestinationPath = $item.DestinationPath
                    ErrorMessage    = $_.Exception.Message
                }
            )
        }
    }
 
    # --------------------------------------------------------
    # 第1段階
    #
    # 対象ファイルを元のディレクトリ内の一時名へ退避する。
    # A.txtとB.txtの入れ替えや、循環リネームにも対応できる。
    # --------------------------------------------------------
 
    foreach ($item in $validatedItems) {
        try {
            $temporaryPath = New-TemporaryPath `
                -SourcePath $item.SourcePath
 
            Move-Item `
                -LiteralPath $item.SourcePath `
                -Destination $temporaryPath `
                -ErrorAction Stop
 
            $item.TemporaryPath = $temporaryPath
            $item.IsStaged = $true
        }
        catch {
            $item.IsStaged = $false
 
            $failedItems.Add(
                [pscustomobject]@{
                    Id              = $item.Id
                    SourcePath      = $item.SourcePath
                    DestinationPath = $item.DestinationPath
                                        ErrorMessage        = (
                        '一時ファイルへの退避に失敗しました: ' +
                        $_.Exception.Message
                    )
                }
            )
        }
    }
 
    # --------------------------------------------------------
    # 第2段階
    #
    # 一時名へ退避できたファイルを最終的な移動先へ移動する。
    # --------------------------------------------------------
 
    foreach ($item in $validatedItems) {
        if (-not $item.IsStaged) {
            continue
        }
 
        try {
            $destinationDirectory = [System.IO.Path]::GetDirectoryName($item.DestinationPath)
 
            if (
                -not (
                    Test-TextIsEmpty `
                        -Text $destinationDirectory
                ) -and
                -not (
                    Test-Path `
                        -LiteralPath $destinationDirectory `
                        -PathType Container
                )
            ) {
                New-Item `
                    -ItemType Directory `
                    -Path $destinationDirectory `
                    -Force `
                    -ErrorAction Stop |
                Out-Null
            }
 
            if (
                Test-Path `
                    -LiteralPath $item.DestinationPath
            ) {
                throw (
                    '一時退避後に移動先が使用されています。'
                )
            }
 
            Move-Item `
                -LiteralPath $item.TemporaryPath `
                -Destination $item.DestinationPath `
                -ErrorAction Stop
 
            $successfulItems.Add(
                [pscustomobject]@{
                    Id       = $item.Id
                    SourcePath      = $item.SourcePath
                    DestinationPath = $item.DestinationPath
                }
            )
        }
        catch {
            $moveError = $_.Exception.Message
            $currentSourcePath = $item.TemporaryPath
 
            # 最終移動に失敗した場合は、可能なら元の場所へ戻す
            try {
                if (
                    -not (
                        Test-Path `
                            -LiteralPath $item.SourcePath
                    )
                ) {
                    Move-Item `
                        -LiteralPath $item.TemporaryPath `
                        -Destination $item.SourcePath `
                        -ErrorAction Stop
 
                    $currentSourcePath = $item.SourcePath
                }
            }
            catch {
                $moveError += (
                    ' 元の場所への復旧にも失敗しました: ' +
                    $_.Exception.Message
                )
            }
 
            $failedItems.Add(
                [pscustomobject]@{
                    Id              = $item.Id
                    SourcePath      = $currentSourcePath
                    DestinationPath = $item.DestinationPath
                    ErrorMessage    = $moveError
                }
            )
        }
    }
 
    return [pscustomobject]@{
        Success = $successfulItems
        Failed  = $failedItems
    }
}
 
 
function Save-RenameHistory {
    param(
        [Parameter(Mandatory)]
        $SuccessfulItems,
 
        [string]$Mode = 'Rename',
 
        [string]$RelatedSessionId = ''
    )
 
    if ($SuccessfulItems.Count -eq 0) {
        return $null
    }
 
    $sessionId = Get-Date `
        -Format 'yyyyMMdd-HHmmss-fff'
 
    $historyObject = [ordered]@{
        SchemaVersion  = 1
        SessionId      = $sessionId
        Created        = (Get-Date).ToString('o')
        Mode           = $Mode
        RelatedSession = $RelatedSessionId
        Undone         = $false
        Entries        = @(
            foreach ($entry in $SuccessfulItems) { $entry }
        )
    }
 
    $historyFilePath = Join-Path `
        -Path $HistoryDirectory `
        -ChildPath "$sessionId.json"
 
    $historyObject |
    ConvertTo-Json -Depth 10 |
    Set-Content `
        -LiteralPath $historyFilePath `
        -Encoding UTF8
 
    return $historyFilePath
}

function Edit-AndRetryFailedItems {
    param(
        [Parameter(Mandatory)]
        $FailedItems,
 
        [string]$Caption = '失敗項目の再試行',
 
        [string]$HistoryMode = 'Retry',
 
        [string]$RelatedSessionId = ''
    )
 
    $pendingItems = @(
        foreach ($it in $FailedItems) { $it }
    )
 
    while ($pendingItems.Count -gt 0) {
        $detailLines = New-Object `
            System.Collections.Generic.List[string]
 
        $maximumDetailCount = [System.Math]::Min(
            15,
            $pendingItems.Count
        )
 
        for (
            $index = 0
            $index -lt $maximumDetailCount
            $index++
        ) {
            $failure = $pendingItems[$index]
 
            $detailLines.Add(
                (
                    '{0}: {1}' -f
                    $failure.Id,
                    $failure.ErrorMessage
                )
            )
        }
 
        if ($pendingItems.Count -gt $maximumDetailCount) {
            $remainingCount = (
                $pendingItems.Count -
                $maximumDetailCount
            )
 
            $detailLines.Add(
                "ほか $remainingCount 件"
            )
        }
 
        $detailText = $detailLines -join "`r`n"
 
        $message = @"
失敗: $($pendingItems.Count) 件
 
$detailText
 
失敗した項目だけをエディターで開き、
移動先を修正して再試行しますか？
 
行を削除した項目は再試行しません。
"@
 
        if (
            -not (
                Confirm-Operation `
                    -Text $message `
                    -Title $Caption
            )
        ) {
            break
        }
 
        $sourcePathMap = @{}
        $editorItems = New-Object `
            System.Collections.Generic.List[object]
 
        foreach ($failure in $pendingItems) {
            $sourcePathMap[
            [string]$failure.Id
            ] = [string]$failure.SourcePath
 
            $editorItems.Add(
                [pscustomobject]@{
                    Id              = [string]$failure.Id
                    SourcePath      = [string]$failure.SourcePath
                    DestinationPath = [string]$failure.DestinationPath
                }
            )
        }
 
        $listFilePath = New-EditorListFile `
            -Items $editorItems `
            -Prefix 'retry'
 
        if (
            -not (
                Open-FileInConfiguredEditor `
                    -FilePath $listFilePath
            )
        ) {
            break
        }

        if (
            -not (
                Show-EditorWaitDialog `
                    -ListFilePath $listFilePath `
                    -ItemCount $editorItems.Count `
                    -Caption '再試行の編集'
            )
        ) {
            break
        }
 
        $parsedResult = Read-EditorListFile `
            -ListFilePath $listFilePath `
            -SourcePathMap $sourcePathMap
 
        if ($parsedResult.Errors.Count -gt 0) {
            $maximumErrorCount = [System.Math]::Min(
                30,
                $parsedResult.Errors.Count
            )
 
            $errorLines = New-Object `
                System.Collections.Generic.List[string]
 
            for (
                $index = 0
                $index -lt $maximumErrorCount
                $index++
            ) {
                $errorLines.Add(
                    [string]$parsedResult.Errors[$index]
                )
            }
 
            if (
                $parsedResult.Errors.Count -gt
                $maximumErrorCount
            ) {
                $remainingErrorCount = (
                    $parsedResult.Errors.Count -
                    $maximumErrorCount
                )
 
                $errorLines.Add(
                    "ほか $remainingErrorCount 件"
                )
            }
 
            Show-Warning (
                "編集一覧にエラーがあります。`r`n`r`n" +
                ($errorLines -join "`r`n")
            )
 
            continue
        }
 
        if (
            -not (
                Show-OperationPreview `
                    -Items $parsedResult.Items `
                    -Caption '再試行予定'
            )
        ) {
            break
        }
 
        $retryResult = Invoke-RenameMoveBatch `
            -Items $parsedResult.Items
 
        if ($retryResult.Success.Count -gt 0) {
            Save-RenameHistory `
                -SuccessfulItems $retryResult.Success `
                -Mode $HistoryMode `
                -RelatedSessionId $RelatedSessionId |
            Out-Null
        }
 
        $pendingItems = @(
            foreach ($it in $retryResult.Failed) { $it }
        )
 
        Show-Info (
            "再試行が完了しました。`r`n`r`n" +
            "成功: $($retryResult.Success.Count) 件`r`n" +
            "失敗: $($pendingItems.Count) 件"
        )
    }
 
    return [pscustomobject]@{
        RemainingFailedItems = @(
            foreach ($item in $pendingItems) {
                $item
            }
        )
    }
}
 
 


function Start-RenameOperation {
    param(
        [Parameter(Mandatory)]
        [string[]]$SelectedPaths
    )

    $targetFiles = New-Object System.Collections.Generic.List[string]
    $registeredFiles = @{}
 
    foreach ($rawPath in $SelectedPaths) {
        if (-not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
            continue
        }
    
        try {
            $normalized = ConvertTo-NormalPath `
                -Path $rawPath
    
            $pathKey = $normalized.ToUpperInvariant()
    
            if (-not $registeredFiles.ContainsKey($pathKey)) {
                $registeredFiles[$pathKey] = $true
                $targetFiles.Add($normalized)
            }
        }
        catch {
            Write-AppLog (
                "パスの正規化に失敗しました: $rawPath - " +
                $_.Exception.Message
            )
        }
    }

    if ($targetFiles.Count -eq 0) {
        Show-Warning '名前変更または移動の対象となるファイルが選択されていません。'
        return
    }

    $config = Get-ConfiguredEditor
    if ($null -eq $config) {
        return
    }

    $items = New-Object System.Collections.Generic.List[object]
    $sourcePathMap = @{}
    $counter = 1

    foreach ($filePath in $targetFiles) {
        $id = 'F{0:D8}' -f $counter
        $counter++

        $items.Add(
            [pscustomobject]@{
                Id              = $id
                SourcePath      = $filePath
                DestinationPath = $filePath
            }
        )

        $sourcePathMap[$id] = $filePath
    }

    $listFilePath = New-EditorListFile `
        -Items $items `
        -Prefix 'rename'

    while ($true) {
        if (
            -not (
                Open-FileInConfiguredEditor `
                    -FilePath $listFilePath
            )
        ) {
            return
        }

        $onFilesDropped = {
            param([string[]]$droppedPaths)
            if ($null -eq $droppedPaths -or $droppedPaths.Count -eq 0) { return }

            $addedPaths = New-Object System.Collections.Generic.List[string]
            foreach ($raw in $droppedPaths) {
                if (Test-Path -LiteralPath $raw -PathType Leaf) {
                    try {
                        $norm = ConvertTo-NormalPath -Path $raw
                        $key = $norm.ToUpperInvariant()
                        if (-not $registered.ContainsKey($key)) {
                            $registered[$key] = $true
                            $addedPaths.Add($norm)
                        }
                    }
                    catch {
                        Write-AppLog "パス正規化失敗: $raw - $($_.Exception.Message)"
                    }
                }
                elseif (Test-Path -LiteralPath $raw -PathType Container) {
                    try {
                        $dirFiles = [System.IO.Directory]::GetFiles($raw, '*', [System.IO.SearchOption]::AllDirectories)
                        foreach ($df in $dirFiles) {
                            $norm = ConvertTo-NormalPath -Path $df
                            $key = $norm.ToUpperInvariant()
                            if (-not $registered.ContainsKey($key)) {
                                $registered[$key] = $true
                                $addedPaths.Add($norm)
                            }
                        }
                    }
                    catch {
                        Write-AppLog "フォルダー内ファイル取得失敗: $raw - $($_.Exception.Message)"
                    }
                }
            }

            if ($addedPaths.Count -gt 0) {
                $appendLines = New-Object System.Collections.Generic.List[string]
                foreach ($p in $addedPaths) {
                    $id = 'F{0:D8}' -f $counter
                    $counter++
                    $items.Add([pscustomobject]@{
                        Id = $id
                        SourcePath = $p
                        DestinationPath = $p
                    })
                    $sourcePathMap[$id] = $p
                    $appendLines.Add("$id`t$p")
                }

                try {
                    [System.IO.File]::AppendAllLines($listFilePath, $appendLines, (New-Object System.Text.UTF8Encoding($true)))
                    $fileName = [System.IO.Path]::GetFileName($listFilePath)
                    $infoLabel.Text = "対象件数: $($items.Count) 件   ファイル: $fileName"
                    Show-Info "$($addedPaths.Count) 件のファイルを追加しました（計 $($items.Count) 件）。`r`n`r`nエディター側の一覧を再読み込みするか、`r`n［エディターを再度開く］ボタンを押して編集を続けてください。"
                }
                catch {
                    Show-ErrorMessage "一覧ファイルへの追記に失敗しました:`r`n$($_.Exception.Message)"
                }
            }
            else {
                Show-Info '追加対象となる新しいファイルはありませんでした（既に登録済みか無効なファイルです）。'
            }
        }

        if (
            -not (
                Show-EditorWaitDialog `
                    -ListFilePath $listFilePath `
                    -ItemCount $items.Count `
                    -Caption '編集待機中' `
                    -OnFilesDropped $onFilesDropped
            )
        ) {
            return
        }

        $parsedResult = Read-EditorListFile `
            -ListFilePath $listFilePath `
            -SourcePathMap $sourcePathMap

        if ($parsedResult.Errors.Count -gt 0) {
            $maximumErrorCount = [System.Math]::Min(
                30,
                $parsedResult.Errors.Count
            )

            $errorLines = New-Object System.Collections.Generic.List[string]

            for ($index = 0; $index -lt $maximumErrorCount; $index++) {
                $errorLines.Add([string]$parsedResult.Errors[$index])
            }

            if ($parsedResult.Errors.Count -gt $maximumErrorCount) {
                $remainingErrorCount = (
                    $parsedResult.Errors.Count -
                    $maximumErrorCount
                )

                $errorLines.Add("ほか $remainingErrorCount 件")
            }

            $errorMessage = "編集一覧にエラーがあります。`r`n`r`n" +
            ($errorLines -join "`r`n") +
            "`r`n`r`nエディターを再度開いて修正しますか？"

            if (Confirm-Operation -Text $errorMessage -Title "$AppName - 編集一覧のエラー") {
                continue
            }
            else {
                return
            }
        }

        if (
            -not (
                Show-OperationPreview `
                    -Items $parsedResult.Items `
                    -Caption '実行予定'
            )
        ) {
            return
        }

        $batchResult = Invoke-RenameMoveBatch `
            -Items $parsedResult.Items

        $sessionId = ''

        if ($batchResult.Success.Count -gt 0) {
            $historyFilePath = Save-RenameHistory `
                -SuccessfulItems $batchResult.Success `
                -Mode 'Rename'

            if ($null -ne $historyFilePath) {
                $sessionId = [System.IO.Path]::GetFileNameWithoutExtension($historyFilePath)
            }
        }

        $summaryMessage = "処理が完了しました。`r`n`r`n" +
        "成功: $($batchResult.Success.Count) 件`r`n" +
        "失敗: $($batchResult.Failed.Count) 件"

        if ($batchResult.Failed.Count -eq 0) {
            Show-Info $summaryMessage
            return
        }
        else {
            Show-Warning $summaryMessage

            Edit-AndRetryFailedItems `
                -FailedItems $batchResult.Failed `
                -Caption '失敗項目の再試行' `
                -HistoryMode 'Retry' `
                -RelatedSessionId $sessionId |
            Out-Null

            return
        }
    }
}


function Get-RenameHistorySessions {
    $sessions = New-Object System.Collections.Generic.List[object]

    if (-not (Test-Path -LiteralPath $HistoryDirectory -PathType Container)) {
        return $sessions
    }

    $jsonFiles = Get-ChildItem `
        -LiteralPath $HistoryDirectory `
        -Filter '*.json' `
        -File |
    Sort-Object -Property Name -Descending

    foreach ($file in $jsonFiles) {
        try {
            $content = Get-Content `
                -LiteralPath $file.FullName `
                -Raw `
                -Encoding UTF8 |
            ConvertFrom-Json

            if (
                $null -ne $content -and
                $null -ne $content.Entries -and
                $content.Mode -in @(
                    'Rename'
                    'Retry'
                )
            ) {
                $sessions.Add(
                    [pscustomobject]@{
                        SessionId      = [string]$content.SessionId
                        Created        = [string]$content.Created
                        Mode           = [string]$content.Mode
                        RelatedSession = [string]$content.RelatedSession
                        Undone         = [bool]$content.Undone
                        UndoneAt       = [string]$content.UndoneAt
                        Entries        = @(
                            foreach ($entry in $content.Entries) {
                                $entry
                            }
                        )
                        FilePath       = $file.FullName
                    }
                )
            }
        }
        catch {
            Write-AppLog "履歴ファイルの読み込みに失敗しました: $($file.FullName) - $($_.Exception.Message)"
        }
    }

    return $sessions
}


function Set-SessionUndone {
    param(
        [Parameter(Mandatory)]
        $Session
    )

    try {
        $rawSession = Get-Content `
            -LiteralPath $Session.FilePath `
            -Raw `
            -Encoding UTF8 |
        ConvertFrom-Json

        $rawSession.Undone = $true
        $undoneAt = (Get-Date).ToString('o')

        $rawSession |
            Add-Member `
                -NotePropertyName 'UndoneAt' `
                -NotePropertyValue $undoneAt `
                -Force

        $rawSession |
            ConvertTo-Json -Depth 10 |
            Set-Content `
                -LiteralPath $Session.FilePath `
                -Encoding UTF8

        $Session.Undone = $true
        $Session |
            Add-Member `
                -NotePropertyName 'UndoneAt' `
                -NotePropertyValue $undoneAt `
                -Force
    }
    catch {
        Write-AppLog (
            'セッションのUndoフラグ更新に失敗しました: ' +
            $_.Exception.Message
        )
    }
}


function Start-UndoOperation {
    $sessions = Get-RenameHistorySessions

    if ($sessions.Count -eq 0) {
        Show-Info '元に戻せる履歴が存在しません。'
        return
    }

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName - Undo履歴"
    $form.Size = New-Object System.Drawing.Size(760, 520)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.ShowInTaskbar = $true

    $instructionLabel = New-Object System.Windows.Forms.Label
    $instructionLabel.Text = 'Undoする履歴を選択してください:'
    $instructionLabel.Location = New-Object System.Drawing.Point(15, 12)
    $instructionLabel.AutoSize = $true

    $listBox = New-Object System.Windows.Forms.ListBox
    $listBox.Location = New-Object System.Drawing.Point(15, 35)
    $listBox.Size = New-Object System.Drawing.Size(715, 150)
    $listBox.Font = New-Object System.Drawing.Font('Lucida Console', 9.5)

    $detailLabel = New-Object System.Windows.Forms.Label
    $detailLabel.Text = '履歴詳細:'
    $detailLabel.Location = New-Object System.Drawing.Point(15, 195)
    $detailLabel.AutoSize = $true

    $detailTextBox = New-Object System.Windows.Forms.TextBox
    $detailTextBox.Location = New-Object System.Drawing.Point(15, 218)
    $detailTextBox.Size = New-Object System.Drawing.Size(715, 200)
    $detailTextBox.Multiline = $true
    $detailTextBox.ReadOnly = $true
    $detailTextBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
    $detailTextBox.Font = New-Object System.Drawing.Font('Lucida Console', 9)

    $undoButton = New-Object System.Windows.Forms.Button
    $undoButton.Text = '選択履歴を元に戻す'
    $undoButton.Location = New-Object System.Drawing.Point(475, 430)
    $undoButton.Size = New-Object System.Drawing.Size(155, 36)

    $closeButton = New-Object System.Windows.Forms.Button
    $closeButton.Text = '閉じる'
    $closeButton.Location = New-Object System.Drawing.Point(645, 430)
    $closeButton.Size = New-Object System.Drawing.Size(85, 36)

    $populateList = {
        $listBox.Items.Clear()
        $script:currentSessions = Get-RenameHistorySessions

        foreach ($session in $script:currentSessions) {
            $createdDate = [DateTime]::MinValue
            if (-not [DateTime]::TryParse($session.Created, [ref]$createdDate)) {
                $createdDate = Get-Date
            }

            $dateString = $createdDate.ToString('yyyy-MM-dd HH:mm:ss')
            $itemCount = $session.Entries.Count
            $modeString = "[{0}]" -f $session.Mode
            $undoneFlag = if ($session.Undone) { '  (Undo済み)' } else { '' }

            $lineText = "{0}   {1,5} 件   {2,-10}{3}" -f `
                $dateString,
            $itemCount,
            $modeString,
            $undoneFlag

            [void]$listBox.Items.Add($lineText)
        }

        if ($listBox.Items.Count -gt 0) {
            $listBox.SelectedIndex = 0
        }
    }

    $updateDetail = {
        $selectedIndex = $listBox.SelectedIndex
        if ($selectedIndex -lt 0 -or $selectedIndex -ge $script:currentSessions.Count) {
            $detailTextBox.Text = ''
            return
        }

        $session = $script:currentSessions[$selectedIndex]
        $lines = New-Object System.Collections.Generic.List[string]

        $lines.Add("セッションID: $($session.SessionId)")
        $lines.Add("実行日時: $($session.Created)")
        $lines.Add("処理種別: $($session.Mode)")
        $lines.Add("対象件数: $($session.Entries.Count) 件")
        $undoStatusText = if ($session.Undone) {
            if (-not (Test-TextIsEmpty ([string]$session.UndoneAt))) {
                "Undo済み ($($session.UndoneAt))"
            }
            else {
                'Undo済み'
            }
        }
        else {
            '未実行'
        }
        $lines.Add("Undo状態: $undoStatusText")
        $lines.Add('------------------------------------------------------------')

        $displayMax = [System.Math]::Min(30, $session.Entries.Count)
        for ($i = 0; $i -lt $displayMax; $i++) {
            $entry = $session.Entries[$i]
            $lines.Add("[$($entry.Id)]")
            $src = if ($entry.SourcePath) { $entry.SourcePath } else { $entry.Original }
            $dst = if ($entry.DestinationPath) { $entry.DestinationPath } else { $entry.Current }
            $lines.Add("  変更前: $src")
            $lines.Add("  変更後: $dst")
        }

        if ($session.Entries.Count -gt $displayMax) {
            $remain = $session.Entries.Count - $displayMax
            $lines.Add("ほか $remain 件...")
        }

        $detailTextBox.Text = ($lines -join "`r`n")
    }

    $listBox.Add_SelectedIndexChanged({
            & $updateDetail
        })

    $undoButton.Add_Click({
            $selectedIndex = $listBox.SelectedIndex
            if ($selectedIndex -lt 0 -or $selectedIndex -ge $script:currentSessions.Count) {
                Show-Warning '元に戻す履歴を選択してください。'
                return
            }

            $session = $script:currentSessions[$selectedIndex]

            if ($session.Undone) {
                if (-not (Confirm-Operation -Text "選択した履歴は既にUndoされています。`r`n再度元に戻す処理を実行しますか？" -Title "$AppName - Undoの確認")) {
                    return
                }
            }

            $undoItems = New-Object System.Collections.Generic.List[object]
            foreach ($entry in $session.Entries) {
                $undoSourcePath = if (
                    -not (
                        Test-TextIsEmpty `
                            -Text ([string]$entry.DestinationPath)
                    )
                ) {
                    [string]$entry.DestinationPath
                }
                else {
                    [string]$entry.Current
                }

                $undoDestinationPath = if (
                    -not (
                        Test-TextIsEmpty `
                            -Text ([string]$entry.SourcePath)
                    )
                ) {
                    [string]$entry.SourcePath
                }
                else {
                    [string]$entry.Original
                }

                $undoItems.Add(
                    [pscustomobject]@{
                        Id              = [string]$entry.Id
                        SourcePath      = $undoSourcePath
                        DestinationPath = $undoDestinationPath
                    }
                )
            }

            if (-not (Show-OperationPreview -Items $undoItems -Caption 'Undo実行予定')) {
                return
            }

            $batchResult = Invoke-RenameMoveBatch -Items $undoItems

            $undoHistoryFile = $null
            if ($batchResult.Success.Count -gt 0) {
                $undoHistoryFile = Save-RenameHistory `
                    -SuccessfulItems $batchResult.Success `
                    -Mode 'Undo' `
                    -RelatedSessionId $session.SessionId
            }

            $summaryMessage = "Undo処理が完了しました。`r`n`r`n" +
            "成功: $($batchResult.Success.Count) 件`r`n" +
            "失敗: $($batchResult.Failed.Count) 件"

            if ($batchResult.Failed.Count -eq 0) {
                Set-SessionUndone -Session $session
                Show-Info $summaryMessage
            }
            else {
                Show-Warning $summaryMessage

                $newSessionId = if ($null -ne $undoHistoryFile) {
                    [System.IO.Path]::GetFileNameWithoutExtension($undoHistoryFile)
                }
                else {
                    ''
                }

                $retryResult = Edit-AndRetryFailedItems `
                    -FailedItems $batchResult.Failed `
                    -Caption 'Undo失敗項目の再試行' `
                    -HistoryMode 'UndoRetry' `
                    -RelatedSessionId $newSessionId

                if ($null -ne $retryResult -and $retryResult.RemainingFailedItems.Count -eq 0) {
                    Set-SessionUndone `
                        -Session $session
                }
            }

            & $populateList
        })

    $closeButton.Add_Click({
            $form.Close()
        })

    $form.Controls.AddRange(@(
            $instructionLabel
            $listBox
            $detailLabel
            $detailTextBox
            $undoButton
            $closeButton
        ))

    $form.AcceptButton = $undoButton
    $form.CancelButton = $closeButton

    & $populateList

    [void]$form.ShowDialog()
    $form.Dispose()
}


function Show-MainWindow {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = $AppName
    $form.Size = New-Object System.Drawing.Size(730, 560)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.ShowInTaskbar = $true
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 9.5)
    $form.AllowDrop = $true

    $targetFiles = New-Object System.Collections.Generic.List[string]
    $registeredPaths = @{}

    $titleLabel = New-Object System.Windows.Forms.Label
    $titleLabel.Text = $AppName
    $titleLabel.Font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
    $titleLabel.Location = New-Object System.Drawing.Point(25, 15)
    $titleLabel.AutoSize = $true

    $descLabel = New-Object System.Windows.Forms.Label
    $descLabel.Text = "変更したいファイルをここにドラッグ＆ドロップして追加してください。`r`n「送る」メニューのパス長上限を超える大量のファイル（数千件以上）も一括登録できます。"
    $descLabel.Location = New-Object System.Drawing.Point(27, 46)
    $descLabel.Size = New-Object System.Drawing.Size(660, 38)

    $fileListBox = New-Object System.Windows.Forms.ListBox
    $fileListBox.Location = New-Object System.Drawing.Point(25, 90)
    $fileListBox.Size = New-Object System.Drawing.Size(515, 230)
    $fileListBox.HorizontalScrollbar = $true
    $fileListBox.SelectionMode = [System.Windows.Forms.SelectionMode]::MultiExtended
    $fileListBox.AllowDrop = $true

    $btnStartRename = New-Object System.Windows.Forms.Button
    $btnStartRename.Text = "エディターで`r`n名前変更を開始"
    $btnStartRename.Font = New-Object System.Drawing.Font('Segoe UI', 9.5, [System.Drawing.FontStyle]::Bold)
    $btnStartRename.Location = New-Object System.Drawing.Point(555, 90)
    $btnStartRename.Size = New-Object System.Drawing.Size(140, 52)
    $btnStartRename.Enabled = $false

    $btnAddFiles = New-Object System.Windows.Forms.Button
    $btnAddFiles.Text = 'ファイル追加...'
    $btnAddFiles.Location = New-Object System.Drawing.Point(555, 152)
    $btnAddFiles.Size = New-Object System.Drawing.Size(140, 32)

    $btnAddFolder = New-Object System.Windows.Forms.Button
    $btnAddFolder.Text = 'フォルダー追加...'
    $btnAddFolder.Location = New-Object System.Drawing.Point(555, 192)
    $btnAddFolder.Size = New-Object System.Drawing.Size(140, 32)

    $btnRemoveSelected = New-Object System.Windows.Forms.Button
    $btnRemoveSelected.Text = '選択項目を削除'
    $btnRemoveSelected.Location = New-Object System.Drawing.Point(555, 232)
    $btnRemoveSelected.Size = New-Object System.Drawing.Size(140, 32)
    $btnRemoveSelected.Enabled = $false

    $btnClear = New-Object System.Windows.Forms.Button
    $btnClear.Text = '一覧をクリア'
    $btnClear.Location = New-Object System.Drawing.Point(555, 272)
    $btnClear.Size = New-Object System.Drawing.Size(140, 32)
    $btnClear.Enabled = $false

    $lblCount = New-Object System.Windows.Forms.Label
    $lblCount.Text = '対象ファイル: 0 件'
    $lblCount.Font = New-Object System.Drawing.Font('Segoe UI', 9.5, [System.Drawing.FontStyle]::Bold)
    $lblCount.Location = New-Object System.Drawing.Point(25, 332)
    $lblCount.Size = New-Object System.Drawing.Size(200, 22)

    $chkSubfolders = New-Object System.Windows.Forms.CheckBox
    $chkSubfolders.Text = 'フォルダー投入時にサブフォルダーも含める'
    $chkSubfolders.Location = New-Object System.Drawing.Point(240, 331)
    $chkSubfolders.Size = New-Object System.Drawing.Size(300, 22)
    $chkSubfolders.Checked = $true

    $updateUIState = {
        $lblCount.Text = "対象ファイル: $($targetFiles.Count) 件"
        $btnStartRename.Enabled = ($targetFiles.Count -gt 0)
        $btnClear.Enabled = ($targetFiles.Count -gt 0)
        $btnRemoveSelected.Enabled = ($fileListBox.SelectedIndices.Count -gt 0)
    }

    $addSingleFile = {
        param([string]$rawPath)
        try {
            $normalized = ConvertTo-NormalPath -Path $rawPath
            $key = $normalized.ToUpperInvariant()
            if (-not $registeredPaths.ContainsKey($key)) {
                $registeredPaths[$key] = $true
                $targetFiles.Add($normalized)
                $fileListBox.Items.Add($normalized) | Out-Null
                return $true
            }
        }
        catch {
            Write-AppLog "パス正規化失敗: $rawPath - $($_.Exception.Message)"
        }
        return $false
    }

    $addPaths = {
        param([string[]]$paths, [bool]$includeSubfolders)
        if ($null -eq $paths -or $paths.Count -eq 0) { return }

        $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
        $fileListBox.BeginUpdate()
        try {
            foreach ($raw in $paths) {
                if (Test-TextIsEmpty -Text $raw) { continue }
                if (Test-Path -LiteralPath $raw -PathType Leaf) {
                    & $addSingleFile $raw | Out-Null
                }
                elseif (Test-Path -LiteralPath $raw -PathType Container) {
                    $searchOpt = if ($includeSubfolders) { [System.IO.SearchOption]::AllDirectories } else { [System.IO.SearchOption]::TopDirectoryOnly }
                    try {
                        $dirFiles = [System.IO.Directory]::GetFiles($raw, '*', $searchOpt)
                        foreach ($df in $dirFiles) {
                            & $addSingleFile $df | Out-Null
                        }
                    }
                    catch {
                        Write-AppLog "フォルダー内ファイル取得失敗: $raw - $($_.Exception.Message)"
                    }
                }
            }
        }
        finally {
            $fileListBox.EndUpdate()
            $form.Cursor = [System.Windows.Forms.Cursors]::Default
            & $updateUIState
        }
    }

    $clearItems = {
        $registeredPaths.Clear()
        $targetFiles.Clear()
        $fileListBox.Items.Clear()
        & $updateUIState
    }

    $removeSelectedItems = {
        if ($fileListBox.SelectedIndices.Count -eq 0) { return }
        $indices = New-Object System.Collections.Generic.List[int]
        foreach ($idx in $fileListBox.SelectedIndices) {
            $indices.Add([int]$idx)
        }
        $indices.Sort()
        $indices.Reverse()

        $fileListBox.BeginUpdate()
        try {
            foreach ($idx in $indices) {
                if ($idx -ge 0 -and $idx -lt $targetFiles.Count) {
                    $norm = $targetFiles[$idx]
                    $registeredPaths.Remove($norm.ToUpperInvariant())
                    $targetFiles.RemoveAt($idx)
                    $fileListBox.Items.RemoveAt($idx)
                }
            }
        }
        finally {
            $fileListBox.EndUpdate()
            & $updateUIState
        }
    }

    $fileListBox.Add_SelectedIndexChanged({
        $btnRemoveSelected.Enabled = ($fileListBox.SelectedIndices.Count -gt 0)
    })

    $dragEnterHandler = {
        param($sender, $e)
        if ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)) {
            $e.Effect = [System.Windows.Forms.DragDropEffects]::Copy
        }
        else {
            $e.Effect = [System.Windows.Forms.DragDropEffects]::None
        }
    }

    $dragDropHandler = {
        param($sender, $e)
        if ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)) {
            $dropped = $e.Data.GetData([System.Windows.Forms.DataFormats]::FileDrop)
            if ($null -ne $dropped -and $dropped.Length -gt 0) {
                & $addPaths ([string[]]$dropped) $chkSubfolders.Checked
            }
        }
    }

    $form.Add_DragEnter($dragEnterHandler)
    $form.Add_DragDrop($dragDropHandler)
    $fileListBox.Add_DragEnter($dragEnterHandler)
    $fileListBox.Add_DragDrop($dragDropHandler)

    $btnStartRename.Add_Click({
        if ($targetFiles.Count -eq 0) {
            Show-Warning '名前変更の対象となるファイルが追加されていません。ファイルをドラッグ＆ドロップするか、［ファイル追加...］から選択してください。'
            return
        }
        Start-RenameOperation -SelectedPaths ([string[]]$targetFiles.ToArray())
        & $clearItems
    })

    $btnAddFiles.Add_Click({
        $ofd = New-Object System.Windows.Forms.OpenFileDialog
        $ofd.Multiselect = $true
        $ofd.Title = '名前変更対象のファイルを選択'
        $ofd.Filter = 'すべてのファイル (*.*)|*.*'
        if ($ofd.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            & $addPaths ([string[]]$ofd.FileNames) $false
        }
        $ofd.Dispose()
    })

    $btnAddFolder.Add_Click({
        $fbd = New-Object System.Windows.Forms.FolderBrowserDialog
        $fbd.Description = '対象ファイルを含むフォルダーを選択してください。'
        $fbd.ShowNewFolderButton = $false
        if ($fbd.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            & $addPaths @([string]$fbd.SelectedPath) $chkSubfolders.Checked
        }
        $fbd.Dispose()
    })

    $btnRemoveSelected.Add_Click({
        & $removeSelectedItems
    })

    $btnClear.Add_Click({
        & $clearItems
    })

    $toolsGroup = New-Object System.Windows.Forms.GroupBox
    $toolsGroup.Text = 'ツール・設定'
    $toolsGroup.Location = New-Object System.Drawing.Point(25, 365)
    $toolsGroup.Size = New-Object System.Drawing.Size(670, 135)

    $undoButton = New-Object System.Windows.Forms.Button
    $undoButton.Text = 'Undo履歴...'
    $undoButton.Location = New-Object System.Drawing.Point(20, 30)
    $undoButton.Size = New-Object System.Drawing.Size(150, 36)
    $undoButton.Add_Click({ Start-UndoOperation })

    $settingsButton = New-Object System.Windows.Forms.Button
    $settingsButton.Text = 'エディター設定...'
    $settingsButton.Location = New-Object System.Drawing.Point(180, 30)
    $settingsButton.Size = New-Object System.Drawing.Size(150, 36)
    $settingsButton.Add_Click({ Show-EditorSettings })

    $openFolderButton = New-Object System.Windows.Forms.Button
    $openFolderButton.Text = '作業フォルダー'
    $openFolderButton.Location = New-Object System.Drawing.Point(340, 30)
    $openFolderButton.Size = New-Object System.Drawing.Size(140, 36)
    $openFolderButton.Add_Click({
        try { [System.Diagnostics.Process]::Start('explorer.exe', $AppDirectory) | Out-Null }
        catch { Show-ErrorMessage $_.Exception.Message }
    })

    $openLogButton = New-Object System.Windows.Forms.Button
    $openLogButton.Text = 'ログを開く'
    $openLogButton.Location = New-Object System.Drawing.Point(20, 80)
    $openLogButton.Size = New-Object System.Drawing.Size(150, 36)
    $openLogButton.Add_Click({
        try {
            if (Test-Path -LiteralPath $LogPath) {
                [System.Diagnostics.Process]::Start('notepad.exe', $LogPath) | Out-Null
            } else {
                Show-Info 'ログファイルはまだ作成されていません。'
            }
        }
        catch { Show-ErrorMessage $_.Exception.Message }
    })

    $closeButton = New-Object System.Windows.Forms.Button
    $closeButton.Text = '閉じる'
    $closeButton.Location = New-Object System.Drawing.Point(510, 80)
    $closeButton.Size = New-Object System.Drawing.Size(140, 36)
    $closeButton.Add_Click({ $form.Close() })

    $toolsGroup.Controls.AddRange(@(
        $undoButton
        $settingsButton
        $openFolderButton
        $openLogButton
        $closeButton
    ))

    $form.Controls.AddRange(@(
        $titleLabel
        $descLabel
        $fileListBox
        $btnStartRename
        $btnAddFiles
        $btnAddFolder
        $btnRemoveSelected
        $btnClear
        $lblCount
        $chkSubfolders
        $toolsGroup
    ))

    [void]$form.ShowDialog()
    $form.Dispose()
}

try {
    if ($args.Count -gt 0) {
        Start-RenameOperation `
            -SelectedPaths ([string[]]$args)
    }
    else {
        Show-MainWindow
    }
}
catch {
    Write-AppLog $_.Exception.ToString()

    $errorMessage = @"
予期しないエラーが発生しました。

$($_.Exception.Message)

エラーが発生した行:
$($_.InvocationInfo.ScriptLineNumber)

対象:
$($_.InvocationInfo.Line)

ログ:
$LogPath
"@

    Show-ErrorMessage `
        -Text $errorMessage `
        -Title "$AppName - エラー"
}
