using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileSyncX.ViewModels;
using FileSyncX.Views;
using FileSyncX.Views.UserControls;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;

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

        // Obtém o assembly atual para realizar a varredura
        var assembly = Assembly.GetExecutingAssembly();

        // =========================================================================
        // REGISTRO AUTOMÁTICO VIA REFLECTION
        // =========================================================================

        // 1. Registro de ViewModels
        // Varre classes que não são abstratas e cujo nome termina com "ViewModel"
        var viewModels = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("ViewModel"));

        foreach (var type in viewModels)
        {
            // MainWindowViewModel é registrado como Singleton para manter o estado da navegação
            if (type.Name == nameof(MainWindowViewModel))
            {
                services.AddSingleton(type);
            }
            else
            {
                services.AddTransient(type);
            }
        }

        // 2. Registro de Views (Windows e UserControls)
        // Varre classes que herdam de Window ou UserControl e não são abstratas
        var views = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        (t.IsSubclassOf(typeof(Window)) || t.IsSubclassOf(typeof(UserControl))));

        foreach (var type in views)
        {
            services.AddTransient(type);
        }

        // =========================================================================

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolve a MainWindow e o DataContext via Injetor
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}