using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

namespace FileSyncX.ViewModels;

/// <summary>
/// ViewModel do gerenciamento de categorias semânticas.
/// As propriedades e comandos são declarados explicitamente para não depender
/// da geração de código do atributo ObservableProperty nesta classe.
/// </summary>
public sealed class SemanticCategoryManagerViewModel : ViewModelBase
{
    private readonly ISemanticCategoryManagementService _categoryService;
    private readonly IAiCategoryDescriptionService _aiDescriptionService;
    private readonly IPdfTextExtractionService _pdfTextExtractionService;

    private string _ocrText = string.Empty;
    private int? _editingCategoryId;

    private ObservableCollection<SemanticCategoryListItem> _categorias = new();
    private SemanticCategoryListItem? _categoriaSelecionada;
    private string _filtroCategorias = string.Empty;
    private string _nomeCategoria = string.Empty;
    private string _descricaoSemantica = string.Empty;
    private string _palavrasChave = string.Empty;
    private string _pastaDestino = string.Empty;
    private string _arquivoOcr = string.Empty;
    private string _textoOcrPreview = string.Empty;
    private int _caracteresOcr;
    private int _documentosAssociados;
    private bool _isBusy;
    private string _mensagemStatus = "Anexe um PDF ou arquivo de texto OCR para gerar uma categoria.";
    private string _mensagemErro = string.Empty;

    public SemanticCategoryManagerViewModel(
        ISemanticCategoryManagementService categoryService,
        IAiCategoryDescriptionService aiDescriptionService,
        IPdfTextExtractionService pdfTextExtractionService)
    {
        _categoryService = categoryService
            ?? throw new ArgumentNullException(nameof(categoryService));
        _aiDescriptionService = aiDescriptionService
            ?? throw new ArgumentNullException(nameof(aiDescriptionService));
        _pdfTextExtractionService = pdfTextExtractionService
            ?? throw new ArgumentNullException(nameof(pdfTextExtractionService));

        CarregarCategoriasCommand = new AsyncRelayCommand(
            CarregarCategoriasAsync,
            () => !IsBusy);

        NovaCategoriaCommand = new RelayCommand(
            NovaCategoria,
            () => !IsBusy);

        GerarDescricaoComIaCommand = new AsyncRelayCommand(
            GerarDescricaoComIaAsync,
            () => PodeGerarDescricao);

        SalvarCategoriaCommand = new AsyncRelayCommand(
            SalvarCategoriaAsync,
            () => PodeSalvar);

        ReclassificarDocumentosCommand = new AsyncRelayCommand(
            ReclassificarDocumentosAsync,
            () => !IsBusy && Categorias.Count > 0);
    }

    public ObservableCollection<SemanticCategoryListItem> Categorias
    {
        get => _categorias;
        private set => SetProperty(ref _categorias, value);
    }

    public ObservableCollection<SemanticCategoryListItem> CategoriasFiltradas { get; } = new();

    public SemanticCategoryListItem? CategoriaSelecionada
    {
        get => _categoriaSelecionada;
        set
        {
            if (!SetProperty(ref _categoriaSelecionada, value))
                return;

            if (value is not null)
                CarregarCategoriaNoFormulario(value);

            OnPropertyChanged(nameof(PodeExcluir));
            OnPropertyChanged(nameof(EstaEditando));
            OnPropertyChanged(nameof(EstadoFormulario));
        }
    }

    public string FiltroCategorias
    {
        get => _filtroCategorias;
        set
        {
            if (SetProperty(ref _filtroCategorias, value))
                AplicarFiltro();
        }
    }

    public string NomeCategoria
    {
        get => _nomeCategoria;
        set
        {
            if (SetProperty(ref _nomeCategoria, value))
                AtualizarPodeSalvar();
        }
    }

    public string DescricaoSemantica
    {
        get => _descricaoSemantica;
        set
        {
            if (SetProperty(ref _descricaoSemantica, value))
                AtualizarPodeSalvar();
        }
    }

    public string PalavrasChave
    {
        get => _palavrasChave;
        set => SetProperty(ref _palavrasChave, value);
    }

