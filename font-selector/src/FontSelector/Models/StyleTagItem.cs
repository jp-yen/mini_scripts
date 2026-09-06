namespace FontSelector.Models;

/// <summary>
/// 詳細情報パネルでクリック可能なスタイル（Style）タグ項目。
/// いま表示中のスタイルは IsSelected = true で強調表示されます。
/// </summary>
public class StyleTagItem
{
    public string DisplayText { get; init; } = string.Empty;
    public string JapaneseStyleLabel { get; init; } = string.Empty;
    public System.Windows.FontStyle Style { get; init; }
    public bool IsSelected { get; init; }
    public string ToolTipText => IsSelected
        ? $"現在表示中のスタイル: {DisplayText} ({(string.IsNullOrEmpty(JapaneseStyleLabel) ? DisplayText : JapaneseStyleLabel)})"
        : $"スタイル: {DisplayText} ({(string.IsNullOrEmpty(JapaneseStyleLabel) ? DisplayText : JapaneseStyleLabel)}) (クリックしてこのスタイルに切り替え)";
    public TypefaceInfo? Typeface { get; init; }
}
