using System.Windows;
using FontSelector.Services;
using FontSelector.ViewModels;
using FontSelector.Views;
using Microsoft.Extensions.DependencyInjection;

namespace FontSelector;

/// <summary>
/// Application entry point. Sets up DI container and initializes services.
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
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
        _serviceProvider = services.BuildServiceProvider();

        // Initialize active theme
        var themeService = _serviceProvider.GetRequiredService<IThemeService>();
        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        themeService.SetTheme(viewModel.CurrentTheme);

        // Show main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
