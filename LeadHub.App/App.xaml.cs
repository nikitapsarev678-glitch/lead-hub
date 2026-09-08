using System.IO;
using System.Windows;
using LeadHub.App.Services;
using LeadHub.App.ViewModels;
using LeadHub.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LeadHub.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<AppState>();
        services.AddSingleton<SingBoxRuntime>();
        services.AddSingleton<ParseRunner>();
        services.AddTransient<VpnHubViewModel>();
        services.AddTransient<AccountsViewModel>();
        services.AddTransient<ParseViewModel>();
        services.AddTransient<RunViewModel>();
        services.AddTransient<LeadsViewModel>();
        services.AddTransient<StatsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<VpnHubPage>();
        services.AddTransient<AccountsPage>();
        services.AddTransient<ParsePage>();
        services.AddTransient<RunPage>();
        services.AddTransient<LeadsPage>();
        services.AddTransient<StatsPage>();
        services.AddTransient<SettingsPage>();
        Services = services.BuildServiceProvider();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services.GetRequiredService<SingBoxRuntime>().Dispose();
            Services.GetRequiredService<ParseRunner>().Dispose();
            Services.GetRequiredService<AppState>().Dispose();
        }
        catch { /* завершение — не падаем */ }
        base.OnExit(e);
    }
}
