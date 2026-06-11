using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Curatio.Core;
using Curatio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Curatio.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Curatio");
            var databasePath = Path.Combine(appData, "curatio.db");
            var rulesPath = Path.Combine(AppContext.BaseDirectory, "config", "extraction-rules.json");

            var services = new ServiceCollection();
            services.AddCuratioInfrastructure(databasePath, rulesPath);
            services.AddSingleton<MainWindowViewModel>();
            var provider = services.BuildServiceProvider();

            var repository = provider.GetRequiredService<IRecordRepository>();
            repository.InitializeAsync().GetAwaiter().GetResult();

            var viewModel = provider.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow(viewModel);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
