namespace FontSelector.Models;

/// <summary>
/// 詳細情報パネルでクリック可能な太さ（Weight）タグ項目。
/// いま表示中の太さは IsSelected = true で強調表示されます。
/// </summary>
public class WeightTagItem
{
    public string DisplayText { get; init; } = string.Empty;
    public string JapaneseWeightLabel { get; init; } = string.Empty;
    public int OpenTypeWeight { get; init; }
    public bool IsSelected { get; init; }
    public string ToolTipText => IsSelected
        ? $"現在表示中の太さ: {DisplayText} [{JapaneseWeightLabel}]"
        : $"太さ: {DisplayText} [{JapaneseWeightLabel}] (クリックしてこの太さに切り替え)";
    public TypefaceInfo? Typeface { get; init; }
}
