namespace FontSelector.Models;

/// <summary>
/// 詳細情報パネルでクリック可能な言語タグ項目。
/// </summary>
public class LanguageTagItem
{
    public string Name { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
    public string ToolTipText => IsSelected
        ? $"現在「{Name}」でフィルター中（クリックで解除）"
        : $"クリックして「{Name}」でフィルター";
}
