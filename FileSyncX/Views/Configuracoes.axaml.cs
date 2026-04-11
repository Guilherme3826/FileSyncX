using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FileSyncX.ViewModels;


namespace FileSyncX.Views.UserControls;

public partial class Configuracoes : UserControl
{
    public Configuracoes(ConfiguracoesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public Configuracoes()
    {
        InitializeComponent();
    }
}