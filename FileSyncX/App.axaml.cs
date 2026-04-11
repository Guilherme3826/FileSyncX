using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileSyncX.ViewModels;
using FileSyncX.Views;
using FileSyncX.Views.UserControls;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace FileSyncX;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Registro dos ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<GerenciadorArquivosViewModel>();

        // Registro das Views
        services.AddTransient<MainWindow>();
        services.AddTransient<GerenciadorArquivos>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}