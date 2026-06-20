using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>
/// Static XML CRUD helpers – port of xml_handler.py using LINQ to XML.
/// </summary>
public static class ProtoXmlHandler
{
    // ─── Load / Save ──────────────────────────────────────────────────────────

    /// <summary>Parse a proto XML file from disk.</summary>
    public static (XDocument Doc, XElement Root) ParseProtoXml(string path)
    {
        var doc  = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException("Empty XML document.");
        return (doc, root);
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

        var settings = new XmlWriterSettings
        {
            Indent      = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
        };
        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    /// <summary>Create a new proto_mods XML file with a minimal skeleton.</summary>
    public static (XDocument Doc, XElement Root) CreateNewProtoFile(string path)
    {
        var root = new XElement("protomods");
        var doc  = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
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
        => unit.Element(tag)?.Remove();

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
        XElement? tactics = null;
        var costs = new List<XElement>();
        var armors = new List<XElement>();
        var unitTypes = new List<XElement>();
        var flags = new List<XElement>();
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
            else if (name.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
                protoActions.Add(clone);
            else if (name.Equals("tactics", StringComparison.OrdinalIgnoreCase))
                tactics = clone;
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

            var el = simpleFields.FirstOrDefault(x => x.Name.LocalName.Equals(field.Tag, StringComparison.OrdinalIgnoreCase));
            if (el != null)
                ordered.Add(el);
        }

        ordered.AddRange(costs);
        ordered.AddRange(armors);
        ordered.AddRange(unitTypes);
        ordered.AddRange(flags);
        ordered.AddRange(others);

        if (tactics != null)
            ordered.Add(tactics);

        ordered.AddRange(protoActions);

        unit.ReplaceNodes(ordered);
    }
}
