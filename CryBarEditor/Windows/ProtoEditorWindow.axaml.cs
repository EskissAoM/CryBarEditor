using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CryBar;
using CryBar.Bar;
using CryBarEditor.Classes;

namespace CryBarEditor.Windows;

public partial class ProtoEditorWindow : SimpleWindow
{
    private static readonly HashSet<string> SelectionOnlySimpleFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "impacttype",
        "unitaitype",
        "movementtype",
    };

    private static readonly string[] StringBackedFieldTags =
    [
        "displaynameid",
        "editornameid",
        "rollovertextid",
        "shortrollovertextid",
    ];
    private const string ProtoUnitNameFieldKey = "__proto_unit_name";

    private readonly MainWindow _mainWindow;
    private XElement? _barXmlRoot;
    private XDocument? _modXmlDoc;
    private XElement? _modXmlRoot;
    private string? _modFilePath;
    private ProtoBarData? _barData;
    private bool _isDirty;
    private bool _isPopulating;
    private string? _currentUnitName;
    private bool _isReadOnly;

    private List<string> _allCurrentNames = [];
    private readonly ObservableCollectionExtended<string> _filteredUnitNames = [];

    private readonly Dictionary<string, Control> _fieldControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _currentStringFieldIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _costControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _armorControls = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _currentUnitTypes;
    private HashSet<string>? _currentFlags;
    private List<string>? _cachedTechNames;
    private readonly Dictionary<string, string> _protoActionTypeMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _globalTacticsActionTypeMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _currentUnitProtoActionTypeMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _currentUnitTacticsActionTypeMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _tacticsActionTypeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownProtoActionNames = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _protoActionNameSuggestions = [];
    private List<string> _protoActionTypeSuggestions = [];
    private readonly List<CommandRowState> _trainCommandRows = [];
    private readonly List<CommandRowState> _techCommandRows = [];
    private readonly List<ProtoActionWidgetState> _protoActionWidgets = [];

    private class ProtoActionWidgetState
    {
        public required Panel Container { get; set; }
        public required AutoCompleteBox NameAcb { get; set; }
        public required ComboBox TypeCb { get; set; }
        public required TextBox RofTb { get; set; }
        public required TextBox MaxRangeTb { get; set; }
        public List<DamageRowState> DamageRows { get; } = [];
        public List<BonusRowState> BonusRows { get; } = [];
    }

    private class DamageRowState
    {
        public required Panel RowPanel { get; set; }
        public required ComboBox TypeCb { get; set; }
        public required TextBox ValTb { get; set; }
    }

    private class BonusRowState
    {
        public required Panel RowPanel { get; set; }
        public required AutoCompleteBox TypeAcb { get; set; }
        public required TextBox ValTb { get; set; }
    }

    private class CommandRowState
    {
        public required Panel RowPanel { get; set; }
        public required ComboBox RowCb { get; set; }
        public required ComboBox ColumnCb { get; set; }
        public required AutoCompleteBox ValueAcb { get; set; }
        public string MergeMode { get; set; } = "";
    }

    public ProtoEditorWindow()
    {
        InitializeComponent();
        _mainWindow = null!;
    }

    public ProtoEditorWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        DataContext = this;
        InitializeComponent();

        _unitList.ItemsSource = _filteredUnitNames;

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await LoadProtoDataFromBar();
            await EnsureInitialModSelectionAsync();
            RefreshUnitList();
        });
    }

    private async Task EnsureInitialModSelectionAsync()
    {
        var config = ProtoEditorSettings.LoadSettings();
        if (!string.IsNullOrEmpty(config.LastModFilePath) && File.Exists(config.LastModFilePath))
        {
            if (TryLoadModFile(config.LastModFilePath, showErrors: false))
            {
                return;
            }
        }

        await Task.CompletedTask;
        _fileLabel.Text = "No mod selected - browsing base Data.bar";
    }

    private async Task LoadProtoDataFromBar()
    {
        var barFile = _mainWindow.BarFile;
        var barStream = _mainWindow.BarFileStream;

        if (barFile != null && barStream != null && Path.GetFileName(barStream.Name).Equals("Data.bar", StringComparison.OrdinalIgnoreCase))
        {
            ExtractProtoFromBar(barFile, barStream.Name);
            return;
        }

        var dataBarPath = ResolveDataBarPath();
        if (!string.IsNullOrEmpty(dataBarPath) && File.Exists(dataBarPath))
        {
            _statusMessage.Text = "Loading Data.bar...";
            try
            {
                await Task.Run(() =>
                {
                    using var stream = File.OpenRead(dataBarPath);
                    var file = new BarFile(stream);
                    if (file.Load(out _))
                    {
                        ExtractProtoFromBar(file, dataBarPath);
                    }
                });
            }
            catch (Exception ex)
            {
                _statusMessage.Text = $"Failed to load Data.bar: {ex.Message}";
            }
            return;
        }

        if (barFile == null || barStream == null)
        {
            _statusMessage.Text = "Please load the game root folder or select Data.bar in settings.";
            return;
        }

        string barPath = barStream.Name;
        ExtractProtoFromBar(barFile, barPath);
    }

    private string? ResolveDataBarPath()
    {
        var rootDirectory = _mainWindow.RootDirectory;
        if (Directory.Exists(rootDirectory))
        {
            var direct = Path.Combine(rootDirectory, "data", "Data.bar");
            if (File.Exists(direct))
            {
                return direct;
            }

            var nested = Path.Combine(rootDirectory, "game", "data", "Data.bar");
            if (File.Exists(nested))
            {
                return nested;
            }
        }

        var config = ProtoEditorSettings.LoadSettings();
        return !string.IsNullOrEmpty(config.DataBarPath) && File.Exists(config.DataBarPath)
            ? config.DataBarPath
            : null;
    }

    private void ExtractProtoFromBar(BarFile barFile, string barPath)
    {
        DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(barPath);
        double mtime = lastWriteTimeUtc.Ticks;

        var configLoaded = ProtoEditorSettings.LoadSettings();
        string cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache_merged_proto.xml");
        Dictionary<string, string>? tacticsActionTypes = null;

        if (File.Exists(cachePath) && Math.Abs(configLoaded.DataBarMtime - mtime) < 1.0)
        {
            _statusMessage.Text = "Loading proto data from cache...";
            try
            {
                var xmlContent = File.ReadAllText(cachePath);
                var (barData, root) = ProtoDataExtractor.ExtractProtoData(xmlContent);
                _barData = barData;
                _barXmlRoot = root;
                tacticsActionTypes = ExtractProtoActionTypesFromTactics(barFile, barPath);
                foreach (var kvp in LoadProtoActionTypesFromLooseTactics())
                    tacticsActionTypes[kvp.Key] = kvp.Value;
                RefreshProtoActionMetadata(tacticsActionTypes);
                _statusMessage.Text = "";
                return;
            }
            catch (Exception ex)
            {
                _statusMessage.Text = $"Failed to load cache: {ex.Message}. Re-extracting...";
            }
        }

        _statusMessage.Text = "Extracting proto files from BAR...";
        try
        {
            var entries = barFile.Entries;
            if (entries == null)
            {
                _statusMessage.Text = "Failed to load proto data: BAR entries are not loaded.";
                return;
            }

            var protoEntries = entries
                .Where(e => e.Name.Contains("proto", StringComparison.OrdinalIgnoreCase)
                          && e.Name.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var xmlParts = new List<string>();
            using (var tempStream = File.OpenRead(barPath))
            {
                foreach (var entry in protoEntries)
                {
                    int size = entry.IsCompressed ? entry.SizeUncompressed : entry.SizeInArchive;
                    byte[] decompressed = new byte[size];
                    int readBytes = entry.ReadDataDecompressed(tempStream, decompressed);
                    if (readBytes > 0)
                    {
                        var xml = BarFormatConverter.XMBtoFormattedXmlString(decompressed.AsSpan(0, readBytes));
                        if (xml != null)
                        {
                            if (xml.StartsWith("<?xml"))
                            {
                                int endDecl = xml.IndexOf("?>");
                                if (endDecl >= 0)
                                {
                                    xml = xml[(endDecl + 2)..];
                                }
                            }
                            xmlParts.Add(xml);
                        }
                    }
                }
            }

            var combinedXml = $"<protos>\n{string.Join("\n", xmlParts)}\n</protos>";
            var (barData, barRoot) = ProtoDataExtractor.ExtractProtoData(combinedXml);
            tacticsActionTypes = ExtractProtoActionTypesFromTactics(barFile, barPath);
            foreach (var kvp in LoadProtoActionTypesFromLooseTactics())
                tacticsActionTypes[kvp.Key] = kvp.Value;

            _barData = barData;
            _barXmlRoot = barRoot;
            RefreshProtoActionMetadata(tacticsActionTypes);

            File.WriteAllText(cachePath, combinedXml);

            configLoaded.DataBarPath = barPath;
            configLoaded.DataBarMtime = mtime;
            ProtoEditorSettings.SaveSettings(configLoaded);

            _statusMessage.Text = "";
        }
        catch (Exception ex)
        {
            _statusMessage.Text = $"Failed to load proto data: {ex.Message}";
        }
    }

    private void RefreshUnitList()
    {
        if (_barData == null) return;

        var names = new List<string>();
        int selectedIndex = _unitTabs.SelectedIndex;

        if (selectedIndex == 1) // Modified
        {
            if (_modXmlRoot != null)
            {
                names.AddRange(ProtoXmlHandler.GetUnitNames(_modXmlRoot));
            }
        }
        else // Original
        {
            names.AddRange(_barData.UnitNames);
            if (_modXmlRoot != null)
            {
                var modNames = new HashSet<string>(ProtoXmlHandler.GetUnitNames(_modXmlRoot), StringComparer.OrdinalIgnoreCase);
                names.RemoveAll(n => modNames.Contains(n));
            }
        }

        _allCurrentNames = names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        FilterUnitList();
    }

    private void FilterUnitList()
    {
        var query = _searchBox.Text?.Trim().ToLower();
        _filteredUnitNames.Clear();

        if (string.IsNullOrEmpty(query))
        {
            _filteredUnitNames.AddItems(_allCurrentNames);
        }
        else
        {
            _filteredUnitNames.AddItems(_allCurrentNames.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        FilterUnitList();
    }

    private void UnitTab_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshUnitList();
    }

    private void UnitList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_unitList.SelectedItem is string selectedName)
        {
            BuildEditorPanel(selectedName);
        }
    }

    private void AddSectionHeader(string title)
    {
        var lbl = new TextBlock
        {
            Text = $"── {title} ──",
            Classes = { "SectionHeader" },
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = Brush.Parse("#5ba8de"),
            Margin = new Thickness(0, 15, 0, 5)
        };
        _editorPanel.Children.Add(lbl);
    }

    private static bool IsSelectionOnlySimpleField(string tag) => SelectionOnlySimpleFields.Contains(tag);
    private static bool IsStringBackedField(string tag) => StringBackedFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] TrainTechRowOptions = ["0", "1", "2", "3"];
    private static readonly string[] TrainTechColumnOptions = ["0", "1", "2", "3", "4", "5"];
    private static string GetStringSuffixForField(string tag) => tag.ToLowerInvariant() switch
    {
        "displaynameid" => "NAME",
        "editornameid" => "EDITOR",
        "rollovertextid" => "LR",
        "shortrollovertextid" => "SR",
        _ => tag.ToUpperInvariant(),
    };

    private List<string> GetAvailableTrainUnitNames()
    {
        var names = new List<string>();
        if (_barData != null)
            names.AddRange(_barData.UnitNames);
        if (_modXmlRoot != null)
            names.AddRange(ProtoXmlHandler.GetUnitNames(_modXmlRoot));

        return names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> GetAvailableTechNames()
    {
        if (_cachedTechNames != null)
            return _cachedTechNames;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in new[] { ResolveBaseGameplayXmlPath("techtree.xml"), GetCurrentModGameplayFilePath("techtree_mods.xml") })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            try
            {
                var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                foreach (var name in doc.Descendants("tech")
                    .Select(x => (string?)x.Attribute("name"))
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    names.Add(name!);
                }
            }
            catch
            {
                // Ignore malformed or missing techtree data and fall back to what we could read.
            }
        }

        foreach (var name in LoadTechNamesFromBar())
            names.Add(name);

        _cachedTechNames = names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return _cachedTechNames;
    }

    private string? ResolveBaseGameplayXmlPath(string fileName)
    {
        var gameplayDirectory = ResolveBaseGameplayDirectory();
        if (!string.IsNullOrWhiteSpace(gameplayDirectory))
        {
            var path = Path.Combine(gameplayDirectory, fileName);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private string? ResolveBaseGameplayDirectory()
    {
        var rootDirectory = _mainWindow.RootDirectory;
        if (Directory.Exists(rootDirectory))
        {
            var direct = Path.Combine(rootDirectory, "data", "gameplay");
            if (Directory.Exists(direct))
                return direct;

            var nested = Path.Combine(rootDirectory, "game", "data", "gameplay");
            if (Directory.Exists(nested))
                return nested;
        }

        var dataBarPath = ResolveDataBarPath();
        if (!string.IsNullOrWhiteSpace(dataBarPath) && File.Exists(dataBarPath))
        {
            var dataDirectory = Path.GetDirectoryName(dataBarPath);
            if (!string.IsNullOrWhiteSpace(dataDirectory))
            {
                var gameplayDirectory = Path.Combine(dataDirectory, "gameplay");
                if (Directory.Exists(gameplayDirectory))
                    return gameplayDirectory;
            }
        }

        return null;
    }

    private string? GetCurrentModGameplayFilePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(_modFilePath))
            return null;

        var gameplayDir = Path.GetDirectoryName(_modFilePath);
        if (string.IsNullOrWhiteSpace(gameplayDir))
            return null;

        return Path.Combine(gameplayDir, fileName);
    }

    private List<string> LoadTechNamesFromBar()
    {
        try
        {
            var barFile = _mainWindow.BarFile;
            var barStream = _mainWindow.BarFileStream;
            if (barFile != null && barStream != null && Path.GetFileName(barStream.Name).Equals("Data.bar", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractTechNamesFromBar(barFile, barStream.Name);
            }

            var dataBarPath = ResolveDataBarPath();
            if (!string.IsNullOrWhiteSpace(dataBarPath) && File.Exists(dataBarPath))
            {
                using var stream = File.OpenRead(dataBarPath);
                var file = new BarFile(stream);
                if (file.Load(out _))
                    return ExtractTechNamesFromBar(file, dataBarPath);
            }
        }
        catch
        {
            // Fallback to file-based tech lookup if BAR tech extraction fails.
        }

        return [];
    }

    private static List<string> ExtractTechNamesFromBar(BarFile barFile, string barPath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries;
        if (entries == null)
            return [];

        var techtreeEntries = entries
            .Where(e => e.Name.Contains("techtree", StringComparison.OrdinalIgnoreCase)
                     && e.Name.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        using var tempStream = File.OpenRead(barPath);
        foreach (var entry in techtreeEntries)
        {
            int size = entry.IsCompressed ? entry.SizeUncompressed : entry.SizeInArchive;
            byte[] decompressed = new byte[size];
            int readBytes = entry.ReadDataDecompressed(tempStream, decompressed);
            if (readBytes <= 0)
                continue;

            var xml = BarFormatConverter.XMBtoFormattedXmlString(decompressed.AsSpan(0, readBytes));
            if (string.IsNullOrWhiteSpace(xml))
                continue;

            try
            {
                var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                foreach (var name in doc.Descendants("tech")
                    .Select(x => (string?)x.Attribute("name"))
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    names.Add(name!);
                }
            }
            catch
            {
                // Skip malformed techtree entries and keep what we already found.
            }
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void AddCommandSection(string title, string itemLabel, IEnumerable<ProtoCommandEntry> entries, List<string> suggestions, List<CommandRowState> stateStore)
    {
        AddSectionHeader(title);

        var commandContainer = new StackPanel { Spacing = 4 };
        _editorPanel.Children.Add(commandContainer);
        stateStore.Clear();

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("70, 70, *, Auto"),
            Margin = new Thickness(0, 0, 0, 2)
        };

        var rowHeader = new TextBlock
        {
            Text = "Row",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(rowHeader, 0);
        headerGrid.Children.Add(rowHeader);

        var columnHeader = new TextBlock
        {
            Text = "Column",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(columnHeader, 1);
        headerGrid.Children.Add(columnHeader);

        var valueHeader = new TextBlock
        {
            Text = itemLabel,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(valueHeader, 2);
        headerGrid.Children.Add(valueHeader);

        commandContainer.Children.Add(headerGrid);

        void AddCommandRow(ProtoCommandEntry entry)
        {
            var rowOptions = TrainTechRowOptions.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(entry.Row) && !TrainTechRowOptions.Contains(entry.Row, StringComparer.OrdinalIgnoreCase))
                rowOptions = rowOptions.Concat([entry.Row]);

            var columnOptions = TrainTechColumnOptions.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(entry.Column) && !TrainTechColumnOptions.Contains(entry.Column, StringComparer.OrdinalIgnoreCase))
                columnOptions = columnOptions.Concat([entry.Column]);

            var rowPanel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("70, 70, *, Auto"),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var rowCb = new ComboBox
            {
                ItemsSource = rowOptions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SelectedItem = !string.IsNullOrWhiteSpace(entry.Row) ? entry.Row : null,
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 0, 8, 0)
            };
            rowCb.SelectionChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            Grid.SetColumn(rowCb, 0);
            rowPanel.Children.Add(rowCb);

            var columnCb = new ComboBox
            {
                ItemsSource = columnOptions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SelectedItem = !string.IsNullOrWhiteSpace(entry.Column) ? entry.Column : null,
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 0, 8, 0)
            };
            columnCb.SelectionChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            Grid.SetColumn(columnCb, 1);
            rowPanel.Children.Add(columnCb);

            var valueAcb = new AutoCompleteBox
            {
                Text = entry.Value,
                PlaceholderText = itemLabel,
                FilterMode = AutoCompleteFilterMode.Contains,
                ItemsSource = suggestions,
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 0, 8, 0)
            };
            EnableDropdownAutoComplete(valueAcb);
            valueAcb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            Grid.SetColumn(valueAcb, 2);
            rowPanel.Children.Add(valueAcb);

            var rowState = new CommandRowState
            {
                RowPanel = rowPanel,
                RowCb = rowCb,
                ColumnCb = columnCb,
                ValueAcb = valueAcb,
                MergeMode = entry.MergeMode
            };
            stateStore.Add(rowState);

            if (!_isReadOnly)
            {
                var btnDel = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                btnDel.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        stateStore.Remove(rowState);
                        commandContainer.Children.Remove(rowPanel);
                        MarkDirty();
                    }
                };
                Grid.SetColumn(btnDel, 3);
                rowPanel.Children.Add(btnDel);
            }

            commandContainer.Children.Add(rowPanel);
        }

        foreach (var entry in entries)
            AddCommandRow(entry);

        if (!_isReadOnly)
        {
            var btnAdd = new Button
            {
                Content = $"+ Add {itemLabel}",
                Background = Brush.Parse("#2b7a0b"),
                Margin = new Thickness(0, 4, 0, 4)
            };
            btnAdd.Click += async (s, e) =>
            {
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    AddCommandRow(new ProtoCommandEntry { Row = "0", Column = "0" });
                    MarkDirty();
                }
            };
            _editorPanel.Children.Add(btnAdd);
        }
    }

    private static List<ProtoCommandEntry> CollectValidCommandEntries(IEnumerable<CommandRowState> rows, IEnumerable<string> validValues)
    {
        var validSet = new HashSet<string>(validValues, StringComparer.OrdinalIgnoreCase);
        var entries = new List<ProtoCommandEntry>();

        foreach (var row in rows)
        {
            var value = row.ValueAcb.Text?.Trim() ?? "";
            var match = validSet.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match))
                continue;

            entries.Add(new ProtoCommandEntry
            {
                Value = match,
                Row = row.RowCb.SelectedItem as string ?? row.RowCb.SelectedValue as string ?? "",
                Column = row.ColumnCb.SelectedItem as string ?? row.ColumnCb.SelectedValue as string ?? "",
                MergeMode = row.MergeMode
            });
        }

        return entries;
    }

    private static Dictionary<string, string> ExtractProtoActionTypesFromTactics(BarFile barFile, string barPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries;
        if (entries == null)
            return result;

        var tacticsEntries = entries
            .Where(e => e.Name.Contains("tactics", StringComparison.OrdinalIgnoreCase)
                     && e.Name.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        using var tempStream = File.OpenRead(barPath);
        foreach (var entry in tacticsEntries)
        {
            int size = entry.IsCompressed ? entry.SizeUncompressed : entry.SizeInArchive;
            byte[] decompressed = new byte[size];
            int readBytes = entry.ReadDataDecompressed(tempStream, decompressed);
            if (readBytes <= 0)
                continue;

            var xml = BarFormatConverter.XMBtoFormattedXmlString(decompressed.AsSpan(0, readBytes));
            if (string.IsNullOrWhiteSpace(xml))
                continue;

            try
            {
                var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                foreach (var action in doc.Descendants("action"))
                {
                    var name = action.Element("name")?.Value?.Trim();
                    var type = action.Element("type")?.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(type))
                        result[name] = type;
                }
            }
            catch
            {
                // Ignore malformed tactics entries and keep scanning the rest of the BAR.
            }
        }

        return result;
    }

    private Dictionary<string, string> LoadProtoActionTypesFromLooseTactics()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var gameplayDirectory = ResolveBaseGameplayDirectory();
        if (string.IsNullOrWhiteSpace(gameplayDirectory))
            return result;

        var tacticsDirectory = Path.Combine(gameplayDirectory, "tactics");
        if (!Directory.Exists(tacticsDirectory))
            return result;

        foreach (var path in Directory.GetFiles(tacticsDirectory, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            var name = Path.GetFileName(path);
            if (!(name.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase) ||
                  name.EndsWith(".tactics.xmb", StringComparison.OrdinalIgnoreCase) ||
                  extension.Equals(".xmb", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                string? xml = null;
                if (name.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = File.ReadAllBytes(path);
                    xml = BarFormatConverter.XMBtoFormattedXmlString(bytes);
                }
                else
                {
                    xml = File.ReadAllText(path);
                }

                if (string.IsNullOrWhiteSpace(xml))
                    continue;

                MergeTacticsActionTypes(result, xml);
            }
            catch
            {
                // Ignore unreadable tactics files and keep scanning.
            }
        }

        return result;
    }

    private bool UnitNameExistsAnywhere(string name, string? excludeName = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.Trim();
        if (!string.IsNullOrWhiteSpace(excludeName) &&
            normalized.Equals(excludeName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (_modXmlRoot != null && ProtoXmlHandler.UnitExists(_modXmlRoot, normalized)) ||
               (_barXmlRoot != null && ProtoXmlHandler.UnitExists(_barXmlRoot, normalized));
    }

    private void RefreshProtoActionMetadata(Dictionary<string, string>? tacticsTypes = null)
    {
        if (tacticsTypes != null)
        {
            _globalTacticsActionTypeMap.Clear();
            foreach (var kvp in tacticsTypes)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    _globalTacticsActionTypeMap[kvp.Key] = kvp.Value;
            }
        }

        _protoActionTypeMap.Clear();
        _knownProtoActionNames.Clear();

        if (_barData != null)
        {
            foreach (var kvp in _barData.ProtoActionTypes)
            {
                _knownProtoActionNames.Add(kvp.Key);
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                    _protoActionTypeMap[kvp.Key] = kvp.Value;
            }
        }

        if (_modXmlRoot != null)
        {
            foreach (var unitName in ProtoXmlHandler.GetUnitNames(_modXmlRoot))
            {
                var unit = ProtoXmlHandler.GetUnitElement(_modXmlRoot, unitName);
                if (unit == null)
                    continue;

                foreach (var action in ProtoXmlHandler.GetProtoActions(unit))
                {
                    if (!string.IsNullOrWhiteSpace(action.Name))
                    {
                        _knownProtoActionNames.Add(action.Name);
                        if (!string.IsNullOrWhiteSpace(action.Type))
                            _protoActionTypeMap[action.Name] = action.Type;
                    }
                }
            }
        }

        foreach (var kvp in _globalTacticsActionTypeMap)
        {
            _knownProtoActionNames.Add(kvp.Key);
            if (!string.IsNullOrWhiteSpace(kvp.Value))
                _protoActionTypeMap[kvp.Key] = kvp.Value;
        }

        _protoActionNameSuggestions = _knownProtoActionNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _protoActionTypeSuggestions = _protoActionTypeMap.Values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolveProtoActionType(string actionName, string currentType = "")
    {
        if (!string.IsNullOrWhiteSpace(currentType))
            return currentType;

        if (TryResolveProtoActionType(actionName, out var mappedType))
            return mappedType;

        return currentType;
    }

    private bool TryResolveProtoActionType(string actionName, out string mappedType)
    {
        mappedType = "";
        if (string.IsNullOrWhiteSpace(actionName))
            return false;

        var normalizedName = actionName.Trim();

        if (_currentUnitProtoActionTypeMap.TryGetValue(normalizedName, out var unitProtoType) &&
            !string.IsNullOrWhiteSpace(unitProtoType))
        {
            mappedType = unitProtoType;
            return true;
        }

        if (_currentUnitTacticsActionTypeMap.TryGetValue(normalizedName, out var unitTacticsType) &&
            !string.IsNullOrWhiteSpace(unitTacticsType))
        {
            mappedType = unitTacticsType;
            return true;
        }

        if (_protoActionTypeMap.TryGetValue(normalizedName, out var globalType) &&
            !string.IsNullOrWhiteSpace(globalType))
        {
            mappedType = globalType;
            return true;
        }

        mappedType = "";
        return false;
    }

    private List<string> GetProtoActionTypeOptions(string? currentType = null)
    {
        var options = _protoActionTypeSuggestions;

        var set = new HashSet<string>(options.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(currentType))
            set.Add(currentType);

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string GetExactProtoActionTypeMatch(string? value)
    {
        var normalized = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        return _protoActionTypeSuggestions.FirstOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private string GetProtoActionTypeEditorValue(ComboBox typeCb)
    {
        var selected = typeCb.SelectedItem as string ?? typeCb.SelectedValue as string;
        if (!string.IsNullOrWhiteSpace(selected))
            return selected;

        if (typeCb.IsEditable)
            return typeCb.Text?.Trim() ?? "";

        return "";
    }

    private void EnableDropdownAutoComplete(AutoCompleteBox autoCompleteBox)
    {
        autoCompleteBox.MinimumPrefixLength = 0;
        autoCompleteBox.MinimumPopulateDelay = TimeSpan.Zero;
        bool suppressAutoOpen = false;
        bool userInteracted = false;

        void OpenDropdownIfEnabled()
        {
            if (_isPopulating || !autoCompleteBox.IsEnabled || suppressAutoOpen)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (!_isPopulating && autoCompleteBox.IsEnabled && !suppressAutoOpen)
                    autoCompleteBox.IsDropDownOpen = true;
            });
        }

        autoCompleteBox.AddHandler(InputElement.PointerPressedEvent, (sender, e) =>
        {
            if (_isPopulating || !autoCompleteBox.IsEnabled)
                return;

            userInteracted = true;
            suppressAutoOpen = false;
            OpenDropdownIfEnabled();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        autoCompleteBox.SelectionChanged += (s, e) =>
        {
            suppressAutoOpen = true;
            autoCompleteBox.IsDropDownOpen = false;
        };

        autoCompleteBox.TextChanged += (s, e) =>
        {
            if (_isPopulating || !autoCompleteBox.IsEnabled)
                return;

            if (userInteracted && string.IsNullOrWhiteSpace(autoCompleteBox.Text))
            {
                suppressAutoOpen = false;
                OpenDropdownIfEnabled();
            }
        };

        autoCompleteBox.LostFocus += (s, e) =>
        {
            suppressAutoOpen = false;
        };
    }

    private void UpdateProtoActionTypeEditor(ComboBox typeCb, string actionName)
    {
        if (TryResolveProtoActionType(actionName, out var mappedType) && !string.IsNullOrWhiteSpace(mappedType))
        {
            typeCb.ItemsSource = GetProtoActionTypeOptions(mappedType);
            typeCb.SelectedItem = mappedType;
            typeCb.Text = mappedType;
            typeCb.IsEnabled = false;
            typeCb.IsDropDownOpen = false;
            return;
        }

        var currentValue = GetProtoActionTypeEditorValue(typeCb);
        var exactType = GetExactProtoActionTypeMatch(currentValue);
        if (!string.IsNullOrWhiteSpace(exactType))
        {
            typeCb.SelectedItem = exactType;
            typeCb.Text = exactType;
        }

        typeCb.ItemsSource = GetProtoActionTypeOptions(string.IsNullOrWhiteSpace(exactType) ? currentValue : exactType);
        typeCb.IsEnabled = !_isReadOnly;
    }

    private void RefreshCurrentUnitProtoActionMetadata(XElement unit)
    {
        _currentUnitProtoActionTypeMap.Clear();
        _currentUnitTacticsActionTypeMap.Clear();

        foreach (var action in ProtoXmlHandler.GetProtoActions(unit))
        {
            if (!string.IsNullOrWhiteSpace(action.Name) && !string.IsNullOrWhiteSpace(action.Type))
                _currentUnitProtoActionTypeMap[action.Name.Trim()] = action.Type.Trim();
        }

        var tacticsName = ProtoXmlHandler.GetSimpleField(unit, "tactics")?.Trim() ?? "";
        foreach (var kvp in LoadProtoActionTypesForTactics(tacticsName))
        {
            if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                _currentUnitTacticsActionTypeMap[kvp.Key.Trim()] = kvp.Value.Trim();
        }
    }

    private Dictionary<string, string> LoadProtoActionTypesForTactics(string tacticsName)
    {
        if (string.IsNullOrWhiteSpace(tacticsName))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var cacheKey = tacticsName.Trim();
        if (_tacticsActionTypeCache.TryGetValue(cacheKey, out var cached))
            return cached;

        foreach (var path in GetTacticsCandidatePaths(cacheKey))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                var xml = path.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase)
                    ? BarFormatConverter.XMBtoFormattedXmlString(File.ReadAllBytes(path))
                    : File.ReadAllText(path);

                if (!string.IsNullOrWhiteSpace(xml))
                {
                    var parsed = ParseTacticsActionTypes(xml);
                    if (parsed.Count > 0)
                    {
                        _tacticsActionTypeCache[cacheKey] = parsed;
                        return parsed;
                    }
                }
            }
            catch
            {
                // Try the next candidate path.
            }
        }

        var barResolved = LoadProtoActionTypesFromBarTactics(cacheKey);
        _tacticsActionTypeCache[cacheKey] = barResolved;
        return barResolved;
    }

    private IEnumerable<string> GetTacticsCandidatePaths(string tacticsName)
    {
        var relatives = BuildTacticsCandidateRelativePaths(tacticsName);

        if (!string.IsNullOrWhiteSpace(_modFilePath))
        {
            var gameplayDir = Path.GetDirectoryName(_modFilePath);
            if (!string.IsNullOrWhiteSpace(gameplayDir))
            {
                foreach (var relative in relatives)
                    yield return Path.Combine(gameplayDir, "tactics", relative);
            }
        }

        var baseGameplayDir = ResolveBaseGameplayDirectory();
        if (!string.IsNullOrWhiteSpace(baseGameplayDir))
        {
            foreach (var relative in relatives)
                yield return Path.Combine(baseGameplayDir, "tactics", relative);
        }
    }

    private static List<string> BuildTacticsCandidateRelativePaths(string tacticsName)
    {
        var normalized = tacticsName
            .Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var tacticsPrefix = $"tactics{Path.DirectorySeparatorChar}";
        if (normalized.StartsWith(tacticsPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[tacticsPrefix.Length..];

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                candidates.Add(candidate);
        }

        AddCandidate(normalized);

        if (normalized.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(normalized + ".XMB");
        }
        else if (normalized.EndsWith(".tactics.xmb", StringComparison.OrdinalIgnoreCase) ||
                 normalized.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase))
        {
            // Exact path already added.
        }
        else
        {
            AddCandidate(normalized + ".tactics");
            AddCandidate(normalized + ".tactics.XMB");
            AddCandidate(normalized + ".XMB");
        }

        return candidates.ToList();
    }

    private Dictionary<string, string> LoadProtoActionTypesFromBarTactics(string tacticsName)
    {
        try
        {
            var candidateFileNames = BuildTacticsCandidateRelativePaths(tacticsName)
                .Select(Path.GetFileName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (candidateFileNames.Count == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var barFile = _mainWindow.BarFile;
            var barStream = _mainWindow.BarFileStream;
            if (barFile != null && barStream != null &&
                Path.GetFileName(barStream.Name).Equals("Data.bar", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractTacticsActionTypesFromBar(barFile, barStream.Name, candidateFileNames);
            }

            var dataBarPath = ResolveDataBarPath();
            if (!string.IsNullOrWhiteSpace(dataBarPath) && File.Exists(dataBarPath))
            {
                using var stream = File.OpenRead(dataBarPath);
                var file = new BarFile(stream);
                if (file.Load(out _))
                    return ExtractTacticsActionTypesFromBar(file, dataBarPath, candidateFileNames);
            }
        }
        catch
        {
            // Fall through to an empty result if BAR lookup is unavailable.
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ExtractTacticsActionTypesFromBar(BarFile barFile, string barPath, HashSet<string> candidateFileNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries;
        if (entries == null || candidateFileNames.Count == 0)
            return result;

        var tacticsEntries = entries
            .Where(e => e.Name.Contains("tactics", StringComparison.OrdinalIgnoreCase)
                     && e.Name.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase)
                     && candidateFileNames.Contains(Path.GetFileName(e.Name.Replace('/', '\\'))))
            .ToList();

        using var tempStream = File.OpenRead(barPath);
        foreach (var entry in tacticsEntries)
        {
            int size = entry.IsCompressed ? entry.SizeUncompressed : entry.SizeInArchive;
            byte[] decompressed = new byte[size];
            int readBytes = entry.ReadDataDecompressed(tempStream, decompressed);
            if (readBytes <= 0)
                continue;

            var xml = BarFormatConverter.XMBtoFormattedXmlString(decompressed.AsSpan(0, readBytes));
            if (string.IsNullOrWhiteSpace(xml))
                continue;

            MergeTacticsActionTypes(result, xml);
        }

        return result;
    }

    private static Dictionary<string, string> ParseTacticsActionTypes(string xml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        MergeTacticsActionTypes(result, xml);
        return result;
    }

    private static void MergeTacticsActionTypes(Dictionary<string, string> result, string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        foreach (var action in doc.Descendants("action"))
        {
            var actionName = action.Element("name")?.Value?.Trim();
            var actionType = action.Element("type")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(actionName) && !string.IsNullOrWhiteSpace(actionType))
                result[actionName] = actionType;
        }
    }

    private void BuildEditorPanel(string unitName)
    {
        _isPopulating = true;
        _editorPanel.Children.Clear();
        _fieldControls.Clear();
        _currentStringFieldIds.Clear();
        _costControls.Clear();
        _armorControls.Clear();
        _trainCommandRows.Clear();
        _techCommandRows.Clear();
        _protoActionWidgets.Clear();

        XElement? unit = null;
        if (_modXmlRoot != null)
        {
            unit = ProtoXmlHandler.GetUnitElement(_modXmlRoot, unitName);
        }

        if (unit == null && _barXmlRoot != null)
        {
            unit = ProtoXmlHandler.GetUnitElement(_barXmlRoot, unitName);
            _isReadOnly = true;
        }
        else
        {
            _isReadOnly = false;
        }

        if (unit == null)
        {
            _isPopulating = false;
            return;
        }

        _currentUnitName = unitName;
        RefreshCurrentUnitProtoActionMetadata(unit);

        var header = new TextBlock
        {
            Text = $"Editing: {unitName} " + (_isReadOnly ? "(Original - Read-only)" : "(Mod - Editable)"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brush.Parse("#e3bd54")
        };
        _editorPanel.Children.Add(header);

        AddSectionHeader("Properties");

        var propertiesGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180, *"),
        };
        int gridRow = 0;

        propertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var protoNameLabel = new TextBlock
        {
            Text = "Proto Unit Name",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 10, 4)
        };
        Grid.SetColumn(protoNameLabel, 0);
        Grid.SetRow(protoNameLabel, gridRow);
        propertiesGrid.Children.Add(protoNameLabel);

        var protoNameBox = new TextBox
        {
            Text = unitName,
            IsReadOnly = _isReadOnly,
            IsEnabled = !_isReadOnly,
            Margin = new Thickness(0, 4, 0, 4)
        };
        protoNameBox.TextChanged += async (s, e) =>
        {
            if (!_isPopulating)
            {
                var proceed = await CheckStartLocalMod();
                if (proceed) MarkDirty();
            }
        };
        Grid.SetColumn(protoNameBox, 1);
        Grid.SetRow(protoNameBox, gridRow);
        propertiesGrid.Children.Add(protoNameBox);
        _fieldControls[ProtoUnitNameFieldKey] = protoNameBox;
        gridRow++;

        foreach (var field in ProtoConstants.SimpleFields)
        {
            if (field.Tag.Equals("buildlimit", StringComparison.OrdinalIgnoreCase) ||
                field.Tag.Equals("tactics", StringComparison.OrdinalIgnoreCase))
                continue;

            propertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = field.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            };
            Grid.SetColumn(label, 0);
            Grid.SetRow(label, gridRow);
            propertiesGrid.Children.Add(label);

            string initialValue = ProtoXmlHandler.GetSimpleField(unit, field.Tag) ?? "";
            if (field.Tag.Equals("editornameid", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(initialValue))
                initialValue = ProtoXmlHandler.GetSimpleField(unit, "displaynameid") ?? "";
            if (IsStringBackedField(field.Tag))
                _currentStringFieldIds[field.Tag] = initialValue;

            Control control;
            if (field.Mode == FieldInputMode.Suggest)
            {
                if (ProtoConstants.FieldSuggestions.TryGetValue(field.Tag, out var list))
                {
                    if (IsSelectionOnlySimpleField(field.Tag))
                    {
                        var combo = new ComboBox
                        {
                            ItemsSource = list,
                            SelectedItem = list.FirstOrDefault(x => x.Equals(initialValue, StringComparison.OrdinalIgnoreCase)),
                            IsEnabled = !_isReadOnly,
                            Margin = new Thickness(0, 4, 0, 4)
                        };
                        combo.SelectionChanged += async (s, e) =>
                        {
                            if (!_isPopulating)
                            {
                                var proceed = await CheckStartLocalMod();
                                if (proceed) MarkDirty();
                            }
                        };
                        control = combo;
                    }
                    else
                    {
                        var acb = new AutoCompleteBox
                        {
                            Text = initialValue,
                            FilterMode = AutoCompleteFilterMode.Contains,
                            IsEnabled = !_isReadOnly,
                            Margin = new Thickness(0, 4, 0, 4),
                            ItemsSource = list
                        };

                        acb.TextChanged += async (s, e) =>
                        {
                            if (!_isPopulating)
                            {
                                var proceed = await CheckStartLocalMod();
                                if (proceed) MarkDirty();
                            }
                        };

                        control = acb;
                    }
                }
                else
                {
                    var acb = new AutoCompleteBox
                    {
                        Text = initialValue,
                        FilterMode = AutoCompleteFilterMode.Contains,
                        IsEnabled = !_isReadOnly,
                        Margin = new Thickness(0, 4, 0, 4)
                    };

                    acb.TextChanged += async (s, e) =>
                    {
                        if (!_isPopulating)
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed) MarkDirty();
                        }
                    };

                    control = acb;
                }
            }
            else
            {
                var tb = new TextBox
                {
                    Text = IsStringBackedField(field.Tag) ? "" : initialValue,
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 4, 0, 4)
                };

                tb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };

                control = tb;
            }

            Grid.SetColumn(control, 1);
            Grid.SetRow(control, gridRow);
            propertiesGrid.Children.Add(control);

            _fieldControls[field.Tag] = control;
            gridRow++;
        }

        _editorPanel.Children.Add(propertiesGrid);

        string? blVal = ProtoXmlHandler.GetSimpleField(unit, "buildlimit");
        var buildLimitContainer = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        _editorPanel.Children.Add(buildLimitContainer);

        void ShowBuildLimit(string initialLimit)
        {
            buildLimitContainer.Children.Clear();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *, Auto") };

            var lbl = new TextBlock { Text = "Build Limit", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var tb = new TextBox { Text = initialLimit, IsEnabled = !_isReadOnly, Margin = new Thickness(0, 0, 10, 0) };
            tb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        if (int.TryParse(tb.Text, out int num) && num <= 0)
                        {
                            tb.Text = "1";
                        }
                        MarkDirty();
                    }
                }
            };
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);
            _fieldControls["buildlimit"] = tb;

            if (!_isReadOnly)
            {
                var btnDel = new Button { Content = "✕", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                btnDel.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _fieldControls.Remove("buildlimit");
                        MarkDirty();
                        ShowAddBuildLimitButton();
                    }
                };
                Grid.SetColumn(btnDel, 2);
                grid.Children.Add(btnDel);
            }
            buildLimitContainer.Children.Add(grid);
        }

        void ShowAddBuildLimitButton()
        {
            buildLimitContainer.Children.Clear();
            if (!_isReadOnly)
            {
                var btnAdd = new Button { Content = "+ Add a Build Limit", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(0, 4, 0, 4) };
                btnAdd.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        MarkDirty();
                        ShowBuildLimit("1");
                    }
                };
                buildLimitContainer.Children.Add(btnAdd);
            }
        }

        if (!string.IsNullOrEmpty(blVal))
        {
            ShowBuildLimit(blVal);
        }
        else
        {
            ShowAddBuildLimitButton();
        }

        AddSectionHeader("Costs");
        var costs = ProtoXmlHandler.GetCostEntries(unit).ToDictionary(c => c.ResourceType, c => c.Amount, StringComparer.OrdinalIgnoreCase);
        var costsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *") };
        int costRow = 0;
        foreach (var rtype in ProtoConstants.KnownResourceTypes)
        {
            costsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock { Text = rtype, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(lbl, 0);
            Grid.SetRow(lbl, costRow);
            costsGrid.Children.Add(lbl);

            string initialCost = costs.TryGetValue(rtype, out var val) ? val : "0";
            var tb = new TextBox { Text = initialCost, IsEnabled = !_isReadOnly, Margin = new Thickness(0, 4, 0, 4) };
            tb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        if (double.TryParse(tb.Text, out double num) && num < 0)
                        {
                            tb.Text = "0";
                        }
                        MarkDirty();
                    }
                }
            };
            Grid.SetColumn(tb, 1);
            Grid.SetRow(tb, costRow);
            costsGrid.Children.Add(tb);
            _costControls[rtype] = tb;

            costRow++;
        }
        _editorPanel.Children.Add(costsGrid);

        AddSectionHeader("Armor");
        var armors = ProtoXmlHandler.GetArmorEntries(unit).ToDictionary(a => a.ArmorType, a => a.Value, StringComparer.OrdinalIgnoreCase);
        var armorGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *") };
        int armorRow = 0;
        foreach (var atype in ProtoConstants.KnownArmorTypes)
        {
            armorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock { Text = atype, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(lbl, 0);
            Grid.SetRow(lbl, armorRow);
            armorGrid.Children.Add(lbl);

            string initialArmor = armors.TryGetValue(atype, out var val) ? val : "0";
            var tb = new TextBox { Text = initialArmor, IsEnabled = !_isReadOnly, Margin = new Thickness(0, 4, 0, 4) };
            tb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        if (string.IsNullOrWhiteSpace(tb.Text))
                        {
                            tb.Text = "0";
                        }
                        else if (double.TryParse(tb.Text, out double num))
                        {
                            if (num < 0) tb.Text = "0";
                            else if (num > 1) tb.Text = "1";
                        }
                        MarkDirty();
                    }
                }
            };
            Grid.SetColumn(tb, 1);
            Grid.SetRow(tb, armorRow);
            armorGrid.Children.Add(tb);
            _armorControls[atype] = tb;

            armorRow++;
        }
        _editorPanel.Children.Add(armorGrid);

        AddSectionHeader("Unit Types");
        var selectedTypes = new HashSet<string>(ProtoXmlHandler.GetUnitTypeList(unit), StringComparer.OrdinalIgnoreCase);
        var typesWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        _editorPanel.Children.Add(typesWrap);

        void RefreshTypesDisplay()
        {
            typesWrap.Children.Clear();
            foreach (var t in selectedTypes.OrderBy(x => x))
            {
                var chip = CreateChip(t, async () =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        selectedTypes.Remove(t);
                        MarkDirty();
                        RefreshTypesDisplay();
                    }
                });
                typesWrap.Children.Add(chip);
            }
        }
        RefreshTypesDisplay();

        if (!_isReadOnly)
        {
            var addTypeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto"), Margin = new Thickness(0, 4, 0, 4) };
            var acbAdd = new AutoCompleteBox
            {
                FilterMode = AutoCompleteFilterMode.Contains,
                ItemsSource = (_barData != null ? _barData.UnitTypes : Enumerable.Empty<string>())
                                .Concat(ProtoConstants.KnownUnitTypes).Distinct().OrderBy(x => x).ToList(),
                Margin = new Thickness(0, 0, 10, 0)
            };
            EnableDropdownAutoComplete(acbAdd);
            Grid.SetColumn(acbAdd, 0);
            addTypeGrid.Children.Add(acbAdd);

            var btnAdd = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b") };
            async void PerformAddType()
            {
                string t = acbAdd.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(t) && !selectedTypes.Contains(t))
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        selectedTypes.Add(t);
                        acbAdd.Text = "";
                        MarkDirty();
                        RefreshTypesDisplay();
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (acbAdd.IsEnabled && string.IsNullOrWhiteSpace(acbAdd.Text))
                                acbAdd.IsDropDownOpen = true;
                        });
                    }
                }
            }
            btnAdd.Click += (s, e) => PerformAddType();
            acbAdd.SelectionChanged += (s, e) =>
            {
                if (acbAdd.SelectedItem is string sel)
                {
                    acbAdd.Text = sel;
                    PerformAddType();
                }
            };
            Grid.SetColumn(btnAdd, 1);
            addTypeGrid.Children.Add(btnAdd);
            _editorPanel.Children.Add(addTypeGrid);
            _currentUnitTypes = selectedTypes;
        }

        AddSectionHeader("Flags");
        var selectedFlags = new HashSet<string>(ProtoXmlHandler.GetFlagList(unit), StringComparer.OrdinalIgnoreCase);
        var flagsWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        _editorPanel.Children.Add(flagsWrap);

        void RefreshFlagsDisplay()
        {
            flagsWrap.Children.Clear();
            foreach (var f in selectedFlags.OrderBy(x => x))
            {
                var chip = CreateChip(f, async () =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        selectedFlags.Remove(f);
                        MarkDirty();
                        RefreshFlagsDisplay();
                    }
                });
                flagsWrap.Children.Add(chip);
            }
        }
        RefreshFlagsDisplay();
        _currentFlags = selectedFlags;

        if (!_isReadOnly)
        {
            var availableFlags = (_barData != null ? _barData.Flags : Enumerable.Empty<string>())
                .Concat(ProtoConstants.KnownFlags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            var addFlagGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto"), Margin = new Thickness(0, 4, 0, 4) };
            var acbAdd = new AutoCompleteBox
            {
                FilterMode = AutoCompleteFilterMode.Contains,
                ItemsSource = availableFlags,
                Margin = new Thickness(0, 0, 10, 0)
            };
            EnableDropdownAutoComplete(acbAdd);
            Grid.SetColumn(acbAdd, 0);
            addFlagGrid.Children.Add(acbAdd);

            var btnAdd = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b") };
            async void PerformAddFlag()
            {
                string input = acbAdd.Text?.Trim() ?? "";
                string? match = availableFlags.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match) && !selectedFlags.Contains(match))
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        selectedFlags.Add(match);
                        acbAdd.Text = "";
                        MarkDirty();
                        RefreshFlagsDisplay();
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (acbAdd.IsEnabled && string.IsNullOrWhiteSpace(acbAdd.Text))
                                acbAdd.IsDropDownOpen = true;
                        });
                    }
                }
            }
            btnAdd.Click += (s, e) => PerformAddFlag();
            acbAdd.SelectionChanged += (s, e) =>
            {
                if (acbAdd.SelectedItem is string sel)
                {
                    acbAdd.Text = sel;
                    PerformAddFlag();
                }
            };
            Grid.SetColumn(btnAdd, 1);
            addFlagGrid.Children.Add(btnAdd);
            _editorPanel.Children.Add(addFlagGrid);
        }

        AddCommandSection("Train Units", "Proto Unit", ProtoXmlHandler.GetTrainEntries(unit), GetAvailableTrainUnitNames(), _trainCommandRows);
        AddCommandSection("Research Techs", "Tech", ProtoXmlHandler.GetTechEntries(unit), GetAvailableTechNames(), _techCommandRows);

        AddSectionHeader("Tactics");
        var tacticsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *") };
        tacticsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tacticsLabel = new TextBlock
        {
            Text = "Tactics",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 10, 4)
        };
        Grid.SetColumn(tacticsLabel, 0);
        Grid.SetRow(tacticsLabel, 0);
        tacticsGrid.Children.Add(tacticsLabel);

        string initialTactics = ProtoXmlHandler.GetSimpleField(unit, "tactics") ?? "";
        Control tacticsControl;
        if (ProtoConstants.FieldSuggestions.TryGetValue("tactics", out var tacticSuggestions))
        {
            var acb = new AutoCompleteBox
            {
                Text = initialTactics,
                FilterMode = AutoCompleteFilterMode.Contains,
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 4, 0, 4),
                ItemsSource = tacticSuggestions
            };
            EnableDropdownAutoComplete(acb);
            acb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            tacticsControl = acb;
        }
        else
        {
            var tb = new TextBox
            {
                Text = initialTactics,
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 4, 0, 4)
            };
            tb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            tacticsControl = tb;
        }

        Grid.SetColumn(tacticsControl, 1);
        Grid.SetRow(tacticsControl, 0);
        tacticsGrid.Children.Add(tacticsControl);
        _fieldControls["tactics"] = tacticsControl;
        _editorPanel.Children.Add(tacticsGrid);

        AddSectionHeader("Proto Actions");
        var actions = ProtoXmlHandler.GetProtoActions(unit);
        var actionsContainer = new StackPanel { Spacing = 10 };
        _editorPanel.Children.Add(actionsContainer);

        void AddProtoActionWidget(ProtoAction pa)
        {
            var widget = CreateProtoActionWidget(pa, () =>
            {
                MarkDirty();
            });
            actionsContainer.Children.Add(widget);
        }

        foreach (var action in actions)
        {
            AddProtoActionWidget(action);
        }

        if (!_isReadOnly)
        {
            var btnAddAction = new Button
            {
                Content = "+ Add Proto Action",
                Background = Brush.Parse("#2b7a0b"),
                Margin = new Thickness(0, 10, 0, 10)
            };
            btnAddAction.Click += async (s, e) =>
            {
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    var newPa = new ProtoAction();
                    AddProtoActionWidget(newPa);
                    MarkDirty();
                }
            };
            _editorPanel.Children.Add(btnAddAction);
        }

        _ = PopulateStringFieldDisplaysAsync();
    }

    private async Task PopulateStringFieldDisplaysAsync()
    {
        try
        {
            foreach (var tag in StringBackedFieldTags)
            {
                if (!_fieldControls.TryGetValue(tag, out var ctrl) || ctrl is not TextBox tb)
                    continue;

                var stringId = _currentStringFieldIds.GetValueOrDefault(tag);
                var value = await ResolveDisplayStringAsync(stringId);
                tb.Text = value ?? stringId ?? "";
            }
        }
        finally
        {
            _isPopulating = false;
        }
    }

    private Border CreateChip(string text, Action onRemove)
    {
        var border = new Border
        {
            Background = Brush.Parse("#3a5a78"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(2)
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        var tb = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(tb);

        if (!_isReadOnly)
        {
            var btn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                Foreground = Brushes.LightGray,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 0),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            btn.Click += (s, e) => onRemove();
            stack.Children.Add(btn);
        }

        border.Child = stack;
        return border;
    }

    private Border CreateProtoActionWidget(ProtoAction pa, Action onRemove)
    {
        string resolvedType = ResolveProtoActionType(pa.Name, pa.Type);
        var typeOptions = GetProtoActionTypeOptions(resolvedType);

        var border = new Border
        {
            Background = Brush.Parse("#1c1c1c"),
            BorderBrush = Brush.Parse("#3f3f46"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 4)
        };

        var mainStack = new StackPanel { Spacing = 8 };
        border.Child = mainStack;

        var header = new DockPanel();
        mainStack.Children.Add(header);

        var nameLabel = new TextBlock { Text = "Name:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        DockPanel.SetDock(nameLabel, Dock.Left);
        header.Children.Add(nameLabel);

        var nameAcb = new AutoCompleteBox
        {
            Text = pa.Name,
            FilterMode = AutoCompleteFilterMode.Contains,
            ItemsSource = _protoActionNameSuggestions,
            Width = 180,
            IsEnabled = !_isReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        EnableDropdownAutoComplete(nameAcb);
        DockPanel.SetDock(nameAcb, Dock.Left);
        header.Children.Add(nameAcb);

        var typeLabel = new TextBlock { Text = "Type:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        DockPanel.SetDock(typeLabel, Dock.Left);
        header.Children.Add(typeLabel);

        var typeCb = new ComboBox
        {
            ItemsSource = typeOptions,
            SelectedItem = !string.IsNullOrWhiteSpace(resolvedType)
                ? typeOptions.FirstOrDefault(x => x.Equals(resolvedType, StringComparison.OrdinalIgnoreCase))
                : null,
            Text = resolvedType,
            IsEditable = true,
            Width = 150,
            IsEnabled = !_isReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(typeCb, Dock.Left);
        header.Children.Add(typeCb);

        var state = new ProtoActionWidgetState
        {
            Container = mainStack,
            NameAcb = nameAcb,
            TypeCb = typeCb,
            RofTb = null!,
            MaxRangeTb = null!
        };
        _protoActionWidgets.Add(state);
        UpdateProtoActionTypeEditor(typeCb, pa.Name);

        if (!_isReadOnly)
        {
            var btnRemove = new Button
            {
                Content = "🗑 Remove",
                Background = Brush.Parse("#8b0000"),
                VerticalAlignment = VerticalAlignment.Center
            };
            btnRemove.Click += async (s, e) =>
            {
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    _protoActionWidgets.Remove(state);
                    onRemove();
                    if (border.Parent is Panel p)
                    {
                        p.Children.Remove(border);
                    }
                }
            };
            DockPanel.SetDock(btnRemove, Dock.Right);
            header.Children.Add(btnRemove);
        }

        nameAcb.TextChanged += async (s, e) =>
        {
            if (!_isPopulating)
            {
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    string name = nameAcb.Text?.Trim() ?? "";
                    if (TryResolveProtoActionType(name, out string? mappedType) && !string.IsNullOrEmpty(mappedType))
                    {
                        typeCb.ItemsSource = GetProtoActionTypeOptions(mappedType);
                    }
                    else
                    {
                        typeCb.ItemsSource = GetProtoActionTypeOptions(GetProtoActionTypeEditorValue(typeCb));
                    }

                    UpdateProtoActionTypeEditor(typeCb, name);
                    MarkDirty();
                }
            }
        };

        typeCb.SelectionChanged += (s, e) =>
        {
            var selectedType =
                e.AddedItems.OfType<string>().FirstOrDefault() ??
                typeCb.SelectedItem as string;

            if (!string.IsNullOrWhiteSpace(selectedType))
            {
                typeCb.Text = selectedType;
            }

            if (_isPopulating)
                return;

            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var proceed = await CheckStartLocalMod();
                if (!proceed)
                    return;

                var matchedType = GetExactProtoActionTypeMatch(GetProtoActionTypeEditorValue(typeCb));
                if (!string.IsNullOrWhiteSpace(matchedType))
                {
                    typeCb.SelectedItem = matchedType;
                    typeCb.Text = matchedType;
                }

                MarkDirty();
            });
        };

        typeCb.LostFocus += (s, e) =>
        {
            if (typeCb.IsEnabled)
            {
                var matchedType = GetExactProtoActionTypeMatch(GetProtoActionTypeEditorValue(typeCb));
                if (!string.IsNullOrWhiteSpace(matchedType))
                {
                    typeCb.SelectedItem = matchedType;
                    typeCb.Text = matchedType;
                }
            }
        };
        typeCb.GotFocus += (s, e) =>
        {
            if (typeCb.IsEnabled)
            {
                typeCb.ItemsSource = GetProtoActionTypeOptions(GetProtoActionTypeEditorValue(typeCb));
                typeCb.IsDropDownOpen = true;
            }
        };

        var fieldsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto, 80, Auto, 80"),
            Margin = new Thickness(0, 4, 0, 4)
        };
        mainStack.Children.Add(fieldsGrid);

        var rofLabel = new TextBlock { Text = "Rate of Fire:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(rofLabel, 0);
        fieldsGrid.Children.Add(rofLabel);

        var rofTb = new TextBox { Text = pa.Rof, IsEnabled = !_isReadOnly, Margin = new Thickness(0, 0, 10, 0) };
        rofTb.TextChanged += async (s, e) =>
        {
            if (!_isPopulating)
            {
                var proceed = await CheckStartLocalMod();
                if (proceed) MarkDirty();
            }
        };
        Grid.SetColumn(rofTb, 1);
        fieldsGrid.Children.Add(rofTb);
        state.RofTb = rofTb;

        var mrLabel = new TextBlock { Text = "Max Range:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        Grid.SetColumn(mrLabel, 2);
        fieldsGrid.Children.Add(mrLabel);

        var mrTb = new TextBox { Text = pa.MaxRange, IsEnabled = !_isReadOnly };
        mrTb.TextChanged += async (s, e) =>
        {
            if (!_isPopulating)
            {
                var proceed = await CheckStartLocalMod();
                if (proceed) MarkDirty();
            }
        };
        Grid.SetColumn(mrTb, 3);
        fieldsGrid.Children.Add(mrTb);
        state.MaxRangeTb = mrTb;

        var dmgLabel = new TextBlock { Text = "Damage:", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 2) };
        mainStack.Children.Add(dmgLabel);

        var dmgContainer = new StackPanel { Spacing = 4 };
        mainStack.Children.Add(dmgContainer);

        void AddDamageRow(string dtype, string dval)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            var typeCb = new ComboBox
            {
                ItemsSource = ProtoConstants.KnownDamageTypes,
                SelectedItem = ProtoConstants.KnownDamageTypes.FirstOrDefault(x => x.Equals(dtype, StringComparison.OrdinalIgnoreCase)) ?? dtype,
                Width = 120,
                IsEnabled = !_isReadOnly
            };
            typeCb.SelectionChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            rowPanel.Children.Add(typeCb);

            var valTb = new TextBox { Text = dval, IsEnabled = !_isReadOnly, Width = 80 };
            valTb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            rowPanel.Children.Add(valTb);

            var rowState = new DamageRowState { RowPanel = rowPanel, TypeCb = typeCb, ValTb = valTb };
            state.DamageRows.Add(rowState);

            if (!_isReadOnly)
            {
                var btnDel = new Button { Content = "✕", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                btnDel.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        state.DamageRows.Remove(rowState);
                        dmgContainer.Children.Remove(rowPanel);
                        MarkDirty();
                    }
                };
                rowPanel.Children.Add(btnDel);
            }

            dmgContainer.Children.Add(rowPanel);
        }

        foreach (var dmg in pa.Damages)
        {
            AddDamageRow(dmg.DamageType, dmg.Amount);
        }

        if (!_isReadOnly)
        {
            var btnAddDmg = new Button { Content = "+ Damage", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(0, 2, 0, 2) };
            btnAddDmg.Click += async (s, e) =>
            {
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    AddDamageRow("Hack", "0");
                    MarkDirty();
                }
            };
            mainStack.Children.Add(btnAddDmg);
        }

        var bonusLabel = new TextBlock { Text = "Damage Bonuses:", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 2) };
        mainStack.Children.Add(bonusLabel);

        var bonusContainer = new StackPanel { Spacing = 4 };
        mainStack.Children.Add(bonusContainer);

        void AddBonusRow(string btype, string bval)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            var typeAcb = new AutoCompleteBox
            {
                Text = btype,
                FilterMode = AutoCompleteFilterMode.Contains,
                ItemsSource = (_barData != null ? _barData.UnitTypes.Concat(ProtoConstants.KnownUnitTypes).Concat(_barData.UnitNames) : Enumerable.Empty<string>()).Distinct().OrderBy(x => x).ToList(),
                Width = 180,
                IsEnabled = !_isReadOnly
            };
            typeAcb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            rowPanel.Children.Add(typeAcb);

            var valTb = new TextBox { Text = bval, IsEnabled = !_isReadOnly, Width = 80 };
            valTb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed) MarkDirty();
                }
            };
            rowPanel.Children.Add(valTb);

            var rowState = new BonusRowState { RowPanel = rowPanel, TypeAcb = typeAcb, ValTb = valTb };
            state.BonusRows.Add(rowState);

            if (!_isReadOnly)
            {
                var btnDel = new Button { Content = "✕", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                btnDel.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        state.BonusRows.Remove(rowState);
                        bonusContainer.Children.Remove(rowPanel);
                        MarkDirty();
                    }
                };
                rowPanel.Children.Add(btnDel);
            }

            bonusContainer.Children.Add(rowPanel);
        }

        foreach (var db in pa.DamageBonuses)
        {
            AddBonusRow(db.UnitType, db.Multiplier);
        }

        if (!_isReadOnly)
        {
            var btnAddBonus = new Button { Content = "+ Bonus", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(0, 2, 0, 10) };
            btnAddBonus.Click += async (s, e) =>
            {
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    AddBonusRow("AbstractInfantry", "1.0");
                    MarkDirty();
                }
            };
            mainStack.Children.Add(btnAddBonus);
        }

        return border;
    }

    private void ApplyCurrentEdits()
    {
        if (string.IsNullOrEmpty(_currentUnitName) || _modXmlRoot == null) return;

        var unit = ProtoXmlHandler.GetUnitElement(_modXmlRoot, _currentUnitName);
        if (unit == null) return;

        if (_fieldControls.TryGetValue(ProtoUnitNameFieldKey, out var protoNameCtrl) && protoNameCtrl is TextBox protoNameTb)
        {
            var newUnitName = protoNameTb.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(newUnitName) &&
                !newUnitName.Equals(_currentUnitName, StringComparison.OrdinalIgnoreCase) &&
                !ProtoXmlHandler.UnitExists(_modXmlRoot, newUnitName))
            {
                var oldStringIds = _currentStringFieldIds.Values.ToList();
                var displayName = _fieldControls.TryGetValue("displaynameid", out var displayCtrl) && displayCtrl is TextBox displayTb
                    ? displayTb.Text?.Trim() ?? ""
                    : "";
                var longRollover = _fieldControls.TryGetValue("rollovertextid", out var longCtrl) && longCtrl is TextBox longTb
                    ? longTb.Text?.Trim() ?? ""
                    : "";
                var shortRollover = _fieldControls.TryGetValue("shortrollovertextid", out var shortCtrl) && shortCtrl is TextBox shortTb
                    ? shortTb.Text?.Trim() ?? ""
                    : "";

                unit.SetAttributeValue("name", newUnitName);
                _currentUnitName = newUnitName;
                AssignGeneratedStringIds(newUnitName);
                InitializeUnitStringValues(newUnitName, displayName, longRollover, shortRollover);
                RemoveStringEntries(oldStringIds);
            }
        }

        foreach (var field in ProtoConstants.SimpleFields)
        {
            if (field.Tag.Equals("buildlimit", StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsStringBackedField(field.Tag))
                continue;

            if (_fieldControls.TryGetValue(field.Tag, out var ctrl))
            {
                string val = "";
                if (ctrl is AutoCompleteBox acb) val = acb.Text?.Trim() ?? "";
                else if (ctrl is ComboBox cb) val = cb.SelectedItem as string ?? cb.SelectedValue as string ?? "";
                else if (ctrl is TextBox tb) val = tb.Text?.Trim() ?? "";

                if (!string.IsNullOrEmpty(val))
                    ProtoXmlHandler.SetSimpleField(unit, field.Tag, val);
                else
                    ProtoXmlHandler.RemoveSimpleField(unit, field.Tag);
            }
        }

        foreach (var tag in StringBackedFieldTags)
        {
            if (!_fieldControls.TryGetValue(tag, out var ctrl) || ctrl is not TextBox tb)
                continue;

            var id = _currentStringFieldIds.GetValueOrDefault(tag);
            if (!string.IsNullOrWhiteSpace(id))
                ProtoXmlHandler.SetSimpleField(unit, tag, id);
            else
                ProtoXmlHandler.RemoveSimpleField(unit, tag);
        }

        if (_fieldControls.TryGetValue("buildlimit", out var blCtrl) && blCtrl is TextBox blTb)
        {
            string val = blTb.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(val))
                ProtoXmlHandler.SetSimpleField(unit, "buildlimit", val);
            else
                ProtoXmlHandler.RemoveSimpleField(unit, "buildlimit");
        }
        else
        {
            ProtoXmlHandler.RemoveSimpleField(unit, "buildlimit");
        }

        var costs = new List<(string ResourceType, string Amount)>();
        foreach (var kvp in _costControls)
        {
            costs.Add((kvp.Key, kvp.Value.Text?.Trim() ?? "0"));
        }
        ProtoXmlHandler.SetCostEntries(unit, costs);

        var armors = new List<(string ArmorType, string Value)>();
        foreach (var kvp in _armorControls)
        {
            var value = kvp.Value.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                value = "0";
            else if (double.TryParse(value, out double num))
                value = Math.Clamp(num, 0, 1).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            else
                value = "0";

            armors.Add((kvp.Key, value));
        }
        ProtoXmlHandler.SetArmorEntries(unit, armors);

        if (_currentUnitTypes != null)
        {
            ProtoXmlHandler.SetUnitTypeList(unit, _currentUnitTypes.OrderBy(x => x));
        }

        if (_currentFlags != null)
        {
            ProtoXmlHandler.SetFlagList(unit, _currentFlags.OrderBy(x => x));
        }

        ProtoXmlHandler.SetTrainEntries(unit, CollectValidCommandEntries(_trainCommandRows, GetAvailableTrainUnitNames()));
        ProtoXmlHandler.SetTechEntries(unit, CollectValidCommandEntries(_techCommandRows, GetAvailableTechNames()));

        var actionsList = new List<ProtoAction>();
        foreach (var pw in _protoActionWidgets)
        {
            var pa = new ProtoAction
            {
                Name = pw.NameAcb.Text?.Trim() ?? "",
                Type = TryResolveProtoActionType(pw.NameAcb.Text?.Trim() ?? "", out var resolvedType)
                    ? resolvedType
                    : GetExactProtoActionTypeMatch(GetProtoActionTypeEditorValue(pw.TypeCb)),
                Rof = pw.RofTb.Text?.Trim() ?? "",
                MaxRange = pw.MaxRangeTb.Text?.Trim() ?? ""
            };

            foreach (var dr in pw.DamageRows)
            {
                string dtype = dr.TypeCb.SelectedItem as string ?? dr.TypeCb.Text ?? "";
                string dval = dr.ValTb.Text?.Trim() ?? "0";
                if (!string.IsNullOrEmpty(dtype))
                {
                    pa.Damages.Add((dtype, dval));
                }
            }

            foreach (var br in pw.BonusRows)
            {
                string btype = br.TypeAcb.Text?.Trim() ?? "";
                string bval = br.ValTb.Text?.Trim() ?? "0";
                if (!string.IsNullOrEmpty(btype))
                {
                    pa.DamageBonuses.Add((btype, bval));
                }
            }

            actionsList.Add(pa);
        }
        ProtoXmlHandler.SetProtoActions(unit, actionsList);
    }

    private void MarkDirty()
    {
        if (_isPopulating) return;
        _isDirty = true;
        _fileLabel.Text = (_modFilePath ?? "Unsaved Mod") + " *";
    }

    private string? GetConfiguredUserFolderPath()
    {
        var config = ProtoEditorSettings.LoadSettings();
        if (!string.IsNullOrWhiteSpace(config.UserFolderPath))
        {
            return config.UserFolderPath;
        }

        var rootDirectory = _mainWindow.RootDirectory;
        if (Directory.Exists(rootDirectory))
        {
            return Directory.GetParent(rootDirectory)?.FullName;
        }

        return null;
    }

    private static string SanitizeModFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return cleaned.Trim();
    }

    private bool TryLoadModFile(string path, bool showErrors = true)
    {
        if (!Path.GetFileName(path).Equals("proto_mods.xml", StringComparison.OrdinalIgnoreCase))
        {
            if (showErrors)
            {
                _ = new Prompt(PromptType.Error, "Wrong Mod File", "The proto editor can only write to proto_mods.xml.").ShowDialog(this);
            }
            return false;
        }

        try
        {
            var (doc, root) = ProtoXmlHandler.ParseProtoXml(path);
            _modXmlDoc = doc;
            _modXmlRoot = root;
            _modFilePath = path;
            _fileLabel.Text = path;
            _isDirty = false;
            RefreshProtoActionMetadata();

            var config = ProtoEditorSettings.LoadSettings();
            config.LastModFilePath = path;
            ProtoEditorSettings.SaveSettings(config);
            return true;
        }
        catch (Exception ex)
        {
            if (showErrors)
            {
                _ = new Prompt(PromptType.Error, "Error", $"Failed to open proto_mods.xml:\n{ex.Message}").ShowDialog(this);
            }
            return false;
        }
    }

    private async Task<bool> EnsureLocalModAsync(bool forceNew = false)
    {
        if (!forceNew && _modXmlRoot != null && _modFilePath != null)
        {
            return true;
        }

        var inputPrompt = new InputPromptWindow("Enter a name for your local mod:");
        await inputPrompt.ShowDialog(this);
        var modName = SanitizeModFolderName(inputPrompt.InputText ?? "");
        if (string.IsNullOrEmpty(modName))
        {
            return false;
        }

        var userBase = GetConfiguredUserFolderPath();
        if (string.IsNullOrWhiteSpace(userBase))
        {
            var pErr = new Prompt(PromptType.Error, "Settings Error", "User folder path is not configured. Set it in Proto Editor Settings or load the game root folder in CryBar first.");
            await pErr.ShowDialog(this);
            return false;
        }

        var modDir = Path.Combine(userBase, "mods", "local", modName, "game", "data", "gameplay");
        try
        {
            Directory.CreateDirectory(modDir);
            var xmlPath = Path.Combine(modDir, "proto_mods.xml");

            if (!File.Exists(xmlPath))
            {
                var (doc, root) = ProtoXmlHandler.CreateNewProtoFile(xmlPath);
                _modXmlDoc = doc;
                _modXmlRoot = root;
                RefreshProtoActionMetadata();
            }
            else if (!TryLoadModFile(xmlPath))
            {
                return false;
            }

            _modFilePath = xmlPath;
            _fileLabel.Text = xmlPath;
            _isDirty = false;
            EnsureCurrentModStringFileExists();

            var config = ProtoEditorSettings.LoadSettings();
            config.UserFolderPath = userBase;
            config.LastModFilePath = xmlPath;
            ProtoEditorSettings.SaveSettings(config);
            return true;
        }
        catch (Exception ex)
        {
            var pErr = new Prompt(PromptType.Error, "Error", $"Failed to create local mod:\n{ex.Message}");
            await pErr.ShowDialog(this);
            return false;
        }
    }

    private string? GetCurrentModStringsPath()
    {
        if (string.IsNullOrWhiteSpace(_modFilePath))
            return null;

        var gameplayDir = Path.GetDirectoryName(_modFilePath);
        if (string.IsNullOrWhiteSpace(gameplayDir))
            return null;

        var dataDir = Directory.GetParent(gameplayDir)?.FullName;
        if (string.IsNullOrWhiteSpace(dataDir))
            return null;

        return Path.Combine(dataDir, "strings", "English", "stringmods.txt");
    }

    private static string BuildStringIdForUnit(string unitName, string suffix)
    {
        var normalized = new string(unitName
            .Trim()
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());

        while (normalized.Contains("__", StringComparison.Ordinal))
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);

        normalized = normalized.Trim('_');
        return $"STR_UNIT_{normalized}_{suffix}";
    }

    private void AssignGeneratedStringIds(string unitName)
    {
        _currentStringFieldIds["displaynameid"] = BuildStringIdForUnit(unitName, GetStringSuffixForField("displaynameid"));
        _currentStringFieldIds["editornameid"] = _currentStringFieldIds["displaynameid"];
        _currentStringFieldIds["rollovertextid"] = BuildStringIdForUnit(unitName, GetStringSuffixForField("rollovertextid"));
        _currentStringFieldIds["shortrollovertextid"] = BuildStringIdForUnit(unitName, GetStringSuffixForField("shortrollovertextid"));

        if (_modXmlRoot == null)
            return;

        var unit = ProtoXmlHandler.GetUnitElement(_modXmlRoot, unitName);
        if (unit == null)
            return;

        foreach (var kvp in _currentStringFieldIds)
            ProtoXmlHandler.SetSimpleField(unit, kvp.Key, kvp.Value);
    }

    private async Task<string?> ResolveDisplayStringAsync(string? stringId)
    {
        if (string.IsNullOrWhiteSpace(stringId))
            return null;

        var modEntries = LoadCurrentModStringEntries();
        if (modEntries.TryGetValue(stringId, out var modValue))
            return modValue;

        return await _mainWindow.LookupStringKeyAsync(stringId);
    }

    private Dictionary<string, string> LoadCurrentModStringEntries()
    {
        var path = GetCurrentModStringsPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return StringTableParser.Parse(File.ReadAllText(path));
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveCurrentModStringEntries(Dictionary<string, string> entries)
    {
        var path = GetCurrentModStringsPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StringWriter();
        writer.WriteLine("Language = \"English\"");
        writer.WriteLine();

        foreach (var entry in entries.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var escaped = entry.Value.Replace("\"", "\\\"", StringComparison.Ordinal);
            writer.WriteLine($"ID = \"{entry.Key}\"   ;   Str = \"{escaped}\"");
        }

        File.WriteAllText(path, writer.ToString());
    }

    private void EnsureCurrentModStringFileExists()
    {
        var path = GetCurrentModStringsPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(path))
            File.WriteAllText(path, "Language = \"English\"\n\n");
    }

    private void RemoveStringEntries(IEnumerable<string> ids)
    {
        var entries = LoadCurrentModStringEntries();
        var changed = false;

        foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x)))
            changed |= entries.Remove(id);

        if (changed)
            SaveCurrentModStringEntries(entries);
    }

    private void SaveCurrentUnitStringValues()
    {
        if (_modXmlRoot == null || string.IsNullOrWhiteSpace(_currentUnitName))
            return;

        var entries = LoadCurrentModStringEntries();
        var displayId = _currentStringFieldIds.GetValueOrDefault("displaynameid");
        var displayText = _fieldControls.TryGetValue("displaynameid", out var displayCtrl) && displayCtrl is TextBox displayTb
            ? displayTb.Text?.Trim() ?? ""
            : "";

        foreach (var tag in StringBackedFieldTags)
        {
            if (!_fieldControls.TryGetValue(tag, out var ctrl) || ctrl is not TextBox tb)
                continue;

            var id = _currentStringFieldIds.GetValueOrDefault(tag);
            if (tag.Equals("editornameid", StringComparison.OrdinalIgnoreCase))
            {
                var editorText = tb.Text?.Trim() ?? "";
                if (string.Equals(editorText, displayText, StringComparison.Ordinal))
                {
                    id = displayId;
                    _currentStringFieldIds[tag] = displayId ?? "";
                    entries.Remove(BuildStringIdForUnit(_currentUnitName, GetStringSuffixForField(tag)));
                }
                else if (string.IsNullOrWhiteSpace(id) || string.Equals(id, displayId, StringComparison.OrdinalIgnoreCase))
                {
                    id = BuildStringIdForUnit(_currentUnitName, GetStringSuffixForField(tag));
                    _currentStringFieldIds[tag] = id;
                }
            }

            if (string.IsNullOrWhiteSpace(id))
                continue;

            entries[id] = tb.Text?.Trim() ?? "";
        }

        SaveCurrentModStringEntries(entries);
    }

    private void InitializeUnitStringValues(string unitName, string displayName, string longRollover, string shortRollover)
    {
        var entries = LoadCurrentModStringEntries();
        entries[BuildStringIdForUnit(unitName, GetStringSuffixForField("displaynameid"))] = displayName;
        entries[BuildStringIdForUnit(unitName, GetStringSuffixForField("rollovertextid"))] = longRollover;
        entries[BuildStringIdForUnit(unitName, GetStringSuffixForField("shortrollovertextid"))] = shortRollover;
        SaveCurrentModStringEntries(entries);
    }

    private async Task<bool> CheckStartLocalMod(bool allowReadOnlyPrompt = false)
    {
        if (!_isReadOnly) return true;
        if (_isPopulating || !allowReadOnlyPrompt) return false;

        var prompt = new Prompt(PromptType.Confirm, "Base Game Unit", "You are editing a base game unit which cannot be changed directly.\n\nWould you like to start a local mod to save these changes?");
        await prompt.ShowDialog(this);
        if (!prompt.Confirmed)
        {
            if (_currentUnitName != null)
            {
                Dispatcher.UIThread.Post(() => BuildEditorPanel(_currentUnitName));
            }
            return false;
        }

        if (_modXmlRoot == null)
        {
            if (!await EnsureLocalModAsync())
            {
                if (_currentUnitName != null)
                {
                    Dispatcher.UIThread.Post(() => BuildEditorPanel(_currentUnitName));
                }
                return false;
            }
        }

        if (_modXmlRoot != null && _currentUnitName != null)
        {
            if (_barXmlRoot != null && ProtoXmlHandler.UnitExists(_barXmlRoot, _currentUnitName))
            {
                var source = ProtoXmlHandler.GetUnitElement(_barXmlRoot, _currentUnitName);
                if (source != null && !ProtoXmlHandler.UnitExists(_modXmlRoot, _currentUnitName))
                {
                    ProtoXmlHandler.CloneUnit(_modXmlRoot, source, _currentUnitName);
                }
            }
            else if (!ProtoXmlHandler.UnitExists(_modXmlRoot, _currentUnitName))
            {
                ProtoXmlHandler.AddNewUnit(_modXmlRoot, _currentUnitName);
            }

            _isReadOnly = false;
            ApplyCurrentEdits();

            _unitTabs.SelectedIndex = 1;
            RefreshUnitList();
            BuildEditorPanel(_currentUnitName);
        }

        return true;
    }

    private async Task<bool> PromptUnsavedChangesAsync()
    {
        if (!_isDirty) return true;

        var prompt = new Prompt(PromptType.Confirm, "Unsaved Changes", "You have unsaved changes. Do you want to discard them and continue?");
        await prompt.ShowDialog(this);
        return prompt.Confirmed;
    }

    private string? GetLocalModsDirectory()
    {
        var userBase = GetConfiguredUserFolderPath();
        if (string.IsNullOrWhiteSpace(userBase))
            return null;

        return Path.Combine(userBase, "mods", "local");
    }

    private async void SwitchMod_Click(object? sender, RoutedEventArgs e)
    {
        var proceed = await PromptUnsavedChangesAsync();
        if (!proceed) return;

        var localModsDir = GetLocalModsDirectory();
        if (string.IsNullOrWhiteSpace(localModsDir) || !Directory.Exists(localModsDir))
        {
            var pErr = new Prompt(PromptType.Error, "Mods Folder Missing", "Could not find the local mods folder. Load the game root or set the user folder path first.");
            await pErr.ShowDialog(this);
            return;
        }

        var modFolders = Directory.GetDirectories(localModsDir)
            .Select(Path.GetFileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (modFolders.Count == 0)
        {
            var pErr = new Prompt(PromptType.Error, "No Local Mods", "No local mod folders were found under mods\\local.");
            await pErr.ShowDialog(this);
            return;
        }

        int? preselect = null;
        if (!string.IsNullOrWhiteSpace(_modFilePath))
        {
            var gameplayDir = Path.GetDirectoryName(_modFilePath);
            var dataDir = !string.IsNullOrWhiteSpace(gameplayDir) ? Directory.GetParent(gameplayDir)?.FullName : null;
            var gameDir = !string.IsNullOrWhiteSpace(dataDir) ? Directory.GetParent(dataDir)?.FullName : null;
            var modDir = !string.IsNullOrWhiteSpace(gameDir) ? Directory.GetParent(gameDir)?.FullName : null;
            var currentModName = !string.IsNullOrWhiteSpace(modDir) ? Path.GetFileName(modDir) : null;
            if (!string.IsNullOrWhiteSpace(currentModName))
                preselect = modFolders.FindIndex(x => x.Equals(currentModName!, StringComparison.OrdinalIgnoreCase));
            if (preselect < 0) preselect = null;
        }

        var picker = new PickerWindow("Select Local Mod", modFolders, preselect);
        await picker.ShowDialog(this);
        if (!string.IsNullOrWhiteSpace(picker.PickedItem))
        {
            var path = Path.Combine(localModsDir, picker.PickedItem, "game", "data", "gameplay", "proto_mods.xml");
            if (TryLoadModFile(path))
            {
                _unitTabs.SelectedIndex = 1;
                RefreshUnitList();
                _editorPanel.Children.Clear();
                _currentUnitName = null;
            }
        }
    }

    private async void CreateMod_Click(object? sender, RoutedEventArgs e)
    {
        var proceed = await PromptUnsavedChangesAsync();
        if (!proceed) return;

        _modXmlDoc = null;
        _modXmlRoot = null;
        _modFilePath = null;
        _isDirty = false;

        if (await EnsureLocalModAsync(forceNew: true))
        {
            _unitTabs.SelectedIndex = 1;
            RefreshUnitList();
            _editorPanel.Children.Clear();
            _currentUnitName = null;
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_modXmlDoc == null || _modFilePath == null)
        {
            SaveAs_Click(sender, e);
            return;
        }

        ApplyCurrentEdits();
        try
        {
            SaveCurrentUnitStringValues();
            ProtoXmlHandler.SaveProtoXml(_modXmlDoc, _modFilePath);
            _isDirty = false;
            _fileLabel.Text = _modFilePath;
            _statusMessage.Text = "Saved successfully.";
            _ = Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(() => _statusMessage.Text = ""));
        }
        catch (Exception ex)
        {
            _ = new Prompt(PromptType.Error, "Error", $"Failed to save file:\n{ex.Message}").ShowDialog(this);
        }
    }

    private async void SaveAs_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Mod As",
            DefaultExtension = "xml",
            SuggestedFileName = "proto_mods.xml",
            FileTypeChoices = [new FilePickerFileType("XML Files") { Patterns = ["*.xml"] }]
        });

        if (file != null)
        {
            var path = file.Path.LocalPath;
            if (!Path.GetFileName(path).Equals("proto_mods.xml", StringComparison.OrdinalIgnoreCase))
            {
                var pErr = new Prompt(PromptType.Error, "Wrong Mod File", "Save the proto editor file as proto_mods.xml.");
                await pErr.ShowDialog(this);
                return;
            }

            ApplyCurrentEdits();

            if (_modXmlDoc == null)
            {
                var (doc, root) = ProtoXmlHandler.CreateNewProtoFile(path);
                _modXmlDoc = doc;
                _modXmlRoot = root;
            }

            try
            {
                SaveCurrentUnitStringValues();
                ProtoXmlHandler.SaveProtoXml(_modXmlDoc, path);
                _modFilePath = path;
                _fileLabel.Text = path;
                _isDirty = false;

                var config = ProtoEditorSettings.LoadSettings();
                config.LastModFilePath = path;
                ProtoEditorSettings.SaveSettings(config);

                RefreshUnitList();
            }
            catch (Exception ex)
            {
                var pErr = new Prompt(PromptType.Error, "Error", $"Failed to save file:\n{ex.Message}");
                await pErr.ShowDialog(this);
            }
        }
    }

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWin = new ProtoSettingsWindow();
        await settingsWin.ShowDialog(this);
        if (settingsWin.IsSaved)
        {
            await LoadProtoDataFromBar();
            RefreshUnitList();
        }
    }

    private async void AddUnit_Click(object? sender, RoutedEventArgs e)
    {
        if (_modXmlRoot == null && !await EnsureLocalModAsync())
        {
            return;
        }
        if (_modXmlRoot == null)
        {
            return;
        }

        bool duplicate = false;
        if (!string.IsNullOrEmpty(_currentUnitName))
        {
            var pChoice = new Prompt(PromptType.Confirm, "Add Unit", $"Do you want to DUPLICATE the selected unit '{_currentUnitName}'?\n(Click Confirm to duplicate, or Cancel to create a blank unit instead.)");
            await pChoice.ShowDialog(this);
            duplicate = pChoice.Confirmed;
        }

        var input = new InputPromptWindow(duplicate ? "Enter duplicate unit name:" : "Enter new unit name:");
        await input.ShowDialog(this);
        string? name = input.InputText?.Trim();

        if (!string.IsNullOrEmpty(name))
        {
            var sourceDisplay = _fieldControls.TryGetValue("displaynameid", out var displayCtrl) && displayCtrl is TextBox displayTb
                ? displayTb.Text?.Trim() ?? ""
                : "";
            var sourceLong = _fieldControls.TryGetValue("rollovertextid", out var longCtrl) && longCtrl is TextBox longTb
                ? longTb.Text?.Trim() ?? ""
                : "";
            var sourceShort = _fieldControls.TryGetValue("shortrollovertextid", out var shortCtrl) && shortCtrl is TextBox shortTb
                ? shortTb.Text?.Trim() ?? ""
                : "";

            if (UnitNameExistsAnywhere(name))
            {
                var pErr = new Prompt(PromptType.Error, "Duplicate", $"Unit '{name}' already exists as a proto unit in the base game or this mod.");
                await pErr.ShowDialog(this);
                return;
            }

            if (duplicate && _currentUnitName != null)
            {
                if (_isReadOnly && _barXmlRoot != null)
                {
                    var source = ProtoXmlHandler.GetUnitElement(_barXmlRoot, _currentUnitName);
                    if (source == null)
                    {
                        var pErr = new Prompt(PromptType.Error, "Missing Unit", $"Could not find base unit '{_currentUnitName}' in Data.bar.");
                        await pErr.ShowDialog(this);
                        return;
                    }
                    ProtoXmlHandler.CloneUnit(_modXmlRoot, source, name);
                }
                else
                {
                    var clone = ProtoXmlHandler.CloneUnit(_modXmlRoot, _currentUnitName, name);
                    if (clone == null)
                    {
                        var pErr = new Prompt(PromptType.Error, "Missing Unit", $"Could not find mod unit '{_currentUnitName}' in proto_mods.xml.");
                        await pErr.ShowDialog(this);
                        return;
                    }
                }

                AssignGeneratedStringIds(name);
                InitializeUnitStringValues(
                    name,
                    string.IsNullOrWhiteSpace(sourceDisplay) ? name : sourceDisplay,
                    sourceLong,
                    sourceShort);
            }
            else
            {
                var newUnit = ProtoXmlHandler.AddNewUnit(_modXmlRoot, name);
                ProtoXmlHandler.SetCostEntries(newUnit, ProtoConstants.KnownResourceTypes.Select(r => (r, "0")));
                ProtoXmlHandler.SetArmorEntries(newUnit, ProtoConstants.KnownArmorTypes.Select(a => (a, "0")));
                AssignGeneratedStringIds(name);
                InitializeUnitStringValues(name, name, "", "");
            }

            MarkDirty();
            RefreshUnitList();
            _unitTabs.SelectedIndex = 1;

            _unitList.SelectedItem = name;
            BuildEditorPanel(name);
        }
    }

    private async void DeleteUnit_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentUnitName))
        {
            var pErr = new Prompt(PromptType.Error, "Select Unit", "Please select a unit to delete.");
            await pErr.ShowDialog(this);
            return;
        }

        if (_isReadOnly)
        {
            var pErr = new Prompt(PromptType.Error, "Original Unit", $"Unit '{_currentUnitName}' is a base game unit and cannot be deleted.");
            await pErr.ShowDialog(this);
            return;
        }

        var pConfirm = new Prompt(PromptType.Confirm, "Delete Unit", $"Are you sure you want to delete '{_currentUnitName}'?");
        await pConfirm.ShowDialog(this);
        if (pConfirm.Confirmed && _modXmlRoot != null)
        {
            ProtoXmlHandler.DeleteUnit(_modXmlRoot, _currentUnitName);
            _currentUnitName = null;
            MarkDirty();
            RefreshUnitList();
            _editorPanel.Children.Clear();
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isDirty)
        {
            e.Cancel = true;
            var proceed = await PromptUnsavedChangesAsync();
            if (proceed)
            {
                _isDirty = false;
                Close();
            }
        }
        else
        {
            base.OnClosing(e);
        }
    }
}
