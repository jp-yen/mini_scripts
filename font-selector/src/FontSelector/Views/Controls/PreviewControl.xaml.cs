using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FontSelector.Services;
using FontSelector.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace FontSelector.Views.Controls;

public partial class PreviewControl : UserControl
{
    private bool _isWebViewReady;

    public PreviewControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeWebViewAsync();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            oldVm.Preview.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            newVm.Preview.PropertyChanged += OnViewModelPropertyChanged;
            
            // Initial sync if ready
            if (_isWebViewReady)
            {
                UpdatePreviewStyles();
                UpdatePreviewText();
            }
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = Path.Combine(localAppData, "FontSelector", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            await PreviewWebView.EnsureCoreWebView2Async(env);

            PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PreviewWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PreviewWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            PreviewWebView.NavigationCompleted += (s, e) =>
            {
                _isWebViewReady = true;
                UpdatePreviewStyles();
                UpdatePreviewText();
            };

            PreviewWebView.NavigateToString(PreviewHtmlBuilder.BuildInitialHtml());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 initialization failed: {ex.Message}");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_isWebViewReady) return;

        switch (e.PropertyName)
        {
            case nameof(PreviewViewModel.PreviewLines):
            case nameof(PreviewViewModel.FilteredPreviewText):
                UpdatePreviewText();
                break;

            case nameof(MainViewModel.SelectedTypeface):
                UpdatePreviewStyles();
                UpdatePreviewText();
                break;

            case nameof(PreviewViewModel.FontSize):
            case nameof(PreviewViewModel.WeightValue):
            case nameof(PreviewViewModel.WidthScale):
            case nameof(PreviewViewModel.SlantAngle):
            case nameof(PreviewViewModel.IsItalic):
            case nameof(MainViewModel.IsDarkTheme):
            case nameof(MainViewModel.CurrentTheme):
                UpdatePreviewStyles();
                break;

            case nameof(MainViewModel.IsLoading):
                if (sender is MainViewModel { IsLoading: false })
                {
                    UpdatePreviewStyles();
                    UpdatePreviewText();
                }
                break;
        }
    }

    private void UpdatePreviewStyles()
    {
        if (!_isWebViewReady || PreviewWebView.CoreWebView2 is null || DataContext is not MainViewModel vm) return;

        var familyName = vm.SelectedTypeface?.FamilyName;
        var displayName = vm.SelectedTypeface?.DisplayName;

        string fontCss;
        if (string.IsNullOrEmpty(familyName))
        {
            fontCss = "sans-serif";
        }
        else if (vm.SelectedTypeface?.IsVariableFont == true || string.IsNullOrEmpty(displayName) || displayName == familyName)
        {
            // バリアブルフォントの場合は、"Bahnschrift Regular" などの静的インスタンス名を指定すると
            // Chromium / Blink 側で可変軸（wght, wdth, slnt）が無効化・固定化されてしまうため、
            // 純粋なファミリー名（"Bahnschrift"）を指定してバリアブル軸設定を有効にします。
            fontCss = $"\"{familyName}\"";
        }
        else
        {
            fontCss = $"\"{displayName}\", \"{familyName}\"";
        }

        var fontSize = vm.Preview.FontSize;
        var fontWeight = vm.Preview.WeightValue;
        var isItalic = vm.Preview.IsItalic;

        // バリアブルフォント軸の判定
        var variationAxes = vm.SelectedTypeface?.VariationAxes;
        var hasWdthAxis = variationAxes is not null && variationAxes.Any(a => a.Tag == "wdth");
        var hasSlntAxis = variationAxes is not null && variationAxes.Any(a => a.Tag == "slnt");
        var hasWghtAxis = variationAxes is not null && variationAxes.Any(a => a.Tag == "wght");

        // font-variation-settings の構築
        var varSettingsList = new List<string>();
        if (hasWghtAxis)
        {
            varSettingsList.Add($"\"wght\" {fontWeight:F0}");
        }

        double scaleX;
        string fontStretch = "100%";
        if (hasWdthAxis)
        {
            varSettingsList.Add($"\"wdth\" {vm.Preview.WidthScale:F0}");
            fontStretch = $"{vm.Preview.WidthScale:F0}%";
            scaleX = 1.0; // フォント自身のネイティブ軸で字幅と文字送りを調整
        }
        else
        {
            scaleX = vm.Preview.WidthScale / 100.0; // CSS で均等スケール
        }

        var italAxis = variationAxes?.FirstOrDefault(a => a.Tag == "ital");
        if (italAxis is not null && Math.Abs(italAxis.MaxValue - italAxis.MinValue) > 0.001)
        {
            varSettingsList.Add(isItalic ? "\"ital\" 1" : "\"ital\" 0");
        }

        // font-style / slantAngle の処理
        // skewX による平行四辺形変形（行頭ズレ）を排除し、
        // CSS Fonts Level 4 の font-style: oblique <angle>deg を使用して
        // 各文字が自身のベースライン基準で傾く正しい組版を実現します。
        string fontStyle;
        if (Math.Abs(vm.Preview.SlantAngle) > 0.01)
        {
            fontStyle = $"oblique {vm.Preview.SlantAngle:F1}deg";
            if (hasSlntAxis)
            {
                // OpenType slnt 軸は右傾斜が負値
                varSettingsList.Add($"\"slnt\" {-vm.Preview.SlantAngle:F1}");
            }
        }
        else if (isItalic)
        {
            fontStyle = vm.SelectedTypeface?.Style == FontStyles.Oblique ? "oblique" : "italic";
        }
        else
        {
            fontStyle = "normal";
        }

        var fontVariationSettings = varSettingsList.Count > 0
            ? string.Join(", ", varSettingsList)
            : "normal";

        var textColor = vm.GetPreviewTextColor();
        var bgColor = "transparent";

        var script = PreviewHtmlBuilder.BuildUpdateStylesScript(
            fontCss, fontSize, fontWeight, fontStyle, scaleX, textColor, bgColor, fontVariationSettings, fontStretch);

        _ = PreviewWebView.ExecuteScriptAsync(script);
    }

    private void UpdatePreviewText()
    {
        if (!_isWebViewReady || PreviewWebView.CoreWebView2 is null || DataContext is not MainViewModel vm) return;

        var script = PreviewHtmlBuilder.BuildUpdateTextScript(vm.Preview.PreviewLines);
        _ = PreviewWebView.ExecuteScriptAsync(script);
    }
}
