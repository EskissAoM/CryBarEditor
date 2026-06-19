using Avalonia.Controls;
using Avalonia.Interactivity;
using CryBarEditor.Classes;

namespace CryBarEditor.Windows;

public partial class InputPromptWindow : SimpleWindow
{
    public string? InputText { get; private set; }

    public InputPromptWindow()
    {
        InitializeComponent();
    }

    public InputPromptWindow(string message, string defaultValue = "") : this()
    {
        _messageText.Text = message;
        _inputBox.Text = defaultValue;
        Opened += (s, e) => _inputBox.Focus();
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        InputText = _inputBox.Text?.Trim();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        InputText = null;
        Close();
    }
}
