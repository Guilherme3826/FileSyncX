using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FileSyncX.ViewModels;
using FileSyncX.Views;
using Microsoft.Extensions.DependencyInjection;

namespace FileSyncX.Views.UserControls;

public partial class GerenciadorArquivos : UserControl
{
    public GerenciadorArquivos()
    {
        InitializeComponent();
        InicializarEventos();
    }

    public GerenciadorArquivos(GerenciadorArquivosViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private void InicializarEventos()
    {
        (this.FindControl<Button>("AbrirProcessamentoSemanticoButton")
            ?? throw new InvalidOperationException("O botão AbrirProcessamentoSemanticoButton não foi encontrado no XAML."))
            .Click += AbrirProcessamentoSemantico_Click;
        (this.FindControl<Button>("AbrirGerenciadorCategoriasButton")
            ?? throw new InvalidOperationException("O botão AbrirGerenciadorCategoriasButton não foi encontrado no XAML."))
            .Click += AbrirGerenciadorCategorias_Click;
        (this.FindControl<MenuItem>("ConsolidarPastasSemelhantesMenuItem")
            ?? throw new InvalidOperationException("O item ConsolidarPastasSemelhantesMenuItem não foi encontrado no XAML."))
            .Click += ConsolidarPastasSemelhantes_Click;
        AddHandler(DragDrop.DropEvent, DropFolder);
    }

    private async void AbrirProcessamentoSemantico_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = App.Services.GetRequiredService<PdfBatchProcessingWindow>();

            if (DataContext is GerenciadorArquivosViewModel viewModel &&
                !string.IsNullOrWhiteSpace(viewModel.PastaAtual) &&
                Directory.Exists(viewModel.PastaAtual))
            {
                window.ViewModel.PastaOrigem = viewModel.PastaAtual;
            }

            if (TopLevel.GetTopLevel(this) is Window owner)
                await window.ShowDialog(owner);
            else
                window.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao abrir IA Semântica PDF: {ex}");
            await MostrarErroAsync("IA Semântica PDF", ex);
        }
    }

    private async void AbrirGerenciadorCategorias_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = App.Services.GetRequiredService<CategoryManagerWindow>();

            if (TopLevel.GetTopLevel(this) is Window owner)
                await window.ShowDialog(owner);
            else
                window.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao abrir Categorias IA: {ex}");
            await MostrarErroAsync("Categorias IA", ex);
        }
    }

    private async void ConsolidarPastasSemelhantes_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not GerenciadorArquivosViewModel viewModel)
                throw new InvalidOperationException("O DataContext do GerenciadorArquivos não é GerenciadorArquivosViewModel.");

            var service = viewModel.CategoryFolderConsolidationService
                ?? throw new InvalidOperationException(
                    "O serviço de consolidação não foi injetado na GerenciadorArquivosViewModel. " +
                    "Verifique o registro de ICategoryFolderConsolidationService no contêiner de DI.");

            var window = new SimilarCategoryConsolidationWindow(service);
            if (TopLevel.GetTopLevel(this) is Window owner)
                await window.ShowDialog(owner);
            else
                window.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao abrir consolidação de pastas: {ex}");
            await MostrarErroAsync("Consolidação de pastas", ex);
        }
    }

    private async Task MostrarErroAsync(string titulo, Exception exception)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var dialog = new Window
        {
            Title = titulo,
            Width = 620,
            Height = 220,
            MinWidth = 620,
            MinHeight = 220,
            MaxWidth = 620,
            MaxHeight = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(18),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = exception.GetBaseException().Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "Fechar", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, MinWidth = 90 }
                }
            }
        };
        if (dialog.Content is StackPanel panel && panel.Children[1] is Button closeButton)
            closeButton.Click += (_, _) => dialog.Close();

        if (owner is not null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();
    }

    public void DropFolder(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files != null && files.Any() && files.First() is IStorageFolder folder &&
            DataContext is GerenciadorArquivosViewModel vm)
        {
            _ = vm.IrParaAsync(folder.Path.LocalPath);
        }
    }

    public void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is GerenciadorArquivosViewModel vm &&
            vm.ArquivoSelecionado?.IconKind == "Folder")
        {
            _ = vm.IrParaAsync(vm.ArquivoSelecionado.CaminhoCompleto);
        }
    }
}
