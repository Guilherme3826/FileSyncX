using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

namespace FileSyncX.ViewModels;

public partial class PdfBatchProcessingViewModel : ViewModelBase
{
    private readonly IPdfBatchProcessingService _batchProcessingService;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string _pastaOrigem = string.Empty;

    [ObservableProperty]
    private bool _incluirSubpastas = true;

    [ObservableProperty]
    private bool _moverSeClassificado = true;

    [ObservableProperty]
    private double _similaridadeMinima = 0.75;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _totalEncontrado;

    [ObservableProperty]
    private int _processados;

    [ObservableProperty]
    private int _sucesso;

    [ObservableProperty]
    private int _falhas;

    [ObservableProperty]
    private int _jaIndexados;

    [ObservableProperty]
    private int _classificados;

    [ObservableProperty]
    private int _paraRevisao;

    [ObservableProperty]
    private int _naoClassificados;

    [ObservableProperty]
    private int _movidos;

    [ObservableProperty]
    private double _percentualProgresso;

    [ObservableProperty]
    private string _arquivoAtual = string.Empty;

    [ObservableProperty]
    private string _mensagemStatus = "Selecione uma pasta para começar.";

    [ObservableProperty]
    private string _avisosTexto = string.Empty;

    public ObservableCollection<PdfBatchFileResult> Resultados { get; } = new();

    public bool PodeIniciar =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(PastaOrigem) &&
        Directory.Exists(PastaOrigem);

    public bool PodeCancelar => IsBusy;

    public string SimilaridadeFormatada => SimilaridadeMinima.ToString("P0");

    public bool TemAvisos => !string.IsNullOrWhiteSpace(AvisosTexto);

    public PdfBatchProcessingViewModel(
        IPdfBatchProcessingService batchProcessingService)
    {
        _batchProcessingService = batchProcessingService
            ?? throw new ArgumentNullException(nameof(batchProcessingService));
    }

    partial void OnPastaOrigemChanged(string value)
    {
        OnPropertyChanged(nameof(PodeIniciar));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(PodeIniciar));
        OnPropertyChanged(nameof(PodeCancelar));
    }

    partial void OnSimilaridadeMinimaChanged(double value)
    {
        OnPropertyChanged(nameof(SimilaridadeFormatada));
    }

    partial void OnAvisosTextoChanged(string value)
    {
        OnPropertyChanged(nameof(TemAvisos));
    }

    [RelayCommand]
    private async Task IniciarProcessamentoAsync()
    {
        if (IsBusy)
            return;

        if (string.IsNullOrWhiteSpace(PastaOrigem) || !Directory.Exists(PastaOrigem))
        {
            MensagemStatus = "Informe uma pasta de origem válida.";
            return;
        }

        LimparResultadosAnteriores();

        _cancellationTokenSource = new CancellationTokenSource();
        IsBusy = true;
        MensagemStatus = "Preparando processamento...";

        var progresso = new UiProgress<PdfBatchProgress>(AtualizarProgresso);

        try
        {
            PdfBatchProcessingResult resultado =
                await _batchProcessingService.ProcessarPastaAsync(
                    caminhoPasta: PastaOrigem,
                    incluirSubpastas: IncluirSubpastas,
                    similaridadeMinima: SimilaridadeMinima,
                    moverSeClassificado: MoverSeClassificado,
                    progresso: progresso,
                    cancellationToken: _cancellationTokenSource.Token);

            foreach (PdfBatchFileResult arquivo in resultado.Files)
                Resultados.Add(arquivo);

            TotalEncontrado = resultado.TotalFound;
            Processados = resultado.Processed;
            Sucesso = resultado.Succeeded;
            Falhas = resultado.Failed;
            JaIndexados = resultado.AlreadyIndexed;
            Classificados = resultado.Classified;
            ParaRevisao = resultado.NeedsReview;
            NaoClassificados = resultado.Unclassified;
            Movidos = resultado.Moved;
            PercentualProgresso = TotalEncontrado == 0
                ? 100
                : Math.Clamp(Processados * 100d / TotalEncontrado, 0, 100);

            AvisosTexto = resultado.Warnings.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine,
                    resultado.Warnings.Select(w => $"{w.FolderPath}: {w.Message}"));

            MensagemStatus = resultado.WasCancelled
                ? "Processamento cancelado. Os resultados dos arquivos concluídos foram preservados."
                : "Processamento concluído.";
        }
        catch (OperationCanceledException)
        {
            MensagemStatus = "Processamento cancelado.";
        }
        catch (Exception ex)
        {
            MensagemStatus = $"Falha no processamento: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            ArquivoAtual = string.Empty;
        }
    }

    [RelayCommand]
    private void CancelarProcessamento()
    {
        if (_cancellationTokenSource is null || _cancellationTokenSource.IsCancellationRequested)
            return;

        MensagemStatus = "Solicitando cancelamento...";
        _cancellationTokenSource.Cancel();
    }

    private void LimparResultadosAnteriores()
    {
        Resultados.Clear();
        AvisosTexto = string.Empty;
        TotalEncontrado = 0;
        Processados = 0;
        Sucesso = 0;
        Falhas = 0;
        JaIndexados = 0;
        Classificados = 0;
        ParaRevisao = 0;
        NaoClassificados = 0;
        Movidos = 0;
        PercentualProgresso = 0;
        ArquivoAtual = string.Empty;
    }

    private void AtualizarProgresso(PdfBatchProgress progresso)
    {
        TotalEncontrado = progresso.TotalFiles;
        Processados = progresso.ProcessedFiles;
        ArquivoAtual = string.IsNullOrWhiteSpace(progresso.CurrentFile)
            ? string.Empty
            : Path.GetFileName(progresso.CurrentFile);
        MensagemStatus = progresso.Message;

        PercentualProgresso = TotalEncontrado <= 0
            ? 0
            : Math.Clamp(Processados * 100d / TotalEncontrado, 0, 100);
    }

    private sealed class UiProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public UiProgress(Action<T> callback)
        {
            _callback = callback;
        }

        public void Report(T value)
        {
            Dispatcher.UIThread.Post(() => _callback(value));
        }
    }
}
