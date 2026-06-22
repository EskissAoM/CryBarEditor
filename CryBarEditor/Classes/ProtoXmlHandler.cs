using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CryBarEditor.Classes;

/// <summary>
/// Parsed representation of a ProtoAction element within a unit.
/// </summary>
public sealed class ProtoAction
{
    public string Name    { get; set; } = "";
    public string Type    { get; set; } = "";
    public string Rof     { get; set; } = "";
    public string MaxRange{ get; set; } = "";
    public List<(string DamageType, string Amount)> Damages       { get; } = [];
    public List<(string UnitType, string Multiplier)> DamageBonuses { get; } = [];
}

public sealed class ProtoCommandEntry
{
    public string Value { get; set; } = "";
    public string Row { get; set; } = "";
    public string Column { get; set; } = "";
    public string MergeMode { get; set; } = "";
}

public sealed class ProtoBuildLimitEntry
{
    public string Value { get; set; } = "";
    public string Weight { get; set; } = "";
}

public sealed class ProtoCultureFieldEntry
{
    public string Culture { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// Static XML CRUD helpers – port of xml_handler.py using LINQ to XML.
/// </summary>
public static class ProtoXmlHandler
{
    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }

    // ─── Load / Save ──────────────────────────────────────────────────────────

    /// <summary>Parse a proto XML file from disk.</summary>
    public static (XDocument Doc, XElement Root) ParseProtoXml(string path)
    {
        try
        {
            var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var root = doc.Root ?? throw new InvalidOperationException("Empty XML document.");
            return (doc, root);
        }
        catch (XmlException)
        {
            var xml = File.ReadAllText(path, Encoding.UTF8);
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            if (doc.Declaration != null)
                doc.Declaration.Encoding = "utf-8";
            var root = doc.Root ?? throw new InvalidOperationException("Empty XML document.");
            return (doc, root);
        }
    }

    /// <summary>Parse a proto XML string (from BAR / cache).</summary>
    public static (XDocument Doc, XElement Root) ParseProtoXmlString(string xml)
    {
        var doc  = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException("Empty XML document.");
        return (doc, root);
    }

    /// <summary>Save a proto XDocument to disk with standard indentation.</summary>
    public static void SaveProtoXml(XDocument doc, string path)
    {
        NormalizeProtoDocument(doc);
        doc.Declaration = null;

        var settings = new XmlWriterSettings
        {
            Indent      = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true,
            Encoding = new UTF8Encoding(false),
        };
        using var stringWriter = new Utf8StringWriter();
        using (var writer = XmlWriter.Create(stringWriter, settings))
            doc.Save(writer);

        var xml = stringWriter.ToString();
        xml = Regex.Replace(xml, @"<(armor|directionalarmor)([^>]*)\s*/>", "<$1$2></$1>");
        File.WriteAllText(path, xml, new UTF8Encoding(false));
    }

    /// <summary>Create a new proto_mods XML file with a minimal skeleton.</summary>
    public static (XDocument Doc, XElement Root) CreateNewProtoFile(string path)
    {
        var root = new XElement("protomods");
        var doc  = new XDocument(root);
        SaveProtoXml(doc, path);
        return (doc, root);
    }

    // ─── Unit Enumeration ─────────────────────────────────────────────────────

    /// <summary>Return sorted list of unit names from the root element.</summary>
    public static List<string> GetUnitNames(XElement root)
        => root.Descendants("unit")
               .Select(u => (string?)u.Attribute("name") ?? "")
               .Where(n => n.Length > 0)
               .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>Find a unit element by name (case-insensitive).</summary>
    public static XElement? GetUnitElement(XElement root, string name)
        => root.Descendants("unit")
               .FirstOrDefault(u =>
                   string.Equals((string?)u.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));

    // ─── Simple Fields ────────────────────────────────────────────────────────

    /// <summary>Get text content of a direct child element, or null if absent.</summary>
    public static string? GetSimpleField(XElement unit, string tag)
        => (string?)unit.Element(tag);

    /// <summary>Set (create/update) a direct child element's text content.</summary>
    public static void SetSimpleField(XElement unit, string tag, string value)
    {
        var el = unit.Element(tag);
        if (el == null)
            unit.Add(new XElement(tag, value));
        else
            el.Value = value;
    }

    /// <summary>Remove a direct child element entirely.</summary>
    public static void RemoveSimpleField(XElement unit, string tag)
        => unit.Elements(tag).Remove();

    public static List<ProtoCultureFieldEntry> GetCultureAwareSimpleFields(XElement unit, string tag)
        => unit.Elements(tag)
               .Select(e => new ProtoCultureFieldEntry
               {
                   Culture = (string?)e.Attribute("culture") ?? "",
                   Value = e.Value?.Trim() ?? "",
               })
               .Where(x => !string.IsNullOrWhiteSpace(x.Value))
               .ToList();

    public static void SetCultureAwareSimpleFields(XElement unit, string tag, IEnumerable<ProtoCultureFieldEntry> entries)
    {
        unit.Elements(tag).Remove();

        foreach (var entry in entries.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var element = new XElement(tag, entry.Value.Trim());
            if (!string.IsNullOrWhiteSpace(entry.Culture))
                element.SetAttributeValue("culture", entry.Culture.Trim());
            unit.Add(element);
        }
    }

    // ─── Costs ───────────────────────────────────────────────────────────────

    /// <summary>Return cost entries as (resourceType, amount) pairs.</summary>
    public static List<(string ResourceType, string Amount)> GetCostEntries(XElement unit)
        => unit.Elements("cost")
               .Select(c => ((string?)c.Attribute("resourcetype") ?? "", c.Value))
               .Where(t => t.Item1.Length > 0)
               .ToList();

    /// <summary>Replace all cost entries with the supplied pairs.</summary>
    public static void SetCostEntries(XElement unit, IEnumerable<(string ResourceType, string Amount)> entries)
    {
        unit.Elements("cost").Remove();
        foreach (var (rt, amount) in entries)
            unit.Add(new XElement("cost", new XAttribute("resourcetype", rt), amount));
    }

    // ─── Armor ───────────────────────────────────────────────────────────────

    /// <summary>Return armor entries as (armorType, value) pairs.</summary>
    public static List<(string ArmorType, string Value)> GetArmorEntries(XElement unit)
        => unit.Elements("armor")
               .Select(a => ((string?)a.Attribute("type") ?? "", (string?)a.Attribute("value") ?? a.Value))
               .Where(t => t.Item1.Length > 0)
               .ToList();

    /// <summary>Replace all armor entries with the supplied pairs.</summary>
    public static void SetArmorEntries(XElement unit, IEnumerable<(string ArmorType, string Value)> entries)
    {
        unit.Elements("armor").Remove();
        foreach (var (at, val) in entries)
            unit.Add(new XElement("armor", new XAttribute("type", at), new XAttribute("value", val)));
    }

    // ─── Unit Types ───────────────────────────────────────────────────────────

    /// <summary>Return all <unittype> values for a unit.</summary>
    public static List<string> GetUnitTypeList(XElement unit)
        => unit.Elements("unittype").Select(e => e.Value).Where(v => v.Length > 0).ToList();

    /// <summary>Replace all <unittype> elements.</summary>
    public static void SetUnitTypeList(XElement unit, IEnumerable<string> types)
    {
        unit.Elements("unittype").Remove();
        foreach (var t in types)
            unit.Add(new XElement("unittype", t));
    }

    // ─── Flags ────────────────────────────────────────────────────────────────

    /// <summary>Return all <flag> values for a unit.</summary>
    public static List<string> GetFlagList(XElement unit)
        => unit.Elements("flag").Select(e => e.Value).Where(v => v.Length > 0).ToList();

    /// <summary>Replace all <flag> elements.</summary>
    public static void SetFlagList(XElement unit, IEnumerable<string> flags)
    {
        unit.Elements("flag").Remove();
        foreach (var f in flags)
            unit.Add(new XElement("flag", f));
    }

    public static List<ProtoCommandEntry> GetTrainEntries(XElement unit)
        => GetCommandEntries(unit, "train");

    public static void SetTrainEntries(XElement unit, IEnumerable<ProtoCommandEntry> entries)
        => SetCommandEntries(unit, "train", entries);

    public static List<ProtoCommandEntry> GetTechEntries(XElement unit)
        => GetCommandEntries(unit, "tech");

    public static void SetTechEntries(XElement unit, IEnumerable<ProtoCommandEntry> entries)
        => SetCommandEntries(unit, "tech", entries);

    public static List<ProtoCommandEntry> GetCommandEntries(XElement unit)
        => GetCommandEntries(unit, "command");

    public static void SetCommandEntries(XElement unit, IEnumerable<ProtoCommandEntry> entries)
        => SetCommandEntries(unit, "command", entries);

    public static List<ProtoCommandEntry> GetOptionalCommandEntries(XElement unit)
        => GetCommandEntries(unit, "optionalcommand");

    public static void SetOptionalCommandEntries(XElement unit, IEnumerable<ProtoCommandEntry> entries)
        => SetCommandEntries(unit, "optionalcommand", entries);

    public static List<string> GetContainList(XElement unit)
        => unit.Elements("contain").Select(e => e.Value?.Trim() ?? "").Where(v => v.Length > 0).ToList();

    public static void SetContainList(XElement unit, IEnumerable<string> values)
    {
        unit.Elements("contain").Remove();
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            unit.Add(new XElement("contain", value.Trim()));
    }

    public static List<string> GetNotContainList(XElement unit)
        => unit.Elements("notcontain").Select(e => e.Value?.Trim() ?? "").Where(v => v.Length > 0).ToList();

    public static void SetNotContainList(XElement unit, IEnumerable<string> values)
    {
        unit.Elements("notcontain").Remove();
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            unit.Add(new XElement("notcontain", value.Trim()));
    }

    public static List<string> GetDynamicBuildLimitUnitTypes(XElement unit)
        => unit.Element("dynamicbuildlimitunittypes")?
               .Elements("unittype")
               .Select(e => e.Value?.Trim() ?? "")
               .Where(v => v.Length > 0)
               .ToList()
           ?? [];

    public static void SetDynamicBuildLimitUnitTypes(XElement unit, IEnumerable<string> unitTypes)
    {
        unit.Element("dynamicbuildlimitunittypes")?.Remove();

        var values = unitTypes
            .Select(x => x?.Trim() ?? "")
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (values.Count == 0)
            return;

        unit.Add(new XElement("dynamicbuildlimitunittypes",
            values.Select(x => new XElement("unittype", x))));
    }

    public static List<ProtoBuildLimitEntry> GetSharedBuildLimitEntries(XElement unit)
        => unit.Element("sharedbuildlimitunittypes")?
               .Elements("unittype")
               .Select(e => new ProtoBuildLimitEntry
               {
                   Value = e.Value?.Trim() ?? "",
                   Weight = (string?)e.Attribute("weight") ?? "",
               })
               .Where(x => x.Value.Length > 0)
               .ToList()
           ?? [];

    public static void SetSharedBuildLimitEntries(XElement unit, IEnumerable<ProtoBuildLimitEntry> entries)
    {
        unit.Element("sharedbuildlimitunit")?.Remove();
        unit.Element("sharedbuildlimitunittypes")?.Remove();

        var values = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new ProtoBuildLimitEntry
                {
                    Value = first.Value.Trim(),
                    Weight = first.Weight?.Trim() ?? "",
                };
            })
            .ToList();

