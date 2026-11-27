using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FileSyncX.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FileSyncX.Views;

public partial class PdfBatchProcessingWindow : Window
{
    public PdfBatchProcessingViewModel ViewModel { get; }

    // O compilador XAML do Avalonia exige um construtor público sem parâmetros.
    // Em execução, a ViewModel continua sendo resolvida pelo contêiner de DI.
    public PdfBatchProcessingWindow()
        : this(global::FileSyncX.App.Services.GetRequiredService<PdfBatchProcessingViewModel>())
    {
    }

    public PdfBatchProcessingWindow(PdfBatchProcessingViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;

        Closed += (_, _) =>
        {
            if (ViewModel.IsBusy)
                ViewModel.CancelarProcessamentoCommand.Execute(null);
        };
    }

    private async void SelecionarPasta_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var pastas = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Selecione a pasta que contém os PDFs",
                AllowMultiple = false
            });

        var pasta = pastas.FirstOrDefault();
        if (pasta is not null)
            ViewModel.PastaOrigem = pasta.Path.LocalPath;
    }
}
