using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System.Linq;

namespace FileSyncX.Views.UserControls;

public partial class GerenciadorArquivos : UserControl
{
    public GerenciadorArquivos()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, DropFolder);
    }

    private void DropFolder(object? sender, DragEventArgs e)
    {
        // Usando a API correta identificada por você! (Com os parênteses do método)
        var files = e.DataTransfer.TryGetFiles();

        if (files != null && files.Any())
        {
            // Pega o primeiro item arrastado
            var firstItem = files.First();

            // Valida se o que foi arrastado é realmente uma pasta (IStorageFolder)
            if (firstItem is IStorageFolder folder)
            {
                // Extrai o caminho absoluto da pasta no Windows
                string caminhoArrastado = folder.Path.LocalPath;

                // TODO: Enviar caminhoArrastado para o ViewModel
            }
        }
    }
}