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
    private List<string>? _cachedTechNames;
    private List<string>? _cachedCommandNames;
    private List<string>? _cachedResourceSubtypeNames;
    private List<string>? _cachedPlacementFileNames;
    private List<string>? _cachedPathabilityFlags;
    private List<string>? _cachedHotkeyContexts;
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
    private BuildLimitMode _currentBuildLimitMode = BuildLimitMode.Standard;

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
                _cachedCommandNames = null;
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
            _cachedCommandNames = null;
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
            Text = $"\u2500\u2500\u2500\u2500 {title} \u2500\u2500\u2500\u2500",
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
    private static bool IsCultureAwareSimpleField(string tag) => CultureAwareSimpleFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] TrainTechRowOptions = ["0", "1", "2", "3"];
    private static readonly string[] TrainTechColumnOptions = ["0", "1", "2", "3", "4", "5"];
    private static readonly string[] SupportedCultureLabels = SupportedCultures.Select(x => x.Label).ToArray();
    private static readonly string[] OtherSpecificAttributeChoiceKeys =
    [
        "selectionradius",
        "autoattackrange",
        "lifespan",
        "rechargetime",
        "auxrechargetime",
        "resourcesubtype",
        "minimapicon",
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
        "stealth",
        "selfdestructprotoaction",
        "pathabilityflags",
        "heightbob",
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
        "decaytime",
        "decaydelaytime",
        "researchrate",
        "killreward",
        "autobuildrate",
        "godpowerblockradius",
        "godpowercostfactor",
        "builderlimit",
        "corpsedecaydelay",
        "costescalation",
        "damageshading",
        "initialshading",
        "minimapsize",
        "deadreplacement",
        "deadtransform",
        "eidolonprotoid",
        "enemyshortrollovertextid",
        "socketunittype",
        "disguiseprotoid",
        "stackprotoaction",
        "dodgechance",
        "directionalarmor",
        "placementobstruction",
        "farming",
        "carrycapacity",
        "initialresource",
        "resourceconversion",
        "sharedselectionunittypes",
        "rechargeincludetypes",
        "rechargeexcludetypes",
        "decay",
        "recharge",
        "minimapcolor",
        "replacement",
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
        ["rechargetime"] = "Recharge Time",
        ["auxrechargetime"] = "Aux Recharge Time",
        ["resourcesubtype"] = "Resource Subtype",
        ["minimapicon"] = "Minimap Icon",
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
        ["heightbob"] = "Height Bob",
        ["birthprotoaction"] = "Birth Proto Action",
        ["placementfile"] = "Placement File",
        ["ondiscoverlos"] = "On Discover LOS",
        ["populationcapaddition"] = "Population Cap Addition",
        ["gathererlimit"] = "Gatherer Limit",
        ["creationfadetime"] = "Creation Fade Time",
        ["prioritybonusfactor"] = "Priority Bonus Factor",
        ["buildingworkrate"] = "Building Work Rate",
        ["trainingrate"] = "Training Rate",
        ["gatherratemultiplier"] = "Gather Rate Multiplier",
        ["partisans"] = "Partisans",
        ["decaytime"] = "Decay Time",
        ["decaydelaytime"] = "Decay Delay Time",
        ["researchrate"] = "Research Rate",
        ["killreward"] = "Kill Reward",
        ["autobuildrate"] = "Auto Build Rate",
        ["godpowerblockradius"] = "God Power Block Radius",
        ["godpowercostfactor"] = "God Power Cost Factor",
        ["builderlimit"] = "Builder Limit",
        ["corpsedecaydelay"] = "Corpse Decay Delay",
        ["costescalation"] = "Cost Escalation",
        ["damageshading"] = "Damage Shading",
        ["initialshading"] = "Initial Shading",
        ["minimapsize"] = "Minimap Size",
        ["deadreplacement"] = "Dead Replacement",
        ["deadtransform"] = "Dead Transform",
        ["eidolonprotoid"] = "Eidolon Proto ID",
        ["enemyshortrollovertextid"] = "Enemy Short Rollover Text ID",
        ["socketunittype"] = "Socket Unit Type",
        ["disguiseprotoid"] = "Disguise Proto ID",
        ["stackprotoaction"] = "Stack Proto Action",
        ["dodgechance"] = "Dodge Chance",
        ["directionalarmor"] = "Directional Armor",
        ["placementobstruction"] = "Placement Obstruction",
        ["farming"] = "Farming Data",
        ["carrycapacity"] = "Carry Capacity",
        ["initialresource"] = "Initial Resource",
        ["resourceconversion"] = "Resource Conversion",
        ["sharedselectionunittypes"] = "Shared Selection Unit Types",
        ["rechargeincludetypes"] = "Recharge Include Types",
        ["rechargeexcludetypes"] = "Recharge Exclude Types",
        ["decay"] = "Decay",
        ["recharge"] = "Recharge",
        ["minimapcolor"] = "Minimap Color",
        ["replacement"] = "Replacement",
    };

    private static readonly HashSet<string> OtherSpecificSimpleNumberTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "autoattackrange", "lifespan", "rechargetime", "auxrechargetime", "resourcepriority", "resourcedecay",
        "wanderdistance", "workersoftlimit", "formationorder", "screenshakeondestruction", "stealthdetectionradius", "displayedrange",
        "aistancebasedistance", "heighthitpointbaroffset", "allowedheightvariance", "stealthrevealselfradius",
        "stealthshowsilhouetteradius", "heightbob", "populationcapaddition", "gathererlimit", "creationfadetime",
        "prioritybonusfactor", "buildingworkrate", "trainingrate", "gatherratemultiplier", "partisancount", "decaytime",
        "decaydelaytime", "researchrate", "projectilespinperiod", "killreward", "autobuildrate",
        "godpowerblockradius", "godpowercostfactor", "builderlimit", "corpsedecaydelay", "costescalation",
        "damageshading", "initialshading", "minimapsize", "dodgechance"
    };

    private static readonly HashSet<string> OtherSpecificSimpleSuggestionTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "culture", "resourcesubtype", "socketunittype", "disguiseprotoid", "deadreplacement", "deadtransform",
        "initialunitaistance", "pathabilityflags", "placementfile", "hotkeycontext", "allyhotkeycontext", "partisantype",
        "eidolonprotoid", "selfdestructprotoaction", "birthprotoaction", "stackprotoaction"
    };

    private static readonly HashSet<string> OtherSpecificSimpleTextTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "minimapicon",
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
        "projectilespinperiod"
    };

    private static string GetOtherSpecificAttributeLabel(string key)
        => OtherSpecificAttributeLabels.TryGetValue(key, out var label) ? label : key;

    private static bool IsOtherSpecificAttributeVisible(string key, Dictionary<string, Control> containers)
        => containers.ContainsKey(key);

    private static string? GetFlagForOtherSpecificAttribute(string tag) => tag.ToLowerInvariant() switch
    {
        "displayedrange" => "DisplayRange",
        "dodgechance" => "CanDodgeAttacks",
        _ => null
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

    private List<string> GetAvailableCommandNames()
    {
        if (_cachedCommandNames != null)
            return _cachedCommandNames;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void CollectFromRoot(XElement? root)
        {
            if (root == null)
                return;

            foreach (var unitName in ProtoXmlHandler.GetUnitNames(root))
            {
                var unit = ProtoXmlHandler.GetUnitElement(root, unitName);
                if (unit == null)
                    continue;

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
        var values = new List<string>();
        if (_barData != null)
        {
            values.AddRange(_barData.UnitTypes);
            values.AddRange(_barData.UnitNames);
        }

        values.AddRange(ProtoConstants.KnownUnitTypes);

        if (_modXmlRoot != null)
            values.AddRange(ProtoXmlHandler.GetUnitNames(_modXmlRoot));

        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private void BuildEditorPanel(string unitName)
    {
        _isPopulating = true;
        _editorPanel.Children.Clear();
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
                field.Tag.Equals("tactics", StringComparison.OrdinalIgnoreCase) ||
                field.Tag.Equals("maxcontained", StringComparison.OrdinalIgnoreCase))
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
                    ColumnDefinitions = new ColumnDefinitions("Auto, 140, Auto, 140")
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

                Grid.SetColumn(obstructionGrid, 1);
                Grid.SetRow(obstructionGrid, gridRow);
                propertiesGrid.Children.Add(obstructionGrid);

                _fieldControls["obstructionradiusx"] = obstructionXTb;
                _fieldControls["obstructionradiusz"] = obstructionZTb;
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

        AutoCompleteBox CreateOtherSuggestionBox(string initialValue, IEnumerable<string> suggestions, string? placeholder = null)
        {
            var acb = new AutoCompleteBox
            {
                Text = initialValue,
                PlaceholderText = placeholder,
                FilterMode = AutoCompleteFilterMode.Contains,
                ItemsSource = suggestions.ToList(),
                IsEnabled = !_isReadOnly
            };
            EnableDropdownAutoComplete(acb);
            acb.TextChanged += async (s, e) => await HandleOtherFieldChangedAsync();
            return acb;
        }

        AutoCompleteBox CreateValidatedOtherSuggestionBox(string initialValue, IEnumerable<string> suggestions, string? placeholder = null, bool allowCustom = false)
        {
            var suggestionList = suggestions
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
                    "resourcesubtype" or "initialunitaistance" or "pathabilityflags" or "hotkeycontext" or "allyhotkeycontext" or "partisantype"
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

            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
            stack.Children.Add(new TextBlock { Text = "Farming Data", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 2) });

            var topRow = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 110, Auto, 110") };
            topRow.Children.Add(new TextBlock { Text = "Radius", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            topRow.Children.Add(new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            Grid.SetColumn(topRow.Children[^1], 1);
            var radiusX = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingradiusx") ?? "0", true);
            Grid.SetColumn(radiusX, 2);
            topRow.Children.Add(radiusX);
            _fieldControls["farmingradiusx"] = radiusX;
            topRow.Children.Add(new TextBlock { Text = "Z", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) });
            Grid.SetColumn(topRow.Children[^1], 3);
            var radiusZ = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingradiusz") ?? "0", true);
            Grid.SetColumn(radiusZ, 4);
            topRow.Children.Add(radiusZ);
            _fieldControls["farmingradiusz"] = radiusZ;
            stack.Children.Add(topRow);

            var secondRow = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 110, Auto, 110") };
            secondRow.Children.Add(new TextBlock { Text = "Obstruction", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            secondRow.Children.Add(new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            Grid.SetColumn(secondRow.Children[^1], 1);
            var obstructionX = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingobstructionradiusx") ?? "0", true);
            Grid.SetColumn(obstructionX, 2);
            secondRow.Children.Add(obstructionX);
            _fieldControls["farmingobstructionradiusx"] = obstructionX;
            secondRow.Children.Add(new TextBlock { Text = "Z", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) });
            Grid.SetColumn(secondRow.Children[^1], 3);
            var obstructionZ = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingobstructionradiusz") ?? "0", true);
            Grid.SetColumn(obstructionZ, 4);
            secondRow.Children.Add(obstructionZ);
            _fieldControls["farmingobstructionradiusz"] = obstructionZ;
            stack.Children.Add(secondRow);

            var spotsRow = new Grid { ColumnDefinitions = new ColumnDefinitions("180, 110, Auto") };
            spotsRow.Children.Add(new TextBlock { Text = "Num Stops", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            var numSpots = CreateOtherTextBox(ProtoXmlHandler.GetSimpleField(unit, "farmingnumstops") ?? "0", true);
            Grid.SetColumn(numSpots, 1);
            spotsRow.Children.Add(numSpots);
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
                Grid.SetColumn(deleteButton, 2);
                spotsRow.Children.Add(deleteButton);
            }
            stack.Children.Add(spotsRow);

            RegisterOtherSpecificContainer(key, stack);
            otherSpecificContainer.Children.Add(stack);
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
            var partisantypeAcb = CreateValidatedOtherSuggestionBox(partisantypeInitial, otherProtoUnitSuggestions, "Proto Unit");
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
                    Spacing = 4,
                    Margin = new Thickness(180, 0, 0, 0)
                };
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
                    var detailRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    detailRow.Children.Add(new TextBlock
                    {
                        Text = resourceType,
                        Width = 60,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 4)
                    });

                    var toggleButton = new Button
                    {
                        Content = hasDropOffMultiplier ? "Remove Multiplier" : "+ Add Multiplier",
                        Background = Brush.Parse(hasDropOffMultiplier ? "#8b0000" : "#2b7a0b"),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = !_isReadOnly
                    };
                    detailRow.Children.Add(toggleButton);

                    var multiplierPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        IsVisible = hasDropOffMultiplier
                    };
                    multiplierPanel.Children.Add(new TextBlock
                    {
                        Text = "Drop Off Multiplier",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 4)
                    });
                    dropOffTb.Width = 110;
                    multiplierPanel.Children.Add(dropOffTb);
                    detailRow.Children.Add(multiplierPanel);

                    toggleButton.Click += async (s, e) =>
                    {
                        var proceed = await CheckStartLocalMod();
                        if (!proceed)
                            return;

                        if (multiplierPanel.IsVisible)
                        {
                            multiplierPanel.IsVisible = false;
                            dropOffTb.Text = "";
                            toggleButton.Content = "+ Add Multiplier";
                            toggleButton.Background = Brush.Parse("#2b7a0b");
                        }
                        else
                        {
                            multiplierPanel.IsVisible = true;
                            toggleButton.Content = "Remove Multiplier";
                            toggleButton.Background = Brush.Parse("#8b0000");
                        }

                        MarkDirty();
                    };

                    carryCapacityDetails!.Children.Add(detailRow);
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

                async void PerformAdd()
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

                var addButton = new Button { Content = "+ Add", Background = Brush.Parse("#2b7a0b"), Margin = new Thickness(8, 0, 8, 0) };
                addButton.Click += (s, e) => PerformAdd();
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

            var recharge = unit.Element("recharge");
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 120, Auto, 80, Auto, 120, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Recharge", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            var typeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(typeLabel, 1);
            rowGrid.Children.Add(typeLabel);
            var typeAcb = CreateOtherTextBox((string?)recharge?.Attribute("type") ?? "");
            Grid.SetColumn(typeAcb, 2);
            rowGrid.Children.Add(typeAcb);
            _fieldControls["recharge.type"] = typeAcb;
            var initLabel = new TextBlock { Text = "Init", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(initLabel, 3);
            rowGrid.Children.Add(initLabel);
            var initTb = CreateOtherTextBox((string?)recharge?.Attribute("init") ?? "", true);
            Grid.SetColumn(initTb, 4);
            rowGrid.Children.Add(initTb);
            _fieldControls["recharge.init"] = initTb;
            var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(valueLabel, 5);
            rowGrid.Children.Add(valueLabel);
            var valueTb = CreateOtherTextBox(recharge?.Value?.Trim() ?? "", true);
            Grid.SetColumn(valueTb, 6);
            rowGrid.Children.Add(valueTb);
            _fieldControls["recharge.value"] = valueTb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "recharge.type", "recharge.init", "recharge.value");
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

        void AddReplacementEditor()
        {
            const string key = "replacement";
            if (IsOtherSpecificAttributeVisible(key, _otherSpecificAttributeContainers))
                return;

            var replacement = unit.Element("replacement");
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("180, Auto, 120, Auto, *, Auto"), Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.Children.Add(new TextBlock { Text = "Replacement", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) });
            var typeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            Grid.SetColumn(typeLabel, 1);
            rowGrid.Children.Add(typeLabel);
            var typeTb = CreateOtherTextBox((string?)replacement?.Attribute("type") ?? "dead");
            Grid.SetColumn(typeTb, 2);
            rowGrid.Children.Add(typeTb);
            _fieldControls["replacement.type"] = typeTb;
            var valueLabel = new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 10, 4) };
            Grid.SetColumn(valueLabel, 3);
            rowGrid.Children.Add(valueLabel);
            var valueAcb = CreateOtherSuggestionBox(replacement?.Value?.Trim() ?? "", otherProtoUnitSuggestions, "Proto Unit");
            Grid.SetColumn(valueAcb, 4);
            rowGrid.Children.Add(valueAcb);
            _fieldControls["replacement.value"] = valueAcb;

            if (!_isReadOnly)
            {
                var deleteButton = new Button { Content = "X", Background = Brush.Parse("#8b0000"), VerticalAlignment = VerticalAlignment.Center };
                deleteButton.Click += async (s, e) =>
                {
                    var proceed = await CheckStartLocalMod();
                    if (proceed)
                    {
                        RemoveOtherSpecificContainer(key, "replacement.type", "replacement.value");
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
                case "stealth":
                    AddStealthEditor();
                    break;
                case "partisans":
                    AddPartisansEditor();
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
            "stealth" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "stealthdetectionradius")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "stealthrevealselfradius")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "stealthshowsilhouetteradius")),
            "partisans" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "partisantype")) || !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "partisancount")),
            "dodgechance" => !string.IsNullOrWhiteSpace(ProtoXmlHandler.GetSimpleField(unit, "dodgechance")),
            "directionalarmor" => unit.Element("directionalarmor") != null,
            "carrycapacity" or "initialresource" or "resourceconversion" => unit.Elements(key).Any(),
            "sharedselectionunittypes" or "rechargeincludetypes" or "rechargeexcludetypes" or "decay" or "recharge" or "minimapcolor" or "replacement" => unit.Element(key) != null,
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
        SaveUnitTypeListElement("rechargeincludetypes", _currentRechargeIncludeTypes);
        SaveUnitTypeListElement("rechargeexcludetypes", _currentRechargeExcludeTypes);

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

        if (_fieldControls.TryGetValue("recharge.value", out var rechargeValueCtrl) && rechargeValueCtrl is TextBox rechargeValueTb)
        {
            unit.Element("recharge")?.Remove();
            var rechargeValue = rechargeValueTb.Text?.Trim() ?? "";
            var rechargeType = _fieldControls.TryGetValue("recharge.type", out var rechargeTypeCtrl) && rechargeTypeCtrl is TextBox rechargeTypeTb
                ? rechargeTypeTb.Text?.Trim() ?? ""
                : "";
            var rechargeInit = _fieldControls.TryGetValue("recharge.init", out var rechargeInitCtrl) && rechargeInitCtrl is TextBox rechargeInitTb
                ? rechargeInitTb.Text?.Trim() ?? ""
                : "";
            if (!string.IsNullOrWhiteSpace(rechargeValue) || !string.IsNullOrWhiteSpace(rechargeType) || !string.IsNullOrWhiteSpace(rechargeInit))
            {
                var rechargeElement = new XElement("recharge", rechargeValue);
                if (!string.IsNullOrWhiteSpace(rechargeType))
                    rechargeElement.SetAttributeValue("type", rechargeType);
                if (!string.IsNullOrWhiteSpace(rechargeInit))
                    rechargeElement.SetAttributeValue("init", rechargeInit);
                unit.Add(rechargeElement);
            }
        }
        else
        {
            unit.Element("recharge")?.Remove();
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

        if (_fieldControls.TryGetValue("replacement.value", out var replacementValueCtrl) && replacementValueCtrl is AutoCompleteBox replacementValueAcb)
        {
            unit.Element("replacement")?.Remove();
            var value = replacementValueAcb.Text?.Trim() ?? "";
            var type = _fieldControls.TryGetValue("replacement.type", out var replacementTypeCtrl) && replacementTypeCtrl is TextBox replacementTypeTb
                ? replacementTypeTb.Text?.Trim() ?? ""
                : "";
            if (!string.IsNullOrWhiteSpace(value))
            {
                var replacement = new XElement("replacement", value);
                if (!string.IsNullOrWhiteSpace(type))
                    replacement.SetAttributeValue("type", type);
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
            _cachedCommandNames = null;
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
                _cachedCommandNames = null;
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
        _cachedCommandNames = null;
        try
        {
            SaveCurrentUnitStringValues();
            ProtoXmlHandler.SaveProtoXml(_modXmlDoc, _modFilePath);
            _isDirty = false;
            _fileLabel.Text = _modFilePath;
            _statusMessage.Text = "Saved successfully.";
            if (!string.IsNullOrWhiteSpace(_currentUnitName))
                BuildEditorPanel(_currentUnitName);
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
            _cachedCommandNames = null;

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
                    BuildEditorPanel(_currentUnitName);
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
