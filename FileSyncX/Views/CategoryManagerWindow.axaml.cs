using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FileSyncX.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FileSyncX.Views;

public partial class CategoryManagerWindow : Window
{
    public SemanticCategoryManagerViewModel ViewModel { get; }

    // Mantém um construtor público sem parâmetros para o carregador XAML do Avalonia.
    public CategoryManagerWindow()
        : this(global::FileSyncX.App.Services.GetRequiredService<SemanticCategoryManagerViewModel>())
    {
    }

    public CategoryManagerWindow(SemanticCategoryManagerViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += Window_Loaded;
    }

    private async void Window_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ViewModel.CarregarCategoriasAsync();
    }

    private void Fechar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private async void SelecionarArquivoOcr_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Anexar documento PDF ou texto OCR",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("PDF e arquivos OCR")
                    {
                        Patterns = new[] { "*.pdf", "*.txt", "*.md", "*.ocr" }
                    }
                }
            });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        try
        {
            await ViewModel.AnexarDocumentoOcrAsync(file.Path.LocalPath);
        }
        catch (Exception ex)
        {
            ViewModel.MensagemErro = ex.GetBaseException().Message;
        }
    }

    private async void SelecionarPastaDestino_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Selecione a pasta de destino da categoria",
                AllowMultiple = false
            });

        var folder = folders.FirstOrDefault();
        if (folder is not null)
            ViewModel.PastaDestino = folder.Path.LocalPath;
    }

    private async void ExcluirCategoria_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!ViewModel.PodeExcluir)
            return;

        string categoryName = ViewModel.NomeCategoria;
        int linkedDocuments = ViewModel.DocumentosAssociados;

        var message = new TextBlock
        {
            Text = $"Deseja excluir a categoria '{categoryName}'?\n\n" +
                   $"Existem {linkedDocuments} documento(s) associado(s). Os arquivos e registros serão preservados, " +
                   "mas o vínculo id_hash_default será removido.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 430
        };

        var cancelButton = new Button
        {
            Content = "Manter categoria",
            MinWidth = 130,
            Padding = new Avalonia.Thickness(12, 7)
        };
        var deleteButton = new Button
        {
            Content = "Excluir categoria",
            MinWidth = 130,
            Padding = new Avalonia.Thickness(12, 7),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#C83C4B")),
            Foreground = Avalonia.Media.Brushes.White
        };

        var dialog = new Window
        {
            Title = "Confirmar exclusão",
            Width = 510,
            Height = 230,
            MinWidth = 510,
            MinHeight = 230,
            MaxWidth = 510,
            MaxHeight = 230,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(22),
                Spacing = 22,
                Children =
                {
                    message,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancelButton, deleteButton }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        deleteButton.Click += (_, _) => dialog.Close(true);

        bool confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed)
            await ViewModel.ExcluirCategoriaSelecionadaAsync();
    }
}