    public string PastaDestino
    {
        get => _pastaDestino;
        set
        {
            if (SetProperty(ref _pastaDestino, value))
                AtualizarPodeSalvar();
        }
    }

    public string ArquivoOcr
    {
        get => _arquivoOcr;
        private set => SetProperty(ref _arquivoOcr, value);
    }

    public string TextoOcrPreview
    {
        get => _textoOcrPreview;
        private set => SetProperty(ref _textoOcrPreview, value);
    }

    public int CaracteresOcr
    {
        get => _caracteresOcr;
        private set => SetProperty(ref _caracteresOcr, value);
    }

    public int DocumentosAssociados
    {
        get => _documentosAssociados;
        private set => SetProperty(ref _documentosAssociados, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(PodeGerarDescricao));
            OnPropertyChanged(nameof(PodeSalvar));
            OnPropertyChanged(nameof(PodeExcluir));
            OnPropertyChanged(nameof(PodeReclassificarDocumentos));
            NotificarComandos();
        }
    }

    public string MensagemStatus
    {
        get => _mensagemStatus;
        private set => SetProperty(ref _mensagemStatus, value);
    }

    public string MensagemErro
    {
        get => _mensagemErro;
        set => SetProperty(ref _mensagemErro, value);
    }

    public bool EstaEditando => _editingCategoryId.HasValue;
    public bool PodeGerarDescricao => !IsBusy && !string.IsNullOrWhiteSpace(_ocrText);
    public bool PodeSalvar =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(NomeCategoria) &&
        !string.IsNullOrWhiteSpace(DescricaoSemantica) &&
        !string.IsNullOrWhiteSpace(PastaDestino);
    public bool PodeExcluir => !IsBusy && _editingCategoryId.HasValue;
    public bool PodeReclassificarDocumentos => !IsBusy && Categorias.Count > 0;
    public string EstadoFormulario => EstaEditando
        ? $"Editando categoria #{_editingCategoryId}"
        : "Nova categoria";

    public IAsyncRelayCommand CarregarCategoriasCommand { get; }
    public IRelayCommand NovaCategoriaCommand { get; }
    public IAsyncRelayCommand GerarDescricaoComIaCommand { get; }
    public IAsyncRelayCommand SalvarCategoriaCommand { get; }
    public IAsyncRelayCommand ReclassificarDocumentosCommand { get; }

    public async Task CarregarCategoriasAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            MensagemErro = string.Empty;
            await CarregarCategoriasInternoAsync();
            MensagemStatus = $"{Categorias.Count} categoria(s) carregada(s) do VecktorVault.";
        }
        catch (Exception ex)
        {
            MensagemErro = $"Não foi possível carregar as categorias: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NovaCategoria()
    {
        CategoriaSelecionada = null;
        _editingCategoryId = null;
        _ocrText = string.Empty;

        NomeCategoria = string.Empty;
        DescricaoSemantica = string.Empty;
        PalavrasChave = string.Empty;
        PastaDestino = string.Empty;
        ArquivoOcr = string.Empty;
        TextoOcrPreview = string.Empty;
        CaracteresOcr = 0;
        DocumentosAssociados = 0;

        MensagemErro = string.Empty;
        MensagemStatus = "Nova categoria. Anexe um PDF ou OCR para gerar a descrição semântica.";
        NotificarEstadoFormulario();
    }

    public async Task AnexarDocumentoOcrAsync(
        string caminhoArquivo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caminhoArquivo) || !File.Exists(caminhoArquivo))
        {
            throw new FileNotFoundException(
                "O arquivo OCR selecionado não foi encontrado.",
                caminhoArquivo);
        }

        string extensao = Path.GetExtension(caminhoArquivo).ToLowerInvariant();
        string texto;

        IsBusy = true;
        MensagemErro = string.Empty;
        MensagemStatus = "Lendo o arquivo e extraindo o texto...";

        try
        {
            if (extensao == ".pdf")
            {
                var extraction = await _pdfTextExtractionService.ExtrairTextoAsync(
                    caminhoArquivo,
                    cancellationToken);

                texto = extraction.Text;
                MensagemStatus = extraction.UsedOcr
                    ? $"PDF lido com OCR. {extraction.PageCount} página(s) processada(s)."
                    : $"Texto extraído do PDF. {extraction.PageCount} página(s) processada(s).";
            }
            else if (extensao is ".txt" or ".md" or ".ocr")
            {
                texto = await File.ReadAllTextAsync(caminhoArquivo, cancellationToken);
                MensagemStatus = "Arquivo de texto OCR carregado.";
            }
            else
            {
                throw new NotSupportedException(
                    "Formato não suportado. Anexe um PDF ou arquivo .txt, .md ou .ocr com o texto OCR.");
            }

            if (string.IsNullOrWhiteSpace(texto))
                throw new InvalidDataException("Nenhum texto foi encontrado no arquivo selecionado.");

            _ocrText = texto;
            ArquivoOcr = caminhoArquivo;
            CaracteresOcr = texto.Length;
            TextoOcrPreview = texto.Length > 12000
                ? texto[..12000] + Environment.NewLine +
                  "[...] Prévia limitada a 12.000 caracteres. A IA receberá uma amostra do início e do final de documentos extensos."
                : texto;

            OnPropertyChanged(nameof(PodeGerarDescricao));
            MensagemStatus = $"Texto pronto: {CaracteresOcr:N0} caracteres. Agora gere a descrição com IA.";
            NotificarComandos();
        }
        catch (Exception ex)
        {
            MensagemErro = ex.GetBaseException().Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GerarDescricaoComIaAsync()
    {
        if (string.IsNullOrWhiteSpace(_ocrText))
        {
            MensagemErro = "Anexe primeiro um PDF ou arquivo de texto OCR.";
            return;
        }

        try
        {
            IsBusy = true;
            MensagemErro = string.Empty;
            MensagemStatus = "A IA está analisando o OCR e elaborando uma descrição semântica...";

            AiCategoryDraft draft = await _aiDescriptionService.GenerateFromOcrAsync(
                ArquivoOcr,
                _ocrText);

            NomeCategoria = draft.CategoryName;
            DescricaoSemantica = draft.SemanticDescription;
            PalavrasChave = draft.Keywords;
            MensagemStatus = "Descrição gerada. Revise o nome, a descrição e os termos antes de salvar.";
        }
        catch (Exception ex)
        {
            MensagemErro = $"Falha ao gerar a descrição: {ex.GetBaseException().Message}";
            MensagemStatus = "Não foi possível gerar a descrição.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SalvarCategoriaAsync()
    {
        if (!PodeSalvar)
        {
            MensagemErro = "Preencha o nome, a descrição semântica e a pasta de destino.";
            return;
        }

        try
        {
            IsBusy = true;
            MensagemErro = string.Empty;
            MensagemStatus = "Gerando embedding e salvando categoria no VecktorVault...";

            int id = await _categoryService.SaveAsync(
                _editingCategoryId,
                NomeCategoria,
                DescricaoSemantica,
                PalavrasChave,
                PastaDestino);

            await CarregarCategoriasInternoAsync();
            CategoriaSelecionada = Categorias.FirstOrDefault(x => x.Id == id);
            MensagemStatus = $"Categoria #{id} salva no VecktorVault com embedding atualizado.";
        }
        catch (Exception ex)
        {
            MensagemErro = $"Não foi possível salvar a categoria: {ex.GetBaseException().Message}";
            MensagemStatus = "Falha ao salvar categoria.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExcluirCategoriaSelecionadaAsync()
    {
        if (!_editingCategoryId.HasValue || IsBusy)
            return;

        int id = _editingCategoryId.Value;
        string name = NomeCategoria;

        try
        {
            IsBusy = true;
            MensagemErro = string.Empty;
            MensagemStatus = "Excluindo categoria e preservando os documentos associados...";

            await _categoryService.DeleteAsync(id);
            NovaCategoria();
            await CarregarCategoriasInternoAsync();
            MensagemStatus = $"Categoria '{name}' excluída. Os documentos foram preservados e ficaram sem categoria associada.";
        }
        catch (Exception ex)
        {
            MensagemErro = $"Não foi possível excluir a categoria: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }


    private async Task ReclassificarDocumentosAsync()
    {
        if (IsBusy)
            return;

        if (Categorias.Count == 0)
        {
            MensagemErro = "Cadastre pelo menos uma categoria antes de reclassificar os documentos.";
            return;
        }

        try
        {
            IsBusy = true;
            MensagemErro = string.Empty;
            MensagemStatus = "Reavaliando todos os documentos com embeddings já armazenados...";

            int total = await _categoryService.ReclassifyAllAsync(
                minimumSimilarity: 0.75);

            await CarregarCategoriasInternoAsync();
            MensagemStatus = total == 0
                ? "Nenhum documento com embedding foi encontrado para reclassificação."
                : $"Reclassificação concluída: {total} documento(s) reavaliado(s) com os embeddings existentes. OCR e embeddings dos documentos não foram regenerados.";
        }
        catch (Exception ex)
        {
            MensagemErro = $"Falha ao reclassificar documentos: {ex.GetBaseException().Message}";
            MensagemStatus = "Não foi possível reclassificar os documentos.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CarregarCategoriasInternoAsync()
    {
        var itens = await _categoryService.ListAsync();

        Categorias.Clear();
        foreach (SemanticCategoryListItem item in itens)
            Categorias.Add(item);

        AplicarFiltro();
        OnPropertyChanged(nameof(PodeReclassificarDocumentos));
        ReclassificarDocumentosCommand.NotifyCanExecuteChanged();
    }

    private void CarregarCategoriaNoFormulario(SemanticCategoryListItem value)
    {
        _editingCategoryId = value.Id;
        _ocrText = string.Empty;

        NomeCategoria = value.CategoryName;
        DescricaoSemantica = value.SemanticDescription;
        PalavrasChave = value.Keywords;
        PastaDestino = value.DestinationFolder;
        DocumentosAssociados = value.DocumentCount;
        ArquivoOcr = string.Empty;
        TextoOcrPreview = string.Empty;
        CaracteresOcr = 0;

        MensagemErro = string.Empty;
        MensagemStatus = $"Categoria #{value.Id} carregada. Você pode editar a descrição e a pasta de destino.";
        NotificarEstadoFormulario();
    }

    private void AplicarFiltro()
    {
        CategoriasFiltradas.Clear();
        IEnumerable<SemanticCategoryListItem> query = Categorias;

        if (!string.IsNullOrWhiteSpace(FiltroCategorias))
        {
            string term = FiltroCategorias.Trim();
            query = query.Where(x =>
                x.CategoryName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.SemanticDescription.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.DestinationFolder.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        foreach (SemanticCategoryListItem item in query)
            CategoriasFiltradas.Add(item);
    }

    private void AtualizarPodeSalvar()
    {
        OnPropertyChanged(nameof(PodeSalvar));
        SalvarCategoriaCommand.NotifyCanExecuteChanged();
        ReclassificarDocumentosCommand.NotifyCanExecuteChanged();
    }

    private void NotificarEstadoFormulario()
    {
        OnPropertyChanged(nameof(EstaEditando));
        OnPropertyChanged(nameof(EstadoFormulario));
        OnPropertyChanged(nameof(PodeExcluir));
        OnPropertyChanged(nameof(PodeReclassificarDocumentos));
        OnPropertyChanged(nameof(PodeGerarDescricao));
        OnPropertyChanged(nameof(PodeSalvar));
        NotificarComandos();
    }

    private void NotificarComandos()
    {
        CarregarCategoriasCommand.NotifyCanExecuteChanged();
        NovaCategoriaCommand.NotifyCanExecuteChanged();
        GerarDescricaoComIaCommand.NotifyCanExecuteChanged();
        SalvarCategoriaCommand.NotifyCanExecuteChanged();
        ReclassificarDocumentosCommand.NotifyCanExecuteChanged();
    }
}