        if (values.Count == 0)
            return;

        unit.Add(new XElement("sharedbuildlimitunittypes",
            values.Select(x =>
            {
                var el = new XElement("unittype", x.Value);
                if (!string.IsNullOrWhiteSpace(x.Weight))
                    el.SetAttributeValue("weight", x.Weight.Trim());
                return el;
            })));
    }

    public static void RemoveBuildLimitModeElements(XElement unit)
    {
        unit.Element("dynamicbuildlimitunittypes")?.Remove();
        unit.Element("sharedbuildlimitunit")?.Remove();
        unit.Element("sharedbuildlimitunittypes")?.Remove();
    }

    // ─── ProtoActions ─────────────────────────────────────────────────────────

    /// <summary>Parse all protoaction elements for the given unit.</summary>
    public static List<ProtoAction> GetProtoActions(XElement unit)
    {
        var result = new List<ProtoAction>();
        foreach (var pa in unit.Elements("protoaction"))
        {
            var action = new ProtoAction
            {
                Name     = (string?)pa.Element("name")     ?? "",
                Type     = (string?)pa.Element("type")     ?? "",
                Rof      = (string?)pa.Element("rof")      ?? "",
                MaxRange = (string?)pa.Element("maxrange") ?? "",
            };
            foreach (var d in pa.Elements("damage"))
                action.Damages.Add(((string?)d.Attribute("type") ?? "", d.Value));
            foreach (var db in pa.Elements("damagebonus"))
                action.DamageBonuses.Add(((string?)db.Attribute("type") ?? (string?)db.Attribute("unittype") ?? "", db.Value));
            result.Add(action);
        }
        return result;
    }

    /// <summary>Replace all protoaction elements for the given unit.</summary>
    public static void SetProtoActions(XElement unit, IEnumerable<ProtoAction> actions)
    {
        unit.Elements("protoaction").Remove();
        foreach (var a in actions)
        {
            var pa = new XElement("protoaction");
            if (a.Name.Length     > 0) pa.Add(new XElement("name",     a.Name));
            if (a.Type.Length     > 0) pa.Add(new XElement("type",     a.Type));
            if (a.Rof.Length      > 0) pa.Add(new XElement("rof",      a.Rof));
            if (a.MaxRange.Length > 0) pa.Add(new XElement("maxrange", a.MaxRange));
            foreach (var (dt, amt) in a.Damages)
                pa.Add(new XElement("damage", new XAttribute("type", dt), amt));
            foreach (var (ut, mult) in a.DamageBonuses)
                pa.Add(new XElement("damagebonus", new XAttribute("type", ut), mult));
            unit.Add(pa);
        }
    }

    // ─── Unit CRUD ────────────────────────────────────────────────────────────

    /// <summary>
    /// Add a new unit with default cost and armor entries.
    /// Returns the new XElement.
    /// </summary>
    public static XElement AddNewUnit(XElement root, string name)
    {
        var unit = new XElement("unit", new XAttribute("name", name));
        // Default costs
        foreach (var rt in ProtoConstants.KnownResourceTypes)
            unit.Add(new XElement("cost", new XAttribute("resourcetype", rt), "0"));
        // Default armors
        foreach (var at in ProtoConstants.KnownArmorTypes)
            unit.Add(new XElement("armor", new XAttribute("type", at), "0"));
        root.Add(unit);
        return unit;
    }

    /// <summary>Delete a unit by name. Returns true if found and removed.</summary>
    public static bool DeleteUnit(XElement root, string name)
    {
        var unit = GetUnitElement(root, name);
        if (unit == null) return false;
        unit.Remove();
        return true;
    }

    /// <summary>
    /// Clone an existing unit under a new name. Returns the new element,
    /// or null if the source unit was not found.
    /// </summary>
    public static XElement? CloneUnit(XElement root, string sourceName, string newName)
    {
        var source = GetUnitElement(root, sourceName);
        if (source == null) return null;
        return CloneUnit(root, source, newName);
    }

    /// <summary>Clone an existing unit element into the supplied root under a new name.</summary>
    public static XElement CloneUnit(XElement root, XElement source, string newName)
    {
        var clone = new XElement(source);
        clone.SetAttributeValue("name", newName);
        root.Add(clone);
        return clone;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Test whether a unit element exists in the root.</summary>
    public static bool UnitExists(XElement root, string name)
        => GetUnitElement(root, name) != null;

    static List<ProtoCommandEntry> GetCommandEntries(XElement unit, string elementName)
        => unit.Elements(elementName)
               .Select(e => new ProtoCommandEntry
               {
                   Value = e.Value?.Trim() ?? "",
                   Row = (string?)e.Attribute("row") ?? "",
                   Column = (string?)e.Attribute("column") ?? "",
                   MergeMode = (string?)e.Attribute("mergeMode") ?? "",
               })
               .Where(e => e.Value.Length > 0)
               .ToList();

    static void SetCommandEntries(XElement unit, string elementName, IEnumerable<ProtoCommandEntry> entries)
    {
        unit.Elements(elementName).Remove();
        foreach (var entry in entries.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var element = new XElement(elementName, entry.Value);
            if (!string.IsNullOrWhiteSpace(entry.Row))
                element.SetAttributeValue("row", entry.Row);
            if (!string.IsNullOrWhiteSpace(entry.Column))
                element.SetAttributeValue("column", entry.Column);
            if (!string.IsNullOrWhiteSpace(entry.MergeMode))
                element.SetAttributeValue("mergeMode", entry.MergeMode);
            unit.Add(element);
        }
    }

    static void NormalizeProtoDocument(XDocument doc)
    {
        var root = doc.Root;
        if (root == null) return;

        foreach (var unit in root.Descendants("unit").ToList())
            NormalizeUnit(unit);

        if (root.Name.LocalName.Equals("protomods", StringComparison.OrdinalIgnoreCase))
        {
            var units = root.Elements("unit").ToList();
            root.ReplaceNodes(units);
        }
    }

    static void NormalizeUnit(XElement unit)
    {
        var knownSimpleTags = new HashSet<string>(
            ProtoConstants.SimpleFields.Select(f => f.Tag),
            StringComparer.OrdinalIgnoreCase);

        var simpleFields = new List<XElement>();
        XElement? unitRegen = null;
        XElement? directionalArmor = null;
        XElement? initialShieldPoints = null;
        XElement? maxShieldPoints = null;
        XElement? unitShieldRegen = null;
        XElement? tactics = null;
        var costs = new List<XElement>();
        var resourceConversions = new List<XElement>();
        var armors = new List<XElement>();
        var unitTypes = new List<XElement>();
        var flags = new List<XElement>();
        var contains = new List<XElement>();
        var notContains = new List<XElement>();
        XElement? dynamicBuildLimitUnitTypes = null;
        XElement? sharedBuildLimitUnit = null;
        XElement? sharedBuildLimitUnitTypes = null;
        var trains = new List<XElement>();
        var techs = new List<XElement>();
        var commands = new List<XElement>();
        var optionalCommands = new List<XElement>();
        XElement? transformCommand = null;
        var protoActions = new List<XElement>();
        var others = new List<XElement>();

        foreach (var child in unit.Elements().ToList())
        {
            var clone = new XElement(child);
            var name = child.Name.LocalName;

            if (name.Equals("cost", StringComparison.OrdinalIgnoreCase))
                costs.Add(clone);
            else if (name.Equals("armor", StringComparison.OrdinalIgnoreCase))
                armors.Add(clone);
            else if (name.Equals("unittype", StringComparison.OrdinalIgnoreCase))
                unitTypes.Add(clone);
            else if (name.Equals("flag", StringComparison.OrdinalIgnoreCase))
                flags.Add(clone);
            else if (name.Equals("contain", StringComparison.OrdinalIgnoreCase))
                contains.Add(clone);
            else if (name.Equals("notcontain", StringComparison.OrdinalIgnoreCase))
                notContains.Add(clone);
            else if (name.Equals("dynamicbuildlimitunittypes", StringComparison.OrdinalIgnoreCase))
                dynamicBuildLimitUnitTypes = clone;
            else if (name.Equals("sharedbuildlimitunit", StringComparison.OrdinalIgnoreCase))
                sharedBuildLimitUnit = clone;
            else if (name.Equals("sharedbuildlimitunittypes", StringComparison.OrdinalIgnoreCase))
                sharedBuildLimitUnitTypes = clone;
            else if (name.Equals("train", StringComparison.OrdinalIgnoreCase))
                trains.Add(clone);
            else if (name.Equals("tech", StringComparison.OrdinalIgnoreCase))
                techs.Add(clone);
            else if (name.Equals("command", StringComparison.OrdinalIgnoreCase))
                commands.Add(clone);
            else if (name.Equals("optionalcommand", StringComparison.OrdinalIgnoreCase))
                optionalCommands.Add(clone);
            else if (name.Equals("transformcommand", StringComparison.OrdinalIgnoreCase))
                transformCommand = clone;
            else if (name.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
                protoActions.Add(clone);
            else if (name.Equals("tactics", StringComparison.OrdinalIgnoreCase))
                tactics = clone;
            else if (name.Equals("unitregen", StringComparison.OrdinalIgnoreCase))
                unitRegen = clone;
            else if (name.Equals("directionalarmor", StringComparison.OrdinalIgnoreCase))
                directionalArmor = clone;
            else if (name.Equals("resourceconversion", StringComparison.OrdinalIgnoreCase))
                resourceConversions.Add(clone);
            else if (name.Equals("initialshieldpoints", StringComparison.OrdinalIgnoreCase))
                initialShieldPoints = clone;
            else if (name.Equals("maxshieldpoints", StringComparison.OrdinalIgnoreCase))
                maxShieldPoints = clone;
            else if (name.Equals("unitshieldregen", StringComparison.OrdinalIgnoreCase))
                unitShieldRegen = clone;
            else if (knownSimpleTags.Contains(name))
                simpleFields.Add(clone);
            else
                others.Add(clone);
        }

        var ordered = new List<object>();

        foreach (var field in ProtoConstants.SimpleFields)
        {
            if (field.Tag.Equals("tactics", StringComparison.OrdinalIgnoreCase))
                continue;

            ordered.AddRange(simpleFields.Where(x => x.Name.LocalName.Equals(field.Tag, StringComparison.OrdinalIgnoreCase)));

            if (field.Tag.Equals("maxhitpoints", StringComparison.OrdinalIgnoreCase))
            {
                if (unitRegen != null)
                    ordered.Add(unitRegen);
                if (maxShieldPoints != null)
                    ordered.Add(maxShieldPoints);
                if (initialShieldPoints != null)
                    ordered.Add(initialShieldPoints);
                if (unitShieldRegen != null)
                    ordered.Add(unitShieldRegen);
            }

            if (field.Tag.Equals("buildlimit", StringComparison.OrdinalIgnoreCase))
            {
                if (sharedBuildLimitUnit != null)
                    ordered.Add(sharedBuildLimitUnit);
                if (dynamicBuildLimitUnitTypes != null)
                    ordered.Add(dynamicBuildLimitUnitTypes);
                if (sharedBuildLimitUnitTypes != null)
                    ordered.Add(sharedBuildLimitUnitTypes);
            }
        }

        ordered.AddRange(costs);
        ordered.AddRange(resourceConversions);
        if (directionalArmor != null)
            ordered.Add(directionalArmor);
        ordered.AddRange(armors);
        ordered.AddRange(unitTypes);
        ordered.AddRange(flags);
        ordered.AddRange(contains);
        ordered.AddRange(notContains);
        ordered.AddRange(trains);
        ordered.AddRange(techs);
        ordered.AddRange(commands);
        ordered.AddRange(optionalCommands);
        if (transformCommand != null)
            ordered.Add(transformCommand);
        ordered.AddRange(others);

        if (tactics != null)
            ordered.Add(tactics);

        ordered.AddRange(protoActions);

        unit.ReplaceNodes(ordered);
    }
}
