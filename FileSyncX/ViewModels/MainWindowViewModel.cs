using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncX.Views.UserControls;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace FileSyncX.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private UserControl? _telaAtual;

    private readonly IServiceProvider _services;

    public MainWindowViewModel(IServiceProvider services)
    {
        _services = services;
    }

    [RelayCommand]
    private void MostrarDashboard()
    {
        TelaAtual = _services.GetRequiredService<GerenciadorArquivos>();
    }

    [RelayCommand]
    private void MostrarSincronizacao()
    {
        // Aqui você pode chamar a mesma tela ou uma nova tela específica
        // Por enquanto, chamaremos o GerenciadorArquivos como exemplo
        TelaAtual = _services.GetRequiredService<GerenciadorArquivos>();
    }

    [RelayCommand]
    private void MostrarConfiguracoes()
    {
        TelaAtual = _services.GetRequiredService<Configuracoes>();
    }
}