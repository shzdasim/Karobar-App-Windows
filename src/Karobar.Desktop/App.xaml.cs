using System.Windows;
using Karobar.Desktop.ViewModels;
using Karobar.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Karobar.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices((_, services) =>
                {
                    services.AddKarobarInfrastructure();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            // Create and show MainWindow synchronously BEFORE any async await so WPF never exits early
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.Activate();

            await _host.StartAsync();

            var mainVm = _host.Services.GetRequiredService<MainViewModel>();
            await mainVm.EnsureDatabaseInitializedAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start Karobar App:\n\n{ex.Message}\n\n{ex}",
                "Karobar Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
