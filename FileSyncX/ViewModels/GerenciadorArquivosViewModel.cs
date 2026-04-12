using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UglyToad.PdfPig;
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
using Avalonia.Threading;

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

    [ObservableProperty]
    private bool _isPaused = false;

    [RelayCommand]
    public void PausarRetomar()
    {
        IsPaused = !IsPaused;
    }

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

                if (!processando && statusMsg != "PAUSADO - Caches salvos.")
                {
                    arquivo.IconKind = "CheckCircle";
                }
            });
        }
    }

    [RelayCommand]
    public async Task EmbutirOcrNosPdfsAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual)) return;

        // Executamos em uma Task para manter a UI responsiva enquanto o OCR processa na GPU/CPU
        await Task.Run(async () =>
        {
            await ProcessadorOcrPdf.InjetarOcrEmPdfsAsync(PastaAtual, () => IsPaused, (caminho, status) =>
            {
                // Define se o arquivo ainda está em processamento baseando-se nos status finalizadores
                // Adicionado "concluído" e "PAUSADO" para cobrir as novas mensagens da função
                bool isProcessing = !(status.Contains("sucesso") ||
                                      status.Contains("Erro") ||
                                      status.Contains("Ignorado") ||
                                      status.Contains("concluído") ||
                                      status.Contains("PAUSADO"));

                ReportarProgresso(caminho, isProcessing, status);
            });
        });

        Recarregar();
    }

    [RelayCommand]
    public async Task ConverterImagensParaPdfAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || ListaArquivos.Count == 0) return;

        var arquivosSnap = ListaArquivos.ToList();

        await Task.Run(async () =>
        {
            await MoverEOrganizarArquivos.ConverterImagensParaPdfComOcrAsync(PastaAtual, arquivosSnap, () => IsPaused, ReportarProgresso);
        });

        Recarregar();
    }
    [RelayCommand]
    public async Task AgruparPdfsPorPrefixoAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || ListaArquivos.Count == 0) return;

        var arquivosSnap = ListaArquivos.ToList();

        await Task.Run(async () =>
        {
            await Tools.AgruparPdfsPorPrefixoAsync(PastaAtual, arquivosSnap, () => IsPaused, (caminho, isProcessing, status) =>
            {
                // Usando a mesma tratativa de loading visual
                ReportarProgresso(caminho, isProcessing, status);
            });
        });

        Recarregar();
    }

    [RelayCommand]
    public async Task RemoverDuplicatasAsync()
    {
        // Garante que existe uma pasta selecionada válida
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual)) return;

        // Executa a varredura e limpeza em uma thread de segundo plano (GPU/CPU)
        await Task.Run(() =>
        {
            // O segundo parâmetro 'true' indica que vai limpar as subpastas também
            DeduplicadorArquivos.RemoverDuplicatasMaisAntigas(PastaAtual, true);
        });

        // Atualiza o Grid na tela com os arquivos sobreviventes
        Recarregar();
    }


    [RelayCommand]
    public async Task OrganizarPorOcrAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual) || ListaArquivos.Count == 0) return;

        var arquivosSnap = ListaArquivos.ToList();

        await Task.Run(async () =>
        {
            await MoverEOrganizarArquivos.OrganizarPdfsPorSimilaridadeOcrAsync(PastaAtual, arquivosSnap, () => IsPaused, ReportarProgresso);
        });

        Recarregar();
    }

    [RelayCommand]
    public async Task OrganizarPorLayoutVisualAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual) || ListaArquivos.Count == 0) return;

        var arquivosSnap = ListaArquivos.ToList();

        await Task.Run(async () =>
        {
            await MoverEOrganizarArquivos.OrganizarPdfsPorLayoutVisualAsync(PastaAtual, arquivosSnap, () => IsPaused, ReportarProgresso);
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
                IsProcessing = false,
                Conteudo = null
            });
        }

        var arquivosNoDisco = diretorio.GetFiles("*", SearchOption.TopDirectoryOnly);
        foreach (var arquivo in arquivosNoDisco)
        {
            byte[] conteudoLido = null;
            try
            {
                conteudoLido = File.ReadAllBytes(arquivo.FullName);
            }
            catch { }

            ListaArquivos.Add(new ArquivoModel
            {
                Nome = arquivo.Name,
                Extensao = arquivo.Extension.ToUpper(),
                TamanhoFormatado = FormatarTamanho(arquivo.Length),
                DataModificacao = arquivo.LastWriteTime,
                StatusSincronizacao = "Lido na RAM",
                CaminhoCompleto = arquivo.FullName,
                IconKind = ObterIconePorExtensao(arquivo.Extension),
                IsProcessing = false,
                Conteudo = conteudoLido
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
        double doubleValor = Math.Round(bytes / Math.Pow(1024, magnitude), 1);

        return $"{doubleValor} {tamanhos[magnitude]}";
    }

    [RelayCommand]
    private void SelecionarPasta() { }

    [RelayCommand]
    public void Sincronizar()
    {
        if (ListaArquivos.Count > 0)
        {
            var arquivosSnap = ListaArquivos.ToList();
            MoverEOrganizarArquivos.Executar(PastaAtual, arquivosSnap);
            Recarregar();
        }
    }

    [RelayCommand]
    public void OrganizarPastas()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            MoverEOrganizarArquivos.OrganizarPastasPorCategoria(PastaAtual, ListaArquivos.ToList());
            Recarregar();
        }
    }

    [RelayCommand]
    public void OrganizarPastasComIA()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            MoverEOrganizarArquivos.OrganizarPastasDesconhecidasPorConteudo(PastaAtual, ListaArquivos.ToList());
            Recarregar();
        }
    }

    [RelayCommand]
    public void ReCategorizarPastas()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            MoverEOrganizarArquivos.ReCategorizarPastasExistentes(PastaAtual, ListaArquivos.ToList());
            Recarregar();
        }
    }

    [RelayCommand]
    public void OrganizarPorPalavraChave()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            MoverEOrganizarArquivos.OrganizarPastasPorPalavraChave(PastaAtual, ListaArquivos.ToList());
            Recarregar();
        }
    }

    [RelayCommand]
    public void OrganizarPorAnoMes()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            MoverEOrganizarArquivos.OrganizarPastasPorAnoMes(PastaAtual, ListaArquivos.ToList());
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

    [ObservableProperty]
    private bool _isProcessing = false;

    public byte[]? Conteudo { get; set; }
}