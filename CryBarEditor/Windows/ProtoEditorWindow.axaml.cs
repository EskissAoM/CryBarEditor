using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CryBar;
using CryBar.Bar;
using CryBarEditor.Classes;

namespace CryBarEditor.Windows;

public partial class ProtoEditorWindow : SimpleWindow
{
    private enum NewCustomUnitKind
    {
        Unit,
        Building,
        Other
    }

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
    private static readonly string[] CultureAwareSimpleFieldTags = ["icon", "animfile"];
    private static readonly (string Value, string Label)[] SupportedCultures =
    [
        ("greek", "Greek"),
        ("egyptian", "Egyptian"),
        ("norse", "Norse"),
        ("atlantean", "Atlantean"),
        ("chinese", "Chinese"),
        ("japanese", "Japanese"),
        ("aztec", "Aztec"),
    ];
    private static readonly string[] KnownInitialUnitAiStances = ["Aggressive", "Defensive", "StandGround", "Passive"];
    private static readonly string[] KnownInitialShadingTypes = ["bronze", "stone", "frost", "burning", "gold", "clay", "sand", "cocoon", "undead", "blood"];
    private static readonly string[] KnownReplacementTypes = ["dead", "killed", "birth", "build", "hit", "hitGround", "revertToSocket", "hitWater", "selfDestruct"];
    private static readonly string[] KnownSpawnTypes = ["dead", "killed", "birth", "build", "mutate", "hit", "hitGround", "revertToSocket", "hitWater", "selfDestruct"];
    private static readonly string[] KnownVeterancyRankTypes = ["NumKills", "NumAttacks", "TotalDamage", "DamageAndResourcesEaten"];
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
    private readonly Dictionary<string, Control> _otherSpecificAttributeContainers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _currentStringFieldIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _costControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _armorControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CultureFieldRowState>> _cultureFieldRows = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _currentUnitTypes;
    private HashSet<string>? _currentFlags;
    private HashSet<string>? _currentContains;
    private HashSet<string>? _currentNotContains;
    private HashSet<string>? _currentSharedSelectionUnitTypes;
    private HashSet<string>? _currentRechargeIncludeTypes;
    private HashSet<string>? _currentRechargeExcludeTypes;
    private HashSet<string>? _currentAuxRechargeIncludeTypes;
    private HashSet<string>? _currentAuxRechargeExcludeTypes;
    private HashSet<string>? _currentVeterancyIncludeTypes;
    private HashSet<string>? _currentVeterancyExcludeTypes;
    private HashSet<string>? _currentRespawnTrainTypes;
    private HashSet<string>? _currentRespawnTrainExcludeTypes;
    private List<string>? _cachedTrainUnitNames;
    private List<string>? _cachedTechNames;
    private List<string>? _cachedCommandNames;
    private List<string>? _cachedBuildLimitTargets;
    private List<string>? _cachedResourceSubtypeNames;
    private List<string>? _cachedPlacementFileNames;
    private List<string>? _cachedPathabilityFlags;
    private List<string>? _cachedHotkeyContexts;
    private List<string>? _cachedBloodGroupNames;
    private List<string>? _cachedMinimapIcons;
    private string? _cachedModStringEntriesPath;
    private Dictionary<string, string>? _cachedModStringEntries;
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
    private readonly List<CommandRowState> _unitCommandRows = [];
    private readonly List<CommandRowState> _optionalCommandRows = [];
    private readonly List<ProtoActionWidgetState> _protoActionWidgets = [];
    private readonly List<BuildLimitTargetRowState> _buildLimitRows = [];
    private readonly List<ResourceConversionRowState> _resourceConversionRows = [];
    private readonly List<DependentUnitRowState> _dependentUnitRows = [];
    private readonly List<SpawnRowState> _spawnRows = [];
    private readonly List<VeterancyRankRowState> _veterancyRankRows = [];
    private readonly List<VeterancyBonusRowState> _veterancyBonusRows = [];
    private readonly List<OnDamageModifyRowState> _onDamageModifyRows = [];
    private BuildLimitMode _currentBuildLimitMode = BuildLimitMode.Standard;
    private readonly List<PageSearchTarget> _pageSearchTargets = [];
    private readonly List<PageSearchTarget> _pageSearchMatches = [];
    private readonly HashSet<TextBlock> _pageSearchHighlightedBlocks = [];
    private readonly List<SectionJumpTarget> _sectionJumpTargets = [];
    private int _currentPageSearchMatchIndex = -1;

    private sealed class PageSearchTarget
    {
        public required Control Control { get; init; }
        public required string Text { get; init; }
    }

    private sealed class SectionJumpTarget
    {
        public required string Title { get; init; }
        public required TextBlock Header { get; init; }
    }

    private enum BuildLimitMode
    {
        Standard,
        Dynamic,
        Shared,
    }

    private class ProtoActionWidgetState
    {
        public required Panel Container { get; set; }
        public required AutoCompleteBox NameAcb { get; set; }
        public required AutoCompleteBox TypeAcb { get; set; }
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

    private class BuildLimitTargetRowState
    {
        public required Panel RowPanel { get; set; }
        public required AutoCompleteBox ValueAcb { get; set; }
        public TextBox? WeightTb { get; set; }
    }

    private class CultureFieldRowState
    {
        public required Panel RowPanel { get; set; }
        public required ComboBox CultureCb { get; set; }
        public required AutoCompleteBox ValueAcb { get; set; }
    }

    private class ResourceConversionRowState
    {
        public required Panel RowPanel { get; set; }
        public required ComboBox FromResourceCb { get; set; }
        public required ComboBox ToResourceCb { get; set; }
        public required TextBox ValueTb { get; set; }
    }

    private class DependentUnitRowState
    {
        public required Panel RowPanel { get; set; }
        public required AutoCompleteBox ValueAcb { get; set; }
        public required TextBox XTb { get; set; }
        public required TextBox ZTb { get; set; }
        public required TextBox AttachBoneTb { get; set; }
    }

    private class SpawnRowState
    {
        public required Panel RowPanel { get; set; }
        public required AutoCompleteBox ValueAcb { get; set; }
        public required AutoCompleteBox TypeAcb { get; set; }
        public required TextBox CountTb { get; set; }
        public required TextBox LifespanTb { get; set; }
        public required TextBox ChanceTb { get; set; }
        public required TextBox DelayTb { get; set; }
        public required CheckBox SkipPlacementCheckCb { get; set; }
        public required CheckBox ControlGroupCb { get; set; }
        public required CheckBox SetOwnerCb { get; set; }
        public required AutoCompleteBox WaterProtoUnitAcb { get; set; }
        public required AutoCompleteBox ShadingTypeAcb { get; set; }
    }

    private class VeterancyRankRowState
    {
        public required Panel RowPanel { get; set; }
        public required ComboBox TypeCb { get; set; }
        public required TextBox ValueTb { get; set; }
    }

    private class VeterancyBonusRowState
    {
        public required Panel RowPanel { get; set; }
        public required TextBox RankIdTb { get; set; }
        public required AutoCompleteBox ModifyTypeAcb { get; set; }
        public required TextBox ValueTb { get; set; }
        public required AutoCompleteBox DamageTypeAcb { get; set; }
        public required TextBlock DamageTypeLabel { get; set; }
    }

    private class OnDamageModifyRowState
    {
        public required Panel RowPanel { get; set; }
        public required AutoCompleteBox ModifyTypeAcb { get; set; }
        public required TextBox ValueTb { get; set; }
        public required AutoCompleteBox DamageTypeAcb { get; set; }
        public required TextBlock DamageTypeLabel { get; set; }
    }

    public ProtoEditorWindow()
    {
        InitializeComponent();
        InitializePageSearch();
        _mainWindow = null!;
    }

    public ProtoEditorWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        DataContext = this;
        InitializeComponent();
        InitializePageSearch();

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
                InvalidateBarDerivedCaches();
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
            InvalidateBarDerivedCaches();
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

    private void InvalidateSuggestionCaches(bool includeTechNames = false)
    {
        _cachedTrainUnitNames = null;
        _cachedCommandNames = null;
        _cachedBuildLimitTargets = null;

        if (includeTechNames)
            _cachedTechNames = null;
    }

    private void InvalidateBarDerivedCaches()
    {
        InvalidateSuggestionCaches(includeTechNames: true);
        _cachedResourceSubtypeNames = null;
        _cachedPlacementFileNames = null;
        _cachedPathabilityFlags = null;
        _cachedHotkeyContexts = null;
        _cachedBloodGroupNames = null;
        _cachedMinimapIcons = null;
    }

    private void InvalidateModStringEntriesCache()
    {
        _cachedModStringEntriesPath = null;
        _cachedModStringEntries = null;
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

    private void InitializePageSearch()
    {
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        _pageSearchBox.TextChanged += (_, _) => RefreshPageSearchMatches(scrollToCurrentMatch: true);
        _pageSearchBox.KeyDown += PageSearchBox_KeyDown;

        _pageSearchMatchCaseToggle.IsCheckedChanged += OnPageSearchOptionChanged;
        _pageSearchWholeWordToggle.IsCheckedChanged += OnPageSearchOptionChanged;
        _pageSearchRegexToggle.IsCheckedChanged += OnPageSearchOptionChanged;

        _pageSearchPreviousButton.Click += (_, _) => MoveToPreviousPageSearchMatch();
        _pageSearchNextButton.Click += (_, _) => MoveToNextPageSearchMatch();
        _pageSearchCloseButton.Click += (_, _) => ClosePageSearch();

        UpdatePageSearchUiState(hasMatches: false, hasQuery: false, hasValidPattern: true);
        RebuildSectionJumpFlyout();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (!string.IsNullOrWhiteSpace(_currentUnitName))
            {
                OpenPageSearch();
                e.Handled = true;
            }
            return;
        }

        if (!_pageSearchPanel.IsVisible)
            return;

        if (e.Key == Key.Escape)
        {
            ClosePageSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                MoveToPreviousPageSearchMatch();
            else
                MoveToNextPageSearchMatch();

            e.Handled = true;
        }
    }

