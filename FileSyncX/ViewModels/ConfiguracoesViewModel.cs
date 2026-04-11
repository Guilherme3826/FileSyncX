using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace FileSyncX.ViewModels;

public partial class ConfiguracoesViewModel : ObservableObject
{
    [ObservableProperty]
    private string _pastaRaiz = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _categorias = new();

    [ObservableProperty]
    private string _categoriaSelecionada = string.Empty;

    [ObservableProperty]
    private string _extensao = string.Empty;

    [ObservableProperty]
    private string _descricaoExtensao = string.Empty;

    [ObservableProperty]
    private ObservableCollection<JsonNodeModel> _rootNodes = new();

    [ObservableProperty]
    private JsonNodeModel? _nodeSelecionado;

    private ConfigModel _configuracaoAtual = new();

    private readonly string _pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
    private string CaminhoArquivoJson => Path.Combine(_pastaAppData, "extensoes_config.json");

    public ConfiguracoesViewModel()
    {
        CarregarConfiguracoes();
    }

    partial void OnPastaRaizChanged(string value)
    {
        _configuracaoAtual.PastaDestinoRaiz = value;
        SalvarJsonNoDisco();
    }

    partial void OnNodeSelecionadoChanged(JsonNodeModel? value)
    {
        if (value == null) return;

        if (value.Children.Count == 0 && string.IsNullOrEmpty(value.Descricao) == false)
        {
            Extensao = value.Chave;
            DescricaoExtensao = value.Descricao;

            foreach (var cat in _configuracaoAtual.Categorias)
            {
                if (cat.Value.ContainsKey(value.Chave))
                {
                    CategoriaSelecionada = cat.Key;
                    break;
                }
            }
        }
        else
        {
            CategoriaSelecionada = value.Chave;
            Extensao = string.Empty;
            DescricaoExtensao = string.Empty;
        }
    }

    private void CarregarConfiguracoes()
    {
        if (File.Exists(CaminhoArquivoJson))
        {
            try
            {
                string json = File.ReadAllText(CaminhoArquivoJson);
                _configuracaoAtual = JsonSerializer.Deserialize<ConfigModel>(json) ?? new ConfigModel();
                PastaRaiz = _configuracaoAtual.PastaDestinoRaiz;
            }
            catch
            {
                _configuracaoAtual = new ConfigModel();
            }
        }
        else
        {
            _configuracaoAtual = new ConfigModel();
        }

        AtualizarListaCategorias();
        MontarArvore();
    }

    private void AtualizarListaCategorias()
    {
        Categorias.Clear();
        foreach (var categoria in _configuracaoAtual.Categorias.Keys)
        {
            Categorias.Add(categoria);
        }
    }

    private void MontarArvore()
    {
        RootNodes.Clear();

        foreach (var cat in _configuracaoAtual.Categorias)
        {
            var categoriaNode = new JsonNodeModel(cat.Key, string.Empty);

            foreach (var ext in cat.Value)
            {
                categoriaNode.Children.Add(new JsonNodeModel(ext.Key, ext.Value));
            }

            RootNodes.Add(categoriaNode);
        }
    }

    [RelayCommand]
    private void SalvarExtensao()
    {
        if (string.IsNullOrWhiteSpace(CategoriaSelecionada) ||
            string.IsNullOrWhiteSpace(Extensao) ||
            string.IsNullOrWhiteSpace(DescricaoExtensao))
        {
            return;
        }

        string extLimpa = Extensao.Replace(".", "").ToLower();
        string catAlvo = CategoriaSelecionada.Trim();

        // 1. Verificar se a extensão já existe em alguma categoria
        string? categoriaAntiga = _configuracaoAtual.Categorias
            .FirstOrDefault(c => c.Value.ContainsKey(extLimpa)).Key;

        // 2. Lógica de decisão
        if (categoriaAntiga != null)
        {
            // Se já existe na mesma categoria, não faz nada (ou apenas atualiza descrição)
            if (categoriaAntiga == catAlvo)
            {
                if (_configuracaoAtual.Categorias[catAlvo][extLimpa] == DescricaoExtensao)
                    return;
            }
            else
            {
                // Se existe em categoria diferente, remove da antiga para mover
                _configuracaoAtual.Categorias[categoriaAntiga].Remove(extLimpa);

                // Remove a categoria do dicionário se ela ficar vazia após a remoção
                if (_configuracaoAtual.Categorias[categoriaAntiga].Count == 0)
                {
                    _configuracaoAtual.Categorias.Remove(categoriaAntiga);
                    Categorias.Remove(categoriaAntiga);
                }
            }
        }

        // 3. Garantir que a categoria alvo exista
        if (!_configuracaoAtual.Categorias.ContainsKey(catAlvo))
        {
            _configuracaoAtual.Categorias[catAlvo] = new Dictionary<string, string>();
            if (!Categorias.Contains(catAlvo))
                Categorias.Add(catAlvo);
        }

        // 4. Salvar/Atualizar a extensão na categoria correta
        _configuracaoAtual.Categorias[catAlvo][extLimpa] = DescricaoExtensao;
        _configuracaoAtual.PastaDestinoRaiz = PastaRaiz;

        SalvarJsonNoDisco();

        Extensao = string.Empty;
        DescricaoExtensao = string.Empty;
        NodeSelecionado = null;

        MontarArvore();
    }

    private void SalvarJsonNoDisco()
    {
        try
        {
            if (!Directory.Exists(_pastaAppData))
            {
                Directory.CreateDirectory(_pastaAppData);
            }

            var opcoes = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_configuracaoAtual, opcoes);

            File.WriteAllText(CaminhoArquivoJson, json);
        }
        catch (Exception)
        {
        }
    }
}

public class ConfigModel
{
    public string PastaDestinoRaiz { get; set; } = string.Empty;
    public Dictionary<string, Dictionary<string, string>> Categorias { get; set; } = new();
}

public class JsonNodeModel
{
    public string Chave { get; set; }
    public string Descricao { get; set; }
    public ObservableCollection<JsonNodeModel> Children { get; } = new();

    public JsonNodeModel(string chave, string descricao)
    {
        Chave = chave;
        Descricao = descricao;
    }
}