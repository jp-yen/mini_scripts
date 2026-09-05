using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FontSelector.ViewModels;

namespace FontSelector.Views;

/// <summary>
/// MainWindow code-behind.
/// View initialization is delegated to UserControls and handles native DWM title bar theming.
/// </summary>
public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SourceInitialized += (s, e) => UpdateTitleBarTheme();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentTheme) or nameof(MainViewModel.IsDarkTheme))
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () => UpdateTitleBarTheme());
        }
    }

    public void UpdateTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        bool isDark = true;
        Color bgColor = Color.FromRgb(0x21, 0x25, 0x2B);
        Color textColor = Color.FromRgb(0xE6, 0xED, 0xF3);

        if (Application.Current.TryFindResource("WindowBackgroundColor") is Color c)
            bgColor = c;
        else if (Application.Current.TryFindResource("WindowBackground") is SolidColorBrush b)
            bgColor = b.Color;

        if (Application.Current.TryFindResource("PrimaryTextColor") is Color tc)
            textColor = tc;
        else if (Application.Current.TryFindResource("PrimaryText") is SolidColorBrush tb)
            textColor = tb.Color;

        if (DataContext is MainViewModel vm)
            isDark = vm.IsDarkTheme;
        else
            isDark = (bgColor.R * 0.299 + bgColor.G * 0.587 + bgColor.B * 0.114) < 128;

        int darkModeVal = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkModeVal, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkModeVal, sizeof(int));

        // Windows 11 (build 22000+) supports custom caption background & text colors
        int captionColor = (bgColor.B << 16) | (bgColor.G << 8) | bgColor.R;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

        int textClr = (textColor.B << 16) | (textColor.G << 8) | textColor.R;
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textClr, sizeof(int));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBarTheme();
        if (DataContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
