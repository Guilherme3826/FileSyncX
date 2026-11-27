using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FileSyncX.ViewModels;
using FileSyncX.Views;
using Microsoft.Extensions.DependencyInjection;

namespace FileSyncX.Views.UserControls;

public partial class GerenciadorArquivos : UserControl
{
    public GerenciadorArquivos(GerenciadorArquivosViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, DropFolder);
    }

    public GerenciadorArquivos()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, DropFolder);
    }

    public async void AbrirProcessamentoSemantico_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = App.Services.GetRequiredService<PdfBatchProcessingWindow>();

        // Aproveita a pasta atualmente aberta no Gerenciador de Arquivos.
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

    public async void AbrirGerenciadorCategorias_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = App.Services.GetRequiredService<CategoryManagerWindow>();

        if (TopLevel.GetTopLevel(this) is Window owner)
            await window.ShowDialog(owner);
        else
            window.Show();
    }

    public void DropFolder(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files != null && files.Any())
        {
            var firstItem = files.First();
            if (firstItem is IStorageFolder folder)
            {
                string caminhoArrastado = folder.Path.LocalPath;
                if (DataContext is GerenciadorArquivosViewModel vm)
                    _ = vm.IrParaAsync(caminhoArrastado);
            }
        }
    }

    public void DataGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is GerenciadorArquivosViewModel vm && vm.ArquivoSelecionado != null)
        {
            if (vm.ArquivoSelecionado.IconKind == "Folder")
                _ = vm.IrParaAsync(vm.ArquivoSelecionado.CaminhoCompleto);
        }
    }
}
