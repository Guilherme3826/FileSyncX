using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FileSyncX.Views;

public partial class MainWindow : Window
{
    private bool _isSidebarExpanded = true;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    public void MaxRestore_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    public void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    public void DragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public void ToggleSidebar_Click(object? sender, RoutedEventArgs e)
    {
        _isSidebarExpanded = !_isSidebarExpanded;

        var sidebar = this.FindControl<Border>("Sidebar");
        if (sidebar != null) sidebar.Width = _isSidebarExpanded ? 260 : 68;

        var sidebarHeader = this.FindControl<StackPanel>("SidebarHeader");
        if (sidebarHeader != null) sidebarHeader.IsVisible = _isSidebarExpanded;

        var txtDashboard = this.FindControl<TextBlock>("TxtDashboard");
        if (txtDashboard != null) txtDashboard.IsVisible = _isSidebarExpanded;

        var txtConfig = this.FindControl<TextBlock>("TxtConfig");
        if (txtConfig != null) txtConfig.IsVisible = _isSidebarExpanded;

        var sepConfig = this.FindControl<Separator>("SepConfig");
        if (sepConfig != null) sepConfig.IsVisible = _isSidebarExpanded;
    }

    public void AtribuirSombra(object? sender, RoutedEventArgs e)
    {
    }
}