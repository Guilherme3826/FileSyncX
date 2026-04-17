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
using Avalonia.Input.Platform;

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

    [ObservableProperty]
    private string _caminhoDigitado = string.Empty;

    private Stack<string> _historicoVoltar = new();
    private Stack<string> _historicoAvancar = new();


    // Propriedades computadas para mostrar/ocultar opções específicas
    public bool IsArquivoSelecionado => ArquivoSelecionado != null && ArquivoSelecionado.IconKind != "Folder";
    public bool IsPastaSelecionada => ArquivoSelecionado != null && ArquivoSelecionado.IconKind == "Folder";

    // Dispara a atualização dos botões do menu assim que o usuário clica em uma linha
    partial void OnArquivoSelecionadoChanged(ArquivoModel? value)
    {
        OnPropertyChanged(nameof(IsArquivoSelecionado));
        OnPropertyChanged(nameof(IsPastaSelecionada));
    }

    [RelayCommand]
    public async Task MoverCategoriasAsync()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            IsBusy = true;
            try
            {
                var arquivosSnap = ListaArquivos.ToList();
                await Task.Run(() => MoverEOrganizarArquivos.MoverCategorias(PastaAtual, arquivosSnap));
                await RecarregarAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    public void AbrirItem()
    {
        if (ArquivoSelecionado == null) return;

        try
        {
            // O UseShellExecute = true delega para o Windows a tarefa de abrir o arquivo 
            // no programa padrão (ex: PDF no Edge) ou a pasta no Windows Explorer.
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ArquivoSelecionado.CaminhoCompleto,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch { }
    }

    [RelayCommand]
    public async Task CopiarNomeAsync()
    {
        if (ArquivoSelecionado == null) return;

        try
        {
            // Acessa a área de transferência nativa do Avalonia
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow?.Clipboard != null)
                {
                    await desktop.MainWindow.Clipboard.SetTextAsync(ArquivoSelecionado.Nome);
                }
            }
        }
        catch { }
    }
    // Construtor para carregar o último caminho ao iniciar a View
    [ObservableProperty]
    private bool _isBusy;

    // Construtor
    public GerenciadorArquivosViewModel()
    {
        _ = CarregarUltimaPastaAsync();
    }

    partial void OnPastaAtualChanged(string value)
    {
        CaminhoDigitado = value;
    }

    [RelayCommand]
    public async Task EscanearDiretorioAsync()
    {
        await IrParaAsync(CaminhoDigitado);
    }

    // Método central refatorado para assíncrono
    public async Task IrParaAsync(string caminho, bool viaHistorico = false)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho)) return;

        if (!viaHistorico && !string.IsNullOrWhiteSpace(PastaAtual) && !PastaAtual.Equals(caminho, StringComparison.OrdinalIgnoreCase))
        {
            _historicoVoltar.Push(PastaAtual);
            _historicoAvancar.Clear();
        }

        await LerArquivosDaPastaAsync(caminho);
        await SalvarUltimaPastaAsync(caminho);
    }

    [RelayCommand]
    public async Task VoltarAsync()
    {
        if (_historicoVoltar.Count > 0)
        {
            _historicoAvancar.Push(PastaAtual);
            string anterior = _historicoVoltar.Pop();
            await IrParaAsync(anterior, true);
        }
    }

    [RelayCommand]
    public async Task AvancarAsync()
    {
        if (_historicoAvancar.Count > 0)
        {
            _historicoVoltar.Push(PastaAtual);
            string proximo = _historicoAvancar.Pop();
            await IrParaAsync(proximo, true);
        }
    }

    // PERSISTÊNCIA EM JSON ASSÍNCRONA
    private async Task CarregarUltimaPastaAsync()
    {
        try
        {
            string pathCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX", "ultima_pasta.json");
            if (File.Exists(pathCache))
            {
                string json = await File.ReadAllTextAsync(pathCache);
                string caminho = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(caminho) && Directory.Exists(caminho))
                {
                    await IrParaAsync(caminho);
                }
            }
        }
        catch { }
    }

    private async Task SalvarUltimaPastaAsync(string caminho)
    {
        try
        {
            string dirCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
            if (!Directory.Exists(dirCache)) Directory.CreateDirectory(dirCache);

            string pathCache = Path.Combine(dirCache, "ultima_pasta.json");
            string json = System.Text.Json.JsonSerializer.Serialize(caminho);
            await File.WriteAllTextAsync(pathCache, json);
        }
        catch { }
    }

    [RelayCommand]
    public void PausarRetomar()
    {
        IsPaused = !IsPaused;
    }

    [RelayCommand]
    public void LimparGrid()
    {
        ListaArquivos.Clear();
        PastaAtual = string.Empty;
        TotalArquivos = 0;
        ArquivoSelecionado = null;
    }

    [RelayCommand]
    public async Task ApagarItemAsync()
    {
        if (ArquivoSelecionado == null) return;

        IsBusy = true;
        try
        {
            var arquivo = ArquivoSelecionado;
            await Task.Run(() =>
            {
                if (arquivo.IconKind == "Folder")
                {
                    if (Directory.Exists(arquivo.CaminhoCompleto))
                        Directory.Delete(arquivo.CaminhoCompleto, true);
                }
                else
                {
                    if (File.Exists(arquivo.CaminhoCompleto))
                        File.Delete(arquivo.CaminhoCompleto);
                }
            });

            ListaArquivos.Remove(arquivo);
            TotalArquivos = ListaArquivos.Count;
        }
        catch { }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RemoverItensSelecionadosAsync(object parameter)
    {
        if (parameter is System.Collections.IList itens)
        {
            var arquivosParaRemover = itens.Cast<ArquivoModel>().ToList();
            if (arquivosParaRemover.Count == 0) return;

            IsBusy = true;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var arquivo in arquivosParaRemover)
                    {
                        try
                        {
                            if (arquivo.IconKind == "Folder")
                            {
                                if (Directory.Exists(arquivo.CaminhoCompleto))
                                    Directory.Delete(arquivo.CaminhoCompleto, true);
                            }
                            else
                            {
                                if (File.Exists(arquivo.CaminhoCompleto))
                                    File.Delete(arquivo.CaminhoCompleto);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Erro ao remover {arquivo.Nome}: {ex.Message}");
                        }
                    }
                });

                // Remove da interface na thread principal
                foreach (var arq in arquivosParaRemover)
                {
                    ListaArquivos.Remove(arq);
                }

                TotalArquivos = ListaArquivos.Count;
                ArquivoSelecionado = null;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    // LEITURA PESADA TRANSFERIDA PARA BACKGROUND (Evita o travamento)
    public async Task LerArquivosDaPastaAsync(string caminhoPasta)
    {
        if (string.IsNullOrWhiteSpace(caminhoPasta) || !Directory.Exists(caminhoPasta))
            return;

        IsBusy = true; // Mostra a animação
        PastaAtual = caminhoPasta;
        ListaArquivos.Clear();

        try
        {
            var tempList = new List<ArquivoModel>();

            await Task.Run(() =>
            {
                var diretorio = new DirectoryInfo(caminhoPasta);

                var pastasNoDisco = diretorio.GetDirectories("*", SearchOption.TopDirectoryOnly);
                foreach (var pasta in pastasNoDisco)
                {
                    ObterTamanhoEContagemPastaSegura(pasta, out long tamanhoPastaBytes, out int qtdItens);

                    tempList.Add(new ArquivoModel
                    {
                        Nome = pasta.Name,
                        Extensao = $"Pasta ({qtdItens} itens)",
                        QuantidadeItens = qtdItens,
                        TamanhoBytes = tamanhoPastaBytes,
                        TamanhoFormatado = FormatarTamanho(tamanhoPastaBytes),
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
                    tempList.Add(new ArquivoModel
                    {
                        Nome = arquivo.Name,
                        Extensao = arquivo.Extension.ToUpper(),
                        TamanhoBytes = arquivo.Length,
                        TamanhoFormatado = FormatarTamanho(arquivo.Length),
                        DataModificacao = arquivo.LastWriteTime,
                        StatusSincronizacao = "Aguardando",
                        CaminhoCompleto = arquivo.FullName,
                        IconKind = ObterIconePorExtensao(arquivo.Extension),
                        IsProcessing = false,
                        Conteudo = null
                    });
                }
            });

            // Ordena a lista em memória do maior para o menor (em Bytes) antes de enviar para a tela
            var listaOrdenada = tempList.OrderByDescending(arq => arq.TamanhoBytes).ToList();

            // Despeja a lista já classificada na UI
            foreach (var item in listaOrdenada)
            {
                ListaArquivos.Add(item);
            }
            TotalArquivos = ListaArquivos.Count;
        }
        finally
        {
            IsBusy = false; // Esconde a animação
        }
    }

    [RelayCommand]
    public async Task SincronizarAsync()
    {
        if (ListaArquivos.Count > 0)
        {
            IsBusy = true;
            try
            {
                var arquivosSnap = ListaArquivos.ToList();
                await Task.Run(() => MoverEOrganizarArquivos.Executar(PastaAtual, arquivosSnap));
                await RecarregarAsync();
            }
            finally { IsBusy = false; }
        }
    }

    [RelayCommand]
    public async Task OrganizarPastasAsync()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            IsBusy = true;
            try
            {
                var arquivosSnap = ListaArquivos.ToList();
                await Task.Run(() => MoverEOrganizarArquivos.OrganizarPastasPorCategoria(PastaAtual, arquivosSnap));
                await RecarregarAsync();
            }
            finally { IsBusy = false; }
        }
    }

    [RelayCommand]
    public async Task OrganizarPastasComIAAsync()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            IsBusy = true;
            try
            {
                var arquivosSnap = ListaArquivos.ToList();
                await Task.Run(() => MoverEOrganizarArquivos.OrganizarPastasDesconhecidasPorConteudo(PastaAtual, arquivosSnap));
                await RecarregarAsync();
            }
            finally { IsBusy = false; }
        }
    }

    [RelayCommand]
    public async Task ReCategorizarPastasAsync()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            IsBusy = true;
            try
            {
                var arquivosSnap = ListaArquivos.ToList();
                await Task.Run(() => MoverEOrganizarArquivos.ReCategorizarPastasExistentes(PastaAtual, arquivosSnap));
                await RecarregarAsync();
            }
            finally { IsBusy = false; }
        }
    }

    [RelayCommand]
    public async Task OrganizarPorPalavraChaveAsync()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            IsBusy = true;
            try
            {
                var arquivosSnap = ListaArquivos.ToList();
                await Task.Run(() => MoverEOrganizarArquivos.OrganizarPastasPorPalavraChave(PastaAtual, arquivosSnap));
                await RecarregarAsync();
            }
            finally { IsBusy = false; }
        }
    }

    [RelayCommand]
    public async Task OrganizarPorAnoMesAsync()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual) && ListaArquivos.Count > 0)
        {
            IsBusy = true;
            try
            {
                var arquivosSnap = ListaArquivos.ToList();
                await Task.Run(() => MoverEOrganizarArquivos.OrganizarPastasPorAnoMes(PastaAtual, arquivosSnap));
                await RecarregarAsync();
            }
            finally { IsBusy = false; }
        }
    }

    [RelayCommand]
    public async Task RecarregarAsync()
    {
        if (!string.IsNullOrWhiteSpace(PastaAtual))
        {
            await LerArquivosDaPastaAsync(PastaAtual);
        }
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

        IsBusy = true;
        try
        {
            await Task.Run(async () =>
            {
                await ProcessadorOcrPdf.InjetarOcrEmPdfsAsync(PastaAtual, () => IsPaused, (caminho, status) =>
                {
                    bool isProcessing = !(status.Contains("sucesso") ||
                                          status.Contains("Erro") ||
                                          status.Contains("Ignorado") ||
                                          status.Contains("concluído") ||
                                          status.Contains("PAUSADO"));

                    ReportarProgresso(caminho, isProcessing, status);
                });
            });
            await RecarregarAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task ConverterImagensParaPdfAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || ListaArquivos.Count == 0) return;

        IsBusy = true;
        try
        {
            var arquivosSnap = ListaArquivos.ToList();
            await Task.Run(async () =>
            {
                await MoverEOrganizarArquivos.ConverterImagensParaPdfComOcrAsync(PastaAtual, arquivosSnap, () => IsPaused, ReportarProgresso);
            });
            await RecarregarAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task AgruparPdfsPorPrefixoAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || ListaArquivos.Count == 0) return;

        IsBusy = true;
        try
        {
            var arquivosSnap = ListaArquivos.ToList();
            await Task.Run(async () =>
            {
                await Tools.AgruparPdfsPorPrefixoAsync(PastaAtual, arquivosSnap, () => IsPaused, (caminho, isProcessing, status) =>
                {
                    ReportarProgresso(caminho, isProcessing, status);
                });
            });
            await RecarregarAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RemoverDuplicatasAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual)) return;

        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                DeduplicadorArquivos.RemoverDuplicatasMaisAntigas(PastaAtual, true);
            });
            await RecarregarAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task OrganizarPorOcrAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual) || ListaArquivos.Count == 0) return;

        IsBusy = true;
        try
        {
            var arquivosSnap = ListaArquivos.ToList();
            await Task.Run(async () =>
            {
                await MoverEOrganizarArquivos.OrganizarPdfsPorSimilaridadeOcrAsync(PastaAtual, arquivosSnap, () => IsPaused, ReportarProgresso);
            });
            await RecarregarAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task OrganizarPorLayoutVisualAsync()
    {
        if (string.IsNullOrWhiteSpace(PastaAtual) || !Directory.Exists(PastaAtual) || ListaArquivos.Count == 0) return;

        IsBusy = true;
        try
        {
            var arquivosSnap = ListaArquivos.ToList();
            await Task.Run(async () =>
            {
                await MoverEOrganizarArquivos.OrganizarPdfsPorLayoutVisualAsync(PastaAtual, arquivosSnap, () => IsPaused, ReportarProgresso);
            });
            await RecarregarAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ExportarRelatorio() { }

    private void ObterTamanhoEContagemPastaSegura(DirectoryInfo dir, out long tamanhoTotal, out int qtdItens)
    {
        tamanhoTotal = 0;
        qtdItens = 0;
        var pastasPendentes = new Queue<DirectoryInfo>();
        pastasPendentes.Enqueue(dir);

        while (pastasPendentes.Count > 0)
        {
            var pastaAtual = pastasPendentes.Dequeue();
            try
            {
                var arquivos = pastaAtual.GetFiles();
                foreach (var arq in arquivos)
                {
                    tamanhoTotal += arq.Length;
                    qtdItens++;
                }

                var subPastas = pastaAtual.GetDirectories();
                foreach (var sub in subPastas)
                {
                    pastasPendentes.Enqueue(sub);
                }
            }
            catch { }
        }
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

    [ObservableProperty]
    private long _tamanhoBytes;

    [ObservableProperty]
    private int _quantidadeItens;
}