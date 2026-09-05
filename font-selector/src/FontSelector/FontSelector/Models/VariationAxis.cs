namespace FontSelector.Models;

/// <summary>
/// バリアブルフォントの可変軸情報（fvar テーブル）
/// </summary>
public class VariationAxis
{
    /// <summary>4文字の軸タグ（例: "wght", "wdth", "slnt", "ital", "opsz"）</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>日本語の表示名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>最小値</summary>
    public double MinValue { get; set; }

    /// <summary>初期値（デフォルト値）</summary>
    public double DefaultValue { get; set; }

    /// <summary>最大値</summary>
    public double MaxValue { get; set; }

    /// <summary>両端の値が同じ場合は単一値、異なる場合は範囲の文字列表記（例: "200〜700" または "0"）</summary>
    public string RangeOrValueText => Math.Abs(MinValue - MaxValue) < 0.001
        ? FormatNumber(MinValue)
        : $"{FormatNumber(MinValue)}〜{FormatNumber(MaxValue)}";

    /// <summary>サマリー表示用文字列（例: "太さ (Weight): 200〜700" または "イタリック (Italic): 0"）</summary>
    public string SummaryText => $"{Name}: {RangeOrValueText}";

    /// <summary>表示用文字列（例: "太さ: 100〜900 (標準 400)" または "イタリック: 0 (標準 0)"）</summary>
    public string DisplayText => $"{Name}: {RangeOrValueText} (標準 {FormatNumber(DefaultValue)})";

    private static string FormatNumber(double val) =>
        Math.Abs(val % 1) < 0.001 ? val.ToString("0") : val.ToString("0.##");
}
