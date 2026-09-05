using System.Windows;
using FontSelector.Services;
using FontSelector.ViewModels;
using FontSelector.Views;
using Microsoft.Extensions.DependencyInjection;

namespace FontSelector;

/// <summary>
/// Application entry point. Sets up DI container and manages theme switching.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Build DI container
        var services = new ServiceCollection();
        services.AddSingleton<IFontService, FontService>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
        _serviceProvider = services.BuildServiceProvider();

        // Subscribe to theme changes
        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        ApplyTheme(viewModel.CurrentTheme);
        viewModel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CurrentTheme))
            {
                ApplyTheme(viewModel.CurrentTheme);
            }
        };

        // Show main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ApplyTheme(string themeKey)
    {
        var themeFile = themeKey switch
        {
            "tal7aouy" => "Themes/Tal7aouyTheme.xaml",
            "spinel" => "Themes/SpinelTheme.xaml",
            "light" => "Themes/LightTheme.xaml",
            _ => "Themes/DarkTheme.xaml"
        };

        var newTheme = new ResourceDictionary { Source = new Uri(themeFile, UriKind.Relative) };

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(newTheme);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
