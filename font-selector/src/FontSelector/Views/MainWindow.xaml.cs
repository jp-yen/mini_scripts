using System.Windows;
using FontSelector.Services;
using FontSelector.ViewModels;

namespace FontSelector.Views;

/// <summary>
/// MainWindow code-behind.
/// View initialization is delegated to UserControls and ThemeService handles native DWM title bar theming.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IThemeService _themeService;

    public MainWindow(MainViewModel viewModel, IThemeService themeService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _themeService = themeService;

        if (Icon == null)
        {
            try
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
            }
            catch
            {
                try
                {
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.png", UriKind.Absolute));
                }
                catch
                {
                    // Ignore if resource cannot be loaded
                }
            }
        }

        _themeService.ThemeChanged += _ =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () => _themeService.ApplyTitleBarTheme(this));
        };

        SourceInitialized += (s, e) => _themeService.ApplyTitleBarTheme(this);
    }

    public void UpdateTitleBarTheme() => _themeService.ApplyTitleBarTheme(this);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _themeService.ApplyTitleBarTheme(this);
        if (DataContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
