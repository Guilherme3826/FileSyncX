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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Pgvector.EntityFrameworkCore;
using FileSyncX.EfCore.Contexts;
using FileSyncX.Services;



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

        // =========================================================
        // CONFIGURAÇÕES DA APLICAÇÃO
        // =========================================================

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(
                "appsettings.json",
                optional: false,
                reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // =========================================================
        // DATABASE: VECKTORVAULT
        // =========================================================

        services.AddDbContextFactory<VecktorVaultDbContext>(
            (serviceProvider, options) =>
            {
                var config = serviceProvider
                    .GetRequiredService<IConfiguration>();

                var connectionString = config
                    .GetConnectionString("VecktorVault");

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "A connection string VecktorVault não foi encontrada.");
                }

                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                    {
                        // Habilita o suporte a pgvector
                        npgsqlOptions.UseVector();

                        // Recuperação de erros transitórios de conexão
                        npgsqlOptions.EnableRetryOnFailure();
                    });
            });

        // =========================================================
        // SERVICES
        // =========================================================

        services.AddSingleton<
            IVecktorVaultService,
            VecktorVaultService>();

        // =========================================================
        // REFLECTION: VIEWMODELS
        // =========================================================

        var assembly = Assembly.GetExecutingAssembly();

        var viewModels = assembly.GetTypes()
            .Where(t =>
                t.IsClass &&
                !t.IsAbstract &&
                t.Name.EndsWith("ViewModel"));

        foreach (var type in viewModels)
        {
            services.AddSingleton(type);
        }

        // =========================================================
        // REFLECTION: VIEWS
        // =========================================================

        var views = assembly.GetTypes()
            .Where(t =>
                t.IsClass &&
                !t.IsAbstract &&
                (
                    t.IsSubclassOf(typeof(Window)) ||
                    t.IsSubclassOf(typeof(UserControl))
                ));

        foreach (var type in views)
        {
            services.AddTransient(type);
        }

        // =========================================================
        // CONSTRUÇÃO DO SERVICE PROVIDER
        // =========================================================

        Services = services.BuildServiceProvider();

        // =========================================================
        // MAIN WINDOW
        // =========================================================

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow =
                Services.GetRequiredService<MainWindow>();

            desktop.MainWindow.DataContext =
                Services.GetRequiredService<MainWindowViewModel>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}