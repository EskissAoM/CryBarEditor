using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CryBarEditor.Classes;

public sealed class ProtoBarData
{
    public List<string> UnitTypes { get; set; } = [];
    public List<string> Flags { get; set; } = [];
    public Dictionary<string, string> ProtoActionTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, int>> ProtoActionFieldUsageByType { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> ProtoActionNamesByType { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Icons { get; set; } = [];
    public List<string> AnimFiles { get; set; } = [];
    public List<string> SoundSetFiles { get; set; } = [];
    public List<string> ImpactTypes { get; set; } = [];
    public List<string> UnitAiTypes { get; set; } = [];
    public List<string> MovementTypes { get; set; } = [];
    public List<string> Tactics { get; set; } = [];
    public List<string> UnitNames { get; set; } = [];
}

public static class ProtoDataExtractor
{
    public static (ProtoBarData Data, XElement Root) ExtractProtoData(string xmlString)
    {
        var doc = XDocument.Parse(xmlString, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException("Empty XML document.");

        var unitTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var icons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var animfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var soundsetfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var impacttypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unitaitypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var movementtypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tacticsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var protoactionTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var protoactionFieldUsageByType = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var protoactionNamesByType = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var unitNames = new List<string>();

        // XML structure can be a single proto/protomods root or a combined <protos>
        // wrapper containing several proto documents.
        var units = root.Descendants("unit");

        foreach (var unit in units)
        {
            var name = (string?)unit.Attribute("name");
            if (!string.IsNullOrEmpty(name))
            {
                unitNames.Add(name);
            }

            foreach (var ut in unit.Elements("unittype"))
            {
                var val = ut.Value?.Trim();
                if (!string.IsNullOrEmpty(val))
                {
                    unitTypes.Add(val);
                }
            }

            foreach (var f in unit.Elements("flag"))
            {
                var val = f.Value?.Trim();
                if (!string.IsNullOrEmpty(val))
                {
                    flags.Add(val);
                }
            }

            void AddIfPresent(string elementName, HashSet<string> targetSet)
            {
                var el = unit.Element(elementName);
                if (el != null)
                {
                    var val = el.Value?.Trim();
                    if (!string.IsNullOrEmpty(val))
                    {
                        targetSet.Add(val);
                    }
                }
            }

            AddIfPresent("icon", icons);
            AddIfPresent("animfile", animfiles);
            AddIfPresent("soundsetfile", soundsetfiles);
            AddIfPresent("impacttype", impacttypes);
            AddIfPresent("unitaitype", unitaitypes);
            AddIfPresent("movementtype", movementtypes);
            AddIfPresent("tactics", tacticsSet);

            foreach (var pa in unit.Elements("protoaction"))
            {
                var paNameEl = pa.Element("name");
                var paTypeEl = pa.Element("type");
                var paName = paNameEl?.Value?.Trim();
                var paType = paTypeEl?.Value?.Trim() ?? "";
                if (!string.IsNullOrEmpty(paName))
                {
                    if (!protoactionTypes.TryGetValue(paName, out var existingType))
                    {
                        protoactionTypes[paName] = paType;
                    }
                    else if (string.IsNullOrWhiteSpace(existingType) && !string.IsNullOrWhiteSpace(paType))
                    {
                        protoactionTypes[paName] = paType;
                    }
                }

                if (string.IsNullOrWhiteSpace(paType))
                    continue;

                if (!protoactionFieldUsageByType.TryGetValue(paType, out var fieldUsage))
                {
                    fieldUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    protoactionFieldUsageByType[paType] = fieldUsage;
                }

                if (!protoactionNamesByType.TryGetValue(paType, out var actionNames))
                {
                    actionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    protoactionNamesByType[paType] = actionNames;
                }

                if (!string.IsNullOrWhiteSpace(paName))
                    actionNames.Add(paName);

                foreach (var child in pa.Elements())
                {
                    var tag = child.Name.LocalName;
                    if (tag.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                        tag.Equals("type", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    fieldUsage[tag] = fieldUsage.TryGetValue(tag, out var currentCount)
                        ? currentCount + 1
                        : 1;
                }
            }
        }

        var data = new ProtoBarData
        {
            UnitTypes = unitTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Flags = flags.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            ProtoActionTypes = protoactionTypes,
            ProtoActionFieldUsageByType = protoactionFieldUsageByType.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            ProtoActionNamesByType = protoactionNamesByType.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase),
            Icons = icons.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            AnimFiles = animfiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            SoundSetFiles = soundsetfiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            ImpactTypes = impacttypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            UnitAiTypes = unitaitypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            MovementTypes = movementtypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Tactics = tacticsSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            UnitNames = unitNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
        };

        return (data, root);
    }
}
