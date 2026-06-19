using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CryBarEditor.Classes;
using System.IO;
using System.Threading.Tasks;

namespace CryBarEditor.Windows;

public partial class ProtoSettingsWindow : SimpleWindow
{
    private string? _dataBarPath;
    private string? _userFolderPath;

    public string? DataBarPath
    {
        get => _dataBarPath;
        set { _dataBarPath = value; OnSelfChanged(); }
    }

    public string? UserFolderPath
    {
        get => _userFolderPath;
        set { _userFolderPath = value; OnSelfChanged(); }
    }

    public bool IsSaved { get; private set; }

    public ProtoSettingsWindow()
    {
        DataContext = this;
        InitializeComponent();

        var config = ProtoEditorSettings.LoadSettings();
        DataBarPath = config.DataBarPath;
        UserFolderPath = config.UserFolderPath;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        IsSaved = false;
        Close();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var config = ProtoEditorSettings.LoadSettings();
        config.DataBarPath = DataBarPath;
        config.UserFolderPath = UserFolderPath;
        ProtoEditorSettings.SaveSettings(config);

        IsSaved = true;
        Close();
    }

    private async void BrowseBar_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Data.bar file",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("BAR Files") { Patterns = ["*.bar"] }]
        });

        if (files.Count > 0)
        {
            DataBarPath = files[0].Path.LocalPath;
        }
    }

    private async void BrowseUser_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Steam User Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            UserFolderPath = folders[0].Path.LocalPath;
        }
    }
}
