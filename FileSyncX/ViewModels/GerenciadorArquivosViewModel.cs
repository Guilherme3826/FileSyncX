using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncX.Funcoes.GerenciadorArquivos;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks; // Necessário para Task

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

    // Propriedade vital para guardar a raiz de onde os arquivos vieram
    [ObservableProperty]
    private string _pastaAtual = string.Empty;

    public void LerArquivosDaPasta(string caminhoPasta)
    {
        if (string.IsNullOrWhiteSpace(caminhoPasta) || !Directory.Exists(caminhoPasta))
            return;

        PastaAtual = caminhoPasta; // Salva o estado da pasta em uso
        ListaArquivos.Clear();

        var diretorio = new DirectoryInfo(caminhoPasta);

        var pastasNoDisco = diretorio.GetDirectories("*", SearchOption.TopDirectoryOnly);
        foreach (var pasta in pastasNoDisco)
        {
            ListaArquivos.Add(new ArquivoModel
            {
                Nome = pasta.Name,
                Extensao = "Pasta de Arquivos",
                TamanhoFormatado = "-",
                DataModificacao = pasta.LastWriteTime,
                StatusSincronizacao = "Aguardando",
                CaminhoCompleto = pasta.FullName,
                IconKind = "Folder"
            });
        }

        var arquivosNoDisco = diretorio.GetFiles("*", SearchOption.TopDirectoryOnly);
        foreach (var arquivo in arquivosNoDisco)
        {
            ListaArquivos.Add(new ArquivoModel
            {
                Nome = arquivo.Name,
                Extensao = arquivo.Extension.ToUpper(),
                TamanhoFormatado = FormatarTamanho(arquivo.Length),
                DataModificacao = arquivo.LastWriteTime,
                StatusSincronizacao = "Aguardando",
                CaminhoCompleto = arquivo.FullName,
                IconKind = ObterIconePorExtensao(arquivo.Extension)
            });
        }

        TotalArquivos = ListaArquivos.Count;
    }

    private string ObterIconePorExtensao(string extensao)
    {
        return extensao.ToLower() switch
        {
            ".pdf" => "FilePdfBox",
            ".doc" or ".docx" => "FileWordBox",
            ".xls" or ".xlsx" or ".csv" => "FileExcelBox",
            ".ppt" or ".pptx" => "FilePowerpointBox",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".bmp" => "FileImageBox",
            ".mp4" or ".avi" or ".mkv" or ".mov" => "FileVideoOutline",
            ".mp3" or ".wav" or ".flac" => "FileMusicOutline",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "ZipBox",
            ".cs" or ".axaml" or ".json" or ".xml" or ".html" or ".css" or ".js" or ".sql" => "FileCodeOutline",
            ".txt" or ".log" => "FileDocumentOutline",
            ".exe" or ".msi" => "Application",
            _ => "FileOutline"
        };
    }

    private string FormatarTamanho(long bytes)
    {
        string[] tamanhos = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0) return "0 B";

        int magnitude = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        double valorAjustado = Math.Round(bytes / Math.Pow(1024, magnitude), 1);

        return $"{valorAjustado} {tamanhos[magnitude]}";
    }

    [RelayCommand]
    private void SelecionarPasta() { }

    [RelayCommand]
    public void Sincronizar()
    {
        // Intercepta e dispara a organização dos arquivos
        MoverEOrganizarArquivos.Executar(PastaAtual, ListaArquivos);
    }

    [RelayCommand]
    public void OrganizarPastas()
    {
        // Chama a função para agrupar as pastas por categoria com base no JSON
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            MoverEOrganizarArquivos.OrganizarPastasPorCategoria(PastaAtual);
            Recarregar(); // Atualiza a tela imediatamente após mover as pastas
        }
    }

    [RelayCommand]
    public async Task OrganizarPastasComIA()
    {
        // Chama a nova função baseada em Inteligência Artificial
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            await MoverEOrganizarArquivos.OrganizarPastasDesconhecidasComIAAsync(PastaAtual);
            Recarregar(); // Atualiza a interface para refletir as pastas movidas pela IA
        }
    }

    [RelayCommand]
    private void Recarregar()
    {
        // Lê os arquivos novamente com base na pasta que já está carregada
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            LerArquivosDaPasta(PastaAtual);
        }
    }

    [RelayCommand]
    private void ExportarRelatorio() { }
}

public partial class ArquivoModel : ObservableObject
{
    [ObservableProperty]
    private string _nome = string.Empty;

    [ObservableProperty]
    private string _extensao = string.Empty;

    [ObservableProperty]
    private string _tamanhoFormatado = string.Empty;

    [ObservableProperty]
    private System.DateTime _dataModificacao;

    [ObservableProperty]
    private string _statusSincronizacao = string.Empty;

    [ObservableProperty]
    private string _caminhoCompleto = string.Empty;

    [ObservableProperty]
    private string _iconKind = "FileOutline";
}