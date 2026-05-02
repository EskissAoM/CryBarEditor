using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CryBarEditor.Windows;

public partial class BlenderTipsDialog : Window
{
    public BlenderTipsDialog()
    {
        InitializeComponent();
    }

    void CloseClick(object? sender, RoutedEventArgs e) => Close();
}
