using Avalonia.Controls;
using Avalonia.Interactivity;

using CryBarEditor.Classes;

namespace CryBarEditor.Windows;

public partial class NewUnitTypeWindow : SimpleWindow
{
    public string? SelectedType { get; private set; }

    public NewUnitTypeWindow()
    {
        InitializeComponent();
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        SelectedType = (_typeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Unit";
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        SelectedType = null;
        Close();
    }
}
