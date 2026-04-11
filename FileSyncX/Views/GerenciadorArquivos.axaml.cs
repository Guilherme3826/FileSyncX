using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FileSyncX.ViewModels;
using System.Linq;

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

    private void DropFolder(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();

        if (files != null && files.Any())
        {
            var firstItem = files.First();

            if (firstItem is IStorageFolder folder)
            {
                string caminhoArrastado = folder.Path.LocalPath;

                // PASSO 1: Envia o caminho para o ViewModel processar
                if (DataContext is GerenciadorArquivosViewModel vm)
                {
                    vm.LerArquivosDaPasta(caminhoArrastado);
                }
            }
        }
    }
}