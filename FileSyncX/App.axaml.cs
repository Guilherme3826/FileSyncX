using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileSyncX.EfCore.Contexts;
using FileSyncX.Interfaces;
using FileSyncX.Services;
using FileSyncX.ViewModels;
using FileSyncX.Views;
using FileSyncX.Views.UserControls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var assembly = Assembly.GetExecutingAssembly();

        // =============================================================
        // INFRAESTRUTURA DO VECKTORVAULT E SERVIÇOS SEMÂNTICOS
        // TryAdd preserva registros equivalentes que já existam no projeto.
        // =============================================================

        services.TryAddSingleton<VecktorVaultDbContextFactory>();
        services.TryAddSingleton<IDbContextFactory<VecktorVaultDbContext>>(
            provider => provider.GetRequiredService<VecktorVaultDbContextFactory>());

        // Serviços-base já utilizados pelo processamento semântico de PDFs.
        services.TryAddSingleton<IEmbeddingService, OpenAiEmbeddingService>();
        services.TryAddSingleton<IPdfTextExtractionService, PdfTextExtractionService>();
        services.TryAddSingleton<IVecktorVaultService, VecktorVaultService>();
        services.TryAddSingleton<IPdfSemanticProcessingService, PdfSemanticProcessingService>();
        services.TryAddSingleton<IPdfBatchProcessingService, PdfBatchProcessingService>();

        // Serviços exclusivos do Gerenciador de Categorias IA.
        services.TryAddSingleton<IAiCategoryDescriptionService, OpenAiCategoryDescriptionService>();
        services.TryAddSingleton<ISemanticCategoryManagementService, SemanticCategoryManagementService>();

        // =============================================================
        // REGISTRO AUTOMÁTICO DE VIEWMODELS E VIEWS
        // =============================================================

        var viewModels = assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract &&
                           type.Name.EndsWith("ViewModel", StringComparison.Ordinal));

        foreach (Type viewModelType in viewModels)
            services.AddSingleton(viewModelType);

        var views = assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract &&
                           (type.IsSubclassOf(typeof(Window)) ||
                            type.IsSubclassOf(typeof(UserControl))));

        foreach (Type viewType in views)
            services.AddTransient(viewType);

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
