using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UglyToad.PdfPig;
using Tesseract;
using FileSyncX.Funcoes.GerenciadorArquivos;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading; // Necessário para atualizar a UI a partir da Task

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

    [ObservableProperty]
    private string _pastaAtual = string.Empty;

    // Função que será enviada para a classe estática para atualizar a linha do arquivo em tempo real
    private void ReportarProgresso(string caminhoCompleto, bool processando, string statusMsg)
    {
        var arquivo = ListaArquivos.FirstOrDefault(a => a.CaminhoCompleto == caminhoCompleto);
        if (arquivo != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                arquivo.IsProcessing = processando;
                if (!string.IsNullOrWhiteSpace(statusMsg))
                    arquivo.StatusSincronizacao = statusMsg;

                if (!processando)
                {
                    // Quando terminar, define o ícone como concluído
                    arquivo.IconKind = "CheckCircle";
                }
            });
        }
    }

    [RelayCommand]
    public async Task ConverterImagensParaPdfAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual)) return;

        await Task.Run(() =>
        {
            MoverEOrganizarArquivos.ConverterImagensParaPdfComOcr(PastaAtual);
        });

        Recarregar();
    }


    [RelayCommand]
    public async Task OrganizarPorOcrAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual)) return;

        // Roda em segundo plano para não travar a interface
        await Task.Run(() =>
        {
            // Passamos a função ReportarProgresso para controlar a UI (animação de loading e status)
            MoverEOrganizarArquivos.OrganizarPdfsPorSimilaridadeOcr(PastaAtual, ReportarProgresso);
        });

        Recarregar();
    }

    [RelayCommand]
    public async Task OrganizarPorLayoutVisualAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual)) return;

        await Task.Run(() =>
        {
            // Enviamos o callback de progresso para a função
            MoverEOrganizarArquivos.OrganizarPdfsPorLayoutVisual(PastaAtual, ReportarProgresso);
        });

        Recarregar();
    }

    public void LerArquivosDaPasta(string caminhoPasta)
    {
        if (string.IsNullOrWhiteSpace(caminhoPasta) || !Directory.Exists(caminhoPasta))
            return;

        PastaAtual = caminhoPasta;
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
                IconKind = "Folder",
                IsProcessing = false
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
                IconKind = ObterIconePorExtensao(arquivo.Extension),
                IsProcessing = false
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
        MoverEOrganizarArquivos.Executar(PastaAtual, ListaArquivos);
    }

    [RelayCommand]
    public void OrganizarPastas()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            MoverEOrganizarArquivos.OrganizarPastasPorCategoria(PastaAtual);
            Recarregar();
        }
    }

    [RelayCommand]
    public void OrganizarPastasComIA()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            MoverEOrganizarArquivos.OrganizarPastasDesconhecidasPorConteudo(PastaAtual);
            Recarregar();
        }
    }

    [RelayCommand]
    public void ReCategorizarPastas()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            MoverEOrganizarArquivos.ReCategorizarPastasExistentes(PastaAtual);
            Recarregar();
        }
    }

    [RelayCommand]
    public void OrganizarPorPalavraChave()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            MoverEOrganizarArquivos.OrganizarPastasPorPalavraChave(PastaAtual);
            Recarregar();
        }
    }

    [RelayCommand]
    public void OrganizarPorAnoMes()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            MoverEOrganizarArquivos.OrganizarPastasPorAnoMes(PastaAtual);
            Recarregar();
        }
    }

    [RelayCommand]
    private void Recarregar()
    {
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

    // Nova propriedade para controlar a animação na UI
    [ObservableProperty]
    private bool _isProcessing = false;
}