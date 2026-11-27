using Avalonia.Controls;
using Avalonia.Interactivity;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Views;

public partial class SimilarCategoryConsolidationWindow : Window
{
    private readonly ICategoryFolderConsolidationService _service;
    private IReadOnlyList<CategoryFolderMergeCandidate> _candidatas = [];
    private bool _executando;

    public SimilarCategoryConsolidationWindow(
        ICategoryFolderConsolidationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        InitializeComponent();
        RefreshButton.Click += Refresh_Click;
        ConsolidateButton.Click += Consolidate_Click;
        Opened += Window_Opened;
    }

    private async void Window_Opened(object? sender, EventArgs e)
    {
        await CarregarCandidatasAsync();
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        await CarregarCandidatasAsync();
    }

    private async Task CarregarCandidatasAsync()
    {
        if (_executando)
            return;

        RefreshButton.IsEnabled = false;
        ConsolidateButton.IsEnabled = false;
        StatusText.Text = "Consultando categorias semelhantes no VecktorVault...";

        try
        {
            CategoryFolderSearchResult busca = await _service.BuscarCandidatasAsync();
            _candidatas = busca.Candidatas;
            CandidatesGrid.ItemsSource = _candidatas;

            string resumoPastas =
                $"Categorias com embedding verificadas: {busca.CategoriasVerificadas} | " +
                $"Com diretório físico válido: {busca.CategoriasComPastaFisica} | " +
                $"Excluídas por ausência/indisponibilidade: {busca.CategoriasSemPastaFisica}.";

            StatusText.Text = _candidatas.Count == 0
                ? "Nenhum par elegível encontrado. A análise considera somente categorias cujas duas pastas existem fisicamente."
                : $"{_candidatas.Count} par(es) elegível(is) encontrado(s). Os dois diretórios de cada par foram verificados no disco.";

            if (busca.CategoriasSemPastaFisica > 0)
                StatusText.Text += $" {busca.CategoriasSemPastaFisica} categoria(s) foram excluídas porque a pasta não existe ou não está acessível.";

            var diagnosticos = new List<string>
            {
                resumoPastas,
                "Categorias secundárias serão preservadas. Nenhuma FK de categoria será alterada."
            };
            diagnosticos.AddRange(busca.CaminhosIndisponiveis.Take(12));
            if (busca.CaminhosIndisponiveis.Count > 12)
                diagnosticos.Add($"... e mais {busca.CaminhosIndisponiveis.Count - 12} caminho(s) indisponível(is) não exibido(s).");
            DetailsText.Text = string.Join(Environment.NewLine, diagnosticos);

            ConsolidateButton.IsEnabled = _candidatas.Count > 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao consultar o VecktorVault: " + ex.GetBaseException().Message;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async void Consolidate_Click(object? sender, RoutedEventArgs e)
    {
        if (_executando || _candidatas.Count == 0)
            return;

        _executando = true;
        RefreshButton.IsEnabled = false;
        ConsolidateButton.IsEnabled = false;
        ConsolidateButton.Content = "Consolidando...";
        StatusText.Text = "Movendo arquivos físicos e atualizando os destinos no PostgreSQL...";
        DetailsText.Text = string.Empty;

        try
        {
            CategoryFolderConsolidationResult result =
                await Task.Run(() => _service.ConsolidarAsync(_candidatas));

            DetailsText.Text =
                $"Pares recebidos: {result.ParesEncontrados} | " +
                $"Pares consolidados: {result.ParesConsolidados} | " +
                $"Ignorados: {result.ParesIgnorados} | " +
                $"Arquivos movidos: {result.ArquivosMovidos} | " +
                $"Documentos atualizados: {result.DocumentosAtualizados} | " +
                $"Erros: {result.Erros.Count}";

            if (result.ParesConsolidados == 0)
            {
                StatusText.Text = result.Erros.Count > 0
                    ? "Nenhuma pasta foi fundida. Todas as tentativas falharam ou foram bloqueadas. Consulte os detalhes."
                    : "Nenhuma pasta foi fundida. Os pares foram ignorados; consulte os motivos abaixo.";
            }
            else if (result.Erros.Count > 0)
            {
                StatusText.Text = "Consolidação parcial: algumas pastas foram fundidas e outras falharam. Consulte os detalhes.";
            }
            else
            {
                StatusText.Text = "Consolidação concluída: as pastas secundárias foram processadas.";
            }

            var detalhes = new List<string>();
            detalhes.AddRange(result.Erros.Select(x => "ERRO: " + x));
            detalhes.AddRange(result.Avisos.Select(x => "AVISO: " + x));
            if (detalhes.Count > 0)
                DetailsText.Text += Environment.NewLine + string.Join(Environment.NewLine, detalhes.Take(12));

            // A operação já foi concluída. Uma falha na consulta de atualização
            // da grade não pode mascarar o resultado físico da consolidação.
            _candidatas = [];
            CandidatesGrid.ItemsSource = _candidatas;
            try
            {
                await CarregarCandidatasAposExecucaoAsync();
            }
            catch (Exception refreshException)
            {
                StatusText.Text += Environment.NewLine +
                    "A consolidação já terminou, mas não foi possível atualizar a lista: " +
                    refreshException.GetBaseException().Message;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha na consolidação: " + ex.GetBaseException().Message;
        }
        finally
        {
            _executando = false;
            RefreshButton.IsEnabled = true;
            ConsolidateButton.Content = "Consolidar pares listados";
            ConsolidateButton.IsEnabled = _candidatas.Count > 0;
        }
    }

    private async Task CarregarCandidatasAposExecucaoAsync()
    {
        CategoryFolderSearchResult busca = await _service.BuscarCandidatasAsync();
        _candidatas = busca.Candidatas;
        CandidatesGrid.ItemsSource = _candidatas;

        if (_candidatas.Count == 0)
        {
            StatusText.Text += " Não restaram pares com as duas pastas fisicamente disponíveis.";
            ConsolidateButton.IsEnabled = false;
        }
        else
        {
            StatusText.Text += $" Restam {_candidatas.Count} par(es) com ambas as pastas existentes.";
        }

        if (busca.CategoriasSemPastaFisica > 0)
            StatusText.Text += $" {busca.CategoriasSemPastaFisica} categoria(s) foram excluídas por pasta ausente/inacessível.";
    }
}