    private void PageSearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                MoveToPreviousPageSearchMatch();
            else
                MoveToNextPageSearchMatch();

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            ClosePageSearch();
            e.Handled = true;
        }
    }

    private void OnPageSearchOptionChanged(object? sender, RoutedEventArgs e)
    {
        RefreshPageSearchMatches(scrollToCurrentMatch: true);
    }

    private void AddSectionHeader(string title)
    {
        var lbl = new TextBlock
        {
            Text = $"\u2500\u2500\u2500\u2500 {title} \u2500\u2500\u2500\u2500",
            Classes = { "SectionHeader" },
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = Brush.Parse("#5ba8de"),
            Margin = new Thickness(0, 15, 0, 5)
        };
        _editorPanel.Children.Add(lbl);
        _sectionJumpTargets.Add(new SectionJumpTarget
        {
            Title = title,
            Header = lbl
        });
    }

    private void RebuildSectionJumpFlyout()
    {
        _sectionsFlyoutPanel.Children.Clear();

        foreach (var target in _sectionJumpTargets)
        {
            var button = new Button
            {
                Content = target.Title,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6),
                MinWidth = 180
            };

            button.Click += (_, _) =>
            {
                _sectionsButton.Flyout?.Hide();
                Dispatcher.UIThread.Post(() => ScrollEditorControlToTop(target.Header), DispatcherPriority.Loaded);
            };

            _sectionsFlyoutPanel.Children.Add(button);
        }

        var hasSections = _sectionJumpTargets.Count > 0;
        _sectionsButton.IsVisible = hasSections;
        _sectionsButton.IsEnabled = hasSections;
    }

    private void ScrollEditorControlToTop(Control target, double topPadding = 8)
    {
        var targetOrigin = target.TranslatePoint(new Point(0, 0), _editorPanel);
        if (targetOrigin == null)
        {
            target.BringIntoView();
            return;
        }

        var desiredOffsetY = Math.Max(0, targetOrigin.Value.Y - topPadding);
        var maxOffsetY = Math.Max(0, _editorScroll.Extent.Height - _editorScroll.Viewport.Height);
        var clampedOffsetY = Math.Min(desiredOffsetY, maxOffsetY);

        _editorScroll.Offset = new Vector(_editorScroll.Offset.X, clampedOffsetY);
    }

    private void OpenPageSearch()
    {
        _pageSearchPanel.IsVisible = true;
        RefreshPageSearchMatches(scrollToCurrentMatch: !string.IsNullOrWhiteSpace(_pageSearchBox.Text));
        Dispatcher.UIThread.Post(() =>
        {
            _pageSearchBox.Focus();
            _pageSearchBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void ClosePageSearch()
    {
        ClearPageSearchHighlights();
        _pageSearchPanel.IsVisible = false;
        _editorScroll.Focus();
    }

    private void RebuildPageSearchTargets()
    {
        _pageSearchTargets.Clear();

        foreach (var target in EnumeratePageSearchTargets(_editorPanel))
        {
            _pageSearchTargets.Add(target);
        }

        RefreshPageSearchMatches(scrollToCurrentMatch: _pageSearchPanel.IsVisible && !string.IsNullOrWhiteSpace(_pageSearchBox.Text));
    }

    private IEnumerable<PageSearchTarget> EnumeratePageSearchTargets(ILogical root)
    {
        foreach (var child in root.LogicalChildren)
        {
            if (child is TextBlock textBlock)
            {
                var text = NormalizePageSearchTargetText(textBlock.Text);
                if (!string.IsNullOrWhiteSpace(text) && ShouldIncludePageSearchTextBlock(textBlock, text))
                {
                    yield return new PageSearchTarget
                    {
                        Control = textBlock,
                        Text = text
                    };
                }
            }
            else if (child is CheckBox checkBox && checkBox.Content is string checkBoxText)
            {
                var text = NormalizePageSearchTargetText(checkBoxText);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return new PageSearchTarget
                    {
                        Control = checkBox,
                        Text = text
                    };
                }
            }

            if (child is ILogical logicalChild)
            {
                foreach (var nestedTarget in EnumeratePageSearchTargets(logicalChild))
                {
                    yield return nestedTarget;
                }
            }
        }
    }

    private bool ShouldIncludePageSearchTextBlock(TextBlock textBlock, string text)
    {
        if (text.StartsWith("Editing:", StringComparison.OrdinalIgnoreCase))
            return false;

        for (StyledElement? current = textBlock; current != null; current = current.Parent as StyledElement)
        {
            if (current is Button ||
                current is ToggleButton ||
                current is TabStripItem ||
                current is ListBoxItem)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPageSearchTargetVisible(Control control)
    {
        for (StyledElement? current = control; current != null; current = current.Parent as StyledElement)
        {
            if (current is Visual visual && !visual.IsVisible)
                return false;
        }

        return true;
    }

    private static string NormalizePageSearchTargetText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace('\u2500', ' ').Trim();
        return Regex.Replace(normalized, "\\s+", " ");
    }

    private void RefreshPageSearchMatches(bool scrollToCurrentMatch)
    {
        ClearPageSearchHighlights();
        _pageSearchMatches.Clear();
        _currentPageSearchMatchIndex = -1;

        var query = _pageSearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            UpdatePageSearchUiState(hasMatches: false, hasQuery: false, hasValidPattern: true);
            return;
        }

        if (!TryCreatePageSearchRegex(query, out var regex))
        {
            UpdatePageSearchUiState(hasMatches: false, hasQuery: true, hasValidPattern: false);
            return;
        }

        foreach (var target in _pageSearchTargets)
        {
            if (IsPageSearchTargetVisible(target.Control) && regex.IsMatch(target.Text))
            {
                _pageSearchMatches.Add(target);
            }
        }

        if (_pageSearchMatches.Count == 0)
        {
            UpdatePageSearchUiState(hasMatches: false, hasQuery: true, hasValidPattern: true);
            return;
        }

        _currentPageSearchMatchIndex = 0;
        UpdatePageSearchUiState(hasMatches: true, hasQuery: true, hasValidPattern: true);
        ApplyCurrentPageSearchMatch(scrollToCurrentMatch);
    }

    private bool TryCreatePageSearchRegex(string query, out Regex regex)
    {
        try
        {
            var pattern = _pageSearchRegexToggle.IsChecked == true
                ? query
                : Regex.Escape(query);

            if (_pageSearchWholeWordToggle.IsChecked == true)
            {
                pattern = $"\\b{pattern}\\b";
            }

            var options = RegexOptions.CultureInvariant;
            if (_pageSearchMatchCaseToggle.IsChecked != true)
            {
                options |= RegexOptions.IgnoreCase;
            }

            regex = new Regex(pattern, options);
            return true;
        }
        catch (ArgumentException)
        {
            regex = null!;
            return false;
        }
    }

    private void MoveToNextPageSearchMatch()
    {
        if (_pageSearchMatches.Count == 0)
            return;

        _currentPageSearchMatchIndex = (_currentPageSearchMatchIndex + 1) % _pageSearchMatches.Count;
        ApplyCurrentPageSearchMatch(scrollToCurrentMatch: true);
    }

    private void MoveToPreviousPageSearchMatch()
    {
        if (_pageSearchMatches.Count == 0)
            return;

        _currentPageSearchMatchIndex = (_currentPageSearchMatchIndex - 1 + _pageSearchMatches.Count) % _pageSearchMatches.Count;
        ApplyCurrentPageSearchMatch(scrollToCurrentMatch: true);
    }

    private void ApplyCurrentPageSearchMatch(bool scrollToCurrentMatch)
    {
        if (_currentPageSearchMatchIndex < 0 || _currentPageSearchMatchIndex >= _pageSearchMatches.Count)
            return;

        ClearPageSearchHighlights();

        var target = _pageSearchMatches[_currentPageSearchMatchIndex].Control;
        if (target is TextBlock textBlock)
        {
            textBlock.Classes.Add("PageSearchQueryMatch");
            _pageSearchHighlightedBlocks.Add(textBlock);
        }

        UpdatePageSearchMatchCountText();

        if (scrollToCurrentMatch)
        {
            Dispatcher.UIThread.Post(() => target.BringIntoView(), DispatcherPriority.Loaded);
        }
    }

    private void ClearPageSearchHighlights()
    {
        foreach (var textBlock in _pageSearchHighlightedBlocks)
        {
            textBlock.Classes.Remove("PageSearchQueryMatch");
        }

        _pageSearchHighlightedBlocks.Clear();
    }

    private void UpdatePageSearchMatchCountText()
    {
        if (string.IsNullOrWhiteSpace(_pageSearchBox.Text))
        {
            _pageSearchMatchCountText.Text = string.Empty;
            return;
        }

        if (_currentPageSearchMatchIndex >= 0 && _pageSearchMatches.Count > 0)
        {
            _pageSearchMatchCountText.Text = $"{_currentPageSearchMatchIndex + 1} of {_pageSearchMatches.Count}";
            return;
        }

        _pageSearchMatchCountText.Text = _pageSearchMatches.Count == 0 ? "0 of 0" : $"0 of {_pageSearchMatches.Count}";
    }

    private void UpdatePageSearchUiState(bool hasMatches, bool hasQuery, bool hasValidPattern)
    {
        var borderBrush = hasMatches || !hasQuery
            ? "#3f3f46"
            : "#8b0000";

        _pageSearchPanel.BorderBrush = Brush.Parse(borderBrush);
        _pageSearchPreviousButton.IsEnabled = hasMatches;
        _pageSearchNextButton.IsEnabled = hasMatches;
        _pageSearchMatchCountText.Foreground = Brush.Parse(hasValidPattern ? "#d9d9d9" : "#ff7a7a");

        if (!hasQuery)
        {
            _pageSearchMatchCountText.Text = string.Empty;
        }
        else if (!hasValidPattern)
        {
            _pageSearchMatchCountText.Text = "Invalid pattern";
        }
        else
        {
            UpdatePageSearchMatchCountText();
        }
    }

    private static bool IsSelectionOnlySimpleField(string tag) => SelectionOnlySimpleFields.Contains(tag);
    private static bool IsStringBackedField(string tag) => StringBackedFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    private static bool IsCultureAwareSimpleField(string tag) => CultureAwareSimpleFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] TrainTechRowOptions = ["0", "1", "2", "3"];
    private static readonly string[] TrainTechColumnOptions = ["0", "1", "2", "3", "4", "5"];
    private static readonly string[] KnownRechargeTypes = ["Kills", "Damage", "Attacks", "resourceDropoff"];
    private static readonly string[] SupportedCultureLabels = SupportedCultures.Select(x => x.Label).ToArray();
    private static readonly string[] OtherSpecificAttributeChoiceKeys =
    [
        "selectionradius",
        "autoattackrange",
        "lifespan",
        "auxrecharge",
        "resourcesubtype",
        "minimapvisuals",
        "resourcepriority",
        "hotkeycontext",
        "allyhotkeycontext",
        "resourcedecay",
        "culture",
        "wanderdistance",
        "workersoftlimit",
        "formationorder",
        "screenshakeondestruction",
        "initialunitaistance",
        "displayedrange",
        "aistancebasedistance",
        "heighthitpointbaroffset",
        "allowedheightvariance",
        "creationfade",
        "stealth",
        "selfdestructprotoaction",
        "pathabilityflags",
        "heightbobdata",
        "birthprotoaction",
        "placementfile",
        "ondiscoverlos",
        "populationcapaddition",
        "gathererlimit",
        "creationfadetime",
        "prioritybonusfactor",
        "buildingworkrate",
        "trainingrate",
        "gatherratemultiplier",
        "partisans",
        "bloodandbones",
        "decaytime",
        "decaydelaytime",
        "researchrate",
        "killrewarddata",
        "resourcereturn",
        "resourcereturnrate",
        "autobuildrate",
        "godpowerblockradius",
        "godpowercostfactor",
        "builderlimit",
        "corpsedecaydelay",
        "costescalation",
        "damageshading",
        "initialshading",
        "deadreplacement",
        "deadtransform",
        "dependentunitdata",
        "eidolonprotoid",
        "enemyshortrollovertextid",
        "socketdata",
        "disguiseprotoid",
        "spawndata",
        "veterancydata",
        "stackprotoaction",
        "dodgechance",
        "directionalarmor",
        "placementobstruction",
        "farming",
        "carrycapacity",
        "initialresource",
        "resourceconversion",
        "conversionresistance",
        "respawntraindata",
        "sharedselectionunittypes",
        "decay",
        "recharge",
        "replacement",
        "ondamagemodifiers",
    ];
    private static string GetStringSuffixForField(string tag) => tag.ToLowerInvariant() switch
    {
        "displaynameid" => "NAME",
        "editornameid" => "EDITOR",
        "rollovertextid" => "LR",
        "shortrollovertextid" => "SR",
        _ => tag.ToUpperInvariant(),
    };
    private static string GetCultureLabel(string? cultureValue)
    {
        var normalized = cultureValue?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        return SupportedCultures.FirstOrDefault(x => x.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)).Label
            ?? normalized;
    }

    private static string GetCultureValue(string? cultureLabel)
    {
        var normalized = cultureLabel?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        return SupportedCultures.FirstOrDefault(x => x.Label.Equals(normalized, StringComparison.OrdinalIgnoreCase)).Value
            ?? normalized.ToLowerInvariant();
    }

    private static readonly Dictionary<string, string> OtherSpecificAttributeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["selectionradius"] = "Selection Radius",
        ["autoattackrange"] = "Auto Attack Range",
        ["lifespan"] = "Lifespan",
        ["auxrecharge"] = "Aux Ability Recharge",
        ["resourcesubtype"] = "Resource Subtype",
        ["minimapvisuals"] = "Minimap Visuals",
        ["resourcepriority"] = "Resource Priority",
        ["hotkeycontext"] = "Hotkey Context",
        ["allyhotkeycontext"] = "Ally Hotkey Context",
        ["resourcedecay"] = "Resource Decay",
        ["culture"] = "Culture",
        ["wanderdistance"] = "Wander Distance",
        ["workersoftlimit"] = "Worker Soft Limit",
        ["formationorder"] = "Formation Order",
        ["screenshakeondestruction"] = "Screenshake On Destruction",
        ["initialunitaistance"] = "Initial Unit AI Stance",
        ["stealth"] = "Stealth",
        ["displayedrange"] = "Displayed Range",
        ["aistancebasedistance"] = "AI Stance Base Distance",
        ["heighthitpointbaroffset"] = "Height Hitpoint Bar Offset",
        ["allowedheightvariance"] = "Allowed Height Variance",
        ["selfdestructprotoaction"] = "Self Destruct Proto Action",
        ["pathabilityflags"] = "Pathability Flags",
        ["heightbobdata"] = "Height Bob",
        ["birthprotoaction"] = "Birth Proto Action",
        ["placementfile"] = "Placement File",
        ["ondiscoverlos"] = "On Discover LOS",
        ["populationcapaddition"] = "Population Cap Addition",
        ["gathererlimit"] = "Gatherer Limit",
        ["creationfade"] = "Creation Fade Time",
        ["prioritybonusfactor"] = "Priority Bonus Factor",
        ["buildingworkrate"] = "Building Work Rate",
        ["trainingrate"] = "Training Rate",
        ["gatherratemultiplier"] = "Gather Rate Multiplier",
        ["partisans"] = "Partisans",
        ["bloodandbones"] = "Blood and bones data",
        ["decaytime"] = "Decay Time",
        ["decaydelaytime"] = "Decay Delay Time",
        ["researchrate"] = "Research Rate",
        ["killrewarddata"] = "Kill Reward",
        ["resourcereturn"] = "Resource Return",
        ["resourcereturnrate"] = "Resource Return Rate",
        ["autobuildrate"] = "Auto Build Rate",
        ["godpowerblockradius"] = "God Power Block Radius",
        ["godpowercostfactor"] = "God Power Cost Factor",
        ["builderlimit"] = "Builder Limit",
        ["corpsedecaydelay"] = "Corpse Decay Delay",
        ["costescalation"] = "Cost Escalation",
        ["damageshading"] = "Damage Shading",
        ["initialshading"] = "Initial Shading",
        ["deadreplacement"] = "Dead Replacement",
        ["deadtransform"] = "Dead Transform",
        ["dependentunitdata"] = "Dependent Unit",
        ["eidolonprotoid"] = "Eidolon Proto ID",
        ["enemyshortrollovertextid"] = "Enemy Short Rollover Text ID",
        ["socketdata"] = "Socket",
        ["disguiseprotoid"] = "Disguise Proto ID",
        ["spawndata"] = "Spawn",
        ["veterancydata"] = "Veterancy",
        ["stackprotoaction"] = "Stack Proto Action",
        ["dodgechance"] = "Dodge Chance",
        ["directionalarmor"] = "Directional Armor",
        ["placementobstruction"] = "Placement Obstruction",
        ["farming"] = "Farming Data",
        ["carrycapacity"] = "Carry Capacity",
        ["initialresource"] = "Initial Resource",
        ["resourceconversion"] = "Resource Conversion",
        ["conversionresistance"] = "Conversion Resistance",
        ["respawntraindata"] = "Respawn Train Data",
        ["sharedselectionunittypes"] = "Shared Selection Unit Types",
        ["decay"] = "Decay",
        ["recharge"] = "Ability Recharge",
        ["replacement"] = "Replacement",
        ["ondamagemodifiers"] = "On Damage Modifiers",
    };

    private static readonly HashSet<string> OtherSpecificSimpleNumberTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "autoattackrange", "lifespan", "resourcepriority", "resourcedecay",
        "wanderdistance", "workersoftlimit", "formationorder", "screenshakeondestruction", "stealthdetectionradius", "displayedrange",
        "aistancebasedistance", "heighthitpointbaroffset", "allowedheightvariance", "stealthrevealselfradius",
        "stealthshowsilhouetteradius", "populationcapaddition", "gathererlimit",
        "prioritybonusfactor", "buildingworkrate", "trainingrate", "gatherratemultiplier", "partisancount", "decaytime",
        "decaydelaytime", "researchrate", "projectilespinperiod", "autobuildrate",
        "godpowerblockradius", "godpowercostfactor", "builderlimit", "corpsedecaydelay", "costescalation", "bloodscalemodify", "bonescalemodify",
        "dodgechance", "conversionresistance"
    };

    private static readonly HashSet<string> OtherSpecificSimpleSuggestionTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "culture", "resourcesubtype", "disguiseprotoid", "deadreplacement", "deadtransform",
        "initialunitaistance", "pathabilityflags", "placementfile", "hotkeycontext", "allyhotkeycontext", "partisantype", "bloodgroupoverride",
        "eidolonprotoid", "selfdestructprotoaction", "birthprotoaction", "stackprotoaction"
    };

    private static readonly HashSet<string> OtherSpecificSimpleTextTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "ondiscoverlos", "enemyshortrollovertextid", "dodgesoundset",
        "dodgemessageid"
    };

    private static readonly HashSet<string> OriginalOnlyOtherSpecificTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "resourcepriority",
        "prioritybonusfactor",
        "corpsedecaydelay",
        "decaytime",
        "decaydelaytime",
        "projectilespinperiod",
        "enemyshortrollovertextid"
    };

    private static string GetOtherSpecificAttributeLabel(string key)
        => OtherSpecificAttributeLabels.TryGetValue(key, out var label) ? label : key;

    private static bool IsOtherSpecificAttributeVisible(string key, Dictionary<string, Control> containers)
        => containers.ContainsKey(key);

    private static string? GetFlagForOtherSpecificAttribute(string tag) => tag.ToLowerInvariant() switch
    {
        "displayedrange" => "DisplayRange",
        "dodgechance" => "CanDodgeAttacks",
        "farming" => "UseFarmingAnims",
        _ => null
    };

    private List<string> GetAvailableTrainUnitNames()
    {
        if (_cachedTrainUnitNames != null)
            return _cachedTrainUnitNames;

        var names = new List<string>();
        if (_barData != null)
            names.AddRange(_barData.UnitNames);
        if (_modXmlRoot != null)
            names.AddRange(ProtoXmlHandler.GetUnitNames(_modXmlRoot));

        _cachedTrainUnitNames = names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return _cachedTrainUnitNames;
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

    private List<string> GetAvailableCommandNames()
    {
        if (_cachedCommandNames != null)
            return _cachedCommandNames;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void CollectFromRoot(XElement? root)
        {
            if (root == null)
                return;

            foreach (var unit in root.Descendants("unit"))
            {
                foreach (var entry in ProtoXmlHandler.GetCommandEntries(unit))
                {
                    if (!string.IsNullOrWhiteSpace(entry.Value))
                        names.Add(entry.Value.Trim());
                }
            }
        }

        CollectFromRoot(_barXmlRoot);
        CollectFromRoot(_modXmlRoot);

        _cachedCommandNames = names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return _cachedCommandNames;
    }

    private List<string> GetAvailableBuildLimitTargets()
    {
        if (_cachedBuildLimitTargets != null)
            return _cachedBuildLimitTargets;

        var values = new List<string>();
        if (_barData != null)
        {
            values.AddRange(_barData.UnitTypes);
            values.AddRange(_barData.UnitNames);
        }

        values.AddRange(ProtoConstants.KnownUnitTypes);

        if (_modXmlRoot != null)
            values.AddRange(ProtoXmlHandler.GetUnitNames(_modXmlRoot));

        _cachedBuildLimitTargets = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return _cachedBuildLimitTargets;
    }

    private List<string> GetDistinctBarSimpleFieldValues(string tag)
    {
        if (_barXmlRoot == null)
            return [];

        return _barXmlRoot
            .Descendants(tag)
            .Select(x => x.Value?.Trim() ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> GetKnownResourceSubtypeNames()
    {
        if (_cachedResourceSubtypeNames != null)
            return _cachedResourceSubtypeNames;

        _cachedResourceSubtypeNames = GetDistinctBarSimpleFieldValues("resourcesubtype");
        return _cachedResourceSubtypeNames;
    }

    private List<string> GetKnownPlacementFileNames()
    {
        if (_cachedPlacementFileNames != null)
            return _cachedPlacementFileNames;

        _cachedPlacementFileNames = GetDistinctBarSimpleFieldValues("placementfile");
        return _cachedPlacementFileNames;
    }

    private List<string> GetKnownPathabilityFlags()
    {
        if (_cachedPathabilityFlags != null)
            return _cachedPathabilityFlags;

        _cachedPathabilityFlags = GetDistinctBarSimpleFieldValues("pathabilityflags");
        return _cachedPathabilityFlags;
    }

    private List<string> GetKnownHotkeyContexts()
    {
        if (_cachedHotkeyContexts != null)
            return _cachedHotkeyContexts;

        _cachedHotkeyContexts = GetDistinctBarSimpleFieldValues("hotkeycontext");
        return _cachedHotkeyContexts;
    }

    private List<string> GetKnownMinimapIcons()
    {
        if (_cachedMinimapIcons != null)
            return _cachedMinimapIcons;

        _cachedMinimapIcons = GetDistinctBarSimpleFieldValues("minimapicon");
        return _cachedMinimapIcons;
    }

    private List<string> GetKnownBloodGroupNames()
    {
        if (_cachedBloodGroupNames != null)
            return _cachedBloodGroupNames;

        _cachedBloodGroupNames = LoadBloodGroupNamesFromBar();
        return _cachedBloodGroupNames;
    }

    private List<string> LoadBloodGroupNamesFromBar()
    {
        try
        {
            var barFile = _mainWindow.BarFile;
            var barStream = _mainWindow.BarFileStream;
            if (barFile != null && barStream != null && Path.GetFileName(barStream.Name).Equals("Data.bar", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractBloodGroupNamesFromBar(barFile, barStream.Name);
            }

            var dataBarPath = ResolveDataBarPath();
            if (!string.IsNullOrWhiteSpace(dataBarPath) && File.Exists(dataBarPath))
            {
                using var stream = File.OpenRead(dataBarPath);
                var file = new BarFile(stream);
                if (file.Load(out _))
                    return ExtractBloodGroupNamesFromBar(file, dataBarPath);
            }
        }
        catch
        {
            // Fall back to an empty list if BAR blood extraction fails.
        }

        return [];
    }

    private static List<string> ExtractBloodGroupNamesFromBar(BarFile barFile, string barPath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries;
        if (entries == null)
            return [];

        var bloodEntries = entries
            .Where(e => e.Name.Contains("blood", StringComparison.OrdinalIgnoreCase)
                     && e.Name.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        using var tempStream = File.OpenRead(barPath);
        foreach (var entry in bloodEntries)
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
                foreach (var name in doc.Descendants("bloodgroup")
                    .Select(x => (string?)x.Attribute("name"))
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    names.Add(name!);
                }
            }
            catch
            {
                // Skip malformed blood entries and keep what we already found.
            }
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetBuildLimitModeLabel(BuildLimitMode mode) => mode switch
    {
        BuildLimitMode.Standard => "Standard",
        BuildLimitMode.Dynamic => "Dynamic",
        BuildLimitMode.Shared => "Shared Build Limit",
        _ => "Standard",
    };

    private static BuildLimitMode ParseBuildLimitMode(string? value) => value switch
    {
        "Dynamic" => BuildLimitMode.Dynamic,
        "Shared Build Limit" => BuildLimitMode.Shared,
        _ => BuildLimitMode.Standard,
    };

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
        var entryList = entries.ToList();

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

        headerGrid.IsVisible = entryList.Count > 0;
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
            headerGrid.IsVisible = true;

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
                        headerGrid.IsVisible = stateStore.Count > 0;
                        MarkDirty();
                    }
                };
                Grid.SetColumn(btnDel, 3);
                rowPanel.Children.Add(btnDel);
            }

            commandContainer.Children.Add(rowPanel);
        }

        foreach (var entry in entryList)
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

    private void UpdateProtoActionTypeEditor(AutoCompleteBox typeAcb, string actionName)
    {
        if (TryResolveProtoActionType(actionName, out var mappedType) && !string.IsNullOrWhiteSpace(mappedType))
        {
            typeAcb.ItemsSource = GetProtoActionTypeOptions(mappedType);
            typeAcb.Text = mappedType;
            typeAcb.IsEnabled = false;
            typeAcb.IsDropDownOpen = false;
            return;
        }

        var currentValue = typeAcb.Text?.Trim() ?? "";
        var exactType = GetExactProtoActionTypeMatch(currentValue);
        if (!string.IsNullOrWhiteSpace(exactType))
            typeAcb.Text = exactType;

        typeAcb.ItemsSource = GetProtoActionTypeOptions(string.IsNullOrWhiteSpace(exactType) ? currentValue : exactType);
        typeAcb.IsEnabled = !_isReadOnly;
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

    private void ResetEditorScrollToTop()
    {
        _editorScroll.Offset = new Vector(0, 0);
        Dispatcher.UIThread.Post(() => _editorScroll.Offset = new Vector(0, 0), DispatcherPriority.Loaded);
    }

    private void BuildEditorPanel(string unitName, bool resetScroll = true)
    {
        _isPopulating = true;
        _editorPanel.Children.Clear();
        if (resetScroll)
            ResetEditorScrollToTop();
        _sectionJumpTargets.Clear();
        RebuildSectionJumpFlyout();
        _fieldControls.Clear();
        _otherSpecificAttributeContainers.Clear();
        _currentStringFieldIds.Clear();
        _costControls.Clear();
        _armorControls.Clear();
        _cultureFieldRows.Clear();
        _trainCommandRows.Clear();
        _techCommandRows.Clear();
        _unitCommandRows.Clear();
        _optionalCommandRows.Clear();
        _protoActionWidgets.Clear();
        _buildLimitRows.Clear();
        _resourceConversionRows.Clear();
        _dependentUnitRows.Clear();
        _spawnRows.Clear();
        _veterancyRankRows.Clear();
        _veterancyBonusRows.Clear();

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
            _pageSearchTargets.Clear();
            _pageSearchMatches.Clear();
            ClearPageSearchHighlights();
            RebuildSectionJumpFlyout();
            UpdatePageSearchUiState(hasMatches: false, hasQuery: !string.IsNullOrWhiteSpace(_pageSearchBox.Text), hasValidPattern: true);
            if (resetScroll)
                ResetEditorScrollToTop();
            _isPopulating = false;
            return;
        }

        _currentUnitName = unitName;
        RefreshCurrentUnitProtoActionMetadata(unit);
        var persistedUnitTypes = new HashSet<string>(
            unit.Elements("unittype")
                .Select(x => x.Value?.Trim() ?? "")
                .Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        bool useBuildingLayout =
            persistedUnitTypes.Contains("Building") ||
            !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "buildpoints"));

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
                field.Tag.Equals("tactics", StringComparison.OrdinalIgnoreCase) ||
                field.Tag.Equals("maxcontained", StringComparison.OrdinalIgnoreCase))
                continue;

            if (field.Tag.Equals("trainpoints", StringComparison.OrdinalIgnoreCase) && useBuildingLayout)
                continue;

            if (field.Tag.Equals("buildpoints", StringComparison.OrdinalIgnoreCase) && !useBuildingLayout)
                continue;

            if (field.Tag.Equals("turnrate", StringComparison.OrdinalIgnoreCase))
                continue;

            if (field.Tag.Equals("weightclass", StringComparison.OrdinalIgnoreCase) && useBuildingLayout)
                continue;

            if (field.Tag.Equals("obstructionradiusz", StringComparison.OrdinalIgnoreCase) ||
                field.Tag.Equals("maxvelocity", StringComparison.OrdinalIgnoreCase) ||
                field.Tag.Equals("maxrunvelocity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (field.Tag.Equals("maxhitpoints", StringComparison.OrdinalIgnoreCase))
                continue;

            if (field.Tag.Equals("obstructionradiusx", StringComparison.OrdinalIgnoreCase))
            {
                propertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var obstructionLabel = new TextBlock
                {
                    Text = "Obstruction Radius",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(obstructionLabel, 0);
                Grid.SetRow(obstructionLabel, gridRow);
                propertiesGrid.Children.Add(obstructionLabel);

                var obstructionGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto, 140, Auto, 140, Auto, 140")
                };

                var obstructionXLabel = new TextBlock
                {
                    Text = "X",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(obstructionXLabel, 0);
                obstructionGrid.Children.Add(obstructionXLabel);

                var obstructionXTb = new TextBox
                {
                    Text = ProtoXmlHandler.GetSimpleField(unit, "obstructionradiusx") ?? "",
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 4, 16, 4)
                };
                obstructionXTb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };
                Grid.SetColumn(obstructionXTb, 1);
                obstructionGrid.Children.Add(obstructionXTb);

                var obstructionZLabel = new TextBlock
                {
                    Text = "Z",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(obstructionZLabel, 2);
                obstructionGrid.Children.Add(obstructionZLabel);

                var obstructionZTb = new TextBox
                {
                    Text = ProtoXmlHandler.GetSimpleField(unit, "obstructionradiusz") ?? "",
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                obstructionZTb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };
                Grid.SetColumn(obstructionZTb, 3);
                obstructionGrid.Children.Add(obstructionZTb);

                var turnRateLabel = new TextBlock
                {
                    Text = "Turn Rate",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 4, 10, 4)
                };
                Grid.SetColumn(turnRateLabel, 4);
                obstructionGrid.Children.Add(turnRateLabel);

                var turnRateTb = new TextBox
                {
                    Text = ProtoXmlHandler.GetSimpleField(unit, "turnrate") ?? "",
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                turnRateTb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };
                Grid.SetColumn(turnRateTb, 5);
                obstructionGrid.Children.Add(turnRateTb);

                Grid.SetColumn(obstructionGrid, 1);
                Grid.SetRow(obstructionGrid, gridRow);
                propertiesGrid.Children.Add(obstructionGrid);

                _fieldControls["obstructionradiusx"] = obstructionXTb;
                _fieldControls["obstructionradiusz"] = obstructionZTb;
                _fieldControls["turnrate"] = turnRateTb;
                gridRow++;
                continue;
            }

            if (field.Tag.Equals("initialhitpoints", StringComparison.OrdinalIgnoreCase))
            {
                propertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var hitPointsRowLabel = new TextBlock
                {
                    Text = "Hit Points",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(hitPointsRowLabel, 0);
                Grid.SetRow(hitPointsRowLabel, gridRow);
                propertiesGrid.Children.Add(hitPointsRowLabel);

                string maxHitPointsInitial = ProtoXmlHandler.GetSimpleField(unit, "maxhitpoints") ?? "";
                string initialHitPointsStored = ProtoXmlHandler.GetSimpleField(unit, "initialhitpoints") ?? "";
                string initialHitPointsInitial = !string.IsNullOrWhiteSpace(initialHitPointsStored)
                    ? initialHitPointsStored
                    : maxHitPointsInitial;

                bool initialHitPointsLinkedToMax =
                    string.IsNullOrWhiteSpace(initialHitPointsStored) ||
                    string.Equals(initialHitPointsStored, maxHitPointsInitial, StringComparison.OrdinalIgnoreCase);

                var unitRegenElement = unit.Element("unitregen");
                string unitRegenInitial = unitRegenElement?.Value?.Trim() ?? "";
                string unitRegenIdleTimeoutInitial = unitRegenElement?.Attribute("idletimeout")?.Value?.Trim()
                    ?? unitRegenElement?.Attribute("idleTimeout")?.Value?.Trim()
                    ?? "";
                string unitRegenDamageTimeoutInitial = unitRegenElement?.Attribute("damagetimeout")?.Value?.Trim()
                    ?? unitRegenElement?.Attribute("damageTimeout")?.Value?.Trim()
                    ?? "";
                string unitRegenCombatMultiplierInitial = unitRegenElement?.Attribute("combatmultiplier")?.Value?.Trim()
                    ?? unitRegenElement?.Attribute("combatMultiplier")?.Value?.Trim()
                    ?? "";
                string unitRegenRateLimitInitial = unitRegenElement?.Attribute("ratelimit")?.Value?.Trim()
                    ?? unitRegenElement?.Attribute("rateLimit")?.Value?.Trim()
                    ?? unitRegenElement?.Attribute("ratelimit")?.Value?.Trim()
                    ?? "";

                var hitPointsGrid = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    ItemHeight = double.NaN
                };
                var unitRegenControls = new List<Control>();

                Grid SetInlineGridMargin(Grid grid, double right = 16)
                {
                    grid.Margin = new Thickness(0, 0, right, 0);
                    return grid;
                }

                Grid CreateInlineField(string label, double width, string value, out TextBox textBox, bool signedNumeric = false)
                {
                    var grid = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions($"Auto, {width}"),
                        Margin = new Thickness(0, 0, 16, 0)
                    };

                    var textLabel = new TextBlock
                    {
                        Text = label,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 4, 10, 4)
                    };
                    Grid.SetColumn(textLabel, 0);
                    grid.Children.Add(textLabel);

                    textBox = new TextBox
                    {
                        Text = value,
                        IsEnabled = !_isReadOnly,
                        Width = width,
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    if (signedNumeric)
                        AttachSignedDecimalBehavior(textBox);
                    else
                        AttachDecimalBehavior(textBox);

                    Grid.SetColumn(textBox, 1);
                    grid.Children.Add(textBox);
                    return grid;
                }

                var maxHitPointsGrid = CreateInlineField("Max Hit Points", 140, maxHitPointsInitial, out var maxHitPointsTb);
                hitPointsGrid.Children.Add(maxHitPointsGrid);

                var initialHitPointsGrid = CreateInlineField("Initial Hit Points", 140, initialHitPointsInitial, out var initialHitPointsTb);
                hitPointsGrid.Children.Add(initialHitPointsGrid);

                Button? addRegenButton = null;

                Button CreateAddRegenButton()
                {
                    var button = new Button
                    {
                        Content = "Add Regen",
                        Background = Brush.Parse("#2b7a0b"),
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    button.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (!proceed)
                            return;

                        button.IsVisible = false;
                        AddUnitRegenEditors();
                        MarkDirty();
                    };
                    return button;
                }

                void AddUnitRegenEditors()
                {
                    if (_fieldControls.ContainsKey("unitregen"))
                        return;

                    if (string.IsNullOrWhiteSpace(unitRegenInitial))
                        unitRegenInitial = "0";
                    if (string.IsNullOrWhiteSpace(unitRegenIdleTimeoutInitial))
                        unitRegenIdleTimeoutInitial = "0";
                    if (string.IsNullOrWhiteSpace(unitRegenDamageTimeoutInitial))
                        unitRegenDamageTimeoutInitial = "0";
                if (string.IsNullOrWhiteSpace(unitRegenCombatMultiplierInitial))
                    unitRegenCombatMultiplierInitial = "1";
                    if (string.IsNullOrWhiteSpace(unitRegenRateLimitInitial))
                        unitRegenRateLimitInitial = "1";

                    var regenRateGrid = CreateInlineField("Regen", 90, unitRegenInitial, out var unitRegenTb, true);
                    hitPointsGrid.Children.Add(regenRateGrid);
                    unitRegenControls.Add(regenRateGrid);
                    _fieldControls["unitregen"] = unitRegenTb;

                    var idleTimeoutGrid = CreateInlineField("Idle Timeout", 90, unitRegenIdleTimeoutInitial, out var idleTimeoutTb, true);
                    hitPointsGrid.Children.Add(idleTimeoutGrid);
                    unitRegenControls.Add(idleTimeoutGrid);
                    _fieldControls["unitregen.idleTimeout"] = idleTimeoutTb;

                    var damageTimeoutGrid = CreateInlineField("Damage Timeout", 90, unitRegenDamageTimeoutInitial, out var damageTimeoutTb, true);
                    hitPointsGrid.Children.Add(damageTimeoutGrid);
                    unitRegenControls.Add(damageTimeoutGrid);
                    _fieldControls["unitregen.damageTimeout"] = damageTimeoutTb;

                    var combatMultiplierGrid = CreateInlineField("Combat Multiplier", 90, unitRegenCombatMultiplierInitial, out var combatMultiplierTb, true);
                    hitPointsGrid.Children.Add(combatMultiplierGrid);
                    unitRegenControls.Add(combatMultiplierGrid);
                    _fieldControls["unitregen.combatMultiplier"] = combatMultiplierTb;

                    var rateLimitGrid = CreateInlineField("Rate Limit", 90, unitRegenRateLimitInitial, out var rateLimitTb, true);
                    hitPointsGrid.Children.Add(SetInlineGridMargin(rateLimitGrid, _isReadOnly ? 0 : 8));
                    unitRegenControls.Add(rateLimitGrid);
                    _fieldControls["unitregen.ratelimit"] = rateLimitTb;

                    foreach (var tb in new[] { unitRegenTb, idleTimeoutTb, damageTimeoutTb, combatMultiplierTb, rateLimitTb })
                    {
                        tb.TextChanged += async (s, e) =>
                        {
                            if (!_isPopulating)
                            {
                                var proceed = await CheckStartLocalMod();
                                if (proceed) MarkDirty();
                            }
                        };
                    }

                    rateLimitTb.LostFocus += (s, e) =>
                    {
                        if (_isPopulating)
                            return;

                        if (string.IsNullOrWhiteSpace(rateLimitTb.Text))
                            return;

                        if (double.TryParse(rateLimitTb.Text, out var rateLimit))
                        {
                            if (rateLimit < 0) rateLimit = 0;
                            if (rateLimit > 1) rateLimit = 1;
                            rateLimitTb.Text = rateLimit.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            rateLimitTb.Text = "";
                        }
                    };

                    if (!_isReadOnly)
                    {
                        var removeButton = new Button
                        {
                            Content = "X",
                            Background = Brush.Parse("#a00000"),
                            Margin = new Thickness(0, 4, 0, 4),
                            Width = 34
                        };
                        removeButton.Click += async (s, e) =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (!proceed)
                                return;

                            foreach (var key in new[] { "unitregen", "unitregen.idleTimeout", "unitregen.damageTimeout", "unitregen.combatMultiplier", "unitregen.ratelimit" })
                                _fieldControls.Remove(key);
                            foreach (var control in unitRegenControls.ToList())
                                hitPointsGrid.Children.Remove(control);
                            hitPointsGrid.Children.Remove(removeButton);
                            unitRegenControls.Clear();
                            unitRegenInitial = "";
                            unitRegenIdleTimeoutInitial = "";
                            unitRegenDamageTimeoutInitial = "";
                            unitRegenCombatMultiplierInitial = "";
                            unitRegenRateLimitInitial = "";
                            if (addRegenButton == null)
                            {
                                addRegenButton = CreateAddRegenButton();
                                hitPointsGrid.Children.Add(addRegenButton);
                            }
                            else
                            {
                                addRegenButton.IsVisible = true;
                            }
                            MarkDirty();
                        };
                        hitPointsGrid.Children.Add(removeButton);
                        unitRegenControls.Add(removeButton);
                    }
                }

                if (!string.IsNullOrWhiteSpace(unitRegenInitial) || unitRegenElement != null)
                {
                    AddUnitRegenEditors();
                }
                else if (!_isReadOnly)
                {
                    addRegenButton = CreateAddRegenButton();
                    hitPointsGrid.Children.Add(addRegenButton);
                }

                maxHitPointsTb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        if (initialHitPointsLinkedToMax)
                        {
                            initialHitPointsTb.Text = maxHitPointsTb.Text;
                        }

                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };

                initialHitPointsTb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };
                initialHitPointsTb.GotFocus += (s, e) =>
                {
                    if (!_isPopulating)
                        initialHitPointsLinkedToMax = false;
                };

                Grid.SetColumn(hitPointsGrid, 1);
                Grid.SetRow(hitPointsGrid, gridRow);
                propertiesGrid.Children.Add(hitPointsGrid);

                _fieldControls["maxhitpoints"] = maxHitPointsTb;
                _fieldControls["initialhitpoints"] = initialHitPointsTb;
                gridRow++;

                string maxShieldInitial = ProtoXmlHandler.GetSimpleField(unit, "maxshieldpoints") ?? "";
                string initialShieldStored = ProtoXmlHandler.GetSimpleField(unit, "initialshieldpoints") ?? "";
                string initialShieldInitial = !string.IsNullOrWhiteSpace(initialShieldStored)
                    ? initialShieldStored
                    : maxShieldInitial;
                var shieldRegenElement = unit.Element("unitshieldregen");
                string shieldRegenInitial = shieldRegenElement?.Value?.Trim() ?? "";
                string shieldRegenIdleTimeoutInitial = shieldRegenElement?.Attribute("idletimeout")?.Value?.Trim()
                    ?? shieldRegenElement?.Attribute("idleTimeout")?.Value?.Trim()
                    ?? "";
                string shieldRegenDamageTimeoutInitial = shieldRegenElement?.Attribute("damagetimeout")?.Value?.Trim()
                    ?? shieldRegenElement?.Attribute("damageTimeout")?.Value?.Trim()
                    ?? "";
                string shieldRegenCombatMultiplierInitial = shieldRegenElement?.Attribute("combatmultiplier")?.Value?.Trim()
                    ?? shieldRegenElement?.Attribute("combatMultiplier")?.Value?.Trim()
                    ?? "";
                string shieldRegenRateLimitInitial = shieldRegenElement?.Attribute("ratelimit")?.Value?.Trim()
                    ?? shieldRegenElement?.Attribute("rateLimit")?.Value?.Trim()
                    ?? "";

                bool initialShieldLinkedToMax =
                    string.IsNullOrWhiteSpace(initialShieldStored) ||
                    string.Equals(initialShieldStored, maxShieldInitial, StringComparison.OrdinalIgnoreCase);

                propertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var shieldRowLabel = new TextBlock
                {
                    Text = "Shield",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4),
                    IsVisible = !string.IsNullOrWhiteSpace(maxShieldInitial) || !string.IsNullOrWhiteSpace(initialShieldStored) || !string.IsNullOrWhiteSpace(shieldRegenInitial) || shieldRegenElement != null
                };
                Grid.SetColumn(shieldRowLabel, 0);
                Grid.SetRow(shieldRowLabel, gridRow);
                propertiesGrid.Children.Add(shieldRowLabel);

                var shieldGrid = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    ItemHeight = double.NaN
                };
                var shieldControls = new List<Control>();
                Button? addShieldButton = null;

                Button CreateAddShieldButton()
                {
                    var button = new Button
                    {
                        Content = "Add Shield",
                        Background = Brush.Parse("#2b7a0b"),
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    button.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (!proceed)
                            return;

                        shieldRowLabel.IsVisible = true;
                        button.IsVisible = false;
                        AddShieldEditors();
                        MarkDirty();
                    };
                    return button;
                }

                void AddShieldEditors()
                {
                    if (_fieldControls.ContainsKey("maxshieldpoints"))
                        return;

                    if (string.IsNullOrWhiteSpace(maxShieldInitial))
                        maxShieldInitial = "0";
                    if (string.IsNullOrWhiteSpace(initialShieldInitial))
                        initialShieldInitial = maxShieldInitial;
                    if (string.IsNullOrWhiteSpace(shieldRegenInitial))
                        shieldRegenInitial = "0";
                    if (string.IsNullOrWhiteSpace(shieldRegenIdleTimeoutInitial))
                        shieldRegenIdleTimeoutInitial = "0";
                    if (string.IsNullOrWhiteSpace(shieldRegenDamageTimeoutInitial))
                        shieldRegenDamageTimeoutInitial = "0";
                    if (string.IsNullOrWhiteSpace(shieldRegenCombatMultiplierInitial))
                        shieldRegenCombatMultiplierInitial = "1";
                    if (string.IsNullOrWhiteSpace(shieldRegenRateLimitInitial))
                        shieldRegenRateLimitInitial = "1";

                    var maxShieldGrid = CreateInlineField("Max Shield", 120, maxShieldInitial, out var maxShieldTb);
                    shieldGrid.Children.Add(maxShieldGrid);
                    shieldControls.Add(maxShieldGrid);
                    _fieldControls["maxshieldpoints"] = maxShieldTb;

                    var initialShieldGrid = CreateInlineField("Initial Shield", 120, initialShieldInitial, out var initialShieldTb);
                    shieldGrid.Children.Add(initialShieldGrid);
                    shieldControls.Add(initialShieldGrid);
                    _fieldControls["initialshieldpoints"] = initialShieldTb;

                    var shieldRegenGrid = CreateInlineField("Shield Regen", 90, shieldRegenInitial, out var shieldRegenTb, true);
                    shieldGrid.Children.Add(SetInlineGridMargin(shieldRegenGrid, _isReadOnly ? 0 : 8));
                    shieldControls.Add(shieldRegenGrid);
                    _fieldControls["unitshieldregen"] = shieldRegenTb;

                    var shieldIdleTimeoutGrid = CreateInlineField("Idle Timeout", 90, shieldRegenIdleTimeoutInitial, out var shieldIdleTimeoutTb, true);
                    shieldGrid.Children.Add(shieldIdleTimeoutGrid);
                    shieldControls.Add(shieldIdleTimeoutGrid);
                    _fieldControls["unitshieldregen.idletimeout"] = shieldIdleTimeoutTb;

                    var shieldDamageTimeoutGrid = CreateInlineField("Damage Timeout", 90, shieldRegenDamageTimeoutInitial, out var shieldDamageTimeoutTb, true);
                    shieldGrid.Children.Add(shieldDamageTimeoutGrid);
                    shieldControls.Add(shieldDamageTimeoutGrid);
                    _fieldControls["unitshieldregen.damagetimeout"] = shieldDamageTimeoutTb;

                    var shieldCombatMultiplierGrid = CreateInlineField("Combat Multiplier", 90, shieldRegenCombatMultiplierInitial, out var shieldCombatMultiplierTb, true);
                    shieldGrid.Children.Add(shieldCombatMultiplierGrid);
                    shieldControls.Add(shieldCombatMultiplierGrid);
                    _fieldControls["unitshieldregen.combatmultiplier"] = shieldCombatMultiplierTb;

                    var shieldRateLimitGrid = CreateInlineField("Rate Limit", 90, shieldRegenRateLimitInitial, out var shieldRateLimitTb, true);
                    shieldGrid.Children.Add(SetInlineGridMargin(shieldRateLimitGrid, _isReadOnly ? 0 : 8));
                    shieldControls.Add(shieldRateLimitGrid);
                    _fieldControls["unitshieldregen.ratelimit"] = shieldRateLimitTb;

                    maxShieldTb.TextChanged += async (s, e) =>
                    {
                        if (!_isPopulating)
                        {
                            if (initialShieldLinkedToMax)
                                initialShieldTb.Text = maxShieldTb.Text;

                            var proceed = await CheckStartLocalMod();
                            if (proceed) MarkDirty();
                        }
                    };

                    initialShieldTb.TextChanged += async (s, e) =>
                    {
                        if (!_isPopulating)
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed) MarkDirty();
                        }
                    };
                    initialShieldTb.GotFocus += (s, e) =>
                    {
                        if (!_isPopulating)
                            initialShieldLinkedToMax = false;
                    };

                    foreach (var tb in new[] { shieldRegenTb, shieldIdleTimeoutTb, shieldDamageTimeoutTb, shieldCombatMultiplierTb, shieldRateLimitTb })
                    {
                        tb.TextChanged += async (s, e) =>
                        {
                            if (!_isPopulating)
                            {
                                var proceed = await CheckStartLocalMod();
                                if (proceed) MarkDirty();
                            }
                        };
                    }

                    shieldRateLimitTb.LostFocus += (s, e) =>
                    {
                        if (_isPopulating)
                            return;

                        if (string.IsNullOrWhiteSpace(shieldRateLimitTb.Text))
                            return;

                        if (double.TryParse(shieldRateLimitTb.Text, out var rateLimit))
                        {
                            if (rateLimit < 0) rateLimit = 0;
                            if (rateLimit > 1) rateLimit = 1;
                            shieldRateLimitTb.Text = rateLimit.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            shieldRateLimitTb.Text = "";
                        }
                    };

                    if (!_isReadOnly)
                    {
                        var removeButton = new Button
                        {
                            Content = "X",
                            Background = Brush.Parse("#a00000"),
                            Margin = new Thickness(0, 4, 0, 4),
                            Width = 34
                        };
                        removeButton.Click += async (s, e) =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (!proceed)
                                return;

                            foreach (var key in new[] { "maxshieldpoints", "initialshieldpoints", "unitshieldregen", "unitshieldregen.idletimeout", "unitshieldregen.damagetimeout", "unitshieldregen.combatmultiplier", "unitshieldregen.ratelimit" })
                                _fieldControls.Remove(key);
                            foreach (var control in shieldControls.ToList())
                                shieldGrid.Children.Remove(control);
                            shieldGrid.Children.Remove(removeButton);
                            shieldControls.Clear();
                            maxShieldInitial = "";
                            initialShieldInitial = "";
                            shieldRegenInitial = "";
                            shieldRegenIdleTimeoutInitial = "";
                            shieldRegenDamageTimeoutInitial = "";
                            shieldRegenCombatMultiplierInitial = "";
                            shieldRegenRateLimitInitial = "";
                            shieldRowLabel.IsVisible = false;
                            if (addShieldButton == null)
                            {
                                addShieldButton = CreateAddShieldButton();
                                shieldGrid.Children.Add(addShieldButton);
                            }
                            else
                            {
                                addShieldButton.IsVisible = true;
                            }
                            MarkDirty();
                        };
                        shieldGrid.Children.Add(removeButton);
                        shieldControls.Add(removeButton);
                    }
                }

                if (!string.IsNullOrWhiteSpace(maxShieldInitial) || !string.IsNullOrWhiteSpace(initialShieldStored) || !string.IsNullOrWhiteSpace(shieldRegenInitial) || shieldRegenElement != null)
                {
                    AddShieldEditors();
                }
                else if (!_isReadOnly)
                {
                    addShieldButton = CreateAddShieldButton();
                    shieldGrid.Children.Add(addShieldButton);
                }

                Grid.SetColumn(shieldGrid, 1);
                Grid.SetRow(shieldGrid, gridRow);
                propertiesGrid.Children.Add(shieldGrid);
                gridRow++;
                continue;
            }

            if (field.Tag.Equals("movementtype", StringComparison.OrdinalIgnoreCase))
            {
                if (useBuildingLayout)
                    continue;

                propertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var movementLabel = new TextBlock
                {
                    Text = "Movement",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(movementLabel, 0);
                Grid.SetRow(movementLabel, gridRow);
                propertiesGrid.Children.Add(movementLabel);

                var movementGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto, 140, Auto, 120, Auto, 120")
                };

                var movementTypeLabel = new TextBlock
                {
                    Text = "Type",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(movementTypeLabel, 0);
                movementGrid.Children.Add(movementTypeLabel);

                var movementOptions = ProtoConstants.FieldSuggestions.TryGetValue("movementtype", out var movementList)
                    ? movementList
                    : [];
                string maxVelocityInitial = ProtoXmlHandler.GetSimpleField(unit, "maxvelocity") ?? "";
                string maxRunVelocityStored = ProtoXmlHandler.GetSimpleField(unit, "maxrunvelocity") ?? "";
                string maxRunVelocityInitial = !string.IsNullOrWhiteSpace(maxRunVelocityStored)
                    ? maxRunVelocityStored
                    : maxVelocityInitial;

                bool maxRunVelocityLinkedToMax =
                    string.IsNullOrWhiteSpace(maxRunVelocityStored) ||
                    string.Equals(maxRunVelocityStored, maxVelocityInitial, StringComparison.OrdinalIgnoreCase);

                var movementTypeCb = new ComboBox
                {
                    ItemsSource = movementOptions,
                    SelectedItem = movementOptions.FirstOrDefault(x => x.Equals(ProtoXmlHandler.GetSimpleField(unit, "movementtype") ?? "", StringComparison.OrdinalIgnoreCase)),
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 4, 16, 4)
                };
                movementTypeCb.SelectionChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };
                Grid.SetColumn(movementTypeCb, 1);
                movementGrid.Children.Add(movementTypeCb);

                var maxVelocityLabel = new TextBlock
                {
                    Text = "Max Velocity",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(maxVelocityLabel, 2);
                movementGrid.Children.Add(maxVelocityLabel);

                TextBox? maxRunVelocityTb = null;
                var maxVelocityTb = new TextBox
                {
                    Text = maxVelocityInitial,
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 4, 16, 4)
                };
                maxVelocityTb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        if (maxRunVelocityLinkedToMax && maxRunVelocityTb != null)
                        {
                            maxRunVelocityTb.Text = maxVelocityTb.Text;
                        }

                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };
                Grid.SetColumn(maxVelocityTb, 3);
                movementGrid.Children.Add(maxVelocityTb);

                var maxRunVelocityLabel = new TextBlock
                {
                    Text = "Max Run Velocity",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(maxRunVelocityLabel, 4);
                movementGrid.Children.Add(maxRunVelocityLabel);

                maxRunVelocityTb = new TextBox
                {
                    Text = maxRunVelocityInitial,
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                maxRunVelocityTb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };
                maxRunVelocityTb.GotFocus += (s, e) =>
                {
                    if (!_isPopulating)
                        maxRunVelocityLinkedToMax = false;
                };
                Grid.SetColumn(maxRunVelocityTb, 5);
                movementGrid.Children.Add(maxRunVelocityTb);

                Grid.SetColumn(movementGrid, 1);
                Grid.SetRow(movementGrid, gridRow);
                propertiesGrid.Children.Add(movementGrid);

                _fieldControls["movementtype"] = movementTypeCb;
                _fieldControls["maxvelocity"] = maxVelocityTb;
                _fieldControls["maxrunvelocity"] = maxRunVelocityTb;
                gridRow++;
                continue;
            }

            if (IsCultureAwareSimpleField(field.Tag))
            {
                propertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var cultureFieldLabel = new TextBlock
                {
                    Text = field.Label,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                Grid.SetColumn(cultureFieldLabel, 0);
                Grid.SetRow(cultureFieldLabel, gridRow);
                propertiesGrid.Children.Add(cultureFieldLabel);

                var fieldEntries = ProtoXmlHandler.GetCultureAwareSimpleFields(unit, field.Tag);
                var defaultEntry = fieldEntries.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Culture));
                var cultureEntries = fieldEntries
                    .Where(x => !string.IsNullOrWhiteSpace(x.Culture))
                    .ToList();
                var suggestionList = ProtoConstants.FieldSuggestions.TryGetValue(field.Tag, out var suggestions)
                    ? suggestions
                    : [];

                var fieldStack = new StackPanel { Spacing = 4 };
                var rowStates = new List<CultureFieldRowState>();
                _cultureFieldRows[field.Tag] = rowStates;
                AutoCompleteBox? defaultAcb = null;
                Control? defaultControl = null;

                AutoCompleteBox CreateValueEditor(string value, Thickness margin)
                {
                    var acb = new AutoCompleteBox
                    {
                        Text = value,
                        FilterMode = AutoCompleteFilterMode.Contains,
                        ItemsSource = suggestionList,
                        IsEnabled = !_isReadOnly,
                        Margin = margin
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
                    return acb;
                }

                void ShowDefaultRowLabelIfNeeded()
                {
                    if (defaultAcb == null || defaultControl is Grid)
                        return;

                    var defaultGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("150, *") };
                    var defaultLabel = new TextBlock
                    {
                        Text = "Default",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 4, 10, 4)
                    };
                    Grid.SetColumn(defaultLabel, 0);
                    defaultGrid.Children.Add(defaultLabel);

                    int index = -1;
                    if (defaultControl != null)
                    {
                        index = fieldStack.Children.IndexOf(defaultControl);
                        if (index >= 0)
                            fieldStack.Children.RemoveAt(index);
                    }

                    defaultAcb.Margin = new Thickness(0, 4, 0, 4);
                    Grid.SetColumn(defaultAcb, 1);
                    defaultGrid.Children.Add(defaultAcb);

                    if (index >= 0)
                        fieldStack.Children.Insert(index, defaultGrid);

                    defaultControl = defaultGrid;
                }

                bool showDefaultRow = !_isReadOnly || defaultEntry != null || cultureEntries.Count == 0;
                if (showDefaultRow)
                {
                    if (cultureEntries.Count > 0)
                    {
                        var defaultGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("150, *") };
                        var defaultLabel = new TextBlock
                        {
                            Text = "Default",
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 4, 10, 4)
                        };
                        Grid.SetColumn(defaultLabel, 0);
                        defaultGrid.Children.Add(defaultLabel);

                        defaultAcb = CreateValueEditor(defaultEntry?.Value ?? "", new Thickness(0, 4, 0, 4));
                        Grid.SetColumn(defaultAcb, 1);
                        defaultGrid.Children.Add(defaultAcb);
                        defaultControl = defaultGrid;
                        _fieldControls[field.Tag] = defaultAcb;
                    }
                    else
                    {
                        defaultAcb = CreateValueEditor(defaultEntry?.Value ?? "", new Thickness(0, 4, 0, 4));
                        defaultControl = defaultAcb;
                        _fieldControls[field.Tag] = defaultAcb;
                    }

                    fieldStack.Children.Add(defaultControl);
                }

                void AddCultureRow(string cultureLabel, string value)
                {
                    var rowGrid = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("150, *, Auto"),
                        Margin = new Thickness(0, 0, 0, 0)
                    };

                    var cultureCb = new ComboBox
                    {
                        ItemsSource = SupportedCultureLabels,
                        SelectedItem = SupportedCultureLabels.FirstOrDefault(x => x.Equals(cultureLabel, StringComparison.OrdinalIgnoreCase)) ?? cultureLabel,
                        IsEnabled = !_isReadOnly,
                        Width = 150,
                        Margin = new Thickness(0, 4, 8, 4)
                    };
                    cultureCb.SelectionChanged += async (s, e) =>
                    {
                        if (!_isPopulating)
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed) MarkDirty();
                        }
                    };
                    Grid.SetColumn(cultureCb, 0);
                    rowGrid.Children.Add(cultureCb);

                    var valueAcb = CreateValueEditor(value, new Thickness(0, 4, 8, 4));
                    Grid.SetColumn(valueAcb, 1);
                    rowGrid.Children.Add(valueAcb);

                    var rowState = new CultureFieldRowState
                    {
                        RowPanel = rowGrid,
                        CultureCb = cultureCb,
                        ValueAcb = valueAcb
                    };
                    rowStates.Add(rowState);

                    if (!_isReadOnly)
                    {
                        var btnDel = new Button
                        {
                            Content = "X",
                            Background = Brush.Parse("#8b0000"),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        btnDel.Click += async (s, e) =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                rowStates.Remove(rowState);
                                fieldStack.Children.Remove(rowGrid);
                                MarkDirty();
                            }
                        };
                        Grid.SetColumn(btnDel, 2);
                        rowGrid.Children.Add(btnDel);
                    }

                    if (!_isReadOnly && fieldStack.Children.Count > 0 && fieldStack.Children[^1] is Button)
                        fieldStack.Children.Insert(fieldStack.Children.Count - 1, rowGrid);
                    else
                        fieldStack.Children.Add(rowGrid);
                }

                foreach (var entry in cultureEntries)
                    AddCultureRow(GetCultureLabel(entry.Culture), entry.Value);

                if (!_isReadOnly)
                {
                    var btnAddCulture = new Button
                    {
                        Content = field.Tag.Equals("icon", StringComparison.OrdinalIgnoreCase)
                            ? "+ Add Culture Specific Icon"
                            : "+ Add Culture Specific Animfile",
                        Background = Brush.Parse("#2b7a0b"),
                        Margin = new Thickness(0, 4, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    btnAddCulture.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            var usedCultures = new HashSet<string>(
                                rowStates
                                    .Select(x => GetCultureValue(x.CultureCb.SelectedItem as string ?? x.CultureCb.SelectedValue as string ?? "")),
                                StringComparer.OrdinalIgnoreCase);
                            var nextCulture = SupportedCultures
                                .Select(x => x.Label)
                                .FirstOrDefault(x => !usedCultures.Contains(GetCultureValue(x)))
                                ?? SupportedCultures[0].Label;
                            ShowDefaultRowLabelIfNeeded();
                            AddCultureRow(nextCulture, "");
                            MarkDirty();
                        }
                    };
                    fieldStack.Children.Add(btnAddCulture);
                }

                Grid.SetColumn(fieldStack, 1);
                Grid.SetRow(fieldStack, gridRow);
                propertiesGrid.Children.Add(fieldStack);
                gridRow++;
                continue;
            }

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
                        EnableDropdownAutoComplete(acb);

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
                    EnableDropdownAutoComplete(acb);

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
        var initialDynamicBuildLimitEntries = ProtoXmlHandler.GetDynamicBuildLimitUnitTypes(unit)
            .Select(x => new ProtoBuildLimitEntry { Value = x })
            .ToList();
        var initialSharedBuildLimitEntries = ProtoXmlHandler.GetSharedBuildLimitEntries(unit);
        var buildLimitSuggestions = GetAvailableBuildLimitTargets();
        var buildLimitContainer = new StackPanel { Margin = new Thickness(0, 4, 0, 4), Spacing = 4 };
        _editorPanel.Children.Add(buildLimitContainer);

        BuildLimitMode GetInitialBuildLimitMode()
        {
            if (initialSharedBuildLimitEntries.Count > 0)
                return BuildLimitMode.Shared;
            if (initialDynamicBuildLimitEntries.Count > 0)
                return BuildLimitMode.Dynamic;
            return BuildLimitMode.Standard;
        }

        List<ProtoBuildLimitEntry> GetInitialBuildLimitEntries(BuildLimitMode mode) => mode switch
        {
            BuildLimitMode.Dynamic => initialDynamicBuildLimitEntries
                .Select(x => new ProtoBuildLimitEntry { Value = x.Value })
                .ToList(),
            BuildLimitMode.Shared => initialSharedBuildLimitEntries
                .Select(x => new ProtoBuildLimitEntry { Value = x.Value, Weight = x.Weight })
                .ToList(),
            _ => []
        };

        List<ProtoBuildLimitEntry> ReadCurrentBuildLimitEntries()
            => _buildLimitRows
                .Select(row => new ProtoBuildLimitEntry
                {
                    Value = row.ValueAcb.Text?.Trim() ?? "",
                    Weight = row.WeightTb?.Text?.Trim() ?? "",
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Value) || !string.IsNullOrWhiteSpace(x.Weight))
                .ToList();

        void ShowBuildLimit(string initialLimit, BuildLimitMode initialMode, IEnumerable<ProtoBuildLimitEntry>? initialEntries = null)
        {
            buildLimitContainer.Children.Clear();
            _buildLimitRows.Clear();
            _currentBuildLimitMode = initialMode;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, 120, 240, Auto") };

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

            var modePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            modePanel.Children.Add(new TextBlock
            {
                Text = "Type",
                VerticalAlignment = VerticalAlignment.Center
            });

            var modeCb = new ComboBox
            {
                ItemsSource = new[]
                {
                    GetBuildLimitModeLabel(BuildLimitMode.Standard),
                    GetBuildLimitModeLabel(BuildLimitMode.Dynamic),
                    GetBuildLimitModeLabel(BuildLimitMode.Shared)
                },
                SelectedItem = GetBuildLimitModeLabel(initialMode),
                IsEnabled = !_isReadOnly,
                Width = 180
            };
            modePanel.Children.Add(modeCb);
            Grid.SetColumn(modePanel, 2);
            grid.Children.Add(modePanel);

            var detailsContainer = new StackPanel { Spacing = 4, Margin = new Thickness(180, 0, 0, 0) };

            void AddBuildLimitTargetRow(BuildLimitMode mode, ProtoBuildLimitEntry? entry = null)
            {
                var rowPanel = new Grid
                {
                    ColumnDefinitions = mode == BuildLimitMode.Shared
                        ? new ColumnDefinitions("*, 120, Auto")
                        : new ColumnDefinitions("*, Auto"),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var valueAcb = new AutoCompleteBox
                {
                    Text = entry?.Value ?? "",
                    PlaceholderText = mode == BuildLimitMode.Dynamic ? "Unit Type or Proto Unit" : "Shared Unit Type or Proto Unit",
                    FilterMode = AutoCompleteFilterMode.Contains,
                    ItemsSource = buildLimitSuggestions,
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                string? selectedBuildLimitTarget = entry?.Value;
                EnableDropdownAutoComplete(valueAcb);
                valueAcb.TextChanged += async (s, e) =>
                {
                    if (!_isPopulating)
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed) MarkDirty();
                    }
                };
                valueAcb.SelectionChanged += (s, e) =>
                {
                    if (valueAcb.SelectedItem is string sel)
                    {
                        selectedBuildLimitTarget = sel;
                        valueAcb.Text = sel;
                    }
                };
                valueAcb.LostFocus += (s, e) =>
                {
                    if (_isPopulating)
                        return;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_isPopulating)
                            return;

                        var input = valueAcb.Text?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(input))
                            return;

                        var match = buildLimitSuggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrWhiteSpace(match) && !string.IsNullOrWhiteSpace(selectedBuildLimitTarget))
                        {
                            match = buildLimitSuggestions.FirstOrDefault(x => x.Equals(selectedBuildLimitTarget, StringComparison.OrdinalIgnoreCase));
                        }

                        valueAcb.Text = match ?? "";
                        selectedBuildLimitTarget = match;
                    }, DispatcherPriority.Background);
                };
                Grid.SetColumn(valueAcb, 0);
                rowPanel.Children.Add(valueAcb);

                TextBox? weightTb = null;
                if (mode == BuildLimitMode.Shared)
                {
                    weightTb = new TextBox
                    {
                        Text = string.IsNullOrWhiteSpace(entry?.Weight) ? "1" : entry!.Weight,
                        PlaceholderText = "Weight",
                        IsEnabled = !_isReadOnly,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    weightTb.AddHandler(InputElement.TextInputEvent, (sender, args) =>
                    {
                        var currentText = weightTb.Text ?? "";
                        var proposedText = currentText + args.Text;
                        if (!double.TryParse(proposedText, out _) &&
                            !string.Equals(proposedText, ".", StringComparison.Ordinal))
                        {
                            args.Handled = true;
                        }
                    }, RoutingStrategies.Tunnel);
                    weightTb.TextChanged += async (s, e) =>
                    {
                        if (!_isPopulating)
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                var text = weightTb.Text ?? "";
                                if (!string.IsNullOrWhiteSpace(text) &&
                                    (!double.TryParse(text, out double weight) || weight < 0))
                                {
                                    var filtered = new string(text
                                        .Where(ch => char.IsDigit(ch) || ch == '.')
                                        .ToArray());

                                    var dotIndex = filtered.IndexOf('.');
                                    if (dotIndex >= 0)
                                    {
                                        filtered = filtered[..(dotIndex + 1)] + filtered[(dotIndex + 1)..].Replace(".", "", StringComparison.Ordinal);
                                    }

                                    if (filtered.Length == 0)
                                        filtered = "0";

                                    if (double.TryParse(filtered, out var filteredWeight) && filteredWeight < 0)
                                        filtered = "0";

                                    if (!string.Equals(weightTb.Text, filtered, StringComparison.Ordinal))
                                        weightTb.Text = filtered;
                                }

                                MarkDirty();
                            }
                        }
                    };
                    Grid.SetColumn(weightTb, 1);
                    rowPanel.Children.Add(weightTb);
                }

                var rowState = new BuildLimitTargetRowState
                {
                    RowPanel = rowPanel,
                    ValueAcb = valueAcb,
                    WeightTb = weightTb
                };
                _buildLimitRows.Add(rowState);

                if (!_isReadOnly)
                {
                    var btnDelRow = new Button
                    {
                        Content = "X",
                        Background = Brush.Parse("#8b0000"),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    btnDelRow.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _buildLimitRows.Remove(rowState);
                            detailsContainer.Children.Remove(rowPanel);
                            MarkDirty();
                        }
                    };
                    Grid.SetColumn(btnDelRow, mode == BuildLimitMode.Shared ? 2 : 1);
                    rowPanel.Children.Add(btnDelRow);
                }

                if (!_isReadOnly &&
                    detailsContainer.Children.Count > 0 &&
                    detailsContainer.Children[^1] is Button)
                {
                    detailsContainer.Children.Insert(detailsContainer.Children.Count - 1, rowPanel);
                }
                else
                {
                    detailsContainer.Children.Add(rowPanel);
                }
            }

            void RenderBuildLimitModeDetails(BuildLimitMode mode, IEnumerable<ProtoBuildLimitEntry>? seedEntries = null)
            {
                _currentBuildLimitMode = mode;
                _buildLimitRows.Clear();
                detailsContainer.Children.Clear();

                if (mode == BuildLimitMode.Standard)
                    return;

                var headerGrid = new Grid
                {
                    ColumnDefinitions = mode == BuildLimitMode.Shared
                        ? new ColumnDefinitions("*, 120, Auto")
                        : new ColumnDefinitions("*, Auto"),
                    Margin = new Thickness(0, 0, 0, 2)
                };

                var valueHeader = new TextBlock
                {
                    Text = "Unit Type / Proto Unit",
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(valueHeader, 0);
                headerGrid.Children.Add(valueHeader);

                if (mode == BuildLimitMode.Shared)
                {
                    var weightHeader = new TextBlock
                    {
                        Text = "Weight",
                        FontWeight = FontWeight.Bold,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(weightHeader, 1);
                    headerGrid.Children.Add(weightHeader);
                }

                detailsContainer.Children.Add(headerGrid);

                foreach (var entry in seedEntries ?? [])
                    AddBuildLimitTargetRow(mode, entry);

                if (!_isReadOnly)
                {
                    var btnAddTarget = new Button
                    {
                        Content = mode == BuildLimitMode.Dynamic ? "+ Add Dynamic Type" : "+ Add Shared Type",
                        Background = Brush.Parse("#2b7a0b"),
                        Margin = new Thickness(0, 4, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    btnAddTarget.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            AddBuildLimitTargetRow(mode);
                            MarkDirty();
                        }
                    };
                    detailsContainer.Children.Add(btnAddTarget);
                }
            }

            modeCb.SelectionChanged += async (s, e) =>
            {
                if (_isPopulating)
                    return;

                var proceed = await CheckStartLocalMod();
                if (!proceed)
                    return;

                var seedEntries = ReadCurrentBuildLimitEntries();
                var selectedMode = ParseBuildLimitMode(modeCb.SelectedItem as string);
                RenderBuildLimitModeDetails(selectedMode, seedEntries);
                MarkDirty();
            };

            if (!_isReadOnly)
            {
                var btnDel = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
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
                Grid.SetColumn(btnDel, 3);
                grid.Children.Add(btnDel);
            }
            buildLimitContainer.Children.Add(grid);
            buildLimitContainer.Children.Add(detailsContainer);
            RenderBuildLimitModeDetails(initialMode, initialEntries);
        }

        void ShowAddBuildLimitButton()
        {
            buildLimitContainer.Children.Clear();
            _fieldControls.Remove("buildlimit");
            _buildLimitRows.Clear();
            _currentBuildLimitMode = BuildLimitMode.Standard;
            if (!_isReadOnly)
            {
                var btnAdd = new Button { Content = "+ Add a Build Limit", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(0, 4, 0, 4) };
                btnAdd.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        MarkDirty();
                        ShowBuildLimit("1", BuildLimitMode.Standard);
                    }
                };
                buildLimitContainer.Children.Add(btnAdd);
            }
        }

        var initialBuildLimitMode = GetInitialBuildLimitMode();
        bool hasBuildLimitData = !string.IsNullOrWhiteSpace(blVal)
            || initialDynamicBuildLimitEntries.Count > 0
            || initialSharedBuildLimitEntries.Count > 0;

        if (hasBuildLimitData)
        {
            ShowBuildLimit(blVal ?? "1", initialBuildLimitMode, GetInitialBuildLimitEntries(initialBuildLimitMode));
        }
        else
        {
            ShowAddBuildLimitButton();
        }

        string? maxContainedValue = ProtoXmlHandler.GetSimpleField(unit, "maxcontained");
        string containedHitPointBonusValue = ProtoXmlHandler.GetSimpleField(unit, "containedhitpointbonus") ?? "";
        string containedSpeedBonusValue = ProtoXmlHandler.GetSimpleField(unit, "containedspeedbonus") ?? "";
        string containedRegenRateValue = ProtoXmlHandler.GetSimpleField(unit, "containedregenrate") ?? "";
        var initialContains = new HashSet<string>(ProtoXmlHandler.GetContainList(unit), StringComparer.OrdinalIgnoreCase);
        var initialNotContains = new HashSet<string>(ProtoXmlHandler.GetNotContainList(unit), StringComparer.OrdinalIgnoreCase);
        var containSuggestions = GetAvailableBuildLimitTargets();
        var containContainer = new StackPanel { Margin = new Thickness(0, 4, 0, 4), Spacing = 4 };
        _editorPanel.Children.Add(containContainer);

        void ShowContainEditor(string initialMaxContained)
        {
            containContainer.Children.Clear();

            var containValues = new HashSet<string>(initialContains, StringComparer.OrdinalIgnoreCase);
            var notContainValues = new HashSet<string>(initialNotContains, StringComparer.OrdinalIgnoreCase);
            _currentContains = containValues;
            _currentNotContains = notContainValues;

            void AttachIntegerOnlyBehavior(TextBox textBox, string defaultValue)
            {
                textBox.AddHandler(InputElement.TextInputEvent, (sender, args) =>
                {
                    if (string.IsNullOrWhiteSpace(args.Text) || !args.Text.All(char.IsDigit))
                        args.Handled = true;
                }, RoutingStrategies.Tunnel);

                textBox.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(textBox.Text))
                        textBox.Text = defaultValue;
                };
            }

            void AttachNumberOnlyBehavior(TextBox textBox, string defaultValue)
            {
                textBox.AddHandler(InputElement.TextInputEvent, (sender, args) =>
                {
                    var proposed = (textBox.Text ?? "") + args.Text;
                    if (!double.TryParse(proposed, out _) && !string.Equals(proposed, ".", StringComparison.Ordinal))
                        args.Handled = true;
                }, RoutingStrategies.Tunnel);

                textBox.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(textBox.Text))
                        textBox.Text = defaultValue;
                };
            }

            var topGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, 120, Auto") };

            var lbl = new TextBlock { Text = "Contain", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            topGrid.Children.Add(lbl);

            var maxContainedTb = new TextBox
            {
                Text = initialMaxContained,
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 0, 10, 0)
            };
            AttachIntegerOnlyBehavior(maxContainedTb, "0");
            maxContainedTb.TextChanged += async (s, e) =>
            {
                if (!_isPopulating)
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        var filtered = new string((maxContainedTb.Text ?? "").Where(char.IsDigit).ToArray());
                        if (!string.Equals(maxContainedTb.Text, filtered, StringComparison.Ordinal))
                            maxContainedTb.Text = filtered;
                        MarkDirty();
                    }
                }
            };
            Grid.SetColumn(maxContainedTb, 1);
            topGrid.Children.Add(maxContainedTb);
            _fieldControls["maxcontained"] = maxContainedTb;

            if (!_isReadOnly)
            {
                var btnDel = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                btnDel.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _fieldControls.Remove("maxcontained");
                        _currentContains = null;
                        _currentNotContains = null;
                        MarkDirty();
                        ShowAddContainButton();
                    }
                };
                Grid.SetColumn(btnDel, 2);
                topGrid.Children.Add(btnDel);
            }

            containContainer.Children.Add(topGrid);

            void AddContainPicker(string title, HashSet<string> values)
            {
                var titleLabel = new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 4, 0, 2)
                };
                containContainer.Children.Add(titleLabel);

                var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                containContainer.Children.Add(wrap);

                void RefreshDisplay()
                {
                    wrap.Children.Clear();
                    foreach (var value in values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        var chip = CreateChip(value, async () =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                values.Remove(value);
                                MarkDirty();
                                RefreshDisplay();
                            }
                        });
                        wrap.Children.Add(chip);
                    }
                }

                RefreshDisplay();

                if (!_isReadOnly)
                {
                    var addGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto"), Margin = new Thickness(0, 4, 0, 4) };
                    var acbAdd = new AutoCompleteBox
                    {
                        FilterMode = AutoCompleteFilterMode.Contains,
                        ItemsSource = containSuggestions,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    EnableDropdownAutoComplete(acbAdd);
                    Grid.SetColumn(acbAdd, 0);
                    addGrid.Children.Add(acbAdd);

                    async void PerformAdd()
                    {
                        string input = acbAdd.Text?.Trim() ?? "";
                        string? match = containSuggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(match) && !values.Contains(match))
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                values.Add(match);
                                acbAdd.Text = "";
                                MarkDirty();
                                RefreshDisplay();
                                Dispatcher.UIThread.Post(() =>
                                {
                                    if (acbAdd.IsEnabled && string.IsNullOrWhiteSpace(acbAdd.Text))
                                        acbAdd.IsDropDownOpen = true;
                                });
                            }
                        }
                    }

                    var btnAdd = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b") };
                    btnAdd.Click += (s, e) => PerformAdd();
                    acbAdd.SelectionChanged += (s, e) =>
                    {
                        if (acbAdd.SelectedItem is string sel)
                        {
                            acbAdd.Text = sel;
                            PerformAdd();
                        }
                    };
                    Grid.SetColumn(btnAdd, 1);
                    addGrid.Children.Add(btnAdd);
                    containContainer.Children.Add(addGrid);
                }
            }

            AddContainPicker("Contain", containValues);
            AddContainPicker("Not Contain", notContainValues);

            bool hasContainBonusData =
                !string.IsNullOrWhiteSpace(containedHitPointBonusValue) ||
                !string.IsNullOrWhiteSpace(containedSpeedBonusValue) ||
                !string.IsNullOrWhiteSpace(containedRegenRateValue);

            var containBonusContainer = new StackPanel { Spacing = 4 };
            containContainer.Children.Add(containBonusContainer);

            void ShowContainBonusEditor()
            {
                containBonusContainer.Children.Clear();

                var titleLabel = new TextBlock
                {
                    Text = "Contain Bonus Stats",
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 4, 0, 2)
                };
                containBonusContainer.Children.Add(titleLabel);

                var bonusGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto, 110, Auto, 110, Auto, 110, Auto")
                };

                void AddBonusField(string label, string key, string initialValue, int labelColumn, int valueColumn, Thickness margin)
                {
                    var textLabel = new TextBlock
                    {
                        Text = label,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 4, 10, 4)
                    };
                    Grid.SetColumn(textLabel, labelColumn);
                    bonusGrid.Children.Add(textLabel);

                    var tb = new TextBox
                    {
                        Text = initialValue,
                        IsEnabled = !_isReadOnly,
                        Margin = margin
                    };
                    AttachNumberOnlyBehavior(tb, "0");
                    tb.TextChanged += async (s, e) =>
                    {
                        if (!_isPopulating)
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                var text = tb.Text ?? "";
                                if (!string.IsNullOrWhiteSpace(text) &&
                                    (!double.TryParse(text, out _) || text.Count(ch => ch == '.') > 1))
                                {
                                    var filtered = new string(text.Where(ch => char.IsDigit(ch) || ch == '.').ToArray());
                                    var dotIndex = filtered.IndexOf('.');
                                    if (dotIndex >= 0)
                                        filtered = filtered[..(dotIndex + 1)] + filtered[(dotIndex + 1)..].Replace(".", "", StringComparison.Ordinal);
                                    if (!string.Equals(tb.Text, filtered, StringComparison.Ordinal))
                                        tb.Text = filtered;
                                }
                                MarkDirty();
                            }
                        }
                    };
                    Grid.SetColumn(tb, valueColumn);
                    bonusGrid.Children.Add(tb);
                    _fieldControls[key] = tb;
                }

                AddBonusField("HP Bonus", "containedhitpointbonus", string.IsNullOrWhiteSpace(containedHitPointBonusValue) ? "0" : containedHitPointBonusValue, 0, 1, new Thickness(0, 4, 16, 4));
                AddBonusField("Speed Bonus", "containedspeedbonus", string.IsNullOrWhiteSpace(containedSpeedBonusValue) ? "0" : containedSpeedBonusValue, 2, 3, new Thickness(0, 4, 16, 4));
                AddBonusField("Regen Rate", "containedregenrate", string.IsNullOrWhiteSpace(containedRegenRateValue) ? "0" : containedRegenRateValue, 4, 5, new Thickness(0, 4, 10, 4));

                if (!_isReadOnly)
                {
                    var btnDel = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                    btnDel.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _fieldControls.Remove("containedhitpointbonus");
                            _fieldControls.Remove("containedspeedbonus");
                            _fieldControls.Remove("containedregenrate");
                            containedHitPointBonusValue = "";
                            containedSpeedBonusValue = "";
                            containedRegenRateValue = "";
                            MarkDirty();
                            ShowAddContainBonusButton();
                        }
                    };
                    Grid.SetColumn(btnDel, 6);
                    bonusGrid.Children.Add(btnDel);
                }

                containBonusContainer.Children.Add(bonusGrid);
            }

            void ShowAddContainBonusButton()
            {
                containBonusContainer.Children.Clear();
                _fieldControls.Remove("containedhitpointbonus");
                _fieldControls.Remove("containedspeedbonus");
                _fieldControls.Remove("containedregenrate");

                if (!_isReadOnly)
                {
                    var btnAdd = new Button
                    {
                        Content = "+ Add Contain Bonus Stats",
                        Background = Brush.Parse("#2b7a0b"),
                        Margin = new Thickness(0, 4, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    btnAdd.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            containedHitPointBonusValue = "0";
                            containedSpeedBonusValue = "0";
                            containedRegenRateValue = "0";
                            MarkDirty();
                            ShowContainBonusEditor();
                        }
                    };
                    containBonusContainer.Children.Add(btnAdd);
                }
            }

            if (hasContainBonusData)
                ShowContainBonusEditor();
            else
                ShowAddContainBonusButton();
        }

        void ShowAddContainButton()
        {
            containContainer.Children.Clear();
            _fieldControls.Remove("maxcontained");
            _currentContains = null;
            _currentNotContains = null;

            if (!_isReadOnly)
            {
                var btnAdd = new Button
                {
                    Content = "+ Add Contain",
                    Background = Brush.Parse("#2b7a0b"),
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                btnAdd.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        initialContains = [];
                        initialNotContains = [];
                        MarkDirty();
                        ShowContainEditor("0");
                    }
                };
                containContainer.Children.Add(btnAdd);
            }
        }

        bool hasContainData = !string.IsNullOrWhiteSpace(maxContainedValue) || initialContains.Count > 0 || initialNotContains.Count > 0;
        if (hasContainData)
            ShowContainEditor(maxContainedValue ?? "0");
        else
            ShowAddContainButton();

        var otherSpecificContainer = new StackPanel { Margin = new Thickness(0, 4, 0, 4), Spacing = 4 };
        _editorPanel.Children.Add(otherSpecificContainer);
        _currentSharedSelectionUnitTypes = null;
        _currentRechargeIncludeTypes = null;
        _currentRechargeExcludeTypes = null;
        _currentAuxRechargeIncludeTypes = null;
        _currentAuxRechargeExcludeTypes = null;
        _currentVeterancyIncludeTypes = null;
        _currentVeterancyExcludeTypes = null;
        _currentRespawnTrainTypes = null;
        _currentRespawnTrainExcludeTypes = null;
        _onDamageModifyRows.Clear();

        var otherAttributeSuggestions = GetAvailableBuildLimitTargets();
        var otherProtoUnitSuggestions = GetAvailableTrainUnitNames();
        var protoActionSuggestions = _protoActionNameSuggestions;

        void AttachDecimalBehavior(TextBox textBox)
        {
            textBox.AddHandler(InputElement.TextInputEvent, (sender, args) =>
            {
                var proposed = (textBox.Text ?? "") + args.Text;
                if (!double.TryParse(proposed, out _) && !string.Equals(proposed, ".", StringComparison.Ordinal))
                    args.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        void AttachSignedDecimalBehavior(TextBox textBox)
        {
            textBox.AddHandler(InputElement.TextInputEvent, (sender, args) =>
            {
                var proposed = (textBox.Text ?? "") + args.Text;
                if (string.Equals(proposed, "-", StringComparison.Ordinal) ||
                    string.Equals(proposed, ".", StringComparison.Ordinal) ||
                    string.Equals(proposed, "-.", StringComparison.Ordinal))
                {
                    return;
                }

                if (!double.TryParse(proposed, out _))
                    args.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        async Task HandleOtherFieldChangedAsync()
        {
            if (_isPopulating)
                return;

            var proceed = await CheckStartLocalMod();
            if (proceed)
                MarkDirty();
        }

        void RegisterOtherSpecificContainer(string key, Control control)
            => _otherSpecificAttributeContainers[key] = control;

        void RemoveOtherSpecificContainer(string key, params string[] fieldKeys)
        {
            if (_otherSpecificAttributeContainers.TryGetValue(key, out var control))
            {
                otherSpecificContainer.Children.Remove(control);
                _otherSpecificAttributeContainers.Remove(key);
            }

            foreach (var fieldKey in fieldKeys)
                _fieldControls.Remove(fieldKey);
        }

        static IReadOnlyList<string> MaterializeSuggestionItems(IEnumerable<string> suggestions)
        {
            if (suggestions is IReadOnlyList<string> readOnlyList)
                return readOnlyList;

            return suggestions.ToList();
        }

        AutoCompleteBox CreateOtherSuggestionBox(string initialValue, IEnumerable<string> suggestions, string? placeholder = null)
        {
            var acb = new AutoCompleteBox
            {
                Text = initialValue,
                PlaceholderText = placeholder,
                FilterMode = AutoCompleteFilterMode.Contains,
                ItemsSource = MaterializeSuggestionItems(suggestions),
                IsEnabled = !_isReadOnly
            };
            EnableDropdownAutoComplete(acb);
            acb.TextChanged += async (s, e) => await HandleOtherFieldChangedAsync();
            return acb;
        }

        AutoCompleteBox CreateValidatedOtherSuggestionBox(string initialValue, IEnumerable<string> suggestions, string? placeholder = null, bool allowCustom = false, bool suggestionsAlreadyNormalized = false)
        {
            IReadOnlyList<string> suggestionList = suggestionsAlreadyNormalized
                ? MaterializeSuggestionItems(suggestions)
                : suggestions
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var acb = CreateOtherSuggestionBox(initialValue, suggestionList, placeholder);
            string? selectedValue = suggestionList.FirstOrDefault(x => x.Equals(initialValue, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(selectedValue))
                acb.Text = selectedValue;

            acb.SelectionChanged += (s, e) =>
            {
                if (acb.SelectedItem is string selected)
                {
                    selectedValue = selected;
                    acb.Text = selected;
                }
            };

            acb.LostFocus += (s, e) =>
            {
                if (_isPopulating)
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (_isPopulating)
                        return;

                    var input = acb.Text?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(input))
                    {
                        if (!allowCustom)
                            selectedValue = null;
                        return;
                    }

                    var match = suggestionList.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(match))
                    {
                        acb.Text = match;
                        selectedValue = match;
                        return;
                    }

                    if (allowCustom)
                    {
                        selectedValue = input;
                        return;
                    }

                    acb.Text = "";
                    selectedValue = null;
                }, DispatcherPriority.Background);
            };

            return acb;
        }

        TextBox CreateOtherTextBox(string initialValue, bool numeric = false)
        {
            var tb = new TextBox
            {
                Text = initialValue,
                IsEnabled = !_isReadOnly
            };
            if (numeric)
                AttachDecimalBehavior(tb);
            tb.TextChanged += async (s, e) => await HandleOtherFieldChangedAsync();
            return tb;
        }

        void AddSimpleOtherSpecificAttribute(string key, string tag)
        {
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBlock { Text = GetOtherSpecificAttributeLabel(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(label, 0);
            rowGrid.Children.Add(label);

            Control editor;
            var initialValue = ProtoXmlHandler.GetSimpleField(unit, tag) ?? "";
            if (tag.Equals("dodgechance", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(initialValue))
                initialValue = "0";
            if (tag.Equals("conversionresistance", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(initialValue))
                initialValue = "1";
            if (tag.Equals("formationorder", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(initialValue))
                initialValue = "0";
            if (tag.Equals("workersoftlimit", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(initialValue))
                initialValue = "0";
            if (OtherSpecificSimpleSuggestionTags.Contains(tag))
            {
                IEnumerable<string> suggestions = tag switch
                {
                    "culture" => SupportedCultureLabels,
                    "resourcesubtype" => GetKnownResourceSubtypeNames(),
                    "hotkeycontext" => GetKnownHotkeyContexts(),
                    "allyhotkeycontext" => GetKnownHotkeyContexts(),
                    "partisantype" => otherProtoUnitSuggestions,
                    "socketunittype" => otherAttributeSuggestions,
                    "disguiseprotoid" or "deadreplacement" or "deadtransform" or "eidolonprotoid" => otherProtoUnitSuggestions,
                    "initialunitaistance" => KnownInitialUnitAiStances,
                    "pathabilityflags" => GetKnownPathabilityFlags(),
                    "placementfile" => GetKnownPlacementFileNames(),
                    "selfdestructprotoaction" or "birthprotoaction" or "stackprotoaction" => protoActionSuggestions,
                    _ => Array.Empty<string>()
                };
                editor = tag switch
                {
                    "placementfile" => CreateValidatedOtherSuggestionBox(initialValue, suggestions, GetOtherSpecificAttributeLabel(key), allowCustom: true),
                    "partisantype" => CreateValidatedOtherSuggestionBox(initialValue, suggestions, GetOtherSpecificAttributeLabel(key), suggestionsAlreadyNormalized: true),
                    "resourcesubtype" or "initialunitaistance" or "pathabilityflags" or "hotkeycontext" or "allyhotkeycontext"
                        => CreateValidatedOtherSuggestionBox(initialValue, suggestions, GetOtherSpecificAttributeLabel(key)),
                    _ => CreateOtherSuggestionBox(initialValue, suggestions, GetOtherSpecificAttributeLabel(key))
                };
            }
            else
            {
                editor = CreateOtherTextBox(initialValue, OtherSpecificSimpleNumberTags.Contains(tag));
            }

            Grid.SetColumn(editor, 1);
            rowGrid.Children.Add(editor);
            _fieldControls[tag] = editor;

            var linkedFlag = GetFlagForOtherSpecificAttribute(tag);
            if (!_isReadOnly && !string.IsNullOrWhiteSpace(linkedFlag))
                _currentFlags?.Add(linkedFlag);

            if (tag.Equals("dodgechance", StringComparison.OrdinalIgnoreCase) && editor is TextBox dodgeChanceTb)
            {
                dodgeChanceTb.LostFocus += (s, e) =>
                {
                    if (_isPopulating)
                        return;

                    if (string.IsNullOrWhiteSpace(dodgeChanceTb.Text))
                    {
                        dodgeChanceTb.Text = "0";
                        return;
                    }

                    if (double.TryParse(dodgeChanceTb.Text, out var dodgeChance))
                    {
                        if (dodgeChance < 0) dodgeChance = 0;
                        if (dodgeChance > 1) dodgeChance = 1;
                        dodgeChanceTb.Text = dodgeChance.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        dodgeChanceTb.Text = "0";
                    }
                };
            }
            else if (tag.Equals("formationorder", StringComparison.OrdinalIgnoreCase) && editor is TextBox formationOrderTb)
            {
                formationOrderTb.LostFocus += (s, e) =>
                {
                    if (_isPopulating)
                        return;

                    if (string.IsNullOrWhiteSpace(formationOrderTb.Text))
                    {
                        formationOrderTb.Text = "0";
                        return;
                    }

                    if (int.TryParse(formationOrderTb.Text, out var formationOrder))
                    {
                        if (formationOrder < 0) formationOrder = 0;
                        if (formationOrder > 5) formationOrder = 5;
                        formationOrderTb.Text = formationOrder.ToString();
                    }
                    else
                    {
                        formationOrderTb.Text = "0";
                    }
                };
            }

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        var linkedFlag = GetFlagForOtherSpecificAttribute(tag);
                        if (!string.IsNullOrWhiteSpace(linkedFlag))
                            _currentFlags?.Remove(linkedFlag);
                        RemoveOtherSpecificContainer(key, tag);
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 2);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddSelectionRadiusEditor()
        {
            const string key = "selectionradius";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 140, Auto, 140, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Selection Radius", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var xLabel = new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(xLabel, 1);
            rowGrid.Children.Add(xLabel);

            var xTb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "selectionradiusx") ?? "", true);
            Grid.SetColumn(xTb, 2);
            rowGrid.Children.Add(xTb);
            _fieldControls["selectionradiusx"] = xTb;

            var zLabel = new TextBlock { Text = "Z", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(zLabel, 3);
            rowGrid.Children.Add(zLabel);

            var zTb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "selectionradiusz") ?? "", true);
            Grid.SetColumn(zTb, 4);
            rowGrid.Children.Add(zTb);
            _fieldControls["selectionradiusz"] = zTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "selectionradiusx", "selectionradiusz");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddPlacementObstructionEditor()
        {
            const string key = "placementobstruction";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 140, Auto, 140, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Placement Obstruction", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var xLabel = new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(xLabel, 1);
            rowGrid.Children.Add(xLabel);

            var xTb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "placementobstructionradiusx") ?? "", true);
            Grid.SetColumn(xTb, 2);
            rowGrid.Children.Add(xTb);
            _fieldControls["placementobstructionradiusx"] = xTb;

            var zLabel = new TextBlock { Text = "Z", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(zLabel, 3);
            rowGrid.Children.Add(zLabel);

            var zTb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "placementobstructionradiusz") ?? "", true);
            Grid.SetColumn(zTb, 4);
            rowGrid.Children.Add(zTb);
            _fieldControls["placementobstructionradiusz"] = zTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "placementobstructionradiusx", "placementobstructionradiusz");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddFarmingEditor()
        {
            const string key = "farming";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 90, Auto, 90, Auto, 90, Auto, 90, Auto, 90, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Farming Data", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var radiusXLabel = new TextBlock { Text = "Radius X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(radiusXLabel, 1);
            rowGrid.Children.Add(radiusXLabel);
            var radiusX = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingradiusx") ?? "0", true);
            Grid.SetColumn(radiusX, 2);
            rowGrid.Children.Add(radiusX);
            _fieldControls["farmingradiusx"] = radiusX;

            var radiusZLabel = new TextBlock { Text = "Radius Z", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(radiusZLabel, 3);
            rowGrid.Children.Add(radiusZLabel);
            var radiusZ = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingradiusz") ?? "0", true);
            Grid.SetColumn(radiusZ, 4);
            rowGrid.Children.Add(radiusZ);
            _fieldControls["farmingradiusz"] = radiusZ;

            var obstructionXLabel = new TextBlock { Text = "Obstruct X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(obstructionXLabel, 5);
            rowGrid.Children.Add(obstructionXLabel);
            var obstructionX = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingobstructionradiusx") ?? "0", true);
            Grid.SetColumn(obstructionX, 6);
            rowGrid.Children.Add(obstructionX);
            _fieldControls["farmingobstructionradiusx"] = obstructionX;

            var obstructionZLabel = new TextBlock { Text = "Obstruct Z", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(obstructionZLabel, 7);
            rowGrid.Children.Add(obstructionZLabel);
            var obstructionZ = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingobstructionradiusz") ?? "0", true);
            Grid.SetColumn(obstructionZ, 8);
            rowGrid.Children.Add(obstructionZ);
            _fieldControls["farmingobstructionradiusz"] = obstructionZ;

            var numStopsLabel = new TextBlock { Text = "Num Stops", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(numStopsLabel, 9);
            rowGrid.Children.Add(numStopsLabel);
            var numSpots = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingnumstops") ?? "8", true);
            numSpots.LostFocus += (s, e) =>
            {
                if (int.TryParse(numSpots.Text?.Trim(), out var parsedStops))
                {
                    if (parsedStops < 2)
                        numSpots.Text = "2";
                }
                else if (string.IsNullOrWhiteSpace(numSpots.Text))
                {
                    numSpots.Text = "8";
                }
            };
            Grid.SetColumn(numSpots, 10);
            rowGrid.Children.Add(numSpots);
            _fieldControls["farmingnumstops"] = numSpots;
            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "farmingradiusx", "farmingradiusz", "farmingobstructionradiusx", "farmingobstructionradiusz", "farmingnumstops");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 11);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddStealthEditor()
        {
            const string key = "stealth";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 100, Auto, 100, Auto, 100, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Stealth", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            void AddStealthField(string labelText, string fieldKey, int labelColumn, int valueColumn)
            {
                var label = new TextBlock { Text = labelText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
                Grid.SetColumn(label, labelColumn);
                rowGrid.Children.Add(label);

                var tb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, fieldKey) ?? "0", true);
                Grid.SetColumn(tb, valueColumn);
                rowGrid.Children.Add(tb);
                _fieldControls[fieldKey] = tb;
            }

            AddStealthField("Detect Radius", "stealthdetectionradius", 1, 2);
            AddStealthField("Reveal Self Radius", "stealthrevealselfradius", 3, 4);
            AddStealthField("Show Silhouette Radius", "stealthshowsilhouetteradius", 5, 6);

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "stealthdetectionradius", "stealthrevealselfradius", "stealthshowsilhouetteradius");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 7);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddPartisansEditor()
        {
            const string key = "partisans";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 260, Auto, 100, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Partisans", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var typeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(typeLabel, 1);
            rowGrid.Children.Add(typeLabel);

            var partisantypeInitial = ProtoXmlHandler.GetSimpleField(unit, "partisantype") ?? "";
            var partisantypeAcb = CreateValidatedOtherSuggestionBox(partisantypeInitial, otherProtoUnitSuggestions, "Proto Unit", suggestionsAlreadyNormalized: true);
            Grid.SetColumn(partisantypeAcb, 2);
            rowGrid.Children.Add(partisantypeAcb);
            _fieldControls["partisantype"] = partisantypeAcb;

            var countLabel = new TextBlock { Text = "Count", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(countLabel, 3);
            rowGrid.Children.Add(countLabel);

            var partisancountTb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "partisancount") ?? "0", true);
            Grid.SetColumn(partisancountTb, 4);
            rowGrid.Children.Add(partisancountTb);
            _fieldControls["partisancount"] = partisancountTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "partisantype", "partisancount");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddBloodAndBonesEditor()
        {
            const string key = "bloodandbones";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };

            var row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 280, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            row1.Children.Add(new TextBlock { Text = "Blood and bones data", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var groupLabel = new TextBlock { Text = "Blood Group", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(groupLabel, 1);
            row1.Children.Add(groupLabel);

            var bloodGroupInitial = ProtoXmlHandler.GetSimpleField(unit, "bloodgroupoverride") ?? "";
            var bloodGroupAcb = CreateValidatedOtherSuggestionBox(bloodGroupInitial, GetKnownBloodGroupNames(), "Blood Group");
            Grid.SetColumn(bloodGroupAcb, 2);
            row1.Children.Add(bloodGroupAcb);
            _fieldControls["bloodgroupoverride"] = bloodGroupAcb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "bloodgroupoverride", "bloodscalemodify", "bonescalemodify");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 3);
                row1.Children.Add(deleteButton);
            }

            stack.Children.Add(row1);

            var row2 = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 100, Auto, 100"), Margin = new Thickness(0, 2, 0, 2) };
            row2.Children.Add(new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center });

            var bloodScaleLabel = new TextBlock { Text = "Blood Scale", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(bloodScaleLabel, 1);
            row2.Children.Add(bloodScaleLabel);

            var bloodScaleTb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "bloodscalemodify") ?? "1", true);
            Grid.SetColumn(bloodScaleTb, 2);
            row2.Children.Add(bloodScaleTb);
            _fieldControls["bloodscalemodify"] = bloodScaleTb;

            var boneScaleLabel = new TextBlock { Text = "Bone Scale", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(boneScaleLabel, 3);
            row2.Children.Add(boneScaleLabel);

            var boneScaleTb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "bonescalemodify") ?? "1", true);
            Grid.SetColumn(boneScaleTb, 4);
            row2.Children.Add(boneScaleTb);
            _fieldControls["bonescalemodify"] = boneScaleTb;

            stack.Children.Add(row2);

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddCreationFadeEditor()
        {
            const string key = "creationfade";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var element = unit.Element("creationfadetime");
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 100, Auto, 100, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Creation Fade Time", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(valueLabel, 1);
            rowGrid.Children.Add(valueLabel);

            var valueTb = CreateOtherTextBox(element?.Value?.Trim() ?? "0", true);
            Grid.SetColumn(valueTb, 2);
            rowGrid.Children.Add(valueTb);
            _fieldControls["creationfadetime.value"] = valueTb;

            var initAlphaLabel = new TextBlock { Text = "Init Alpha", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(initAlphaLabel, 3);
            rowGrid.Children.Add(initAlphaLabel);

            var initAlphaTb = CreateOtherTextBox((string?)element?.Attribute("initalpha") ?? "0", true);
            Grid.SetColumn(initAlphaTb, 4);
            rowGrid.Children.Add(initAlphaTb);
            _fieldControls["creationfadetime.initalpha"] = initAlphaTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "creationfadetime.value", "creationfadetime.initalpha");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddHeightBobEditor()
        {
            const string key = "heightbobdata";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var element = unit.Element("heightbob");
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 100, Auto, 100, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Height Bob", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var periodLabel = new TextBlock { Text = "Period", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(periodLabel, 1);
            rowGrid.Children.Add(periodLabel);

            var periodTb = CreateOtherTextBox((string?)element?.Attribute("period") ?? "0", true);
            Grid.SetColumn(periodTb, 2);
            rowGrid.Children.Add(periodTb);
            _fieldControls["heightbob.period"] = periodTb;

            var magnitudeLabel = new TextBlock { Text = "Magnitude", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(magnitudeLabel, 3);
            rowGrid.Children.Add(magnitudeLabel);

            var magnitudeTb = CreateOtherTextBox((string?)element?.Attribute("magnitude") ?? "0", true);
            Grid.SetColumn(magnitudeTb, 4);
            rowGrid.Children.Add(magnitudeTb);
            _fieldControls["heightbob.magnitude"] = magnitudeTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "heightbob.period", "heightbob.magnitude");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddInitialShadingEditor()
        {
            const string key = "initialshading";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var element = unit.Element("initialshading");
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 160, Auto, 100, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Initial Shading", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var typeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(typeLabel, 1);
            rowGrid.Children.Add(typeLabel);

            var typeAcb = CreateValidatedOtherSuggestionBox((string?)element?.Attribute("type") ?? "", KnownInitialShadingTypes, "Type");
            Grid.SetColumn(typeAcb, 2);
            rowGrid.Children.Add(typeAcb);
            _fieldControls["initialshading.type"] = typeAcb;

            var factorLabel = new TextBlock { Text = "Factor", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(factorLabel, 3);
            rowGrid.Children.Add(factorLabel);

            var factorTb = CreateOtherTextBox((string?)element?.Attribute("factor") ?? "1", true);
            Grid.SetColumn(factorTb, 4);
            rowGrid.Children.Add(factorTb);
            _fieldControls["initialshading.factor"] = factorTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "initialshading.type", "initialshading.factor");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddDamageShadingEditor()
        {
            const string key = "damageshading";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var element = unit.Element("damageshading");
            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };

            var row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 160, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            row1.Children.Add(new TextBlock { Text = "Damage Shading", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var typeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(typeLabel, 1);
            row1.Children.Add(typeLabel);

            var typeAcb = CreateValidatedOtherSuggestionBox((string?)element?.Attribute("type") ?? "", KnownInitialShadingTypes, "Type");
            Grid.SetColumn(typeAcb, 2);
            row1.Children.Add(typeAcb);
            _fieldControls["damageshading.type"] = typeAcb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "damageshading.type", "damageshading.threshold", "damageshading.rate", "damageshading.time");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 3);
                row1.Children.Add(deleteButton);
            }

            stack.Children.Add(row1);

            var row2 = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 100, Auto, 100, Auto, 100"), Margin = new Thickness(0, 2, 0, 2) };
            row2.Children.Add(new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center });

            var thresholdLabel = new TextBlock { Text = "Threshold", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(thresholdLabel, 1);
            row2.Children.Add(thresholdLabel);

            var thresholdTb = CreateOtherTextBox((string?)element?.Attribute("threshold") ?? "1", true);
            Grid.SetColumn(thresholdTb, 2);
            row2.Children.Add(thresholdTb);
            _fieldControls["damageshading.threshold"] = thresholdTb;

            var rateLabel = new TextBlock { Text = "Rate", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(rateLabel, 3);
            row2.Children.Add(rateLabel);

            var rateTb = CreateOtherTextBox((string?)element?.Attribute("rate") ?? "1", true);
            Grid.SetColumn(rateTb, 4);
            row2.Children.Add(rateTb);
            _fieldControls["damageshading.rate"] = rateTb;

            var timeLabel = new TextBlock { Text = "Time (ms)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(timeLabel, 5);
            row2.Children.Add(timeLabel);

            var timeTb = CreateOtherTextBox((string?)element?.Attribute("time") ?? "1000", true);
            Grid.SetColumn(timeTb, 6);
            row2.Children.Add(timeTb);
            _fieldControls["damageshading.time"] = timeTb;

            stack.Children.Add(row2);

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddKillRewardEditor()
        {
            const string key = "killrewarddata";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var existingValues = unit.Elements("killreward")
                .Where(x => !string.IsNullOrWhiteSpace((string?)x.Attribute("resourcetype")))
                .ToDictionary(
                    x => (string?)x.Attribute("resourcetype") ?? "",
                    x => x.Value?.Trim() ?? "",
                    StringComparer.OrdinalIgnoreCase);

            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("180, Auto, 100, Auto, 100, Auto, 100, Auto, 100, Auto"),
                Margin = new Thickness(0, 2, 0, 2)
            };
            rowGrid.Children.Add(new TextBlock { Text = "Kill Reward", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            foreach (var rtype in ProtoConstants.KnownResourceTypes)
            {
                int index = Array.IndexOf(ProtoConstants.KnownResourceTypes, rtype);
                int labelColumn = 1 + (index * 2);
                int valueColumn = labelColumn + 1;

                var label = new TextBlock
                {
                    Text = rtype,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(index == 0 ? 0 : 12, 4, 10, 4)
                };
                Grid.SetColumn(label, labelColumn);
                rowGrid.Children.Add(label);

                var tb = CreateOtherTextBox(existingValues.TryGetValue(rtype, out var value) ? value : "", true);
                Grid.SetColumn(tb, valueColumn);
                rowGrid.Children.Add(tb);
                _fieldControls[$"killreward:{rtype}"] = tb;
            }

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, ProtoConstants.KnownResourceTypes.Select(x => $"killreward:{x}").ToArray());
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 9);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddResourceReturnEditor()
        {
            const string key = "resourcereturn";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var existingValues = unit.Elements("ResourceReturn")
                .Where(x => !string.IsNullOrWhiteSpace((string?)x.Attribute("resourceType")))
                .ToDictionary(
                    x => (string?)x.Attribute("resourceType") ?? "",
                    x => x.Value?.Trim() ?? "",
                    StringComparer.OrdinalIgnoreCase);

            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };

            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("180, Auto, 100, Auto, 100, Auto, 100, Auto, 100, Auto"),
                Margin = new Thickness(0, 2, 0, 2)
            };
            rowGrid.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            });

            foreach (var rtype in ProtoConstants.KnownResourceTypes)
            {
                int index = Array.IndexOf(ProtoConstants.KnownResourceTypes, rtype);
                int labelColumn = 1 + (index * 2);
                int valueColumn = labelColumn + 1;

                var label = new TextBlock
                {
                    Text = rtype,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(index == 0 ? 0 : 12, 4, 10, 4)
                };
                Grid.SetColumn(label, labelColumn);
                rowGrid.Children.Add(label);

                var tb = CreateOtherTextBox(existingValues.TryGetValue(rtype, out var value) ? value : "", true);
                Grid.SetColumn(tb, valueColumn);
                rowGrid.Children.Add(tb);
                _fieldControls[$"resourcereturn:{rtype}"] = tb;
            }

            stack.Children.Add(rowGrid);

            var flagRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 18,
                Margin = new Thickness(180, 0, 0, 0)
            };

            var currentFlags = ProtoXmlHandler.GetFlagList(unit);

            var returnOnConstructionCb = new CheckBox
            {
                Content = "Return on Construction",
                IsChecked = currentFlags.Contains("ReturnResourcesOnConstruction", StringComparer.OrdinalIgnoreCase),
                IsEnabled = !_isReadOnly
            };
            returnOnConstructionCb.IsCheckedChanged += async (s, e) => await HandleOtherFieldChangedAsync();
            _fieldControls["resourcereturn:returnonconstruction"] = returnOnConstructionCb;
            flagRow.Children.Add(returnOnConstructionCb);

            var noReturnOnDeleteCb = new CheckBox
            {
                Content = "No Return on Delete",
                IsChecked = currentFlags.Contains("DoApplyResourceReturnIfDeleted", StringComparer.OrdinalIgnoreCase),
                IsEnabled = !_isReadOnly
            };
            noReturnOnDeleteCb.IsCheckedChanged += async (s, e) => await HandleOtherFieldChangedAsync();
            _fieldControls["resourcereturn:noreturnondelete"] = noReturnOnDeleteCb;
            flagRow.Children.Add(noReturnOnDeleteCb);

            stack.Children.Add(flagRow);

            if (!_isReadOnly)
            {
                var deleteButton = new Button
                {
                    Content = "X",
                    Background = Brush.Parse("#8b0000"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(180, 0, 0, 0)
                };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(
                            key,
                            ProtoConstants.KnownResourceTypes.Select(x => $"resourcereturn:{x}")
                                .Concat(["resourcereturn:returnonconstruction", "resourcereturn:noreturnondelete"])
                                .ToArray());
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                stack.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddResourceReturnRateEditor()
        {
            const string key = "resourcereturnrate";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var existingValues = unit.Elements("ResourceReturnRate")
                .Where(x => !string.IsNullOrWhiteSpace((string?)x.Attribute("resourceType")))
                .ToDictionary(
                    x => (string?)x.Attribute("resourceType") ?? "",
                    x => x.Value?.Trim() ?? "",
                    StringComparer.OrdinalIgnoreCase);

            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };

            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("180, Auto, 100, Auto, 100, Auto, 100, Auto, 100, Auto"),
                Margin = new Thickness(0, 2, 0, 2)
            };
            rowGrid.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            });

            foreach (var rtype in ProtoConstants.KnownResourceTypes)
            {
                int index = Array.IndexOf(ProtoConstants.KnownResourceTypes, rtype);
                int labelColumn = 1 + (index * 2);
                int valueColumn = labelColumn + 1;

                var label = new TextBlock
                {
                    Text = rtype,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(index == 0 ? 0 : 12, 4, 10, 4)
                };
                Grid.SetColumn(label, labelColumn);
                rowGrid.Children.Add(label);

                var tb = CreateOtherTextBox(existingValues.TryGetValue(rtype, out var value) ? value : "", true);
                Grid.SetColumn(tb, valueColumn);
                rowGrid.Children.Add(tb);
                _fieldControls[$"resourcereturnrate:{rtype}"] = tb;
            }

            stack.Children.Add(rowGrid);

            var flagRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 18,
                Margin = new Thickness(180, 0, 0, 0)
            };

            var currentFlags = ProtoXmlHandler.GetFlagList(unit);

            var returnOnConstructionCb = new CheckBox
            {
                Content = "Return on Construction",
                IsChecked = currentFlags.Contains("ReturnResourcesOnConstruction", StringComparer.OrdinalIgnoreCase),
                IsEnabled = !_isReadOnly
            };
            returnOnConstructionCb.IsCheckedChanged += async (s, e) => await HandleOtherFieldChangedAsync();
            _fieldControls["resourcereturnrate:returnonconstruction"] = returnOnConstructionCb;
            flagRow.Children.Add(returnOnConstructionCb);

            var noReturnOnDeleteCb = new CheckBox
            {
                Content = "No Return on Delete",
                IsChecked = currentFlags.Contains("DoApplyResourceReturnIfDeleted", StringComparer.OrdinalIgnoreCase),
                IsEnabled = !_isReadOnly
            };
            noReturnOnDeleteCb.IsCheckedChanged += async (s, e) => await HandleOtherFieldChangedAsync();
            _fieldControls["resourcereturnrate:noreturnondelete"] = noReturnOnDeleteCb;
            flagRow.Children.Add(noReturnOnDeleteCb);

            var totalCostBasedCb = new CheckBox
            {
                Content = "Total Cost Based",
                IsChecked = currentFlags.Contains("ResourceReturnRateTotalCost", StringComparer.OrdinalIgnoreCase),
                IsEnabled = !_isReadOnly
            };
            totalCostBasedCb.IsCheckedChanged += async (s, e) => await HandleOtherFieldChangedAsync();
            _fieldControls["resourcereturnrate:totalcostbased"] = totalCostBasedCb;
            flagRow.Children.Add(totalCostBasedCb);

            stack.Children.Add(flagRow);

            if (!_isReadOnly)
            {
                var deleteButton = new Button
                {
                    Content = "X",
                    Background = Brush.Parse("#8b0000"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(180, 0, 0, 0)
                };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(
                            key,
                            ProtoConstants.KnownResourceTypes.Select(x => $"resourcereturnrate:{x}")
                                .Concat(["resourcereturnrate:returnonconstruction", "resourcereturnrate:noreturnondelete", "resourcereturnrate:totalcostbased"])
                                .ToArray());
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                stack.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddDependentUnitEditor()
        {
            const string key = "dependentunitdata";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            _dependentUnitRows.Clear();
            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 4, 0, 2)
            });

            var rowsHost = new StackPanel { Spacing = 4 };
            stack.Children.Add(rowsHost);

            void AddDependentUnitRow(XElement? existing = null)
            {
                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*, Auto, 90, Auto, 90, Auto, 150, Auto"),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var valueAcb = CreateValidatedOtherSuggestionBox(existing?.Value?.Trim() ?? "", otherProtoUnitSuggestions, "Proto Unit", suggestionsAlreadyNormalized: true);
                valueAcb.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(valueAcb, 0);
                rowGrid.Children.Add(valueAcb);

                var xLabel = new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetColumn(xLabel, 1);
                rowGrid.Children.Add(xLabel);

                var xTb = CreateOtherTextBox((string?)existing?.Attribute("x") ?? "0", true);
                Grid.SetColumn(xTb, 2);
                rowGrid.Children.Add(xTb);

                var zLabel = new TextBlock { Text = "Z", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(zLabel, 3);
                rowGrid.Children.Add(zLabel);

                var zTb = CreateOtherTextBox((string?)existing?.Attribute("z") ?? "0", true);
                Grid.SetColumn(zTb, 4);
                rowGrid.Children.Add(zTb);

                var attachBoneLabel = new TextBlock { Text = "Attach Bone", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(attachBoneLabel, 5);
                rowGrid.Children.Add(attachBoneLabel);

                var attachBoneTb = CreateOtherTextBox((string?)existing?.Attribute("attachbone") ?? "");
                Grid.SetColumn(attachBoneTb, 6);
                rowGrid.Children.Add(attachBoneTb);

                var rowState = new DependentUnitRowState
                {
                    RowPanel = rowGrid,
                    ValueAcb = valueAcb,
                    XTb = xTb,
                    ZTb = zTb,
                    AttachBoneTb = attachBoneTb
                };
                _dependentUnitRows.Add(rowState);

                if (!_isReadOnly)
                {
                    var deleteButton = new Button
                    {
                        Content = "X",
                        Background = Brush.Parse("#8b0000"),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    deleteButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _dependentUnitRows.Remove(rowState);
                            rowsHost.Children.Remove(rowGrid);
                            MarkDirty();
                        }
                    };
                    Grid.SetColumn(deleteButton, 7);
                    rowGrid.Children.Add(deleteButton);
                }

                if (!_isReadOnly && rowsHost.Children.Count > 0 && rowsHost.Children[^1] is Button)
                    rowsHost.Children.Insert(rowsHost.Children.Count - 1, rowGrid);
                else
                    rowsHost.Children.Add(rowGrid);
            }

            foreach (var existing in unit.Elements("dependentunit"))
                AddDependentUnitRow(existing);

            if (!_isReadOnly)
            {
                var addButton = new Button
                {
                    Content = "+ Add Dependent Unit",
                    Background = Brush.Parse("#2b7a0b"),
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                addButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        AddDependentUnitRow();
                        MarkDirty();
                    }
                };
                rowsHost.Children.Add(addButton);

                var deleteButton = new Button
                {
                    Content = "X",
                    Background = Brush.Parse("#8b0000"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _dependentUnitRows.Clear();
                        rowsHost.Children.Clear();
                        RemoveOtherSpecificContainer(key);
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                stack.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddSpawnEditor()
        {
            const string key = "spawndata";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            _spawnRows.Clear();
            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 4, 0, 2)
            });

            var rowsHost = new StackPanel { Spacing = 6 };
            stack.Children.Add(rowsHost);

            CheckBox CreateFlagCheckBox(string text, bool isChecked)
            {
                var cb = new CheckBox { Content = text, IsChecked = isChecked, IsEnabled = !_isReadOnly, VerticalAlignment = VerticalAlignment.Center };
                cb.Click += async (s, e) => await HandleOtherFieldChangedAsync();
                return cb;
            }

            void AddSpawnRow(XElement? existing = null)
            {
                var rowStack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };

                var row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto, 160, Auto, 100, Auto"), Margin = new Thickness(0, 2, 0, 2) };
                var valueAcb = CreateValidatedOtherSuggestionBox(existing?.Value?.Trim() ?? "", otherProtoUnitSuggestions, "Proto Unit", suggestionsAlreadyNormalized: true);
                Grid.SetColumn(valueAcb, 0);
                row1.Children.Add(valueAcb);

                var typeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(typeLabel, 1);
                row1.Children.Add(typeLabel);

                var typeAcb = CreateValidatedOtherSuggestionBox((string?)existing?.Attribute("type") ?? "", KnownSpawnTypes, "Type");
                Grid.SetColumn(typeAcb, 2);
                row1.Children.Add(typeAcb);

                var countLabel = new TextBlock { Text = "Count", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(countLabel, 3);
                row1.Children.Add(countLabel);

                var countTb = CreateOtherTextBox((string?)existing?.Attribute("count") ?? "1", true);
                Grid.SetColumn(countTb, 4);
                row1.Children.Add(countTb);

                if (!_isReadOnly)
                {
                    var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                    deleteButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _spawnRows.RemoveAll(x => ReferenceEquals(x.RowPanel, rowStack));
                            rowsHost.Children.Remove(rowStack);
                            MarkDirty();
                        }
                    };
                    Grid.SetColumn(deleteButton, 5);
                    row1.Children.Add(deleteButton);
                }

                rowStack.Children.Add(row1);

                var row2 = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, 100, Auto, 100, Auto, 100, Auto, 180"), Margin = new Thickness(180, 0, 0, 0) };

                var lifespanLabel = new TextBlock { Text = "Lifespan", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetColumn(lifespanLabel, 0);
                row2.Children.Add(lifespanLabel);
                var lifespanTb = CreateOtherTextBox((string?)existing?.Attribute("lifespan") ?? "", true);
                Grid.SetColumn(lifespanTb, 1);
                row2.Children.Add(lifespanTb);

                var chanceLabel = new TextBlock { Text = "Chance", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(chanceLabel, 2);
                row2.Children.Add(chanceLabel);
                var chanceTb = CreateOtherTextBox((string?)existing?.Attribute("chance") ?? "", true);
                Grid.SetColumn(chanceTb, 3);
                row2.Children.Add(chanceTb);

                var delayLabel = new TextBlock { Text = "Delay (ms)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(delayLabel, 4);
                row2.Children.Add(delayLabel);
                var delayTb = CreateOtherTextBox((string?)existing?.Attribute("delay") ?? "", true);
                Grid.SetColumn(delayTb, 5);
                row2.Children.Add(delayTb);

                var shadingLabel = new TextBlock { Text = "Shading", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(shadingLabel, 6);
                row2.Children.Add(shadingLabel);
                var shadingTypeAcb = CreateValidatedOtherSuggestionBox((string?)existing?.Attribute("shadingtype") ?? "", KnownInitialShadingTypes, "Shading Type");
                Grid.SetColumn(shadingTypeAcb, 7);
                row2.Children.Add(shadingTypeAcb);

                rowStack.Children.Add(row2);

                var row3 = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, 180, Auto, 180, Auto, Auto, Auto"), Margin = new Thickness(180, 0, 0, 0) };

                var waterLabel = new TextBlock { Text = "Water Proto Unit", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetColumn(waterLabel, 0);
                row3.Children.Add(waterLabel);
                var waterProtoUnitAcb = CreateValidatedOtherSuggestionBox((string?)existing?.Attribute("waterProtoUnit") ?? "", otherProtoUnitSuggestions, "Water Proto Unit", suggestionsAlreadyNormalized: true);
                Grid.SetColumn(waterProtoUnitAcb, 1);
                row3.Children.Add(waterProtoUnitAcb);

                var skipPlacementCheckCb = CreateFlagCheckBox("Skip Placement Check", existing?.Attribute("skipPlacementCheck") != null || existing?.Attribute("skipplacementcheck") != null);
                Grid.SetColumn(skipPlacementCheckCb, 2);
                row3.Children.Add(skipPlacementCheckCb);

                var controlGroupCb = CreateFlagCheckBox("Control Group", existing?.Attribute("controlGroup") != null || existing?.Attribute("controlgroup") != null);
                controlGroupCb.Margin = new Thickness(16, 0, 0, 0);
                Grid.SetColumn(controlGroupCb, 3);
                row3.Children.Add(controlGroupCb);

                var setOwnerCb = CreateFlagCheckBox("Set Owner", existing?.Attribute("setowner") != null);
                Grid.SetColumn(setOwnerCb, 4);
                row3.Children.Add(setOwnerCb);

                rowStack.Children.Add(row3);

                var rowState = new SpawnRowState
                {
                    RowPanel = rowStack,
                    ValueAcb = valueAcb,
                    TypeAcb = typeAcb,
                    CountTb = countTb,
                    LifespanTb = lifespanTb,
                    ChanceTb = chanceTb,
                    DelayTb = delayTb,
                    SkipPlacementCheckCb = skipPlacementCheckCb,
                    ControlGroupCb = controlGroupCb,
                    SetOwnerCb = setOwnerCb,
                    WaterProtoUnitAcb = waterProtoUnitAcb,
                    ShadingTypeAcb = shadingTypeAcb
                };
                _spawnRows.Add(rowState);

                if (!_isReadOnly && rowsHost.Children.Count > 0 && rowsHost.Children[^1] is Button)
                    rowsHost.Children.Insert(rowsHost.Children.Count - 1, rowStack);
                else
                    rowsHost.Children.Add(rowStack);
            }

            foreach (var existing in unit.Elements("spawn"))
                AddSpawnRow(existing);

            if (!_isReadOnly)
            {
                var addButton = new Button
                {
                    Content = "+ Add Spawn",
                    Background = Brush.Parse("#2b7a0b"),
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                addButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        AddSpawnRow();
                        MarkDirty();
                    }
                };
                rowsHost.Children.Add(addButton);

                var deleteButton = new Button
                {
                    Content = "X",
                    Background = Brush.Parse("#8b0000"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _spawnRows.Clear();
                        rowsHost.Children.Clear();
                        RemoveOtherSpecificContainer(key);
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                stack.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddRespawnTrainDataEditor()
        {
            const string key = "respawntraindata";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var respawnTrainData = unit.Element("respawntraindata");
            var targetSuggestions = GetAvailableBuildLimitTargets();
            var protoSuggestions = GetAvailableTrainUnitNames();
            var existingRespawnTypes = new HashSet<string>(
                respawnTrainData?.Element("respawntypes")?.Elements("unittype")
                    .Select(x => x.Value?.Trim() ?? "")
                    .Where(x => x.Length > 0) ?? [],
                StringComparer.OrdinalIgnoreCase);
            var existingExcludeTypes = new HashSet<string>(
                respawnTrainData?.Element("excludetypes")?.Elements("unittype")
                    .Select(x => x.Value?.Trim() ?? "")
                    .Where(x => x.Length > 0) ?? [],
                StringComparer.OrdinalIgnoreCase);

            _currentRespawnTrainTypes = existingRespawnTypes;
            _currentRespawnTrainExcludeTypes = existingExcludeTypes;

            var hasSelfModeData =
                !string.IsNullOrWhiteSpace(respawnTrainData?.Element("targettype")?.Value) ||
                !string.IsNullOrWhiteSpace(respawnTrainData?.Element("trainproto")?.Value) ||
                !string.IsNullOrWhiteSpace(respawnTrainData?.Element("respawntime")?.Value);
            var initialMode = hasSelfModeData ? "Self Respawn" : "Respawn Point";

            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 2) };
            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 180, Auto") };
            headerGrid.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            });

            var modeLabel = new TextBlock
            {
                Text = "Mode",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            };
            Grid.SetColumn(modeLabel, 1);
            headerGrid.Children.Add(modeLabel);

            var modeCb = new ComboBox
            {
                ItemsSource = new[] { "Self Respawn", "Respawn Point" },
                SelectedItem = initialMode,
                IsEnabled = !_isReadOnly,
                MinWidth = 180
            };
            Grid.SetColumn(modeCb, 2);
            headerGrid.Children.Add(modeCb);
            _fieldControls["respawntraindata.mode"] = modeCb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), Margin = new Thickness(8, 0, 0, 0) };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _currentRespawnTrainTypes = null;
                        _currentRespawnTrainExcludeTypes = null;
                        RemoveOtherSpecificContainer(key,
                            "respawntraindata.mode",
                            "respawntraindata.targettype",
                            "respawntraindata.trainproto",
                            "respawntraindata.respawntime",
                            "respawntraindata.respawnvfx",
                            "respawntraindata.respawnlimit",
                            "respawntraindata.food",
                            "respawntraindata.wood",
                            "respawntraindata.gold",
                            "respawntraindata.favor");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 3);
                headerGrid.Children.Add(deleteButton);
            }

            stack.Children.Add(headerGrid);

            var bodyStack = new StackPanel { Spacing = 6 };
            stack.Children.Add(bodyStack);

            void AddRespawnChipEditor(string title, HashSet<string> values, Action<HashSet<string>> assignTarget)
            {
                assignTarget(values);
                var editorStack = new StackPanel { Spacing = 4 };
                editorStack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 2) });
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                editorStack.Children.Add(wrap);

                void RefreshDisplay()
                {
                    wrap.Children.Clear();
                    foreach (var value in values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        wrap.Children.Add(CreateChip(value, async () =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                values.Remove(value);
                                MarkDirty();
                                RefreshDisplay();
                            }
                        }));
                    }
                }

                RefreshDisplay();

                if (!_isReadOnly)
                {
                    var acb = CreateOtherSuggestionBox("", targetSuggestions, title);
                    string? selectedValue = null;

                    async Task PerformAdd()
                    {
                        var input = acb.Text?.Trim() ?? "";
                        var match = targetSuggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(match) && values.Add(match))
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                acb.Text = "";
                                MarkDirty();
                                RefreshDisplay();
                            }
                            else
                            {
                                values.Remove(match);
                            }
                        }
                    }

                    acb.SelectionChanged += (s, e) =>
                    {
                        if (acb.SelectedItem is string selected)
                        {
                            selectedValue = selected;
                            acb.Text = selected;
                            Dispatcher.UIThread.Post(() =>
                            {
                                var input = acb.Text?.Trim() ?? "";
                                var match = targetSuggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                                if (string.IsNullOrWhiteSpace(match) && !string.IsNullOrWhiteSpace(selectedValue))
                                    match = targetSuggestions.FirstOrDefault(x => x.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));

                                if (!string.IsNullOrWhiteSpace(match))
                                    _ = PerformAdd();
                            }, DispatcherPriority.Background);
                        }
                    };

                    editorStack.Children.Add(acb);
                }

                bodyStack.Children.Add(editorStack);
            }

            void RenderModeBody()
            {
                bodyStack.Children.Clear();
                foreach (var fieldKey in new[]
                         {
                             "respawntraindata.targettype",
                             "respawntraindata.trainproto",
                             "respawntraindata.respawntime",
                             "respawntraindata.respawnvfx",
                             "respawntraindata.respawnlimit",
                             "respawntraindata.food",
                             "respawntraindata.wood",
                             "respawntraindata.gold",
                             "respawntraindata.favor"
                         })
                {
                    _fieldControls.Remove(fieldKey);
                }

                var selectedMode = modeCb.SelectedItem as string ?? "Self Respawn";
                if (selectedMode.Equals("Self Respawn", StringComparison.OrdinalIgnoreCase))
                {
                    var selfGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, *, Auto, 120"), RowDefinitions = new RowDefinitions("Auto, Auto"), Margin = new Thickness(0, 2, 0, 2) };

                    var targetTypeLabel = new TextBlock { Text = "Target Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
                    selfGrid.Children.Add(targetTypeLabel);
                    var targetTypeAcb = CreateValidatedOtherSuggestionBox(respawnTrainData?.Element("targettype")?.Value?.Trim() ?? "", targetSuggestions, "Target Type", suggestionsAlreadyNormalized: true);
                    Grid.SetColumn(targetTypeAcb, 1);
                    selfGrid.Children.Add(targetTypeAcb);
                    _fieldControls["respawntraindata.targettype"] = targetTypeAcb;

                    var trainProtoLabel = new TextBlock { Text = "Train Proto", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
                    Grid.SetColumn(trainProtoLabel, 2);
                    selfGrid.Children.Add(trainProtoLabel);
                    var trainProtoAcb = CreateValidatedOtherSuggestionBox(respawnTrainData?.Element("trainproto")?.Value?.Trim() ?? "", protoSuggestions, "Train Proto", suggestionsAlreadyNormalized: true);
                    Grid.SetColumn(trainProtoAcb, 3);
                    selfGrid.Children.Add(trainProtoAcb);
                    _fieldControls["respawntraindata.trainproto"] = trainProtoAcb;

                    var respawnTimeLabel = new TextBlock { Text = "Respawn Time", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
                    Grid.SetColumn(respawnTimeLabel, 4);
                    selfGrid.Children.Add(respawnTimeLabel);
                    var respawnTimeTb = CreateOtherTextBox(respawnTrainData?.Element("respawntime")?.Value?.Trim() ?? "", true);
                    Grid.SetColumn(respawnTimeTb, 5);
                    selfGrid.Children.Add(respawnTimeTb);
                    _fieldControls["respawntraindata.respawntime"] = respawnTimeTb;

                    var vfxLabel = new TextBlock { Text = "Respawn VFX", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
                    Grid.SetRow(vfxLabel, 1);
                    selfGrid.Children.Add(vfxLabel);
                    var vfxAcb = CreateValidatedOtherSuggestionBox(respawnTrainData?.Element("respawnvfx")?.Value?.Trim() ?? "", protoSuggestions, "Respawn VFX", suggestionsAlreadyNormalized: true);
                    Grid.SetColumn(vfxAcb, 1);
                    Grid.SetColumnSpan(vfxAcb, 5);
                    Grid.SetRow(vfxAcb, 1);
                    selfGrid.Children.Add(vfxAcb);
                    _fieldControls["respawntraindata.respawnvfx"] = vfxAcb;

                    bodyStack.Children.Add(selfGrid);
                    return;
                }

                AddRespawnChipEditor("Respawn Types", _currentRespawnTrainTypes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), values => _currentRespawnTrainTypes = values);
                AddRespawnChipEditor("Exclude Types", _currentRespawnTrainExcludeTypes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), values => _currentRespawnTrainExcludeTypes = values);

                var ratesGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, 100, Auto, 100, Auto, 100, Auto, 100"), Margin = new Thickness(0, 2, 0, 2) };
                var respawnRates = respawnTrainData?.Element("respawnrates");
                foreach (var resourceType in ProtoConstants.KnownResourceTypes)
                {
                    int index = Array.IndexOf(ProtoConstants.KnownResourceTypes, resourceType);
                    int labelColumn = index * 2;
                    int valueColumn = labelColumn + 1;

                    var label = new TextBlock
                    {
                        Text = resourceType,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 4, 10, 4)
                    };
                    Grid.SetColumn(label, labelColumn);
                    ratesGrid.Children.Add(label);

                    var tb = CreateOtherTextBox(respawnRates?.Element(resourceType.ToLowerInvariant())?.Value?.Trim() ?? "0", true);
                    Grid.SetColumn(tb, valueColumn);
                    ratesGrid.Children.Add(tb);
                    _fieldControls[$"respawntraindata.{resourceType.ToLowerInvariant()}"] = tb;
                }

                var ratesHost = new StackPanel { Spacing = 4 };
                ratesHost.Children.Add(new TextBlock { Text = "Respawn Rates", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 2) });
                ratesHost.Children.Add(ratesGrid);
                bodyStack.Children.Add(ratesHost);

                var miscGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, 120, Auto, *"), Margin = new Thickness(0, 2, 0, 2) };
                miscGrid.Children.Add(new TextBlock { Text = "Respawn Limit", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
                var limitTb = CreateOtherTextBox(respawnTrainData?.Element("respawnlimit")?.Value?.Trim() ?? "0", true);
                Grid.SetColumn(limitTb, 1);
                miscGrid.Children.Add(limitTb);
                _fieldControls["respawntraindata.respawnlimit"] = limitTb;

                var vfxLabel2 = new TextBlock { Text = "Respawn VFX", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
                Grid.SetColumn(vfxLabel2, 2);
                miscGrid.Children.Add(vfxLabel2);
                var vfxAcb2 = CreateValidatedOtherSuggestionBox(respawnTrainData?.Element("respawnvfx")?.Value?.Trim() ?? "", protoSuggestions, "Respawn VFX", suggestionsAlreadyNormalized: true);
                Grid.SetColumn(vfxAcb2, 3);
                miscGrid.Children.Add(vfxAcb2);
                _fieldControls["respawntraindata.respawnvfx"] = vfxAcb2;
                bodyStack.Children.Add(miscGrid);
            }

            modeCb.SelectionChanged += async (s, e) =>
            {
                if (_isPopulating)
                    return;

                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    MarkDirty();
                    RenderModeBody();
                }
            };

            RenderModeBody();
            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddVeterancyEditor()
        {
            const string key = "veterancydata";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            _veterancyRankRows.Clear();
            _veterancyBonusRows.Clear();

            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 4, 0, 2)
            });

            var ranksHost = new StackPanel { Spacing = 4 };
            var bonusHost = new StackPanel { Spacing = 4 };
            var typeSuggestions = GetAvailableBuildLimitTargets();
            var veterancyBonus = unit.Element("veterancybonus");
            _currentVeterancyIncludeTypes = new HashSet<string>(veterancyBonus?.Element("includetypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase);
            _currentVeterancyExcludeTypes = new HashSet<string>(veterancyBonus?.Element("excludetypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase);

            stack.Children.Add(new TextBlock { Text = "Ranks", FontWeight = FontWeight.Bold, Margin = new Thickness(180, 0, 0, 0) });
            stack.Children.Add(ranksHost);
            stack.Children.Add(new TextBlock { Text = "Bonuses", FontWeight = FontWeight.Bold, Margin = new Thickness(180, 4, 0, 0) });
            stack.Children.Add(bonusHost);

            void AddVeterancyRankRow(string? type = null, string? value = null)
            {
                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("180, 180, 120, Auto"),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                rowGrid.Children.Add(new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center });

                var typeCb = new ComboBox
                {
                    ItemsSource = KnownVeterancyRankTypes,
                    SelectedItem = KnownVeterancyRankTypes.FirstOrDefault(x => x.Equals(type ?? "", StringComparison.OrdinalIgnoreCase)) ?? KnownVeterancyRankTypes[0],
                    IsEnabled = !_isReadOnly
                };
                typeCb.SelectionChanged += async (s, e) => await HandleOtherFieldChangedAsync();
                Grid.SetColumn(typeCb, 1);
                rowGrid.Children.Add(typeCb);

                var valueTb = CreateOtherTextBox(value ?? "0", true);
                Grid.SetColumn(valueTb, 2);
                rowGrid.Children.Add(valueTb);

                var rowState = new VeterancyRankRowState
                {
                    RowPanel = rowGrid,
                    TypeCb = typeCb,
                    ValueTb = valueTb
                };
                _veterancyRankRows.Add(rowState);

                if (!_isReadOnly)
                {
                    var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                    deleteButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _veterancyRankRows.Remove(rowState);
                            ranksHost.Children.Remove(rowGrid);
                            MarkDirty();
                        }
                    };
                    Grid.SetColumn(deleteButton, 3);
                    rowGrid.Children.Add(deleteButton);
                }

                if (!_isReadOnly && ranksHost.Children.Count > 0 && ranksHost.Children[^1] is Button)
                    ranksHost.Children.Insert(ranksHost.Children.Count - 1, rowGrid);
                else
                    ranksHost.Children.Add(rowGrid);
            }

            void AddVeterancyBonusRow(string? rankId = null, string? modifyType = null, string? value = null, string? damageType = null)
            {
                var modifyTypeSuggestions = ProtoConstants.KnownModifyTypes
                    .Select(ProtoConstants.GetModifyTypeDisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("180, Auto, 70, Auto, 180, Auto, 120, Auto, 160, Auto"),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                rowGrid.Children.Add(new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center });

                var rankLabel = new TextBlock { Text = "Rank Id", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetColumn(rankLabel, 1);
                rowGrid.Children.Add(rankLabel);

                var rankIdTb = CreateOtherTextBox(rankId ?? "0", true);
                Grid.SetColumn(rankIdTb, 2);
                rowGrid.Children.Add(rankIdTb);

                var modifyTypeLabel = new TextBlock { Text = "Modify Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(modifyTypeLabel, 3);
                rowGrid.Children.Add(modifyTypeLabel);

                var modifyTypeAcb = CreateValidatedOtherSuggestionBox(
                    ProtoConstants.GetModifyTypeDisplayName(ProtoConstants.GetModifyTypeValue(modifyType ?? "")),
                    modifyTypeSuggestions,
                    "Modify Type");
                Grid.SetColumn(modifyTypeAcb, 4);
                rowGrid.Children.Add(modifyTypeAcb);

                var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(valueLabel, 5);
                rowGrid.Children.Add(valueLabel);

                var valueTb = CreateOtherTextBox(value ?? "0", true);
                Grid.SetColumn(valueTb, 6);
                rowGrid.Children.Add(valueTb);

                var damageTypeLabel = new TextBlock { Text = "Damage Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(damageTypeLabel, 7);
                rowGrid.Children.Add(damageTypeLabel);

                var damageTypeAcb = CreateValidatedOtherSuggestionBox(damageType ?? "", ProtoConstants.KnownDamageTypes, "Damage Type");
                damageTypeAcb.Width = 160;
                Grid.SetColumn(damageTypeAcb, 8);
                rowGrid.Children.Add(damageTypeAcb);

                void RefreshDamageTypeVisibility()
                {
                    var visible = ProtoConstants.GetModifyTypeValue(modifyTypeAcb.Text?.Trim() ?? "") is "DamageSpecific" or "ArmorSpecific";
                    damageTypeLabel.IsVisible = visible;
                    damageTypeAcb.IsVisible = visible;
                    if (!visible)
                        damageTypeAcb.Text = "";
                }

                modifyTypeAcb.TextChanged += (s, e) => RefreshDamageTypeVisibility();
                RefreshDamageTypeVisibility();

                var rowState = new VeterancyBonusRowState
                {
                    RowPanel = rowGrid,
                    RankIdTb = rankIdTb,
                    ModifyTypeAcb = modifyTypeAcb,
                    ValueTb = valueTb,
                    DamageTypeAcb = damageTypeAcb,
                    DamageTypeLabel = damageTypeLabel
                };
                _veterancyBonusRows.Add(rowState);

                if (!_isReadOnly)
                {
                    var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                    deleteButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _veterancyBonusRows.Remove(rowState);
                            bonusHost.Children.Remove(rowGrid);
                            MarkDirty();
                        }
                    };
                    Grid.SetColumn(deleteButton, 9);
                    rowGrid.Children.Add(deleteButton);
                }

                if (!_isReadOnly && bonusHost.Children.Count > 0 && bonusHost.Children[^1] is Button)
                    bonusHost.Children.Insert(bonusHost.Children.Count - 1, rowGrid);
                else
                    bonusHost.Children.Add(rowGrid);
            }

            var veterancyRanks = unit.Element("veterancyranks");
            foreach (var rank in veterancyRanks?.Elements("rank") ?? [])
            {
                var child = rank.Elements().FirstOrDefault();
                if (child != null)
                    AddVeterancyRankRow(child.Name.LocalName switch
                    {
                        "numkills" => "NumKills",
                        "numattacks" => "NumAttacks",
                        "totaldamage" => "TotalDamage",
                        "damageandresourceseaten" => "DamageAndResourcesEaten",
                        _ => child.Name.LocalName
                    }, child.Value?.Trim() ?? "0");
            }

            foreach (var rank in veterancyBonus?.Elements("rank") ?? [])
            {
                var rankId = (string?)rank.Attribute("id") ?? "0";
                foreach (var modify in rank.Elements("veterancymodify"))
                {
                    AddVeterancyBonusRow(
                        rankId,
                        (string?)modify.Attribute("modifytype") ?? "",
                        modify.Value?.Trim() ?? "0",
                        (string?)modify.Attribute("damagetype") ?? (string?)modify.Attribute("damageType") ?? "");
                }
            }

            StackPanel CreateVeterancyTypeEditor(string title, HashSet<string> values, bool optional)
            {
                var typeStack = new StackPanel { Spacing = 4, Margin = new Thickness(180, 0, 0, 0) };
                typeStack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold });

                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                typeStack.Children.Add(wrap);

                void Refresh()
                {
                    wrap.Children.Clear();
                    foreach (var value in values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        wrap.Children.Add(CreateChip(value, async () =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                values.Remove(value);
                                MarkDirty();
                                Refresh();
                            }
                        }));
                    }
                }

                Refresh();

                if (!_isReadOnly)
                {
                    var addGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
                    var acb = CreateValidatedOtherSuggestionBox("", typeSuggestions, title, suggestionsAlreadyNormalized: true);
                    Grid.SetColumn(acb, 0);
                    addGrid.Children.Add(acb);
                    string? selectedTypeValue = null;

                    async Task PerformAdd()
                    {
                        var proceed = await CheckStartLocalMod();
                        if (!proceed)
                            return;

                        var value = acb.Text?.Trim() ?? "";
                        var match = typeSuggestions.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(match) && values.Add(match))
                        {
                            acb.Text = "";
                            selectedTypeValue = null;
                            MarkDirty();
                            Refresh();
                        }
                    }

                    acb.SelectionChanged += (s, e) =>
                    {
                        if (acb.SelectedItem is string selected)
                        {
                            selectedTypeValue = selected;
                            acb.Text = selected;
                            Dispatcher.UIThread.Post(async () =>
                            {
                                var input = acb.Text?.Trim() ?? "";
                                var match = typeSuggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                                if (string.IsNullOrWhiteSpace(match) && !string.IsNullOrWhiteSpace(selectedTypeValue))
                                    match = typeSuggestions.FirstOrDefault(x => x.Equals(selectedTypeValue, StringComparison.OrdinalIgnoreCase));

                                if (!string.IsNullOrWhiteSpace(match))
                                    await PerformAdd();
                            }, DispatcherPriority.Background);
                        }
                    };

                    var addButton = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(8, 0, 0, 0) };
                    addButton.Click += async (s, e) => await PerformAdd();
                    Grid.SetColumn(addButton, 1);
                    addGrid.Children.Add(addButton);
                    typeStack.Children.Add(addGrid);
                }

                if (!optional || values.Count > 0 || !_isReadOnly)
                    return typeStack;

                return typeStack;
            }

            stack.Children.Add(CreateVeterancyTypeEditor("Include Types", _currentVeterancyIncludeTypes, optional: false));
            stack.Children.Add(CreateVeterancyTypeEditor("Exclude Types", _currentVeterancyExcludeTypes, optional: true));

            if (!_isReadOnly)
            {
                var addRankButton = new Button
                {
                    Content = "+ Add Rank",
                    Background = Brush.Parse("#2b7a0b"),
                    Margin = new Thickness(180, 2, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                addRankButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        AddVeterancyRankRow();
                        MarkDirty();
                    }
                };
                ranksHost.Children.Add(addRankButton);

                var addBonusButton = new Button
                {
                    Content = "+ Add Bonus",
                    Background = Brush.Parse("#2b7a0b"),
                    Margin = new Thickness(180, 2, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                addBonusButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        AddVeterancyBonusRow(rankId: _veterancyBonusRows.Count.ToString());
                        MarkDirty();
                    }
                };
                bonusHost.Children.Add(addBonusButton);

                var deleteButton = new Button
                {
                    Content = "X",
                    Background = Brush.Parse("#8b0000"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _veterancyRankRows.Clear();
                        _veterancyBonusRows.Clear();
                        ranksHost.Children.Clear();
                        bonusHost.Children.Clear();
                        RemoveOtherSpecificContainer(key);
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                stack.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddOnDamageModifiersEditor()
        {
            const string key = "ondamagemodifiers";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            _onDamageModifyRows.Clear();

            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 4, 0, 2)
            });

            var rowsHost = new StackPanel { Spacing = 4 };
            stack.Children.Add(rowsHost);

            static bool NeedsDamageType(string? modifyType)
                => ProtoConstants.GetModifyTypeValue(modifyType ?? "") is "DamageSpecific" or "ArmorSpecific";

            void AddOnDamageModifyRow(string? modifyType = null, string? value = null, string? damageType = null)
            {
                var modifyTypeSuggestions = ProtoConstants.KnownModifyTypes
                    .Select(ProtoConstants.GetModifyTypeDisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("180, Auto, 180, Auto, 120, Auto, 160, Auto"),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                rowGrid.Children.Add(new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center });

                var modifyTypeLabel = new TextBlock { Text = "Modify Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetColumn(modifyTypeLabel, 1);
                rowGrid.Children.Add(modifyTypeLabel);

                var modifyTypeAcb = CreateValidatedOtherSuggestionBox(
                    ProtoConstants.GetModifyTypeDisplayName(ProtoConstants.GetModifyTypeValue(modifyType ?? "")),
                    modifyTypeSuggestions,
                    "Modify Type");
                Grid.SetColumn(modifyTypeAcb, 2);
                rowGrid.Children.Add(modifyTypeAcb);

                var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(valueLabel, 3);
                rowGrid.Children.Add(valueLabel);

                var valueTb = CreateOtherTextBox(value ?? "0", true);
                Grid.SetColumn(valueTb, 4);
                rowGrid.Children.Add(valueTb);

                var damageTypeLabel = new TextBlock { Text = "Damage Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 8, 4) };
                Grid.SetColumn(damageTypeLabel, 5);
                rowGrid.Children.Add(damageTypeLabel);

                var damageTypeAcb = CreateValidatedOtherSuggestionBox(damageType ?? "", ProtoConstants.KnownDamageTypes, "Damage Type");
                damageTypeAcb.Width = 160;
                Grid.SetColumn(damageTypeAcb, 6);
                rowGrid.Children.Add(damageTypeAcb);

                void RefreshDamageTypeVisibility()
                {
                    var visible = NeedsDamageType(modifyTypeAcb.Text?.Trim());
                    damageTypeLabel.IsVisible = visible;
                    damageTypeAcb.IsVisible = visible;
                    if (!visible)
                        damageTypeAcb.Text = "";
                }

                modifyTypeAcb.TextChanged += (s, e) => RefreshDamageTypeVisibility();
                RefreshDamageTypeVisibility();

                var rowState = new OnDamageModifyRowState
                {
                    RowPanel = rowGrid,
                    ModifyTypeAcb = modifyTypeAcb,
                    ValueTb = valueTb,
                    DamageTypeAcb = damageTypeAcb,
                    DamageTypeLabel = damageTypeLabel
                };
                _onDamageModifyRows.Add(rowState);

                if (!_isReadOnly)
                {
                    var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                    deleteButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _onDamageModifyRows.Remove(rowState);
                            rowsHost.Children.Remove(rowGrid);
                            MarkDirty();
                        }
                    };
                    Grid.SetColumn(deleteButton, 7);
                    rowGrid.Children.Add(deleteButton);
                }

                if (!_isReadOnly && rowsHost.Children.Count > 0 && rowsHost.Children[^1] is Button)
                    rowsHost.Children.Insert(rowsHost.Children.Count - 1, rowGrid);
                else
                    rowsHost.Children.Add(rowGrid);
            }

            var onDamageModifiers = unit.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("OnDamageModifiers", StringComparison.OrdinalIgnoreCase));
            foreach (var modify in onDamageModifiers?.Elements().Where(x => x.Name.LocalName.Equals("OnDamageModify", StringComparison.OrdinalIgnoreCase)) ?? [])
            {
                AddOnDamageModifyRow(
                    (string?)modify.Attribute("modifyType") ?? (string?)modify.Attribute("modifytype") ?? "",
                    modify.Value?.Trim() ?? "0",
                    (string?)modify.Attribute("damageType") ?? (string?)modify.Attribute("damagetype") ?? "");
            }

            if (!_isReadOnly)
            {
                var addButton = new Button
                {
                    Content = "+ Add Modifier",
                    Background = Brush.Parse("#2b7a0b"),
                    Margin = new Thickness(180, 2, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                addButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        AddOnDamageModifyRow();
                        MarkDirty();
                    }
                };
                rowsHost.Children.Add(addButton);

                var deleteButton = new Button
                {
                    Content = "X",
                    Background = Brush.Parse("#8b0000"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _onDamageModifyRows.Clear();
                        rowsHost.Children.Clear();
                        RemoveOtherSpecificContainer(key);
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                stack.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddResourceMapEditor(string key, string elementName)
        {
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
            var existingValues = unit.Elements(elementName)
                .Where(x => !string.IsNullOrWhiteSpace((string?)x.Attribute("resourcetype")))
                .ToDictionary(x => (string?)x.Attribute("resourcetype") ?? "", x => x.Value?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);
            var existingDropOffMultipliers = elementName.Equals("carrycapacity", StringComparison.OrdinalIgnoreCase)
                ? unit.Elements(elementName)
                    .Where(x => !string.IsNullOrWhiteSpace((string?)x.Attribute("resourcetype")))
                    .ToDictionary(
                        x => (string?)x.Attribute("resourcetype") ?? "",
                        x => ((string?)x.Attribute("dropoffmultiplier") ?? (string?)x.Attribute("dropOffMultiplier") ?? "").Trim(),
                        StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            stack.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 4, 0, 2)
            });

            var mainRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(0, 2, 0, 2)
            };
            mainRow.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            });

            StackPanel? carryCapacityDetails = null;
            if (elementName.Equals("carrycapacity", StringComparison.OrdinalIgnoreCase))
            {
                carryCapacityDetails = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                carryCapacityDetails.Children.Add(new TextBlock
                {
                    Text = "Drop Off Multiplier",
                    Width = 180,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 4, 10, 4)
                });
            }

            foreach (var resourceType in ProtoConstants.KnownResourceTypes)
            {
                var resourcePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };
                resourcePanel.Children.Add(new TextBlock
                {
                    Text = resourceType,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 4)
                });

                var tb = CreateOtherTextBox(existingValues.TryGetValue(resourceType, out var value) ? value : "0", true);
                tb.Width = 90;
                resourcePanel.Children.Add(tb);
                _fieldControls[$"{elementName}:{resourceType}"] = tb;
                mainRow.Children.Add(resourcePanel);

                if (elementName.Equals("carrycapacity", StringComparison.OrdinalIgnoreCase))
                {
                    var existingMultiplier = existingDropOffMultipliers.TryGetValue(resourceType, out var multiplier) ? multiplier : "";
                    var dropOffTb = CreateOtherTextBox(existingMultiplier, true);
                    _fieldControls[$"{elementName}:dropoffmultiplier:{resourceType}"] = dropOffTb;

                    var hasDropOffMultiplier = !string.IsNullOrWhiteSpace(existingMultiplier);
                    var detailPanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 8,
                        Margin = new Thickness(0, 2, 0, 2),
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    detailPanel.Children.Add(new TextBlock
                    {
                        Text = resourceType,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 4)
                    });

                    var toggleButton = new Button
                    {
                        Content = "+ Add Multiplier",
                        Background = Brush.Parse("#2b7a0b"),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = !_isReadOnly
                    };

                    var multiplierPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        IsVisible = hasDropOffMultiplier
                    };
                    dropOffTb.Width = 110;
                    multiplierPanel.Children.Add(dropOffTb);
                    if (!_isReadOnly)
                    {
                        var removeButton = new Button
                        {
                            Content = "X",
                            Background = Brush.Parse("#8b0000"),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        removeButton.Click += async (s, e) =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (!proceed)
                                return;

                            multiplierPanel.IsVisible = false;
                            toggleButton.IsVisible = true;
                            dropOffTb.Text = "";
                            MarkDirty();
                        };
                        multiplierPanel.Children.Add(removeButton);
                    }

                    toggleButton.IsVisible = !hasDropOffMultiplier;
                    detailPanel.Children.Add(toggleButton);
                    detailPanel.Children.Add(multiplierPanel);

                    toggleButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (!proceed)
                            return;

                        multiplierPanel.IsVisible = true;
                        toggleButton.IsVisible = false;
                        MarkDirty();
                    };

                    carryCapacityDetails!.Children.Add(detailPanel);
                }
            }
            stack.Children.Add(mainRow);
            if (carryCapacityDetails != null)
                stack.Children.Add(carryCapacityDetails);

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        var fieldKeys = ProtoConstants.KnownResourceTypes.Select(x => $"{elementName}:{x}");
                        if (elementName.Equals("carrycapacity", StringComparison.OrdinalIgnoreCase))
                            fieldKeys = fieldKeys.Concat(ProtoConstants.KnownResourceTypes.Select(x => $"{elementName}:dropoffmultiplier:{x}"));
                        RemoveOtherSpecificContainer(key, fieldKeys.ToArray());
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                stack.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddResourceConversionEditor()
        {
            const string key = "resourceconversion";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            _resourceConversionRows.Clear();
            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(new TextBlock
            {
                Text = GetOtherSpecificAttributeLabel(key),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 4, 0, 2)
            });

            var rowsHost = new StackPanel { Spacing = 4 };
            stack.Children.Add(rowsHost);

            void AddResourceConversionRow(XElement? existing = null)
            {
                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto, 120, Auto, 120, Auto, 120, Auto"),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                rowGrid.Children.Add(new TextBlock
                {
                    Text = "From",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 8, 4)
                });

                var fromCb = new ComboBox
                {
                    ItemsSource = ProtoConstants.KnownResourceTypes,
                    SelectedItem = ProtoConstants.KnownResourceTypes.FirstOrDefault(x =>
                        x.Equals(existing?.Attribute("fromresourcetype")?.Value?.Trim() ?? "", StringComparison.OrdinalIgnoreCase))
                        ?? ProtoConstants.KnownResourceTypes.FirstOrDefault(),
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                fromCb.SelectionChanged += async (s, e) => await HandleOtherFieldChangedAsync();
                Grid.SetColumn(fromCb, 1);
                rowGrid.Children.Add(fromCb);

                var toLabel = new TextBlock
                {
                    Text = "To",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 8, 4)
                };
                Grid.SetColumn(toLabel, 2);
                rowGrid.Children.Add(toLabel);

                var toCb = new ComboBox
                {
                    ItemsSource = ProtoConstants.KnownResourceTypes,
                    SelectedItem = ProtoConstants.KnownResourceTypes.FirstOrDefault(x =>
                        x.Equals(existing?.Attribute("toresourcetype")?.Value?.Trim() ?? "", StringComparison.OrdinalIgnoreCase))
                        ?? ProtoConstants.KnownResourceTypes.FirstOrDefault(),
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                toCb.SelectionChanged += async (s, e) => await HandleOtherFieldChangedAsync();
                Grid.SetColumn(toCb, 3);
                rowGrid.Children.Add(toCb);

                var valueLabel = new TextBlock
                {
                    Text = "Value",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 8, 4)
                };
                Grid.SetColumn(valueLabel, 4);
                rowGrid.Children.Add(valueLabel);

                var valueTb = CreateOtherTextBox(existing?.Value?.Trim() ?? "0", true);
                Grid.SetColumn(valueTb, 5);
                rowGrid.Children.Add(valueTb);

                var rowState = new ResourceConversionRowState
                {
                    RowPanel = rowGrid,
                    FromResourceCb = fromCb,
                    ToResourceCb = toCb,
                    ValueTb = valueTb
                };
                _resourceConversionRows.Add(rowState);

                if (!_isReadOnly)
                {
                    var deleteButton = new Button
                    {
                        Content = "X",
                        Background = Brush.Parse("#8b0000"),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    deleteButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _resourceConversionRows.Remove(rowState);
                            rowsHost.Children.Remove(rowGrid);
                            MarkDirty();
                        }
                    };
                    Grid.SetColumn(deleteButton, 6);
                    rowGrid.Children.Add(deleteButton);
                }

                if (!_isReadOnly && rowsHost.Children.Count > 0 && rowsHost.Children[^1] is Button)
                    rowsHost.Children.Insert(rowsHost.Children.Count - 1, rowGrid);
                else
                    rowsHost.Children.Add(rowGrid);
            }

            foreach (var existing in unit.Elements("resourceconversion"))
                AddResourceConversionRow(existing);

            if (!_isReadOnly)
            {
                var addButton = new Button
                {
                    Content = "+ Add Conversion",
                    Background = Brush.Parse("#2b7a0b"),
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                addButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        AddResourceConversionRow();
                        MarkDirty();
                    }
                };
                rowsHost.Children.Add(addButton);

                var deleteButton = new Button
                {
                    Content = "X",
                    Background = Brush.Parse("#8b0000"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _resourceConversionRows.Clear();
                        rowsHost.Children.Clear();
                        RemoveOtherSpecificContainer(key);
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                stack.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddDirectionalArmorEditor()
        {
            const string key = "directionalarmor";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var element = unit.Element("directionalarmor");
            var angleInitial = element?.Attribute("angle")?.Value?.Trim() ?? "0";
            var valueInitial = element?.Attribute("value")?.Value?.Trim() ?? element?.Value?.Trim() ?? "0";

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 120, Auto, 120, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Directional Armor", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var angleLabel = new TextBlock { Text = "Angle", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(angleLabel, 1);
            rowGrid.Children.Add(angleLabel);

            var angleTb = CreateOtherTextBox(angleInitial, true);
            Grid.SetColumn(angleTb, 2);
            rowGrid.Children.Add(angleTb);
            _fieldControls["directionalarmor.angle"] = angleTb;

            var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(valueLabel, 3);
            rowGrid.Children.Add(valueLabel);

            var valueTb = CreateOtherTextBox(valueInitial, true);
            Grid.SetColumn(valueTb, 4);
            rowGrid.Children.Add(valueTb);
            _fieldControls["directionalarmor.value"] = valueTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "directionalarmor.angle", "directionalarmor.value");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddSocketEditor()
        {
            const string key = "socketdata";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var socketSuggestions = GetAvailableBuildLimitTargets();
            var socketUnitType = ProtoXmlHandler.GetSimpleField(unit, "socketunittype") ?? "";
            var nonSocketPlaceProtoId = ProtoXmlHandler.GetSimpleField(unit, "nonsocketplaceprotoid") ?? "";
            bool showNonSocket = !string.IsNullOrWhiteSpace(nonSocketPlaceProtoId);

            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 2) };
            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, *, Auto") };
            headerGrid.Children.Add(new TextBlock { Text = "Socket", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var socketLabel = new TextBlock { Text = "Socket Unit Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(socketLabel, 1);
            headerGrid.Children.Add(socketLabel);

            var socketAcb = CreateValidatedOtherSuggestionBox(socketUnitType, socketSuggestions, "Socket Unit Type", suggestionsAlreadyNormalized: true);
            Grid.SetColumn(socketAcb, 2);
            headerGrid.Children.Add(socketAcb);
            _fieldControls["socketunittype"] = socketAcb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "socketunittype", "nonsocketplaceprotoid");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 3);
                headerGrid.Children.Add(deleteButton);
            }

            stack.Children.Add(headerGrid);
            var bodyStack = new StackPanel { Spacing = 6 };
            stack.Children.Add(bodyStack);

            void RenderBody()
            {
                bodyStack.Children.Clear();
                _fieldControls.Remove("nonsocketplaceprotoid");

                if (!showNonSocket)
                {
                    if (_isReadOnly)
                        return;

                    var addButton = new Button
                    {
                        Content = "+ Add Non Socket Place Proto ID",
                        Background = Brush.Parse("#2b7a0b"),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    addButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            showNonSocket = true;
                            MarkDirty();
                            RenderBody();
                        }
                    };
                    bodyStack.Children.Add(addButton);
                    return;
                }

                var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, *, Auto"), Margin = new Thickness(0, 2, 0, 2) };
                rowGrid.Children.Add(new TextBlock { Text = "", Margin = new Thickness(0, 4, 10, 4) });
                var nonSocketLabel = new TextBlock { Text = "Non Socket Place Proto ID", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
                Grid.SetColumn(nonSocketLabel, 1);
                rowGrid.Children.Add(nonSocketLabel);
                var nonSocketAcb = CreateValidatedOtherSuggestionBox(nonSocketPlaceProtoId, socketSuggestions, "Non Socket Place Proto ID", suggestionsAlreadyNormalized: true);
                Grid.SetColumn(nonSocketAcb, 2);
                rowGrid.Children.Add(nonSocketAcb);
                _fieldControls["nonsocketplaceprotoid"] = nonSocketAcb;

                if (!_isReadOnly)
                {
                    var clearButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    clearButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            nonSocketPlaceProtoId = "";
                            showNonSocket = false;
                            MarkDirty();
                            RenderBody();
                        }
                    };
                    Grid.SetColumn(clearButton, 3);
                    rowGrid.Children.Add(clearButton);
                }

                bodyStack.Children.Add(rowGrid);
            }

            RenderBody();
            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddChipListEditor(string key, string title, HashSet<string> values, Action<HashSet<string>> assignTarget)
        {
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            assignTarget(values);
            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 2) });
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(wrap);

            void RefreshDisplay()
            {
                wrap.Children.Clear();
                foreach (var value in values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    wrap.Children.Add(CreateChip(value, async () =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            values.Remove(value);
                            MarkDirty();
                            RefreshDisplay();
                        }
                    }));
                }
            }

            RefreshDisplay();

            if (!_isReadOnly)
            {
                var addGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto, Auto") };
                var acb = CreateOtherSuggestionBox("", otherAttributeSuggestions, title);
                Grid.SetColumn(acb, 0);
                addGrid.Children.Add(acb);
                string? selectedValue = null;

                async Task PerformAdd()
                {
                    var input = acb.Text?.Trim() ?? "";
                    var match = otherAttributeSuggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(match) && values.Add(match))
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            acb.Text = "";
                            MarkDirty();
                            RefreshDisplay();
                        }
                    }
                }

                acb.SelectionChanged += (s, e) =>
                {
                    if (acb.SelectedItem is string selected)
                    {
                        selectedValue = selected;
                        acb.Text = selected;
                        Dispatcher.UIThread.Post(() =>
                        {
                            var input = acb.Text?.Trim() ?? "";
                            var match = otherAttributeSuggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                            if (string.IsNullOrWhiteSpace(match) && !string.IsNullOrWhiteSpace(selectedValue))
                                match = otherAttributeSuggestions.FirstOrDefault(x => x.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));

                            if (!string.IsNullOrWhiteSpace(match))
                                _ = PerformAdd();
                        }, DispatcherPriority.Background);
                    }
                };

                var addButton = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(8, 0, 8, 0) };
                addButton.Click += async (s, e) => await PerformAdd();
                Grid.SetColumn(addButton, 1);
                addGrid.Children.Add(addButton);

                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000") };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        values.Clear();
                        RemoveOtherSpecificContainer(key);
                        assignTarget(null!);
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 2);
                addGrid.Children.Add(deleteButton);

                stack.Children.Add(addGrid);
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddDecayEditor()
        {
            const string key = "decay";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var decay = unit.Element("decay");
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 120, Auto, 120, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Decay", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            var delayLabel = new TextBlock { Text = "Delay", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(delayLabel, 1);
            rowGrid.Children.Add(delayLabel);
            var delayTb = CreateOtherTextBox((string?)decay?.Attribute("delay") ?? "", true);
            Grid.SetColumn(delayTb, 2);
            rowGrid.Children.Add(delayTb);
            _fieldControls["decay.delay"] = delayTb;
            var durationLabel = new TextBlock { Text = "Duration", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(durationLabel, 3);
            rowGrid.Children.Add(durationLabel);
            var durationTb = CreateOtherTextBox((string?)decay?.Attribute("duration") ?? "", true);
            Grid.SetColumn(durationTb, 4);
            rowGrid.Children.Add(durationTb);
            _fieldControls["decay.duration"] = durationTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "decay.delay", "decay.duration");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddRechargeEditor()
        {
            const string key = "recharge";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var rechargeTime = ProtoXmlHandler.GetSimpleField(unit, "rechargetime") ?? "";
            var recharge = unit.Element("recharge");
            var rechargeTypeRaw = (string?)recharge?.Attribute("type") ?? "";
            var currentMode = !string.IsNullOrWhiteSpace(rechargeTime)
                ? "Time"
                : (KnownRechargeTypes.FirstOrDefault(x => x.Equals(rechargeTypeRaw, StringComparison.OrdinalIgnoreCase)) ?? "Time");
            var initialValue = currentMode.Equals("Time", StringComparison.OrdinalIgnoreCase)
                ? rechargeTime
                : recharge?.Value?.Trim() ?? "";
            var initialInit = currentMode.Equals("Time", StringComparison.OrdinalIgnoreCase)
                ? ((string?)unit.Element("rechargetime")?.Attribute("init") ?? "1")
                : ((string?)recharge?.Attribute("init") ?? "1");

            _currentRechargeIncludeTypes = new HashSet<string>(unit.Element("rechargeincludetypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase);
            _currentRechargeExcludeTypes = new HashSet<string>(unit.Element("rechargeexcludetypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase);
            bool showRechargeTypeFilters =
                (_currentRechargeIncludeTypes?.Count ?? 0) > 0 ||
                (_currentRechargeExcludeTypes?.Count ?? 0) > 0;

            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 2) };
            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 170, Auto, 120, Auto, 120, Auto") };
            headerGrid.Children.Add(new TextBlock { Text = "Ability Recharge", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            var modeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(modeLabel, 1);
            headerGrid.Children.Add(modeLabel);
            var modeCb = new ComboBox
            {
                ItemsSource = (new[] { "Time" }).Concat(KnownRechargeTypes).ToArray(),
                SelectedItem = currentMode,
                IsEnabled = !_isReadOnly,
                MinWidth = 180
            };
            Grid.SetColumn(modeCb, 2);
            headerGrid.Children.Add(modeCb);
            _fieldControls["recharge.mode"] = modeCb;

            var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(valueLabel, 3);
            headerGrid.Children.Add(valueLabel);
            var valueTb = CreateOtherTextBox(initialValue, true);
            Grid.SetColumn(valueTb, 4);
            headerGrid.Children.Add(valueTb);
            _fieldControls["recharge.value"] = valueTb;

            var initLabel = new TextBlock { Text = "Init", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(initLabel, 5);
            headerGrid.Children.Add(initLabel);
            var initTb = CreateOtherTextBox(initialInit, true);
            Grid.SetColumn(initTb, 6);
            headerGrid.Children.Add(initTb);
            _fieldControls["recharge.init"] = initTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _currentRechargeIncludeTypes = null;
                        _currentRechargeExcludeTypes = null;
                        RemoveOtherSpecificContainer(key, "recharge.mode", "recharge.init", "recharge.value");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 7);
                headerGrid.Children.Add(deleteButton);
            }

            stack.Children.Add(headerGrid);
            var bodyStack = new StackPanel { Spacing = 6 };
            stack.Children.Add(bodyStack);

            StackPanel CreateInlineRechargeTypeEditor(string title, HashSet<string> values)
            {
                var editorStack = new StackPanel { Spacing = 4 };
                editorStack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 2) });
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                editorStack.Children.Add(wrap);

                void RefreshDisplay()
                {
                    wrap.Children.Clear();
                    foreach (var value in values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        wrap.Children.Add(CreateChip(value, async () =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                values.Remove(value);
                                MarkDirty();
                                RefreshDisplay();
                            }
                        }));
                    }
                }

                RefreshDisplay();

                if (!_isReadOnly)
                {
                    var addGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
                    var suggestions = GetAvailableBuildLimitTargets();
                    var acb = CreateOtherSuggestionBox("", suggestions, title);
                    Grid.SetColumn(acb, 0);
                    addGrid.Children.Add(acb);
                    string? selectedValue = null;

                    async Task PerformAdd()
                    {
                        var input = acb.Text?.Trim() ?? "";
                        var match = suggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(match) && values.Add(match))
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                acb.Text = "";
                                MarkDirty();
                                RefreshDisplay();
                            }
                            else
                            {
                                values.Remove(match);
                            }
                        }
                    }

                    acb.SelectionChanged += (s, e) =>
                    {
                        if (acb.SelectedItem is string selected)
                        {
                            selectedValue = selected;
                            acb.Text = selected;
                            Dispatcher.UIThread.Post(() =>
                            {
                                var input = acb.Text?.Trim() ?? "";
                                var match = suggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                                if (string.IsNullOrWhiteSpace(match) && !string.IsNullOrWhiteSpace(selectedValue))
                                    match = suggestions.FirstOrDefault(x => x.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));

                                if (!string.IsNullOrWhiteSpace(match))
                                    _ = PerformAdd();
                            }, DispatcherPriority.Background);
                        }
                    };

                    var addButton = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(8, 0, 0, 0) };
                    addButton.Click += async (s, e) => await PerformAdd();
                    Grid.SetColumn(addButton, 1);
                    addGrid.Children.Add(addButton);
                    editorStack.Children.Add(addGrid);
                }

                return editorStack;
            }

            void RenderBody()
            {
                bodyStack.Children.Clear();
                var selectedMode = modeCb.SelectedItem as string ?? "Time";
                if (selectedMode.Equals("Time", StringComparison.OrdinalIgnoreCase))
                    return;

                if (!showRechargeTypeFilters)
                {
                    if (_isReadOnly)
                        return;

                    var addButton = new Button
                    {
                        Content = "+ Add Recharge Include/Exclude Types",
                        Background = Brush.Parse("#2b7a0b"),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    addButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _currentRechargeIncludeTypes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            _currentRechargeExcludeTypes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            showRechargeTypeFilters = true;
                            MarkDirty();
                            RenderBody();
                        }
                    };
                    bodyStack.Children.Add(addButton);
                    return;
                }

                var filtersGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, *, Auto"), ColumnSpacing = 12 };
                var includeEditor = CreateInlineRechargeTypeEditor("Recharge Include Types", _currentRechargeIncludeTypes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                Grid.SetColumn(includeEditor, 0);
                filtersGrid.Children.Add(includeEditor);
                var excludeEditor = CreateInlineRechargeTypeEditor("Recharge Exclude Types", _currentRechargeExcludeTypes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                Grid.SetColumn(excludeEditor, 1);
                filtersGrid.Children.Add(excludeEditor);

                if (!_isReadOnly)
                {
                    var clearButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0) };
                    clearButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _currentRechargeIncludeTypes?.Clear();
                            _currentRechargeExcludeTypes?.Clear();
                            showRechargeTypeFilters = false;
                            MarkDirty();
                            RenderBody();
                        }
                    };
                    Grid.SetColumn(clearButton, 2);
                    filtersGrid.Children.Add(clearButton);
                }

                bodyStack.Children.Add(filtersGrid);
            }

            modeCb.SelectionChanged += async (s, e) =>
            {
                if (_isPopulating)
                    return;
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    MarkDirty();
                    RenderBody();
                }
            };

            RenderBody();
            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddAuxRechargeEditor()
        {
            const string key = "auxrecharge";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var rechargeTime = ProtoXmlHandler.GetSimpleField(unit, "auxrechargetime") ?? "";
            var recharge = unit.Element("auxrecharge");
            var rechargeTypeRaw = (string?)recharge?.Attribute("type") ?? "";
            var currentMode = !string.IsNullOrWhiteSpace(rechargeTime)
                ? "Time"
                : (KnownRechargeTypes.FirstOrDefault(x => x.Equals(rechargeTypeRaw, StringComparison.OrdinalIgnoreCase)) ?? "Time");
            var initialValue = currentMode.Equals("Time", StringComparison.OrdinalIgnoreCase)
                ? rechargeTime
                : recharge?.Value?.Trim() ?? "";
            var initialInit = currentMode.Equals("Time", StringComparison.OrdinalIgnoreCase)
                ? ((string?)unit.Element("auxrechargetime")?.Attribute("init") ?? "1")
                : ((string?)recharge?.Attribute("init") ?? "1");

            _currentAuxRechargeIncludeTypes = new HashSet<string>(unit.Element("auxrechargeincludetypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase);
            _currentAuxRechargeExcludeTypes = new HashSet<string>(unit.Element("auxrechargeexcludetypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase);
            bool showRechargeTypeFilters =
                (_currentAuxRechargeIncludeTypes?.Count ?? 0) > 0 ||
                (_currentAuxRechargeExcludeTypes?.Count ?? 0) > 0;

            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 2) };
            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 170, Auto, 120, Auto, 120, Auto") };
            headerGrid.Children.Add(new TextBlock { Text = "Aux Ability Recharge", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            var modeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(modeLabel, 1);
            headerGrid.Children.Add(modeLabel);
            var modeCb = new ComboBox
            {
                ItemsSource = (new[] { "Time" }).Concat(KnownRechargeTypes).ToArray(),
                SelectedItem = currentMode,
                IsEnabled = !_isReadOnly,
                MinWidth = 180
            };
            Grid.SetColumn(modeCb, 2);
            headerGrid.Children.Add(modeCb);
            _fieldControls["auxrecharge.mode"] = modeCb;

            var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(valueLabel, 3);
            headerGrid.Children.Add(valueLabel);
            var valueTb = CreateOtherTextBox(initialValue, true);
            Grid.SetColumn(valueTb, 4);
            headerGrid.Children.Add(valueTb);
            _fieldControls["auxrecharge.value"] = valueTb;

            var initLabel = new TextBlock { Text = "Init", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(initLabel, 5);
            headerGrid.Children.Add(initLabel);
            var initTb = CreateOtherTextBox(initialInit, true);
            Grid.SetColumn(initTb, 6);
            headerGrid.Children.Add(initTb);
            _fieldControls["auxrecharge.init"] = initTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _currentAuxRechargeIncludeTypes = null;
                        _currentAuxRechargeExcludeTypes = null;
                        RemoveOtherSpecificContainer(key, "auxrecharge.mode", "auxrecharge.init", "auxrecharge.value");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 7);
                headerGrid.Children.Add(deleteButton);
            }

            stack.Children.Add(headerGrid);
            var bodyStack = new StackPanel { Spacing = 6 };
            stack.Children.Add(bodyStack);

            StackPanel CreateInlineRechargeTypeEditor(string title, HashSet<string> values)
            {
                var editorStack = new StackPanel { Spacing = 4 };
                editorStack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 2) });
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                editorStack.Children.Add(wrap);

                void RefreshDisplay()
                {
                    wrap.Children.Clear();
                    foreach (var value in values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        wrap.Children.Add(CreateChip(value, async () =>
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                values.Remove(value);
                                MarkDirty();
                                RefreshDisplay();
                            }
                        }));
                    }
                }

                RefreshDisplay();

                if (!_isReadOnly)
                {
                    var addGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
                    var suggestions = GetAvailableBuildLimitTargets();
                    var acb = CreateOtherSuggestionBox("", suggestions, title);
                    Grid.SetColumn(acb, 0);
                    addGrid.Children.Add(acb);
                    string? selectedValue = null;

                    async Task PerformAdd()
                    {
                        var input = acb.Text?.Trim() ?? "";
                        var match = suggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(match) && values.Add(match))
                        {
                            var proceed = await CheckStartLocalMod();
                            if (proceed)
                            {
                                acb.Text = "";
                                MarkDirty();
                                RefreshDisplay();
                            }
                            else
                            {
                                values.Remove(match);
                            }
                        }
                    }

                    acb.SelectionChanged += (s, e) =>
                    {
                        if (acb.SelectedItem is string selected)
                        {
                            selectedValue = selected;
                            acb.Text = selected;
                            Dispatcher.UIThread.Post(() =>
                            {
                                var input = acb.Text?.Trim() ?? "";
                                var match = suggestions.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                                if (string.IsNullOrWhiteSpace(match) && !string.IsNullOrWhiteSpace(selectedValue))
                                    match = suggestions.FirstOrDefault(x => x.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));

                                if (!string.IsNullOrWhiteSpace(match))
                                    _ = PerformAdd();
                            }, DispatcherPriority.Background);
                        }
                    };

                    var addButton = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(8, 0, 0, 0) };
                    addButton.Click += async (s, e) => await PerformAdd();
                    Grid.SetColumn(addButton, 1);
                    addGrid.Children.Add(addButton);
                    editorStack.Children.Add(addGrid);
                }

                return editorStack;
            }

            void RenderBody()
            {
                bodyStack.Children.Clear();
                var selectedMode = modeCb.SelectedItem as string ?? "Time";
                if (selectedMode.Equals("Time", StringComparison.OrdinalIgnoreCase))
                    return;

                if (!showRechargeTypeFilters)
                {
                    if (_isReadOnly)
                        return;

                    var addButton = new Button
                    {
                        Content = "+ Add Aux Recharge Include/Exclude Types",
                        Background = Brush.Parse("#2b7a0b"),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    addButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _currentAuxRechargeIncludeTypes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            _currentAuxRechargeExcludeTypes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            showRechargeTypeFilters = true;
                            MarkDirty();
                            RenderBody();
                        }
                    };
                    bodyStack.Children.Add(addButton);
                    return;
                }

                var filtersGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, *, Auto"), ColumnSpacing = 12 };
                var includeEditor = CreateInlineRechargeTypeEditor("Aux Recharge Include Types", _currentAuxRechargeIncludeTypes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                Grid.SetColumn(includeEditor, 0);
                filtersGrid.Children.Add(includeEditor);
                var excludeEditor = CreateInlineRechargeTypeEditor("Aux Recharge Exclude Types", _currentAuxRechargeExcludeTypes ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                Grid.SetColumn(excludeEditor, 1);
                filtersGrid.Children.Add(excludeEditor);

                if (!_isReadOnly)
                {
                    var clearButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0) };
                    clearButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            _currentAuxRechargeIncludeTypes?.Clear();
                            _currentAuxRechargeExcludeTypes?.Clear();
                            showRechargeTypeFilters = false;
                            MarkDirty();
                            RenderBody();
                        }
                    };
                    Grid.SetColumn(clearButton, 2);
                    filtersGrid.Children.Add(clearButton);
                }

                bodyStack.Children.Add(filtersGrid);
            }

            modeCb.SelectionChanged += async (s, e) =>
            {
                if (_isPopulating)
                    return;
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    MarkDirty();
                    RenderBody();
                }
            };

            RenderBody();
            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddMinimapColorEditor()
        {
            const string key = "minimapcolor";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var minimapColor = unit.Element("minimapcolor");
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 90, Auto, 90, Auto, 90, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Minimap Color", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            void AddColorField(string labelText, string keyName, string attrName, int labelColumn, int valueColumn)
            {
                var label = new TextBlock { Text = labelText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
                Grid.SetColumn(label, labelColumn);
                rowGrid.Children.Add(label);
                var tb = CreateOtherTextBox((string?)minimapColor?.Attribute(attrName) ?? "", true);
                Grid.SetColumn(tb, valueColumn);
                rowGrid.Children.Add(tb);
                _fieldControls[keyName] = tb;
            }
            AddColorField("Red", "minimapcolor.red", "red", 1, 2);
            AddColorField("Green", "minimapcolor.green", "green", 3, 4);
            AddColorField("Blue", "minimapcolor.blue", "blue", 5, 6);

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "minimapcolor.red", "minimapcolor.green", "minimapcolor.blue");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 7);
                rowGrid.Children.Add(deleteButton);
            }

            RegisterOtherSpecificContainer(key, rowGrid);
            otherSpecificContainer.Children.Add(rowGrid);
        }

        void AddMinimapVisualsEditor()
        {
            const string key = "minimapvisuals";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var minimapColor = unit.Element("minimapcolor");
            var row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, *, Auto, 100, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            row1.Children.Add(new TextBlock { Text = "Minimap Visuals", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var iconLabel = new TextBlock { Text = "Icon", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(iconLabel, 1);
            row1.Children.Add(iconLabel);

            var iconAcb = CreateValidatedOtherSuggestionBox(ProtoXmlHandler.GetSimpleField(unit, "minimapicon") ?? "", GetKnownMinimapIcons(), "Minimap Icon");
            Grid.SetColumn(iconAcb, 2);
            row1.Children.Add(iconAcb);
            _fieldControls["minimapicon"] = iconAcb;

            var sizeLabel = new TextBlock { Text = "Size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(sizeLabel, 3);
            row1.Children.Add(sizeLabel);

            var sizeTb = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "minimapsize") ?? "", true);
            Grid.SetColumn(sizeTb, 4);
            row1.Children.Add(sizeTb);
            _fieldControls["minimapsize"] = sizeTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "minimapicon", "minimapsize", "minimapcolor.red", "minimapcolor.green", "minimapcolor.blue");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                row1.Children.Add(deleteButton);
            }

            var row2 = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 90, Auto, 90, Auto, 90"), Margin = new Thickness(0, 2, 0, 2) };
            row2.Children.Add(new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center });

            void AddColorField(string labelText, string keyName, string attrName, int labelColumn, int valueColumn)
            {
                var label = new TextBlock { Text = labelText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
                Grid.SetColumn(label, labelColumn);
                row2.Children.Add(label);
                var tb = CreateOtherTextBox((string?)minimapColor?.Attribute(attrName) ?? "", true);
                Grid.SetColumn(tb, valueColumn);
                row2.Children.Add(tb);
                _fieldControls[keyName] = tb;
            }

            AddColorField("Red", "minimapcolor.red", "red", 1, 2);
            AddColorField("Green", "minimapcolor.green", "green", 3, 4);
            AddColorField("Blue", "minimapcolor.blue", "blue", 5, 6);

            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(row1);
            stack.Children.Add(row2);

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddReplacementEditor()
        {
            const string key = "replacement";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var replacement = unit.Element("replacement");
            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };

            var row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 160, Auto, *, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            row1.Children.Add(new TextBlock { Text = "Replacement", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });

            var typeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(typeLabel, 1);
            row1.Children.Add(typeLabel);

            var typeAcb = CreateValidatedOtherSuggestionBox((string?)replacement?.Attribute("type") ?? "dead", KnownReplacementTypes, "Type");
            Grid.SetColumn(typeAcb, 2);
            row1.Children.Add(typeAcb);
            _fieldControls["replacement.type"] = typeAcb;

            var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(valueLabel, 3);
            row1.Children.Add(valueLabel);

            var valueAcb = CreateValidatedOtherSuggestionBox(replacement?.Value?.Trim() ?? "", otherProtoUnitSuggestions, "Proto Unit", suggestionsAlreadyNormalized: true);
            Grid.SetColumn(valueAcb, 4);
            row1.Children.Add(valueAcb);
            _fieldControls["replacement.value"] = valueAcb;

            if (!_isReadOnly)
            {
                var lifespan = (string?)replacement?.Attribute("lifespan") ?? "";
                StackPanel? row2 = null;
                TextBox? lifespanTb = null;
                Button? addLifespanButton = null;

                void ShowLifespanEditor(string initialValue)
                {
                    if (row2 != null)
                        return;

                    row2 = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Margin = new Thickness(180, 0, 0, 0)
                    };
                    row2.Children.Add(new TextBlock { Text = "Lifespan", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
                    lifespanTb = CreateOtherTextBox(string.IsNullOrWhiteSpace(initialValue) ? "" : initialValue, true);
                    lifespanTb.Width = 120;
                    row2.Children.Add(lifespanTb);
                    _fieldControls["replacement.lifespan"] = lifespanTb;

                    var removeLifespanButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                    removeLifespanButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed && row2 != null)
                        {
                            stack.Children.Remove(row2);
                            row2 = null;
                            lifespanTb = null;
                            _fieldControls.Remove("replacement.lifespan");
                            addLifespanButton.IsVisible = true;
                            MarkDirty();
                        }
                    };
                    row2.Children.Add(removeLifespanButton);
                    stack.Children.Add(row2);
                    addLifespanButton.IsVisible = false;
                }

                addLifespanButton = new Button
                {
                    Content = "Add lifespan",
                    Background = Brush.Parse("#2b7a0b"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(180, 0, 0, 0),
                    IsVisible = string.IsNullOrWhiteSpace(lifespan)
                };
                addLifespanButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        ShowLifespanEditor(lifespan);
                        MarkDirty();
                    }
                };

                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "replacement.type", "replacement.value", "replacement.lifespan");
                        MarkDirty();
                        RenderOtherSpecificAddControls();
                    }
                };
                Grid.SetColumn(deleteButton, 5);
                row1.Children.Add(deleteButton);

                stack.Children.Add(row1);
                if (!string.IsNullOrWhiteSpace(lifespan))
                    ShowLifespanEditor(lifespan);
                stack.Children.Add(addLifespanButton);
            }
            else
            {
                stack.Children.Add(row1);
                var lifespan = (string?)replacement?.Attribute("lifespan") ?? "";
                if (!string.IsNullOrWhiteSpace(lifespan))
                {
                    var row2 = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Margin = new Thickness(180, 0, 0, 0)
                    };
                    row2.Children.Add(new TextBlock { Text = "Lifespan", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
                    var lifespanTb = CreateOtherTextBox(lifespan, true);
                    lifespanTb.Width = 120;
                    row2.Children.Add(lifespanTb);
                    _fieldControls["replacement.lifespan"] = lifespanTb;
                    stack.Children.Add(row2);
                }
            }

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
        }

        void AddOtherSpecificAttributeByKey(string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "selectionradius":
                    AddSelectionRadiusEditor();
                    break;
                case "placementobstruction":
                    AddPlacementObstructionEditor();
                    break;
                case "farming":
                    AddFarmingEditor();
                    break;
                case "creationfade":
                    AddCreationFadeEditor();
                    break;
                case "stealth":
                    AddStealthEditor();
                    break;
                case "heightbobdata":
                    AddHeightBobEditor();
                    break;
                case "initialshading":
                    AddInitialShadingEditor();
                    break;
                case "damageshading":
                    AddDamageShadingEditor();
                    break;
                case "killrewarddata":
                    AddKillRewardEditor();
                    break;
                case "dependentunitdata":
                    AddDependentUnitEditor();
                    break;
                case "spawndata":
                    AddSpawnEditor();
                    break;
                case "veterancydata":
                    AddVeterancyEditor();
                    break;
                case "minimapvisuals":
                    AddMinimapVisualsEditor();
                    break;
                case "partisans":
                    AddPartisansEditor();
                    break;
                case "bloodandbones":
                    AddBloodAndBonesEditor();
                    break;
                case "resourcereturn":
                    AddResourceReturnEditor();
                    break;
                case "resourcereturnrate":
                    AddResourceReturnRateEditor();
                    break;
                case "dodgechance":
                    AddSimpleOtherSpecificAttribute("dodgechance", "dodgechance");
                    break;
                case "directionalarmor":
                    AddDirectionalArmorEditor();
                    break;
                case "carrycapacity":
                    AddResourceMapEditor(key, "carrycapacity");
                    break;
                case "initialresource":
                    AddResourceMapEditor(key, "initialresource");
                    break;
                case "resourceconversion":
                    AddResourceConversionEditor();
                    break;
                case "respawntraindata":
                    AddRespawnTrainDataEditor();
                    break;
                case "auxrecharge":
                    AddAuxRechargeEditor();
                    break;
                case "socketdata":
                    AddSocketEditor();
                    break;
                case "sharedselectionunittypes":
                    AddChipListEditor(key, "Shared Selection Unit Types",
                        new HashSet<string>(unit.Element("sharedselectionunittypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase),
                        values => _currentSharedSelectionUnitTypes = values);
                    break;
                case "rechargeincludetypes":
                    AddChipListEditor(key, "Recharge Include Types",
                        new HashSet<string>(unit.Element("rechargeincludetypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase),
                        values => _currentRechargeIncludeTypes = values);
                    break;
                case "rechargeexcludetypes":
                    AddChipListEditor(key, "Recharge Exclude Types",
                        new HashSet<string>(unit.Element("rechargeexcludetypes")?.Elements("unittype").Select(x => x.Value?.Trim() ?? "").Where(x => x.Length > 0) ?? [], StringComparer.OrdinalIgnoreCase),
                        values => _currentRechargeExcludeTypes = values);
                    break;
                case "decay":
                    AddDecayEditor();
                    break;
                case "recharge":
                    AddRechargeEditor();
                    break;
                case "minimapcolor":
                    AddMinimapColorEditor();
                    break;
                case "replacement":
                    AddReplacementEditor();
                    break;
                case "ondamagemodifiers":
                    AddOnDamageModifiersEditor();
                    break;
                default:
                    AddSimpleOtherSpecificAttribute(key, key);
                    break;
            }
        }

        void RenderOtherSpecificAddControls()
        {
            if (_otherSpecificAttributeContainers.TryGetValue("__picker", out var existingPicker))
            {
                otherSpecificContainer.Children.Remove(existingPicker);
                _otherSpecificAttributeContainers.Remove("__picker");
            }

            if (_isReadOnly)
                return;

            var remaining = OtherSpecificAttributeChoiceKeys
                .Where(x => !OriginalOnlyOtherSpecificTags.Contains(x))
                .Where(x => !IsOtherSpecificAttributeVisible(x, _otherSpecificAttributeContainers))
                .Select(GetOtherSpecificAttributeLabel)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (remaining.Count == 0)
                return;

            var pickerHost = new StackPanel { Spacing = 4 };
            var remainingByLabel = remaining.ToDictionary(x => x, x => OtherSpecificAttributeLabels.FirstOrDefault(y => y.Value.Equals(x, StringComparison.OrdinalIgnoreCase)).Key, StringComparer.OrdinalIgnoreCase);

            var addButton = new Button
            {
                Content = "Other specific attribute",
                Background = Brush.Parse("#2b7a0b"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addButton.Click += (s, e) =>
            {
                pickerHost.Children.Clear();
                pickerHost.Children.Add(addButton);

                var pickerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("260, Auto"), Margin = new Thickness(0, 4, 0, 0) };
                var pickerAcb = new AutoCompleteBox
                {
                    ItemsSource = remaining,
                    FilterMode = AutoCompleteFilterMode.Contains,
                    MinimumPrefixLength = 0,
                    MinimumPopulateDelay = TimeSpan.Zero,
                    IsEnabled = !_isReadOnly,
                    Margin = new Thickness(0)
                };
                EnableDropdownAutoComplete(pickerAcb);
                Grid.SetColumn(pickerAcb, 0);
                pickerRow.Children.Add(pickerAcb);
                string? selectedAttributeLabel = null;
                bool isAddingAttribute = false;

                async void PerformAdd(string? candidate = null)
                {
                    if (isAddingAttribute)
                        return;

                    var input = (candidate ?? pickerAcb.Text)?.Trim() ?? "";
                    if (!remainingByLabel.TryGetValue(input, out var key) || string.IsNullOrWhiteSpace(key))
                        return;

                    if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                        return;

                    isAddingAttribute = true;
                    try
                    {
                        var proceed = await CheckStartLocalMod();
                        if (proceed)
                        {
                            AddOtherSpecificAttributeByKey(key);
                            pickerAcb.Text = "";
                            selectedAttributeLabel = null;
                            MarkDirty();
                            RenderOtherSpecificAddControls();
                        }
                    }
                    finally
                    {
                        isAddingAttribute = false;
                    }
                }

                pickerAcb.SelectionChanged += (sender, args) =>
                {
                    if (pickerAcb.SelectedItem is string selected)
                    {
                        selectedAttributeLabel = selected;
                        pickerAcb.Text = selected;
                        Dispatcher.UIThread.Post(() =>
                        {
                            var input = pickerAcb.Text?.Trim() ?? "";
                            var match = remaining.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                            if (string.IsNullOrWhiteSpace(match) && !string.IsNullOrWhiteSpace(selectedAttributeLabel))
                            {
                                match = remaining.FirstOrDefault(x => x.Equals(selectedAttributeLabel, StringComparison.OrdinalIgnoreCase));
                            }

                            if (!string.IsNullOrWhiteSpace(match))
                                PerformAdd(match);
                        }, DispatcherPriority.Background);
                    }
                };
                pickerAcb.LostFocus += (sender, args) =>
                {
                    if (_isPopulating)
                        return;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_isPopulating)
                            return;

                        var input = pickerAcb.Text?.Trim() ?? "";
                        var match = remaining.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrWhiteSpace(match) && !string.IsNullOrWhiteSpace(selectedAttributeLabel))
                        {
                            match = remaining.FirstOrDefault(x => x.Equals(selectedAttributeLabel, StringComparison.OrdinalIgnoreCase));
                        }

                        if (string.IsNullOrWhiteSpace(match))
                        {
                            pickerAcb.Text = "";
                            selectedAttributeLabel = null;
                            return;
                        }

                        pickerAcb.Text = match;
                    }, DispatcherPriority.Background);
                };
                pickerAcb.KeyUp += (sender, args) =>
                {
                    if (args.Key == Key.Enter)
                        PerformAdd();
                };

                var btnAdd = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(8, 0, 0, 0) };
                btnAdd.Click += (sender, args) => PerformAdd();
                Grid.SetColumn(btnAdd, 1);
                pickerRow.Children.Add(btnAdd);

                pickerHost.Children.Add(pickerRow);

                Dispatcher.UIThread.Post(() =>
                {
                    pickerAcb.Focus();
                    pickerAcb.IsDropDownOpen = true;
                });
            };

            pickerHost.Children.Add(addButton);
            RegisterOtherSpecificContainer("__picker", pickerHost);
            otherSpecificContainer.Children.Add(pickerHost);
        }

        bool HasAnyOtherSpecificData(string key) => key switch
        {
            "selectionradius" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "selectionradiusx")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "selectionradiusz")),
            "placementobstruction" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "placementobstructionradiusx")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "placementobstructionradiusz")),
            "farming" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "farmingradiusx")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "farmingradiusz")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "farmingobstructionradiusx")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "farmingobstructionradiusz")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "farmingnumstops")),
            "creationfade" => unit.Element("creationfadetime") != null,
            "stealth" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "stealthdetectionradius")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "stealthrevealselfradius")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "stealthshowsilhouetteradius")),
            "heightbobdata" => unit.Element("heightbob") != null,
            "damageshading" => unit.Element("damageshading") != null,
            "killrewarddata" => unit.Elements("killreward").Any(),
            "resourcereturn" => unit.Elements("ResourceReturn").Any(),
            "resourcereturnrate" => unit.Elements("ResourceReturnRate").Any(),
            "dependentunitdata" => unit.Elements("dependentunit").Any(),
            "spawndata" => unit.Elements("spawn").Any(),
            "veterancydata" => unit.Element("veterancyranks") != null || unit.Element("veterancybonus") != null,
            "minimapvisuals" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "minimapicon")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "minimapsize")) || unit.Element("minimapcolor") != null,
            "initialshading" => unit.Element("initialshading") != null,
            "partisans" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "partisantype")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "partisancount")),
            "bloodandbones" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "bloodgroupoverride")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "bloodscalemodify")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "bonescalemodify")),
            "dodgechance" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "dodgechance")),
            "directionalarmor" => unit.Element("directionalarmor") != null,
            "carrycapacity" or "initialresource" or "resourceconversion" => unit.Elements(key).Any(),
            "recharge" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "rechargetime")) || unit.Element("recharge") != null || unit.Element("rechargeincludetypes") != null || unit.Element("rechargeexcludetypes") != null,
            "auxrecharge" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "auxrechargetime")) || unit.Element("auxrecharge") != null || unit.Element("auxrechargeincludetypes") != null || unit.Element("auxrechargeexcludetypes") != null,
            "socketdata" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "socketunittype")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "nonsocketplaceprotoid")),
            "sharedselectionunittypes" or "decay" or "replacement" or "respawntraindata" => unit.Element(key) != null,
            "ondamagemodifiers" => unit.Elements().Any(x => x.Name.LocalName.Equals("OnDamageModifiers", StringComparison.OrdinalIgnoreCase)),
            _ => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, key)),
        };

        foreach (var key in OtherSpecificAttributeChoiceKeys)
        {
            if (HasAnyOtherSpecificData(key))
                AddOtherSpecificAttributeByKey(key);
        }

        if (_isReadOnly)
        {
            foreach (var tag in OriginalOnlyOtherSpecificTags)
            {
                if (!string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, tag)))
                    AddSimpleOtherSpecificAttribute(tag, tag);
            }
        }

        RenderOtherSpecificAddControls();

        AddSectionHeader("Costs");
        var costs = ProtoXmlHandler.GetCostEntries(unit).ToDictionary(c => c.ResourceType, c => c.Amount, StringComparer.OrdinalIgnoreCase);
        var costsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, 100, Auto, 100, Auto, 100, Auto, 100") };
        foreach (var rtype in ProtoConstants.KnownResourceTypes)
        {
            int costIndex = Array.IndexOf(ProtoConstants.KnownResourceTypes, rtype);
            int labelColumn = costIndex * 2;
            int valueColumn = labelColumn + 1;

            var lbl = new TextBlock
            {
                Text = rtype,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            };
            Grid.SetColumn(lbl, labelColumn);
            costsGrid.Children.Add(lbl);

            string initialCost = costs.TryGetValue(rtype, out var val) ? val : "0";
            var tb = new TextBox
            {
                Text = initialCost,
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 4, costIndex < ProtoConstants.KnownResourceTypes.Length - 1 ? 16 : 0, 4)
            };
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
            Grid.SetColumn(tb, valueColumn);
            costsGrid.Children.Add(tb);
            _costControls[rtype] = tb;
        }
        _editorPanel.Children.Add(costsGrid);

        AddSectionHeader("Armor");
        var armors = ProtoXmlHandler.GetArmorEntries(unit).ToDictionary(a => a.ArmorType, a => a.Value, StringComparer.OrdinalIgnoreCase);
        var armorGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, 100, Auto, 100, Auto, 100") };
        foreach (var atype in ProtoConstants.KnownArmorTypes)
        {
            int armorIndex = Array.IndexOf(ProtoConstants.KnownArmorTypes, atype);
            int labelColumn = armorIndex * 2;
            int valueColumn = labelColumn + 1;

            var lbl = new TextBlock
            {
                Text = atype,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            };
            Grid.SetColumn(lbl, labelColumn);
            armorGrid.Children.Add(lbl);

            string initialArmor = armors.TryGetValue(atype, out var val) ? val : "0";
            var tb = new TextBox
            {
                Text = initialArmor,
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 4, armorIndex < ProtoConstants.KnownArmorTypes.Length - 1 ? 16 : 0, 4)
            };
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
            Grid.SetColumn(tb, valueColumn);
            armorGrid.Children.Add(tb);
            _armorControls[atype] = tb;
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
        AddCommandSection("Commands", "Command", ProtoXmlHandler.GetCommandEntries(unit), GetAvailableCommandNames(), _unitCommandRows);

        var optionalCommands = ProtoXmlHandler.GetOptionalCommandEntries(unit);
        if (_isReadOnly && optionalCommands.Count > 0)
        {
            AddCommandSection("Optional Commands", "Command", optionalCommands, GetAvailableCommandNames(), _optionalCommandRows);
        }

        string? initialTransformCommand = ProtoXmlHandler.GetSimpleField(unit, "transformcommand");
        var transformCommandContainer = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        _editorPanel.Children.Add(transformCommandContainer);

        void ShowTransformCommand(string initialValue)
        {
            transformCommandContainer.Children.Clear();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *, Auto") };

            var lbl = new TextBlock
            {
                Text = "Transform Command",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var acb = new AutoCompleteBox
            {
                Text = initialValue,
                PlaceholderText = "Transform Command",
                FilterMode = AutoCompleteFilterMode.Contains,
                ItemsSource = GetAvailableCommandNames(),
                IsEnabled = !_isReadOnly,
                Margin = new Thickness(0, 0, 10, 0)
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
            Grid.SetColumn(acb, 1);
            grid.Children.Add(acb);
            _fieldControls["transformcommand"] = acb;

            if (!_isReadOnly)
            {
                var btnDel = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                btnDel.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        _fieldControls.Remove("transformcommand");
                        MarkDirty();
                        ShowAddTransformCommandButton();
                    }
                };
                Grid.SetColumn(btnDel, 2);
                grid.Children.Add(btnDel);
            }

            transformCommandContainer.Children.Add(grid);
        }

        void ShowAddTransformCommandButton()
        {
            transformCommandContainer.Children.Clear();
            if (!_isReadOnly)
            {
                var btnAdd = new Button
                {
                    Content = "+ Add Transform Command (for one time transformation only)",
                    Background = Brush.Parse("#2b7a0b"),
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                btnAdd.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        MarkDirty();
                        ShowTransformCommand("");
                    }
                };
                transformCommandContainer.Children.Add(btnAdd);
            }
        }

        if (!string.IsNullOrWhiteSpace(initialTransformCommand))
            ShowTransformCommand(initialTransformCommand);
        else
            ShowAddTransformCommandButton();

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

        RebuildPageSearchTargets();
        RebuildSectionJumpFlyout();
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
                Content = "X",
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

        var typeAcb = new AutoCompleteBox
        {
            Text = resolvedType,
            FilterMode = AutoCompleteFilterMode.Contains,
            ItemsSource = typeOptions,
            Width = 150,
            IsEnabled = !_isReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        EnableDropdownAutoComplete(typeAcb);
        DockPanel.SetDock(typeAcb, Dock.Left);
        header.Children.Add(typeAcb);

        var state = new ProtoActionWidgetState
        {
            Container = mainStack,
            NameAcb = nameAcb,
            TypeAcb = typeAcb,
            RofTb = null!,
            MaxRangeTb = null!
        };
        _protoActionWidgets.Add(state);
        UpdateProtoActionTypeEditor(typeAcb, pa.Name);

        if (!_isReadOnly)
        {
            var btnRemove = new Button
            {
                Content = "Remove",
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
                        typeAcb.ItemsSource = GetProtoActionTypeOptions(mappedType);
                    }
                    else
                    {
                        typeAcb.ItemsSource = GetProtoActionTypeOptions(typeAcb.Text);
                    }

                    UpdateProtoActionTypeEditor(typeAcb, name);
                    MarkDirty();
                }
            }
        };

        typeAcb.SelectionChanged += async (s, e) =>
        {
            if (!_isPopulating)
            {
                var proceed = await CheckStartLocalMod();
                if (proceed)
                {
                    if (typeAcb.SelectedItem is string selectedType && !string.IsNullOrWhiteSpace(selectedType))
                        typeAcb.Text = selectedType;

                    MarkDirty();
                }
            }
        };

        typeAcb.TextChanged += async (s, e) =>
        {
            if (!_isPopulating)
            {
                var proceed = await CheckStartLocalMod();
                if (proceed)
                    MarkDirty();
            }
        };

        typeAcb.LostFocus += (s, e) =>
        {
            if (typeAcb.IsEnabled)
            {
                var matchedType = GetExactProtoActionTypeMatch(typeAcb.Text);
                if (!string.IsNullOrWhiteSpace(matchedType))
                    typeAcb.Text = matchedType;
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
                var btnDel = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
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
                var btnDel = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
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
            if (IsCultureAwareSimpleField(field.Tag))
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

        foreach (var tag in OtherSpecificSimpleNumberTags.Concat(OtherSpecificSimpleTextTags).Concat(OtherSpecificSimpleSuggestionTags))
        {
            if (_fieldControls.TryGetValue(tag, out var ctrl))
            {
                string val = ctrl switch
                {
                    AutoCompleteBox acb => acb.Text?.Trim() ?? "",
                    TextBox tb => tb.Text?.Trim() ?? "",
                    ComboBox cb => cb.SelectedItem as string ?? cb.SelectedValue as string ?? "",
                    _ => ""
                };

                if (tag.Equals("dodgechance", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(val))
                    {
                        val = "0";
                    }
                    else if (double.TryParse(val, out var dodgeChance))
                    {
                        if (dodgeChance < 0) dodgeChance = 0;
                        if (dodgeChance > 1) dodgeChance = 1;
                        val = dodgeChance.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        val = "0";
                    }
                }
                else if (tag.Equals("formationorder", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(val))
                    {
                        val = "0";
                    }
                    else if (int.TryParse(val, out var formationOrder))
                    {
                        if (formationOrder < 0) formationOrder = 0;
                        if (formationOrder > 5) formationOrder = 5;
                        val = formationOrder.ToString();
                    }
                    else
                    {
                        val = "0";
                    }
                }
                else if (tag.Equals("partisancount", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(val))
                    {
                        val = "0";
                    }
                }
                else if (tag.Equals("bloodscalemodify", StringComparison.OrdinalIgnoreCase) ||
                         tag.Equals("bonescalemodify", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(val))
                    {
                        val = "1";
                    }
                }
                else if (tag.Equals("resourcesubtype", StringComparison.OrdinalIgnoreCase))
                {
                    val = GetKnownResourceSubtypeNames().FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase)) ?? "";
                }
                else if (tag.Equals("initialunitaistance", StringComparison.OrdinalIgnoreCase))
                {
                    val = KnownInitialUnitAiStances.FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase)) ?? "";
                }
                else if (tag.Equals("pathabilityflags", StringComparison.OrdinalIgnoreCase))
                {
                    val = GetKnownPathabilityFlags().FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase)) ?? "";
                }
                else if (tag.Equals("placementfile", StringComparison.OrdinalIgnoreCase))
                {
                    var matchedPlacementFile = GetKnownPlacementFileNames().FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(matchedPlacementFile))
                        val = matchedPlacementFile;
                }
                else if (tag.Equals("hotkeycontext", StringComparison.OrdinalIgnoreCase))
                {
                    val = GetKnownHotkeyContexts().FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase)) ?? "";
                }
                else if (tag.Equals("allyhotkeycontext", StringComparison.OrdinalIgnoreCase))
                {
                    val = GetKnownHotkeyContexts().FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase)) ?? "";
                }
                else if (tag.Equals("partisantype", StringComparison.OrdinalIgnoreCase))
                {
                    val = GetAvailableTrainUnitNames().FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase)) ?? "";
                }
                else if (tag.Equals("bloodgroupoverride", StringComparison.OrdinalIgnoreCase))
                {
                    val = GetKnownBloodGroupNames().FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase)) ?? "";
                }
                else if (tag.Equals("minimapicon", StringComparison.OrdinalIgnoreCase))
                {
                    val = GetKnownMinimapIcons().FirstOrDefault(x => x.Equals(val, StringComparison.OrdinalIgnoreCase)) ?? "";
                }

                if (!string.IsNullOrWhiteSpace(val))
                    ProtoXmlHandler.SetSimpleField(unit, tag, val);
                else
                    ProtoXmlHandler.RemoveSimpleField(unit, tag);
            }
            else
            {
                ProtoXmlHandler.RemoveSimpleField(unit, tag);
            }
        }

        if (_fieldControls.TryGetValue("socketunittype", out var socketUnitTypeCtrl) && socketUnitTypeCtrl is AutoCompleteBox socketUnitTypeAcb)
        {
            var socketUnitType = socketUnitTypeAcb.Text?.Trim() ?? "";
            socketUnitType = GetAvailableBuildLimitTargets().FirstOrDefault(x => x.Equals(socketUnitType, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (!string.IsNullOrWhiteSpace(socketUnitType))
                ProtoXmlHandler.SetSimpleField(unit, "socketunittype", socketUnitType);
            else
                ProtoXmlHandler.RemoveSimpleField(unit, "socketunittype");
        }
        else
        {
            ProtoXmlHandler.RemoveSimpleField(unit, "socketunittype");
        }

        if (_fieldControls.TryGetValue("nonsocketplaceprotoid", out var nonSocketCtrl) && nonSocketCtrl is AutoCompleteBox nonSocketAcb)
        {
            var nonSocketPlaceProtoId = nonSocketAcb.Text?.Trim() ?? "";
            nonSocketPlaceProtoId = GetAvailableBuildLimitTargets().FirstOrDefault(x => x.Equals(nonSocketPlaceProtoId, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (!string.IsNullOrWhiteSpace(nonSocketPlaceProtoId))
                ProtoXmlHandler.SetSimpleField(unit, "nonsocketplaceprotoid", nonSocketPlaceProtoId);
            else
                ProtoXmlHandler.RemoveSimpleField(unit, "nonsocketplaceprotoid");
        }
        else
        {
            ProtoXmlHandler.RemoveSimpleField(unit, "nonsocketplaceprotoid");
        }

        void SyncFlagWithOtherSpecificField(string tag, bool shouldHaveFlag)
        {
            var flag = GetFlagForOtherSpecificAttribute(tag);
            if (string.IsNullOrWhiteSpace(flag) || _currentFlags == null)
                return;

            if (shouldHaveFlag)
                _currentFlags.Add(flag);
            else
                _currentFlags.Remove(flag);
        }

        SyncFlagWithOtherSpecificField(
            "displayedrange",
            _fieldControls.TryGetValue("displayedrange", out var displayedRangeCtrl) &&
            displayedRangeCtrl is TextBox displayedRangeTb &&
            !string.IsNullOrWhiteSpace(displayedRangeTb.Text));

        SyncFlagWithOtherSpecificField(
            "dodgechance",
            _fieldControls.TryGetValue("dodgechance", out var dodgeChanceCtrl) &&
            dodgeChanceCtrl is TextBox dodgeChanceTbForFlag &&
            !string.IsNullOrWhiteSpace(dodgeChanceTbForFlag.Text));

        SyncFlagWithOtherSpecificField(
            "farming",
            (_fieldControls.TryGetValue("farmingradiusx", out var farmingRadiusXCtrl) &&
             farmingRadiusXCtrl is TextBox farmingRadiusXTb &&
             !string.IsNullOrWhiteSpace(farmingRadiusXTb.Text)) ||
            (_fieldControls.TryGetValue("farmingradiusz", out var farmingRadiusZCtrl) &&
             farmingRadiusZCtrl is TextBox farmingRadiusZTb &&
             !string.IsNullOrWhiteSpace(farmingRadiusZTb.Text)) ||
            (_fieldControls.TryGetValue("farmingobstructionradiusx", out var farmingObstructionXCtrl) &&
             farmingObstructionXCtrl is TextBox farmingObstructionXTb &&
             !string.IsNullOrWhiteSpace(farmingObstructionXTb.Text)) ||
            (_fieldControls.TryGetValue("farmingobstructionradiusz", out var farmingObstructionZCtrl) &&
             farmingObstructionZCtrl is TextBox farmingObstructionZTb &&
             !string.IsNullOrWhiteSpace(farmingObstructionZTb.Text)) ||
            (_fieldControls.TryGetValue("farmingnumstops", out var farmingNumStopsCtrl) &&
             farmingNumStopsCtrl is TextBox farmingNumStopsTb &&
             !string.IsNullOrWhiteSpace(farmingNumStopsTb.Text)));

        unit.Elements("directionalarmor").Remove();
        if (_fieldControls.TryGetValue("directionalarmor.value", out var directionalArmorValueCtrl) && directionalArmorValueCtrl is TextBox directionalArmorValueTb)
        {
            var directionalArmorValue = directionalArmorValueTb.Text?.Trim() ?? "";
            var directionalArmorAngle = _fieldControls.TryGetValue("directionalarmor.angle", out var directionalArmorAngleCtrl) && directionalArmorAngleCtrl is TextBox directionalArmorAngleTb
                ? directionalArmorAngleTb.Text?.Trim() ?? ""
                : "";

            if (!string.IsNullOrWhiteSpace(directionalArmorValue) || !string.IsNullOrWhiteSpace(directionalArmorAngle))
            {
                var directionalArmorElement = new XElement("directionalarmor");
                if (!string.IsNullOrWhiteSpace(directionalArmorAngle))
                    directionalArmorElement.SetAttributeValue("angle", directionalArmorAngle);
                if (!string.IsNullOrWhiteSpace(directionalArmorValue))
                    directionalArmorElement.SetAttributeValue("value", directionalArmorValue);
                unit.Add(directionalArmorElement);
            }
        }

        unit.Elements("creationfadetime").Remove();
        var creationFadeValue = _fieldControls.TryGetValue("creationfadetime.value", out var creationFadeValueCtrl) && creationFadeValueCtrl is TextBox creationFadeValueTb
            ? creationFadeValueTb.Text?.Trim() ?? ""
            : "";
        var creationFadeInitAlpha = _fieldControls.TryGetValue("creationfadetime.initalpha", out var creationFadeInitAlphaCtrl) && creationFadeInitAlphaCtrl is TextBox creationFadeInitAlphaTb
            ? creationFadeInitAlphaTb.Text?.Trim() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(creationFadeValue) && !string.IsNullOrWhiteSpace(creationFadeInitAlpha))
            creationFadeValue = "0";
        if (!string.IsNullOrWhiteSpace(creationFadeValue))
        {
            var creationFadeElement = new XElement("creationfadetime", creationFadeValue);
            if (!string.IsNullOrWhiteSpace(creationFadeInitAlpha))
                creationFadeElement.SetAttributeValue("initalpha", creationFadeInitAlpha);
            unit.Add(creationFadeElement);
        }

        unit.Elements("heightbob").Remove();
        var heightBobPeriod = _fieldControls.TryGetValue("heightbob.period", out var heightBobPeriodCtrl) && heightBobPeriodCtrl is TextBox heightBobPeriodTb
            ? heightBobPeriodTb.Text?.Trim() ?? ""
            : "";
        var heightBobMagnitude = _fieldControls.TryGetValue("heightbob.magnitude", out var heightBobMagnitudeCtrl) && heightBobMagnitudeCtrl is TextBox heightBobMagnitudeTb
            ? heightBobMagnitudeTb.Text?.Trim() ?? ""
            : "";
        if (!string.IsNullOrWhiteSpace(heightBobPeriod) ||
            !string.IsNullOrWhiteSpace(heightBobMagnitude))
        {
            var heightBobElement = new XElement("heightbob");
            if (!string.IsNullOrWhiteSpace(heightBobPeriod))
                heightBobElement.SetAttributeValue("period", heightBobPeriod);
            if (!string.IsNullOrWhiteSpace(heightBobMagnitude))
                heightBobElement.SetAttributeValue("magnitude", heightBobMagnitude);
            unit.Add(heightBobElement);
        }

        unit.Elements("initialshading").Remove();
        var initialShadingType = _fieldControls.TryGetValue("initialshading.type", out var initialShadingTypeCtrl) && initialShadingTypeCtrl is AutoCompleteBox initialShadingTypeAcb
            ? initialShadingTypeAcb.Text?.Trim() ?? ""
            : "";
        var initialShadingFactor = _fieldControls.TryGetValue("initialshading.factor", out var initialShadingFactorCtrl) && initialShadingFactorCtrl is TextBox initialShadingFactorTb
            ? initialShadingFactorTb.Text?.Trim() ?? ""
            : "";
        initialShadingType = KnownInitialShadingTypes.FirstOrDefault(x => x.Equals(initialShadingType, StringComparison.OrdinalIgnoreCase)) ?? "";
        if (string.IsNullOrWhiteSpace(initialShadingFactor))
            initialShadingFactor = "1";
        if (!string.IsNullOrWhiteSpace(initialShadingType))
        {
            var initialShadingElement = new XElement("initialshading");
            initialShadingElement.SetAttributeValue("type", initialShadingType);
            initialShadingElement.SetAttributeValue("factor", initialShadingFactor);
            unit.Add(initialShadingElement);
        }

        unit.Elements("damageshading").Remove();
        var damageShadingType = _fieldControls.TryGetValue("damageshading.type", out var damageShadingTypeCtrl) && damageShadingTypeCtrl is AutoCompleteBox damageShadingTypeAcb
            ? damageShadingTypeAcb.Text?.Trim() ?? ""
            : "";
        var damageShadingThreshold = _fieldControls.TryGetValue("damageshading.threshold", out var damageShadingThresholdCtrl) && damageShadingThresholdCtrl is TextBox damageShadingThresholdTb
            ? damageShadingThresholdTb.Text?.Trim() ?? ""
            : "";
        var damageShadingRate = _fieldControls.TryGetValue("damageshading.rate", out var damageShadingRateCtrl) && damageShadingRateCtrl is TextBox damageShadingRateTb
            ? damageShadingRateTb.Text?.Trim() ?? ""
            : "";
        var damageShadingTime = _fieldControls.TryGetValue("damageshading.time", out var damageShadingTimeCtrl) && damageShadingTimeCtrl is TextBox damageShadingTimeTb
            ? damageShadingTimeTb.Text?.Trim() ?? ""
            : "";
        damageShadingType = KnownInitialShadingTypes.FirstOrDefault(x => x.Equals(damageShadingType, StringComparison.OrdinalIgnoreCase)) ?? "";
        if (string.IsNullOrWhiteSpace(damageShadingThreshold))
            damageShadingThreshold = "1";
        else if (double.TryParse(damageShadingThreshold, out var parsedDamageShadingThreshold))
        {
            if (parsedDamageShadingThreshold < 0) parsedDamageShadingThreshold = 0;
            if (parsedDamageShadingThreshold > 1) parsedDamageShadingThreshold = 1;
            damageShadingThreshold = parsedDamageShadingThreshold.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            damageShadingThreshold = "1";
        }
        if (string.IsNullOrWhiteSpace(damageShadingRate))
            damageShadingRate = "1";
        if (string.IsNullOrWhiteSpace(damageShadingTime))
            damageShadingTime = "1000";
        if (!string.IsNullOrWhiteSpace(damageShadingType))
        {
            var damageShadingElement = new XElement("damageshading");
            damageShadingElement.SetAttributeValue("type", damageShadingType);
            damageShadingElement.SetAttributeValue("threshold", damageShadingThreshold);
            damageShadingElement.SetAttributeValue("rate", damageShadingRate);
            damageShadingElement.SetAttributeValue("time", damageShadingTime);
            unit.Add(damageShadingElement);
        }

        unit.Elements("killreward").Remove();
        foreach (var resourceType in ProtoConstants.KnownResourceTypes)
        {
            if (_fieldControls.TryGetValue($"killreward:{resourceType}", out var killRewardCtrl) && killRewardCtrl is TextBox killRewardTb)
            {
                var killRewardValue = killRewardTb.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(killRewardValue))
                {
                    unit.Add(new XElement("killreward",
                        new XAttribute("resourcetype", resourceType),
                        killRewardValue));
                }
            }
        }

        unit.Elements("ResourceReturn").Remove();
        foreach (var resourceType in ProtoConstants.KnownResourceTypes)
        {
            if (_fieldControls.TryGetValue($"resourcereturn:{resourceType}", out var resourceReturnCtrl) && resourceReturnCtrl is TextBox resourceReturnTb)
            {
                var resourceReturnValue = resourceReturnTb.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(resourceReturnValue))
                {
                    unit.Add(new XElement("ResourceReturn",
                        new XAttribute("resourceType", resourceType),
                        resourceReturnValue));
                }
            }
        }

        unit.Elements("ResourceReturnRate").Remove();
        foreach (var resourceType in ProtoConstants.KnownResourceTypes)
        {
            if (_fieldControls.TryGetValue($"resourcereturnrate:{resourceType}", out var resourceReturnRateCtrl) && resourceReturnRateCtrl is TextBox resourceReturnRateTb)
            {
                var resourceReturnRateValue = resourceReturnRateTb.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(resourceReturnRateValue))
                {
                    unit.Add(new XElement("ResourceReturnRate",
                        new XAttribute("resourceType", resourceType),
                        resourceReturnRateValue));
                }
            }
        }

        if (_currentFlags != null)
        {
            var shouldReturnOnConstruction =
                (_fieldControls.TryGetValue("resourcereturn:returnonconstruction", out var returnOnConstructionCtrl) &&
                 returnOnConstructionCtrl is CheckBox returnOnConstructionCb &&
                 returnOnConstructionCb.IsChecked == true) ||
                (_fieldControls.TryGetValue("resourcereturnrate:returnonconstruction", out var returnRateOnConstructionCtrl) &&
                 returnRateOnConstructionCtrl is CheckBox returnRateOnConstructionCb &&
                 returnRateOnConstructionCb.IsChecked == true);

            if (shouldReturnOnConstruction)
                _currentFlags.Add("ReturnResourcesOnConstruction");
            else
                _currentFlags.Remove("ReturnResourcesOnConstruction");

            var shouldDisableDeleteReturn =
                (_fieldControls.TryGetValue("resourcereturn:noreturnondelete", out var noReturnOnDeleteCtrl) &&
                 noReturnOnDeleteCtrl is CheckBox noReturnOnDeleteCb &&
                 noReturnOnDeleteCb.IsChecked == true) ||
                (_fieldControls.TryGetValue("resourcereturnrate:noreturnondelete", out var noReturnRateOnDeleteCtrl) &&
                 noReturnRateOnDeleteCtrl is CheckBox noReturnRateOnDeleteCb &&
                 noReturnRateOnDeleteCb.IsChecked == true);

            if (shouldDisableDeleteReturn)
                _currentFlags.Add("DoApplyResourceReturnIfDeleted");
            else
                _currentFlags.Remove("DoApplyResourceReturnIfDeleted");

            if (_fieldControls.TryGetValue("resourcereturnrate:totalcostbased", out var totalCostBasedCtrl) &&
                totalCostBasedCtrl is CheckBox totalCostBasedCb &&
                totalCostBasedCb.IsChecked == true)
            {
                _currentFlags.Add("ResourceReturnRateTotalCost");
            }
            else
            {
                _currentFlags.Remove("ResourceReturnRateTotalCost");
            }
        }

        unit.Elements("dependentunit").Remove();
        var validDependentUnitNames = GetAvailableTrainUnitNames();
        foreach (var row in _dependentUnitRows)
        {
            var dependentUnitValue = row.ValueAcb.Text?.Trim() ?? "";
            var dependentUnitName = validDependentUnitNames.FirstOrDefault(x => x.Equals(dependentUnitValue, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (string.IsNullOrWhiteSpace(dependentUnitName))
                continue;

            var dependentUnit = new XElement("dependentunit", dependentUnitName);
            var x = row.XTb.Text?.Trim() ?? "";
            var z = row.ZTb.Text?.Trim() ?? "";
            var attachBone = row.AttachBoneTb.Text?.Trim() ?? "";

            dependentUnit.SetAttributeValue("x", string.IsNullOrWhiteSpace(x) ? "0" : x);
            dependentUnit.SetAttributeValue("z", string.IsNullOrWhiteSpace(z) ? "0" : z);
            if (!string.IsNullOrWhiteSpace(attachBone))
                dependentUnit.SetAttributeValue("attachbone", attachBone);

            unit.Add(dependentUnit);
        }

        unit.Elements("veterancyranks").Remove();
        if (_veterancyRankRows.Count > 0)
        {
            var veterancyRanks = new XElement("veterancyranks");
            foreach (var row in _veterancyRankRows)
            {
                var type = row.TypeCb.SelectedItem as string ?? row.TypeCb.SelectedValue as string ?? "";
                var value = row.ValueTb.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value))
                    continue;

                var tagName = type switch
                {
                    "NumKills" => "numkills",
                    "NumAttacks" => "numattacks",
                    "TotalDamage" => "totaldamage",
                    "DamageAndResourcesEaten" => "damageandresourceseaten",
                    _ => type.ToLowerInvariant()
                };

                veterancyRanks.Add(new XElement("rank", new XElement(tagName, value)));
            }

            if (veterancyRanks.HasElements)
                unit.Add(veterancyRanks);
        }

        unit.Elements("veterancybonus").Remove();
        if (_veterancyBonusRows.Count > 0 && _currentVeterancyIncludeTypes != null && _currentVeterancyIncludeTypes.Count > 0)
        {
            var groupedBonusRows = _veterancyBonusRows
                .Select(row => new
                {
                    RankId = row.RankIdTb.Text?.Trim() ?? "0",
                    ModifyType = ProtoConstants.GetModifyTypeValue(row.ModifyTypeAcb.Text?.Trim() ?? ""),
                    Value = row.ValueTb.Text?.Trim() ?? "",
                    DamageType = row.DamageTypeAcb.Text?.Trim() ?? ""
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.ModifyType) && !string.IsNullOrWhiteSpace(x.Value))
                .GroupBy(x => string.IsNullOrWhiteSpace(x.RankId) ? "0" : x.RankId);

            var veterancyBonus = new XElement("veterancybonus");
            foreach (var group in groupedBonusRows)
            {
                var rank = new XElement("rank");
                rank.SetAttributeValue("id", group.Key);
                foreach (var bonus in group)
                {
                    var modify = new XElement("veterancymodify", bonus.Value);
                    modify.SetAttributeValue("modifytype", bonus.ModifyType);
                    if (bonus.ModifyType is "DamageSpecific" or "ArmorSpecific")
                    {
                        var damageType = ProtoConstants.KnownDamageTypes.FirstOrDefault(x => x.Equals(bonus.DamageType, StringComparison.OrdinalIgnoreCase)) ?? "";
                        if (!string.IsNullOrWhiteSpace(damageType))
                            modify.SetAttributeValue("damagetype", damageType);
                    }
                    rank.Add(modify);
                }
                veterancyBonus.Add(rank);
            }

            veterancyBonus.Add(new XElement("includetypes",
                _currentVeterancyIncludeTypes
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new XElement("unittype", x))));

            if (_currentVeterancyExcludeTypes != null && _currentVeterancyExcludeTypes.Count > 0)
            {
                veterancyBonus.Add(new XElement("excludetypes",
                    _currentVeterancyExcludeTypes
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new XElement("unittype", x))));
            }

            if (veterancyBonus.HasElements)
                unit.Add(veterancyBonus);
        }

        unit.Elements().Where(x => x.Name.LocalName.Equals("OnDamageModifiers", StringComparison.OrdinalIgnoreCase)).Remove();
        if (_onDamageModifyRows.Count > 0)
        {
            var onDamageModifiers = new XElement("OnDamageModifiers");
            foreach (var row in _onDamageModifyRows)
            {
                var modifyType = ProtoConstants.GetModifyTypeValue(row.ModifyTypeAcb.Text?.Trim() ?? "");
                var value = row.ValueTb.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(modifyType) || string.IsNullOrWhiteSpace(value))
                    continue;

                var modify = new XElement("OnDamageModify", value);
                modify.SetAttributeValue("modifyType", modifyType);
                if (modifyType.Equals("DamageSpecific", StringComparison.OrdinalIgnoreCase) ||
                    modifyType.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase))
                {
                    var damageType = row.DamageTypeAcb.Text?.Trim() ?? "";
                    damageType = ProtoConstants.KnownDamageTypes.FirstOrDefault(x => x.Equals(damageType, StringComparison.OrdinalIgnoreCase)) ?? "";
                    if (!string.IsNullOrWhiteSpace(damageType))
                        modify.SetAttributeValue("damageType", damageType);
                }

                onDamageModifiers.Add(modify);
            }

            if (onDamageModifiers.HasElements)
                unit.Add(onDamageModifiers);
        }

        unit.Elements("spawn").Remove();
        var validSpawnProtoUnits = GetAvailableTrainUnitNames();
        foreach (var row in _spawnRows)
        {
            var spawnValue = row.ValueAcb.Text?.Trim() ?? "";
            var spawnProtoUnit = validSpawnProtoUnits.FirstOrDefault(x => x.Equals(spawnValue, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (string.IsNullOrWhiteSpace(spawnProtoUnit))
                continue;

            var spawnType = row.TypeAcb.Text?.Trim() ?? "";
            spawnType = KnownSpawnTypes.FirstOrDefault(x => x.Equals(spawnType, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (string.IsNullOrWhiteSpace(spawnType))
                continue;

            var count = row.CountTb.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(count))
                count = "1";

            var lifespan = row.LifespanTb.Text?.Trim() ?? "";
            var chance = row.ChanceTb.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(chance) && double.TryParse(chance, out var parsedSpawnChance))
            {
                if (parsedSpawnChance < 0) parsedSpawnChance = 0;
                if (parsedSpawnChance > 1) parsedSpawnChance = 1;
                chance = parsedSpawnChance.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (!string.IsNullOrWhiteSpace(chance))
            {
                chance = "1";
            }

            var delay = row.DelayTb.Text?.Trim() ?? "";
            var waterProtoUnit = row.WaterProtoUnitAcb.Text?.Trim() ?? "";
            waterProtoUnit = validSpawnProtoUnits.FirstOrDefault(x => x.Equals(waterProtoUnit, StringComparison.OrdinalIgnoreCase)) ?? "";
            var shadingType = row.ShadingTypeAcb.Text?.Trim() ?? "";
            shadingType = KnownInitialShadingTypes.FirstOrDefault(x => x.Equals(shadingType, StringComparison.OrdinalIgnoreCase)) ?? "";

            var spawn = new XElement("spawn", spawnProtoUnit);
            spawn.SetAttributeValue("type", spawnType);
            spawn.SetAttributeValue("count", count);
            if (!string.IsNullOrWhiteSpace(lifespan))
                spawn.SetAttributeValue("lifespan", lifespan);
            if (!string.IsNullOrWhiteSpace(chance))
                spawn.SetAttributeValue("chance", chance);
            if (!string.IsNullOrWhiteSpace(delay))
                spawn.SetAttributeValue("delay", delay);
            if (row.SkipPlacementCheckCb.IsChecked == true)
                spawn.SetAttributeValue("skipPlacementCheck", "");
            if (row.ControlGroupCb.IsChecked == true)
                spawn.SetAttributeValue("controlGroup", "");
            if (!string.IsNullOrWhiteSpace(waterProtoUnit))
                spawn.SetAttributeValue("waterProtoUnit", waterProtoUnit);
            if (row.SetOwnerCb.IsChecked == true)
                spawn.SetAttributeValue("setowner", "");
            if (!string.IsNullOrWhiteSpace(shadingType))
                spawn.SetAttributeValue("shadingtype", shadingType);

            unit.Add(spawn);
        }

        unit.Elements("respawntraindata").Remove();
        var respawnMode = _fieldControls.TryGetValue("respawntraindata.mode", out var respawnModeCtrl) && respawnModeCtrl is ComboBox respawnModeCb
            ? (respawnModeCb.SelectedItem as string ?? "Self Respawn")
            : "";
        if (!string.IsNullOrWhiteSpace(respawnMode))
        {
            var respawnTrainData = new XElement("respawntraindata");
            if (respawnMode.Equals("Self Respawn", StringComparison.OrdinalIgnoreCase))
            {
                var targetType = _fieldControls.TryGetValue("respawntraindata.targettype", out var targetTypeCtrl) && targetTypeCtrl is AutoCompleteBox targetTypeAcb
                    ? targetTypeAcb.Text?.Trim() ?? ""
                    : "";
                var trainProto = _fieldControls.TryGetValue("respawntraindata.trainproto", out var trainProtoCtrl) && trainProtoCtrl is AutoCompleteBox trainProtoAcb
                    ? trainProtoAcb.Text?.Trim() ?? ""
                    : "";
                var respawnTime = _fieldControls.TryGetValue("respawntraindata.respawntime", out var respawnTimeCtrl) && respawnTimeCtrl is TextBox respawnTimeTb
                    ? respawnTimeTb.Text?.Trim() ?? ""
                    : "";
                var respawnVfx = _fieldControls.TryGetValue("respawntraindata.respawnvfx", out var respawnVfxCtrl) && respawnVfxCtrl is AutoCompleteBox respawnVfxAcb
                    ? respawnVfxAcb.Text?.Trim() ?? ""
                    : "";

                targetType = GetAvailableBuildLimitTargets().FirstOrDefault(x => x.Equals(targetType, StringComparison.OrdinalIgnoreCase)) ?? "";
                trainProto = validSpawnProtoUnits.FirstOrDefault(x => x.Equals(trainProto, StringComparison.OrdinalIgnoreCase)) ?? "";
                respawnVfx = validSpawnProtoUnits.FirstOrDefault(x => x.Equals(respawnVfx, StringComparison.OrdinalIgnoreCase)) ?? "";

                if (!string.IsNullOrWhiteSpace(targetType) && !string.IsNullOrWhiteSpace(trainProto))
                {
                    respawnTrainData.Add(new XElement("targettype", targetType));
                    respawnTrainData.Add(new XElement("trainproto", trainProto));
                    if (!string.IsNullOrWhiteSpace(respawnTime))
                        respawnTrainData.Add(new XElement("respawntime", respawnTime));
                    if (!string.IsNullOrWhiteSpace(respawnVfx))
                        respawnTrainData.Add(new XElement("respawnvfx", respawnVfx));
                }
            }
            else if (respawnMode.Equals("Respawn Point", StringComparison.OrdinalIgnoreCase) &&
                     _currentRespawnTrainTypes != null &&
                     _currentRespawnTrainTypes.Count > 0)
            {
                respawnTrainData.Add(new XElement("respawntypes",
                    _currentRespawnTrainTypes
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new XElement("unittype", x))));

                if (_currentRespawnTrainExcludeTypes != null && _currentRespawnTrainExcludeTypes.Count > 0)
                {
                    respawnTrainData.Add(new XElement("excludetypes",
                        _currentRespawnTrainExcludeTypes
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .Select(x => new XElement("unittype", x))));
                }

                var respawnRates = new XElement("respawnrates");
                foreach (var resourceType in ProtoConstants.KnownResourceTypes)
                {
                    var resourceKey = resourceType.ToLowerInvariant();
                    var value = _fieldControls.TryGetValue($"respawntraindata.{resourceKey}", out var rateCtrl) && rateCtrl is TextBox rateTb
                        ? rateTb.Text?.Trim() ?? "0"
                        : "0";
                    respawnRates.Add(new XElement(resourceKey, string.IsNullOrWhiteSpace(value) ? "0" : value));
                }
                respawnTrainData.Add(respawnRates);

                var respawnLimit = _fieldControls.TryGetValue("respawntraindata.respawnlimit", out var respawnLimitCtrl) && respawnLimitCtrl is TextBox respawnLimitTb
                    ? respawnLimitTb.Text?.Trim() ?? "0"
                    : "0";
                respawnTrainData.Add(new XElement("respawnlimit", string.IsNullOrWhiteSpace(respawnLimit) ? "0" : respawnLimit));

                var respawnVfx = _fieldControls.TryGetValue("respawntraindata.respawnvfx", out var respawnPointVfxCtrl) && respawnPointVfxCtrl is AutoCompleteBox respawnPointVfxAcb
                    ? respawnPointVfxAcb.Text?.Trim() ?? ""
                    : "";
                respawnVfx = validSpawnProtoUnits.FirstOrDefault(x => x.Equals(respawnVfx, StringComparison.OrdinalIgnoreCase)) ?? "";
                if (!string.IsNullOrWhiteSpace(respawnVfx))
                    respawnTrainData.Add(new XElement("respawnvfx", respawnVfx));
            }

            if (respawnTrainData.HasElements)
                unit.Add(respawnTrainData);
        }

        void SaveOptionalSimpleField(string tag)
        {
            if (_fieldControls.TryGetValue(tag, out var ctrl))
            {
                var value = ctrl switch
                {
                    TextBox tb => tb.Text?.Trim() ?? "",
                    AutoCompleteBox acb => acb.Text?.Trim() ?? "",
                    _ => ""
                };

                if (tag.Equals("farmingnumstops", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        value = "8";
                    }
                    else if (int.TryParse(value, out var parsedStops))
                    {
                        if (parsedStops < 2)
                            parsedStops = 2;
                        value = parsedStops.ToString();
                    }
                    else
                    {
                        value = "8";
                    }
                }

                if (!string.IsNullOrWhiteSpace(value))
                    ProtoXmlHandler.SetSimpleField(unit, tag, value);
                else
                    ProtoXmlHandler.RemoveSimpleField(unit, tag);
            }
            else
            {
                ProtoXmlHandler.RemoveSimpleField(unit, tag);
            }
        }

        foreach (var tag in new[]
                 {
                     "selectionradiusx", "selectionradiusz", "placementobstructionradiusx", "placementobstructionradiusz",
                     "initialshieldpoints", "maxshieldpoints", "farmingradiusx", "farmingradiusz",
                     "farmingobstructionradiusx", "farmingobstructionradiusz", "farmingnumstops"
                 })
        {
            SaveOptionalSimpleField(tag);
        }

        var unitRegenValue = _fieldControls.TryGetValue("unitregen", out var unitRegenCtrl) && unitRegenCtrl is TextBox unitRegenTb
            ? unitRegenTb.Text?.Trim() ?? ""
            : "";
        var unitRegenIdleTimeout = _fieldControls.TryGetValue("unitregen.idleTimeout", out var unitRegenIdleCtrl) && unitRegenIdleCtrl is TextBox unitRegenIdleTb
            ? unitRegenIdleTb.Text?.Trim() ?? ""
            : "";
        var unitRegenDamageTimeout = _fieldControls.TryGetValue("unitregen.damageTimeout", out var unitRegenDamageCtrl) && unitRegenDamageCtrl is TextBox unitRegenDamageTb
            ? unitRegenDamageTb.Text?.Trim() ?? ""
            : "";
        var unitRegenCombatMultiplier = _fieldControls.TryGetValue("unitregen.combatMultiplier", out var unitRegenCombatCtrl) && unitRegenCombatCtrl is TextBox unitRegenCombatTb
            ? unitRegenCombatTb.Text?.Trim() ?? ""
            : "";
        var unitRegenRateLimit = _fieldControls.TryGetValue("unitregen.ratelimit", out var unitRegenRateLimitCtrl) && unitRegenRateLimitCtrl is TextBox unitRegenRateLimitTb
            ? unitRegenRateLimitTb.Text?.Trim() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(unitRegenValue) &&
            (!string.IsNullOrWhiteSpace(unitRegenIdleTimeout) ||
             !string.IsNullOrWhiteSpace(unitRegenDamageTimeout) ||
             !string.IsNullOrWhiteSpace(unitRegenCombatMultiplier) ||
             !string.IsNullOrWhiteSpace(unitRegenRateLimit)))
        {
            unitRegenValue = "0";
        }

        if (!string.IsNullOrWhiteSpace(unitRegenRateLimit) && double.TryParse(unitRegenRateLimit, out var parsedRateLimit))
        {
            if (parsedRateLimit < 0) parsedRateLimit = 0;
            if (parsedRateLimit > 1) parsedRateLimit = 1;
            unitRegenRateLimit = parsedRateLimit.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }

        unit.Elements("unitregen").Remove();
        if (!string.IsNullOrWhiteSpace(unitRegenValue))
        {
            var unitRegenElement = new XElement("unitregen", unitRegenValue);
            if (!string.IsNullOrWhiteSpace(unitRegenIdleTimeout))
                unitRegenElement.SetAttributeValue("idletimeout", unitRegenIdleTimeout);
            if (!string.IsNullOrWhiteSpace(unitRegenDamageTimeout))
                unitRegenElement.SetAttributeValue("damagetimeout", unitRegenDamageTimeout);
            if (!string.IsNullOrWhiteSpace(unitRegenCombatMultiplier))
                unitRegenElement.SetAttributeValue("combatmultiplier", unitRegenCombatMultiplier);
            if (!string.IsNullOrWhiteSpace(unitRegenRateLimit))
                unitRegenElement.SetAttributeValue("ratelimit", unitRegenRateLimit);

            var insertAfter = unit.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("maxhitpoints", StringComparison.OrdinalIgnoreCase));
            if (insertAfter != null)
                insertAfter.AddAfterSelf(unitRegenElement);
            else
                unit.Add(unitRegenElement);
        }

        var unitShieldRegenValue = _fieldControls.TryGetValue("unitshieldregen", out var unitShieldRegenCtrl) && unitShieldRegenCtrl is TextBox unitShieldRegenTb
            ? unitShieldRegenTb.Text?.Trim() ?? ""
            : "";
        var unitShieldRegenIdleTimeout = _fieldControls.TryGetValue("unitshieldregen.idletimeout", out var unitShieldRegenIdleCtrl) && unitShieldRegenIdleCtrl is TextBox unitShieldRegenIdleTb
            ? unitShieldRegenIdleTb.Text?.Trim() ?? ""
            : "";
        var unitShieldRegenDamageTimeout = _fieldControls.TryGetValue("unitshieldregen.damagetimeout", out var unitShieldRegenDamageCtrl) && unitShieldRegenDamageCtrl is TextBox unitShieldRegenDamageTb
            ? unitShieldRegenDamageTb.Text?.Trim() ?? ""
            : "";
        var unitShieldRegenCombatMultiplier = _fieldControls.TryGetValue("unitshieldregen.combatmultiplier", out var unitShieldRegenCombatCtrl) && unitShieldRegenCombatCtrl is TextBox unitShieldRegenCombatTb
            ? unitShieldRegenCombatTb.Text?.Trim() ?? ""
            : "";
        var unitShieldRegenRateLimit = _fieldControls.TryGetValue("unitshieldregen.ratelimit", out var unitShieldRegenRateLimitCtrl) && unitShieldRegenRateLimitCtrl is TextBox unitShieldRegenRateLimitTb
            ? unitShieldRegenRateLimitTb.Text?.Trim() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(unitShieldRegenValue) &&
            (!string.IsNullOrWhiteSpace(unitShieldRegenIdleTimeout) ||
             !string.IsNullOrWhiteSpace(unitShieldRegenDamageTimeout) ||
             !string.IsNullOrWhiteSpace(unitShieldRegenCombatMultiplier) ||
             !string.IsNullOrWhiteSpace(unitShieldRegenRateLimit)))
        {
            unitShieldRegenValue = "0";
        }

        if (!string.IsNullOrWhiteSpace(unitShieldRegenRateLimit) && double.TryParse(unitShieldRegenRateLimit, out var parsedShieldRateLimit))
        {
            if (parsedShieldRateLimit < 0) parsedShieldRateLimit = 0;
            if (parsedShieldRateLimit > 1) parsedShieldRateLimit = 1;
            unitShieldRegenRateLimit = parsedShieldRateLimit.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }

        unit.Elements("unitshieldregen").Remove();
        if (!string.IsNullOrWhiteSpace(unitShieldRegenValue))
        {
            var unitShieldRegenElement = new XElement("unitshieldregen", unitShieldRegenValue);
            if (!string.IsNullOrWhiteSpace(unitShieldRegenIdleTimeout))
                unitShieldRegenElement.SetAttributeValue("idletimeout", unitShieldRegenIdleTimeout);
            if (!string.IsNullOrWhiteSpace(unitShieldRegenDamageTimeout))
                unitShieldRegenElement.SetAttributeValue("damagetimeout", unitShieldRegenDamageTimeout);
            if (!string.IsNullOrWhiteSpace(unitShieldRegenCombatMultiplier))
                unitShieldRegenElement.SetAttributeValue("combatmultiplier", unitShieldRegenCombatMultiplier);
            if (!string.IsNullOrWhiteSpace(unitShieldRegenRateLimit))
                unitShieldRegenElement.SetAttributeValue("ratelimit", unitShieldRegenRateLimit);

            var insertAfterShield = unit.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("initialshieldpoints", StringComparison.OrdinalIgnoreCase))
                ?? unit.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("maxshieldpoints", StringComparison.OrdinalIgnoreCase))
                ?? unit.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("unitregen", StringComparison.OrdinalIgnoreCase))
                ?? unit.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("maxhitpoints", StringComparison.OrdinalIgnoreCase));
            if (insertAfterShield != null)
                insertAfterShield.AddAfterSelf(unitShieldRegenElement);
            else
                unit.Add(unitShieldRegenElement);
        }

        void SaveResourceMap(string elementName)
        {
            unit.Elements(elementName).Remove();
            foreach (var resourceType in ProtoConstants.KnownResourceTypes)
            {
                if (_fieldControls.TryGetValue($"{elementName}:{resourceType}", out var ctrl) && ctrl is TextBox tb)
                {
                    var value = tb.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var element = new XElement(elementName, value);
                        element.SetAttributeValue("resourcetype", resourceType);
                        if (elementName.Equals("carrycapacity", StringComparison.OrdinalIgnoreCase) &&
                            _fieldControls.TryGetValue($"{elementName}:dropoffmultiplier:{resourceType}", out var multiplierCtrl) &&
                            multiplierCtrl is TextBox multiplierTb)
                        {
                            var multiplier = multiplierTb.Text?.Trim() ?? "";
                            if (!string.IsNullOrWhiteSpace(multiplier))
                                element.SetAttributeValue("dropoffmultiplier", multiplier);
                        }
                        unit.Add(element);
                    }
                }
            }
        }

        SaveResourceMap("carrycapacity");
        SaveResourceMap("initialresource");
        unit.Elements("resourceconversion").Remove();
        foreach (var row in _resourceConversionRows)
        {
            var fromResource = row.FromResourceCb.SelectedItem as string ?? row.FromResourceCb.SelectedValue as string ?? "";
            var toResource = row.ToResourceCb.SelectedItem as string ?? row.ToResourceCb.SelectedValue as string ?? "";
            var value = row.ValueTb.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(fromResource) || string.IsNullOrWhiteSpace(toResource) || string.IsNullOrWhiteSpace(value))
                continue;

            unit.Add(new XElement("resourceconversion",
                new XAttribute("fromresourcetype", fromResource),
                new XAttribute("toresourcetype", toResource),
                value));
        }

        void SaveUnitTypeListElement(string elementName, HashSet<string>? values)
        {
            unit.Element(elementName)?.Remove();
            if (values == null || values.Count == 0)
                return;

            unit.Add(new XElement(elementName, values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => new XElement("unittype", x))));
        }

        SaveUnitTypeListElement("sharedselectionunittypes", _currentSharedSelectionUnitTypes);

        if (_fieldControls.TryGetValue("decay.delay", out var decayDelayCtrl) && decayDelayCtrl is TextBox decayDelayTb &&
            _fieldControls.TryGetValue("decay.duration", out var decayDurationCtrl) && decayDurationCtrl is TextBox decayDurationTb)
        {
            var delay = decayDelayTb.Text?.Trim() ?? "";
            var duration = decayDurationTb.Text?.Trim() ?? "";
            unit.Element("decay")?.Remove();
            if (!string.IsNullOrWhiteSpace(delay) || !string.IsNullOrWhiteSpace(duration))
            {
                var decayElement = new XElement("decay");
                if (!string.IsNullOrWhiteSpace(delay))
                    decayElement.SetAttributeValue("delay", delay);
                if (!string.IsNullOrWhiteSpace(duration))
                    decayElement.SetAttributeValue("duration", duration);
                unit.Add(decayElement);
            }
        }
        else
        {
            unit.Element("decay")?.Remove();
        }

        unit.Element("rechargetime")?.Remove();
        unit.Element("recharge")?.Remove();
        unit.Element("rechargeincludetypes")?.Remove();
        unit.Element("rechargeexcludetypes")?.Remove();
        if (_fieldControls.TryGetValue("recharge.mode", out var rechargeModeCtrl) && rechargeModeCtrl is ComboBox rechargeModeCb)
        {
            var rechargeMode = rechargeModeCb.SelectedItem as string ?? "Time";
            var rechargeValue = _fieldControls.TryGetValue("recharge.value", out var rechargeValueCtrl) && rechargeValueCtrl is TextBox rechargeValueTb
                ? rechargeValueTb.Text?.Trim() ?? ""
                : "";
            var rechargeInit = _fieldControls.TryGetValue("recharge.init", out var rechargeInitCtrl) && rechargeInitCtrl is TextBox rechargeInitTb
                ? rechargeInitTb.Text?.Trim() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(rechargeInit))
                rechargeInit = "1";

            if (rechargeMode.Equals("Time", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(rechargeValue))
                {
                    var rechargeTimeElement = new XElement("rechargetime", rechargeValue);
                    if (!string.IsNullOrWhiteSpace(rechargeInit))
                        rechargeTimeElement.SetAttributeValue("init", rechargeInit);
                    unit.Add(rechargeTimeElement);
                }
            }
            else
            {
                var validRechargeType = KnownRechargeTypes.FirstOrDefault(x => x.Equals(rechargeMode, StringComparison.OrdinalIgnoreCase)) ?? "";
                if (!string.IsNullOrWhiteSpace(rechargeValue) && !string.IsNullOrWhiteSpace(validRechargeType))
                {
                    var rechargeElement = new XElement("recharge", rechargeValue);
                    rechargeElement.SetAttributeValue("type", validRechargeType);
                    if (!string.IsNullOrWhiteSpace(rechargeInit))
                        rechargeElement.SetAttributeValue("init", rechargeInit);
                    unit.Add(rechargeElement);

                    SaveUnitTypeListElement("rechargeincludetypes", _currentRechargeIncludeTypes);
                    SaveUnitTypeListElement("rechargeexcludetypes", _currentRechargeExcludeTypes);
                }
            }
        }

        unit.Element("auxrechargetime")?.Remove();
        unit.Element("auxrecharge")?.Remove();
        unit.Element("auxrechargeincludetypes")?.Remove();
        unit.Element("auxrechargeexcludetypes")?.Remove();
        if (_fieldControls.TryGetValue("auxrecharge.mode", out var auxRechargeModeCtrl) && auxRechargeModeCtrl is ComboBox auxRechargeModeCb)
        {
            var auxRechargeMode = auxRechargeModeCb.SelectedItem as string ?? "Time";
            var auxRechargeValue = _fieldControls.TryGetValue("auxrecharge.value", out var auxRechargeValueCtrl) && auxRechargeValueCtrl is TextBox auxRechargeValueTb
                ? auxRechargeValueTb.Text?.Trim() ?? ""
                : "";
            var auxRechargeInit = _fieldControls.TryGetValue("auxrecharge.init", out var auxRechargeInitCtrl) && auxRechargeInitCtrl is TextBox auxRechargeInitTb
                ? auxRechargeInitTb.Text?.Trim() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(auxRechargeInit))
                auxRechargeInit = "1";

            if (auxRechargeMode.Equals("Time", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(auxRechargeValue))
                {
                    var auxRechargeTimeElement = new XElement("auxrechargetime", auxRechargeValue);
                    if (!string.IsNullOrWhiteSpace(auxRechargeInit))
                        auxRechargeTimeElement.SetAttributeValue("init", auxRechargeInit);
                    unit.Add(auxRechargeTimeElement);
                }
            }
            else
            {
                var validAuxRechargeType = KnownRechargeTypes.FirstOrDefault(x => x.Equals(auxRechargeMode, StringComparison.OrdinalIgnoreCase)) ?? "";
                if (!string.IsNullOrWhiteSpace(auxRechargeValue) && !string.IsNullOrWhiteSpace(validAuxRechargeType))
                {
                    var auxRechargeElement = new XElement("auxrecharge", auxRechargeValue);
                    auxRechargeElement.SetAttributeValue("type", validAuxRechargeType);
                    if (!string.IsNullOrWhiteSpace(auxRechargeInit))
                        auxRechargeElement.SetAttributeValue("init", auxRechargeInit);
                    unit.Add(auxRechargeElement);

                    SaveUnitTypeListElement("auxrechargeincludetypes", _currentAuxRechargeIncludeTypes);
                    SaveUnitTypeListElement("auxrechargeexcludetypes", _currentAuxRechargeExcludeTypes);
                }
            }
        }

        if (_fieldControls.TryGetValue("minimapcolor.red", out var redCtrl) && redCtrl is TextBox redTb)
        {
            unit.Element("minimapcolor")?.Remove();
            var red = redTb.Text?.Trim() ?? "";
            var green = _fieldControls.TryGetValue("minimapcolor.green", out var greenCtrl) && greenCtrl is TextBox greenTb
                ? greenTb.Text?.Trim() ?? ""
                : "";
            var blue = _fieldControls.TryGetValue("minimapcolor.blue", out var blueCtrl) && blueCtrl is TextBox blueTb
                ? blueTb.Text?.Trim() ?? ""
                : "";
            if (!string.IsNullOrWhiteSpace(red) || !string.IsNullOrWhiteSpace(green) || !string.IsNullOrWhiteSpace(blue))
            {
                var minimapColor = new XElement("minimapcolor");
                if (!string.IsNullOrWhiteSpace(red))
                    minimapColor.SetAttributeValue("red", red);
                if (!string.IsNullOrWhiteSpace(green))
                    minimapColor.SetAttributeValue("green", green);
                if (!string.IsNullOrWhiteSpace(blue))
                    minimapColor.SetAttributeValue("blue", blue);
                unit.Add(minimapColor);
            }
        }
        else
        {
            unit.Element("minimapcolor")?.Remove();
        }

        if (_fieldControls.TryGetValue("minimapicon", out var minimapIconCtrl) && minimapIconCtrl is AutoCompleteBox minimapIconAcb)
        {
            var minimapIcon = minimapIconAcb.Text?.Trim() ?? "";
            minimapIcon = GetKnownMinimapIcons().FirstOrDefault(x => x.Equals(minimapIcon, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (!string.IsNullOrWhiteSpace(minimapIcon))
                ProtoXmlHandler.SetSimpleField(unit, "minimapicon", minimapIcon);
            else
                ProtoXmlHandler.RemoveSimpleField(unit, "minimapicon");
        }
        else
        {
            ProtoXmlHandler.RemoveSimpleField(unit, "minimapicon");
        }

        if (_fieldControls.TryGetValue("minimapsize", out var minimapSizeCtrl) && minimapSizeCtrl is TextBox minimapSizeTb)
        {
            var minimapSize = minimapSizeTb.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(minimapSize))
                ProtoXmlHandler.SetSimpleField(unit, "minimapsize", minimapSize);
            else
                ProtoXmlHandler.RemoveSimpleField(unit, "minimapsize");
        }
        else
        {
            ProtoXmlHandler.RemoveSimpleField(unit, "minimapsize");
        }

        if (_fieldControls.TryGetValue("replacement.value", out var replacementValueCtrl) && replacementValueCtrl is AutoCompleteBox replacementValueAcb)
        {
            unit.Element("replacement")?.Remove();
            var value = replacementValueAcb.Text?.Trim() ?? "";
            var type = _fieldControls.TryGetValue("replacement.type", out var replacementTypeCtrl) && replacementTypeCtrl is AutoCompleteBox replacementTypeAcb
                ? replacementTypeAcb.Text?.Trim() ?? ""
                : "";
            type = KnownReplacementTypes.FirstOrDefault(x => x.Equals(type, StringComparison.OrdinalIgnoreCase)) ?? "";
            value = GetAvailableTrainUnitNames().FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "";
            var lifespan = _fieldControls.TryGetValue("replacement.lifespan", out var replacementLifespanCtrl) && replacementLifespanCtrl is TextBox replacementLifespanTb
                ? replacementLifespanTb.Text?.Trim() ?? ""
                : "";
            if (!string.IsNullOrWhiteSpace(value))
            {
                var replacement = new XElement("replacement", value);
                if (!string.IsNullOrWhiteSpace(type))
                    replacement.SetAttributeValue("type", type);
                if (!string.IsNullOrWhiteSpace(lifespan))
                    replacement.SetAttributeValue("lifespan", lifespan);
                unit.Add(replacement);
            }
        }
        else
        {
            unit.Element("replacement")?.Remove();
        }

        foreach (var tag in CultureAwareSimpleFieldTags)
        {
            var entries = new List<ProtoCultureFieldEntry>();

            if (_fieldControls.TryGetValue(tag, out var ctrl) && ctrl is AutoCompleteBox defaultAcb)
            {
                var defaultValue = defaultAcb.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(defaultValue))
                    entries.Add(new ProtoCultureFieldEntry { Value = defaultValue });
            }

            if (_cultureFieldRows.TryGetValue(tag, out var rows))
            {
                foreach (var row in rows)
                {
                    var cultureLabel = row.CultureCb.SelectedItem as string ?? row.CultureCb.SelectedValue as string ?? "";
                    var cultureValue = GetCultureValue(cultureLabel);
                    var value = row.ValueAcb.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(cultureValue) && !string.IsNullOrWhiteSpace(value))
                    {
                        entries.Add(new ProtoCultureFieldEntry
                        {
                            Culture = cultureValue,
                            Value = value
                        });
                    }
                }
            }

            ProtoXmlHandler.SetCultureAwareSimpleFields(
                unit,
                tag,
                entries
                    .GroupBy(x => x.Culture, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First()));
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

        if (_fieldControls.TryGetValue("transformcommand", out var transformCtrl) && transformCtrl is AutoCompleteBox transformAcb)
        {
            var input = transformAcb.Text?.Trim() ?? "";
            var match = GetAvailableCommandNames().FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (!string.IsNullOrWhiteSpace(match))
                ProtoXmlHandler.SetSimpleField(unit, "transformcommand", match);
            else
                ProtoXmlHandler.RemoveSimpleField(unit, "transformcommand");
        }
        else
        {
            ProtoXmlHandler.RemoveSimpleField(unit, "transformcommand");
        }

        if (_fieldControls.TryGetValue("maxcontained", out var maxContainedCtrl) && maxContainedCtrl is TextBox maxContainedTb)
        {
            var maxContained = maxContainedTb.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(maxContained))
                ProtoXmlHandler.SetSimpleField(unit, "maxcontained", maxContained);
            else
                ProtoXmlHandler.RemoveSimpleField(unit, "maxcontained");

            ProtoXmlHandler.SetContainList(
                unit,
                _currentContains != null
                    ? _currentContains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    : Enumerable.Empty<string>());
            ProtoXmlHandler.SetNotContainList(
                unit,
                _currentNotContains != null
                    ? _currentNotContains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    : Enumerable.Empty<string>());

            void SaveContainBonusField(string tag)
            {
                if (_fieldControls.TryGetValue(tag, out var ctrl) && ctrl is TextBox tb)
                {
                    var value = tb.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(value))
                        ProtoXmlHandler.SetSimpleField(unit, tag, value);
                    else
                        ProtoXmlHandler.RemoveSimpleField(unit, tag);
                }
                else
                {
                    ProtoXmlHandler.RemoveSimpleField(unit, tag);
                }
            }

            SaveContainBonusField("containedhitpointbonus");
            SaveContainBonusField("containedspeedbonus");
            SaveContainBonusField("containedregenrate");
        }
        else
        {
            ProtoXmlHandler.RemoveSimpleField(unit, "maxcontained");
            ProtoXmlHandler.SetContainList(unit, []);
            ProtoXmlHandler.SetNotContainList(unit, []);
            ProtoXmlHandler.RemoveSimpleField(unit, "containedhitpointbonus");
            ProtoXmlHandler.RemoveSimpleField(unit, "containedspeedbonus");
            ProtoXmlHandler.RemoveSimpleField(unit, "containedregenrate");
        }

        if (_fieldControls.TryGetValue("buildlimit", out var blCtrl) && blCtrl is TextBox blTb)
        {
            string val = blTb.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(val))
            {
                ProtoXmlHandler.SetSimpleField(unit, "buildlimit", val);
                var validBuildLimitTargets = GetAvailableBuildLimitTargets();
                var normalizedBuildLimitEntries = _buildLimitRows
                    .Select(row =>
                    {
                        var input = row.ValueAcb.Text?.Trim() ?? "";
                        var value = validBuildLimitTargets.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase)) ?? "";
                        var weight = row.WeightTb?.Text?.Trim() ?? "";
                        if (row.WeightTb != null)
                        {
                            if (string.IsNullOrWhiteSpace(weight))
                            {
                                weight = "1";
                            }
                            else if (double.TryParse(weight, out double parsedWeight) && parsedWeight < 0)
                            {
                                weight = "0";
                            }
                        }

                        return new ProtoBuildLimitEntry
                        {
                            Value = value,
                            Weight = weight
                        };
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                    .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                switch (_currentBuildLimitMode)
                {
                    case BuildLimitMode.Dynamic:
                        ProtoXmlHandler.SetDynamicBuildLimitUnitTypes(unit, normalizedBuildLimitEntries.Select(x => x.Value));
                        ProtoXmlHandler.SetSharedBuildLimitEntries(unit, []);
                        _currentFlags?.Remove("UseSharedBuildLimit");
                        break;
                    case BuildLimitMode.Shared:
                        ProtoXmlHandler.SetDynamicBuildLimitUnitTypes(unit, []);
                        ProtoXmlHandler.SetSharedBuildLimitEntries(unit, normalizedBuildLimitEntries);
                        _currentFlags?.Add("UseSharedBuildLimit");
                        break;
                    default:
                        ProtoXmlHandler.RemoveBuildLimitModeElements(unit);
                        _currentFlags?.Remove("UseSharedBuildLimit");
                        break;
                }
            }
            else
            {
                ProtoXmlHandler.RemoveSimpleField(unit, "buildlimit");
                ProtoXmlHandler.RemoveBuildLimitModeElements(unit);
                _currentFlags?.Remove("UseSharedBuildLimit");
            }
        }
        else
        {
            ProtoXmlHandler.RemoveSimpleField(unit, "buildlimit");
            ProtoXmlHandler.RemoveBuildLimitModeElements(unit);
            _currentFlags?.Remove("UseSharedBuildLimit");
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
        ProtoXmlHandler.SetCommandEntries(unit, CollectValidCommandEntries(_unitCommandRows, GetAvailableCommandNames()));

        var actionsList = new List<ProtoAction>();
        foreach (var pw in _protoActionWidgets)
        {
            var pa = new ProtoAction
            {
                Name = pw.NameAcb.Text?.Trim() ?? "",
                Type = TryResolveProtoActionType(pw.NameAcb.Text?.Trim() ?? "", out var resolvedType)
                    ? resolvedType
                    : GetExactProtoActionTypeMatch(pw.TypeAcb.Text),
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
            InvalidateSuggestionCaches(includeTechNames: true);
            InvalidateModStringEntriesCache();
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
                InvalidateSuggestionCaches(includeTechNames: true);
                InvalidateModStringEntriesCache();
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

        if (string.Equals(_cachedModStringEntriesPath, path, StringComparison.OrdinalIgnoreCase) &&
            _cachedModStringEntries != null)
        {
            return _cachedModStringEntries;
        }

        try
        {
            var parsed = StringTableParser.Parse(File.ReadAllText(path));
            _cachedModStringEntriesPath = path;
            _cachedModStringEntries = parsed;
            return parsed;
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
        _cachedModStringEntriesPath = path;
        _cachedModStringEntries = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
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
        {
            File.WriteAllText(path, "Language = \"English\"\n\n");
            InvalidateModStringEntriesCache();
        }
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

    private HashSet<string> CollectUnitStringIdsForRemoval(string unitName, XElement? unit)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in StringBackedFieldTags)
        {
            var unitId = unit != null ? ProtoXmlHandler.GetSimpleField(unit, tag) : null;
            if (!string.IsNullOrWhiteSpace(unitId))
                ids.Add(unitId);

            if (_currentStringFieldIds.TryGetValue(tag, out var currentId) && !string.IsNullOrWhiteSpace(currentId))
                ids.Add(currentId);

            ids.Add(BuildStringIdForUnit(unitName, GetStringSuffixForField(tag)));
        }

        return ids;
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
        InvalidateSuggestionCaches(includeTechNames: true);
        InvalidateModStringEntriesCache();

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
        InvalidateSuggestionCaches();
        try
        {
            SaveCurrentUnitStringValues();
            ProtoXmlHandler.SaveProtoXml(_modXmlDoc, _modFilePath);
            _isDirty = false;
            _fileLabel.Text = _modFilePath;
            _statusMessage.Text = "Saved successfully.";
            if (!string.IsNullOrWhiteSpace(_currentUnitName))
                BuildEditorPanel(_currentUnitName, resetScroll: false);
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
            InvalidateSuggestionCaches(includeTechNames: true);
            InvalidateModStringEntriesCache();

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
                if (!string.IsNullOrWhiteSpace(_currentUnitName))
                    BuildEditorPanel(_currentUnitName, resetScroll: false);
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
                var typeWindow = new NewUnitTypeWindow();
                await typeWindow.ShowDialog(this);
                if (typeWindow.SelectedType == null)
                    return;

                var selectedKind = typeWindow.SelectedType switch
                {
                    "Building" => NewCustomUnitKind.Building,
                    "Other" => NewCustomUnitKind.Other,
                    _ => NewCustomUnitKind.Unit
                };
                var newUnit = ProtoXmlHandler.AddNewUnit(_modXmlRoot, name);

                switch (selectedKind)
                {
                    case NewCustomUnitKind.Unit:
                        newUnit.Add(new XElement("unittype", "Unit"));
                        break;
                    case NewCustomUnitKind.Building:
                        newUnit.Add(new XElement("unittype", "Building"));
                        ProtoXmlHandler.SetSimpleField(newUnit, "buildpoints", "0");
                        break;
                    case NewCustomUnitKind.Other:
                        break;
                }

                ProtoXmlHandler.SetCostEntries(newUnit, ProtoConstants.KnownResourceTypes.Select(r => (r, "0")));
                ProtoXmlHandler.SetArmorEntries(newUnit, ProtoConstants.KnownArmorTypes.Select(a => (a, "0")));
                AssignGeneratedStringIds(name);
                InitializeUnitStringValues(name, name, "", "");
            }

            MarkDirty();
            InvalidateSuggestionCaches();
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
            var unitToDelete = ProtoXmlHandler.GetUnitElement(_modXmlRoot, _currentUnitName);
            RemoveStringEntries(CollectUnitStringIdsForRemoval(_currentUnitName, unitToDelete));
            ProtoXmlHandler.DeleteUnit(_modXmlRoot, _currentUnitName);
            _currentUnitName = null;
            MarkDirty();
            InvalidateSuggestionCaches();
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
