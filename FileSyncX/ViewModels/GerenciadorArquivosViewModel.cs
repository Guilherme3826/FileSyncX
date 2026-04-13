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

    [RelayCommand]
    public void ApagarItem()
    {
        if (ArquivoSelecionado == null) return;

        try
        {
            if (ArquivoSelecionado.IconKind == "Folder")
            {
                if (Directory.Exists(ArquivoSelecionado.CaminhoCompleto))
                {
                    // O 'true' indica que vai apagar a pasta e todos os arquivos dentro dela
                    Directory.Delete(ArquivoSelecionado.CaminhoCompleto, true);
                }
            }
            else
            {
                if (File.Exists(ArquivoSelecionado.CaminhoCompleto))
                {
                    File.Delete(ArquivoSelecionado.CaminhoCompleto);
                }
            }

            // Remove do Grid imediatamente após apagar no disco e atualiza a contagem
            ListaArquivos.Remove(ArquivoSelecionado);
            TotalArquivos = ListaArquivos.Count;
        }
        catch { }
    }



    // Construtor para carregar o último caminho ao iniciar a View
    public GerenciadorArquivosViewModel()
    {
        CarregarUltimaPasta();
    }

    // Sincroniza o TextBox sempre que o ViewModel muda de diretório internamente
    partial void OnPastaAtualChanged(string value)
    {
        CaminhoDigitado = value;
    }

    [RelayCommand]
    public void EscanearDiretorio()
    {
        IrPara(CaminhoDigitado);
    }

    // Método centralizado que gerencia o histórico e o JSON antes de carregar o grid
    public void IrPara(string caminho, bool viaHistorico = false)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho)) return;

        // Só empilha no histórico se estiver indo para uma pasta diferente
        if (!viaHistorico && !string.IsNullOrWhiteSpace(PastaAtual) && !PastaAtual.Equals(caminho, StringComparison.OrdinalIgnoreCase))
        {
            _historicoVoltar.Push(PastaAtual);
            _historicoAvancar.Clear(); // Limpa o "Avançar" se o usuário tomar um rumo novo
        }

        LerArquivosDaPasta(caminho);
        SalvarUltimaPasta(caminho);
    }

    [RelayCommand]
    public void Voltar()
    {
        if (_historicoVoltar.Count > 0)
        {
            _historicoAvancar.Push(PastaAtual);
            string anterior = _historicoVoltar.Pop();
            IrPara(anterior, true);
        }
    }

    [RelayCommand]
    public void Avancar()
    {
        if (_historicoAvancar.Count > 0)
        {
            _historicoVoltar.Push(PastaAtual);
            string proximo = _historicoAvancar.Pop();
            IrPara(proximo, true);
        }
    }

    // PERSISTÊNCIA EM JSON
    private void CarregarUltimaPasta()
    {
        try
        {
            string pathCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX", "ultima_pasta.json");
            if (File.Exists(pathCache))
            {
                string caminho = System.Text.Json.JsonSerializer.Deserialize<string>(File.ReadAllText(pathCache));
                if (!string.IsNullOrWhiteSpace(caminho) && Directory.Exists(caminho))
                {
                    IrPara(caminho);
                }
            }
        }
        catch { }
    }

    private void SalvarUltimaPasta(string caminho)
    {
        try
        {
            string dirCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
            if (!Directory.Exists(dirCache)) Directory.CreateDirectory(dirCache);

            string pathCache = Path.Combine(dirCache, "ultima_pasta.json");
            File.WriteAllText(pathCache, System.Text.Json.JsonSerializer.Serialize(caminho));
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
    public void RemoverItensSelecionados(object parameter)
    {
        // O Avalonia passa os SelectedItems como um IList
        if (parameter is System.Collections.IList itens)
        {
            // Convertemos para uma lista estática para não haver erro de modificação de coleção durante o loop
            var arquivosParaRemover = itens.Cast<ArquivoModel>().ToList();

            if (arquivosParaRemover.Count == 0) return;

            foreach (var arquivo in arquivosParaRemover)
            {
                try
                {
                    if (arquivo.IconKind == "Folder")
                    {
                        if (Directory.Exists(arquivo.CaminhoCompleto))
                        {
                            // Apaga pasta e subpastas
                            Directory.Delete(arquivo.CaminhoCompleto, true);
                        }
                    }
                    else
                    {
                        if (File.Exists(arquivo.CaminhoCompleto))
                        {
                            File.Delete(arquivo.CaminhoCompleto);
                        }
                    }

                    // Remove da interface
                    ListaArquivos.Remove(arquivo);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erro ao remover {arquivo.Nome}: {ex.Message}");
                }
            }

            TotalArquivos = ListaArquivos.Count;
            ArquivoSelecionado = null;
        }
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
            // 1. Obtém a quantidade de arquivos e o peso total da pasta de forma segura
            ObterTamanhoEContagemPastaSegura(pasta, out long tamanhoPastaBytes, out int qtdItens);

            ListaArquivos.Add(new ArquivoModel
            {
                Nome = pasta.Name,
                Extensao = $"Pasta ({qtdItens} itens)", // Mostra a contagem na coluna de tipo
                QuantidadeItens = qtdItens,
                TamanhoBytes = tamanhoPastaBytes,       // Guarda o valor real em bytes para ordenar
                TamanhoFormatado = FormatarTamanho(tamanhoPastaBytes), // Formata para GB, MB, KB
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
            ListaArquivos.Add(new ArquivoModel
            {
                Nome = arquivo.Name,
                Extensao = arquivo.Extension.ToUpper(),
                TamanhoBytes = arquivo.Length, // Salva para permitir a ordenação
                TamanhoFormatado = FormatarTamanho(arquivo.Length),
                DataModificacao = arquivo.LastWriteTime,
                StatusSincronizacao = "Aguardando",
                CaminhoCompleto = arquivo.FullName,
                IconKind = ObterIconePorExtensao(arquivo.Extension),
                IsProcessing = false,
                Conteudo = null
            });
        }

        TotalArquivos = ListaArquivos.Count;
    }

    // NOVO: Método de varredura recursiva protegida contra pastas de sistema/bloqueadas
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
                // Soma os arquivos da pasta atual
                var arquivos = pastaAtual.GetFiles();
                foreach (var arq in arquivos)
                {
                    tamanhoTotal += arq.Length;
                    qtdItens++;
                }

                // Enfileira subpastas para verificação
                var subPastas = pastaAtual.GetDirectories();
                foreach (var sub in subPastas)
                {
                    pastasPendentes.Enqueue(sub);
                }
            }
            catch
            {
                // Catch silencioso: Se o Windows barrar o acesso a uma pasta interna, ele ignora só ela e continua a contagem do resto
            }
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

    [ObservableProperty]
    private long _tamanhoBytes;

    [ObservableProperty]
    private int _quantidadeItens;
}