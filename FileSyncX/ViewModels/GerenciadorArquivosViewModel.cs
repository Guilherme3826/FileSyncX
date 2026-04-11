using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace FileSyncX.ViewModels;

public partial class GerenciadorArquivosViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _termoPesquisa = string.Empty;

    [ObservableProperty]
    private int _totalArquivos = 0;

    [ObservableProperty]
    private ObservableCollection<ArquivoModel> _listaArquivos = new();

    [ObservableProperty]
    private ArquivoModel? _arquivoSelecionado;

    [RelayCommand]
    private void SelecionarPasta() { }

    [RelayCommand]
    private void Sincronizar() { }

    [RelayCommand]
    private void Recarregar() { }

    [RelayCommand]
    private void ExportarRelatorio() { }
}

// Modelo de dados para preencher o DataGrid
public class ArquivoModel
{
    public string Nome { get; set; } = string.Empty;
    public string Extensao { get; set; } = string.Empty;
    public string TamanhoFormatado { get; set; } = string.Empty;
    public System.DateTime DataModificacao { get; set; }
    public string StatusSincronizacao { get; set; } = string.Empty;
    public string CaminhoCompleto { get; set; } = string.Empty;
}