using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CryBarEditor.Classes;

public enum ProtoActionFieldEditorKind
{
    Text,
    Number,
    Toggle,
    StructuredList,
}

public sealed record ProtoActionFieldDefinition(
    string Tag,
    string Label,
    ProtoActionFieldEditorKind EditorKind,
    bool IsRepeatable = false,
    IReadOnlyList<string>? XmlAttributeNames = null);

public sealed record ProtoActionObservedField(
    ProtoActionFieldDefinition Definition,
    int Occurrences);

public sealed class ProtoActionTypeProfile
{
    public required string ActionType { get; init; }
    public required IReadOnlyList<ProtoActionObservedField> ObservedFields { get; init; }
    public required IReadOnlyList<string> ActionNames { get; init; }

    public IReadOnlyList<string> GetRecommendedFieldTags(int maxCount = 8)
    {
        if (maxCount <= 0)
            return [];

        return ObservedFields
            .Take(maxCount)
            .Select(x => x.Definition.Tag)
            .ToList();
    }
}

public sealed record ProtoActionTypeEditorProfile(
    IReadOnlyList<string> DefaultVisibleTags,
    IReadOnlySet<string> HiddenByDefaultTags,
    IReadOnlyList<string>? DefaultFlagTags = null);

public static class ProtoActionMetadataCatalog
{
    private static readonly HashSet<string> DeferredComplexFieldTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "onhiteffect",
    };

    private static readonly Dictionary<string, int> DisplayPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anim"] = 10,
        ["rof"] = 20,
        ["maxrange"] = 30,
        ["minrange"] = 40,
        ["rate"] = 50,
        ["damage"] = 60,
        ["damagebonus"] = 70,
        ["damagearea"] = 80,
        ["damageflags"] = 90,
        ["accuracy"] = 100,
        ["impacteffect"] = 110,
        ["projectile"] = 120,
        ["attackaction"] = 130,
        ["chargeaction"] = 140,
        ["active"] = 150,
        ["persistent"] = 160,
        ["singleuse"] = 165,
        ["modifytype"] = 170,
        ["modifyamount"] = 180,
        ["modifyduration"] = 190,
    };

    private static readonly Dictionary<string, ProtoActionFieldDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = new("name", "Name", ProtoActionFieldEditorKind.Text),
        ["type"] = new("type", "Type", ProtoActionFieldEditorKind.Text),
        ["displaynameid"] = new("displaynameid", "Display Name ID", ProtoActionFieldEditorKind.Text),
        ["anim"] = new("anim", "Animation", ProtoActionFieldEditorKind.Text),
        ["idleanim"] = new("idleanim", "Idle Animation", ProtoActionFieldEditorKind.Text),
        ["walkanim"] = new("walkanim", "Walk Animation", ProtoActionFieldEditorKind.Text),
        ["reloadanim"] = new("reloadanim", "Reload Animation", ProtoActionFieldEditorKind.Text),
        ["animationrate"] = new("animationrate", "Animation Rate", ProtoActionFieldEditorKind.Number),
        ["devotiontime"] = new("devotiontime", "Devotion Time", ProtoActionFieldEditorKind.Number),
        ["rof"] = new("rof", "Rate of Fire", ProtoActionFieldEditorKind.Number),
        ["maxrange"] = new("maxrange", "Max Range", ProtoActionFieldEditorKind.Number),
        ["minrange"] = new("minrange", "Min Range", ProtoActionFieldEditorKind.Number),
        ["typedmaxrange"] = new("typedmaxrange", "Typed Max Range", ProtoActionFieldEditorKind.StructuredList, true, ["type"]),
        ["typedminrange"] = new("typedminrange", "Typed Min Range", ProtoActionFieldEditorKind.StructuredList, true, ["type"]),
        ["rate"] = new("rate", "Rate", ProtoActionFieldEditorKind.StructuredList, true, ["type", "resource", "yield", "overrideResource", "inventoryRate"]),
        ["minrate"] = new("minrate", "Min Rate", ProtoActionFieldEditorKind.StructuredList, true, ["type"]),
        ["damage"] = new("damage", "Damage", ProtoActionFieldEditorKind.StructuredList, true, ["type"]),
        ["damagebonus"] = new("damagebonus", "Damage Bonus", ProtoActionFieldEditorKind.StructuredList, true, ["type", "unittype"]),
        ["damagearea"] = new("damagearea", "Damage Area", ProtoActionFieldEditorKind.Number),
        ["damageflags"] = new("damageflags", "Damage Flags", ProtoActionFieldEditorKind.Text),
        ["damagecap"] = new("damagecap", "Damage Cap", ProtoActionFieldEditorKind.Number),
        ["outerdamageareadistance"] = new("outerdamageareadistance", "Outer Damage Area Distance", ProtoActionFieldEditorKind.Number),
        ["outerdamageareafactor"] = new("outerdamageareafactor", "Outer Damage Area Factor", ProtoActionFieldEditorKind.Number),
        ["accuracy"] = new("accuracy", "Accuracy", ProtoActionFieldEditorKind.Number),
        ["maxspread"] = new("maxspread", "Max Spread", ProtoActionFieldEditorKind.Number),
        ["trackrating"] = new("trackrating", "Track Rating", ProtoActionFieldEditorKind.Number),
        ["impacteffect"] = new("impacteffect", "Impact Effect", ProtoActionFieldEditorKind.Text),
        ["onhiteffect"] = new("onhiteffect", "On Hit Effect", ProtoActionFieldEditorKind.Text),
        ["projectile"] = new("projectile", "Projectile", ProtoActionFieldEditorKind.Text),
        ["splashvfxproto"] = new("splashvfxproto", "Splash VFX Proto", ProtoActionFieldEditorKind.Text),
        ["modelattachment"] = new("modelattachment", "Model Attachment", ProtoActionFieldEditorKind.Text),
        ["modelattachmentbone"] = new("modelattachmentbone", "Model Attachment Bone", ProtoActionFieldEditorKind.Text),
        ["modelattachmenttimer"] = new("modelattachmenttimer", "Model Attachment Timer (ms)", ProtoActionFieldEditorKind.Number),
        ["targetattachment"] = new("targetattachment", "Target Attachment", ProtoActionFieldEditorKind.Text),
        ["targetattachmentbone"] = new("targetattachmentbone", "Target Attachment Bone", ProtoActionFieldEditorKind.Text),
        ["active"] = new("active", "Active", ProtoActionFieldEditorKind.Toggle),
        ["attackaction"] = new("attackaction", "Attack Action", ProtoActionFieldEditorKind.Toggle),
        ["chargeaction"] = new("chargeaction", "Charge Action", ProtoActionFieldEditorKind.Toggle),
        ["persistent"] = new("persistent", "Persistent", ProtoActionFieldEditorKind.Toggle),
        ["singleuse"] = new("singleuse", "Single Use", ProtoActionFieldEditorKind.Toggle),
        ["targetground"] = new("targetground", "Target Ground", ProtoActionFieldEditorKind.Toggle),
        ["modifytype"] = new("modifytype", "Modify Type", ProtoActionFieldEditorKind.Text),
        ["modifyamount"] = new("modifyamount", "Modify Amount", ProtoActionFieldEditorKind.Number),
        ["modifyduration"] = new("modifyduration", "Modify Duration", ProtoActionFieldEditorKind.Number),
        ["modifyexponent"] = new("modifyexponent", "Modify Exponent", ProtoActionFieldEditorKind.Number),
        ["modifyself"] = new("modifyself", "Modify Self", ProtoActionFieldEditorKind.Toggle),
        ["modifyabstracttype"] = new("modifyabstracttype", "Modify Type", ProtoActionFieldEditorKind.StructuredList, true),
        ["modifyunittype"] = new("modifyunittype", "Modify Unit Type", ProtoActionFieldEditorKind.StructuredList, true),
        ["modifyprotoid"] = new("modifyprotoid", "Modify Proto ID", ProtoActionFieldEditorKind.StructuredList, true),
        ["modifytargetlimit"] = new("modifytargetlimit", "Modify Target Limit", ProtoActionFieldEditorKind.Number),
        ["charged"] = new("charged", "Charged", ProtoActionFieldEditorKind.StructuredList),
        ["chargedmodify"] = new("chargedmodify", "Charged Modify", ProtoActionFieldEditorKind.StructuredList, true, ["modifytype", "applytype"]),
        ["activationtype"] = new("activationtype", "Activation Type", ProtoActionFieldEditorKind.Text),
        ["conversionprotoid"] = new("conversionprotoid", "Conversion Proto ID", ProtoActionFieldEditorKind.StructuredList, true, ["srctype"]),
        ["typedduration"] = new("typedduration", "Typed Duration", ProtoActionFieldEditorKind.StructuredList, true, ["type"]),
        ["typedstunduration"] = new("typedstunduration", "Typed Stun Duration", ProtoActionFieldEditorKind.StructuredList, true, ["type"]),
        ["typedanim"] = new("typedanim", "Typed Animation", ProtoActionFieldEditorKind.StructuredList, true, ["type"]),
        ["sizeclassanim"] = new("sizeclassanim", "Size Class Animation", ProtoActionFieldEditorKind.StructuredList, true, ["class"]),
    };

    private static readonly string[] KnownFlagTags =
    [
        "attackaction",
        "active",
        "activeifcontainsunits",
        "addresourcesfasterwhenowned",
        "addresourcestoinventory",
        "ambushduration",
        "ambushonly",
        "ambushpreferred",
        "animationrate",
        "areadamageignoretacticattacktype",
        "areadamageignoretype",
        "attachaboveunit",
        "attachforcediewithunit",
        "attachvalidtargetonly",
        "autocastbyself",
        "autogatherscalebygatherrate",
        "autogatherteam",
        "autoretarget",
        "auxchargeaction",
        "backtoafterteleport",
        "basedamagecap",
        "canattackground",
        "cannotbeconvertedbyallies",
        "cannotbeconvertedbyenemies",
        "chargeaction",
        "charmedconvert",
        "circularshockwave",
        "containscalebasedamage",
        "converttonatureifforbidden",
        "convertviadestroyandrecreate",
        "damageonpickup",
        "deadexclusive",
        "defaultattack",
        "destroyunitafteruse",
        "devotionneversacrifice",
        "disableautoattack",
        "donotautogatherifgathered",
        "donotautogatherunlessgathering",
        "donotdepositresources",
        "donotignoredead",
        "dropsitegathering",
        "excludefromrangeindicator",
        "exclusive",
        "facetoafterteleport",
        "firechallengepacket",
        "forceareaattackcenter",
        "forceareaattacktarget",
        "forcespawn",
        "forcetrainedunitstogatherpoint",
        "forceupdatemode",
        "fulldamagemaintarget",
        "gatherlinkedresource",
        "handattackdisplayrange",
        "handlogic",
        "healnonidle",
        "hidefromglobalqueue",
        "hidefromstats",
        "homingballistics",
        "includeally",
        "includeenemy",
        "includenature",
        "includeunbuilt",
        "initialrof",
        "instantballistics",
        "isabductdrop",
        "ismanualtransform",
        "keepalive",
        "killoninvalidland",
        "killontrain",
        "linear",
        "linearshockwave",
        "minworkrateasresourcedrain",
        "modifysingleactionbytype",
        "modelattachmentonself",
        "modifyattachonce",
        "modifyexclusive",
        "modifyflyingunits",
        "modifyinventoryscale",
        "modifyrangeuselos",
        "modifyratebytype",
        "modifyself",
        "modifyselfifinfection",
        "modifyselfonly",
        "modifystackadditive",
        "modifyupdateintervalrandomness",
        "mustfinishanimation",
        "nevercontrolaction",
        "nomodifyheight",
        "nostack",
        "nostackignorepuid",
        "notsuspendbyattack",
        "onlyanimmove",
        "onlyattackifpathclear",
        "pausable",
        "passthrough",
        "passthroughbuildings",
        "perfectaccuracy",
        "persistent",
        "predamageaction",
        "progressiveyield",
        "projectilechainbounce",
        "randomtrainunit",
        "rangedlogic",
        "reflecthandattacks",
        "reflectrangedattacks",
        "restrictifempowered",
        "restricttogatherers",
        "restricttoidleunits",
        "restricttovalidrepairtargets",
        "restricttowater",
        "revealareachain",
        "rollingdamagefactor",
        "rollingsmash",
        "scalebycontainedunits",
        "sendunderattackevent",
        "selfdestruct",
        "shockstun",
        "showbloodhitvfx",
        "showqueuewhilewaiting",
        "singleunit",
        "singleuse",
        "singleuseplayer",
        "skipmutatespawnevents",
        "spawnignorebuildlimit",
        "spawnonanimationloop",
        "speedboost",
        "squareaura",
        "stealthanywhere",
        "stealthinshallows",
        "suspendbyattack",
        "targetedspeedmultiplier",
        "targetenemy",
        "targetenemyincludenature",
        "targetgaia",
        "targetground",
        "targetlock",
        "targetnature",
        "targetnonally",
        "targetspeedboost",
        "targetunbuilt",
        "throw",
        "transformskipplacementpush",
        "triggerafteridle",
        "volleymode",
        "workonabductedunits",
        "workonfrozenunits",
        "workonstonedamageunits",
        "workonstoneunits",
    ];

    private static readonly Dictionary<string, string> KnownFlagLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["attackaction"] = "AttackAction",
        ["active"] = "Active",
        ["activeifcontainsunits"] = "ActiveIfContainsUnits",
        ["chargeaction"] = "ChargeAction",
        ["scalebycontainedunits"] = "ScaleByContainedUnits",
        ["handlogic"] = "HandLogic",
        ["rangedlogic"] = "RangedLogic",
        ["speedboost"] = "SpeedBoost",
        ["targetspeedboost"] = "TargetSpeedBoost",
        ["persistent"] = "Persistent",
        ["singleuse"] = "SingleUse",
        ["isabductdrop"] = "IsAbductDrop",
        ["targetenemy"] = "TargetEnemy",
        ["addresourcestoinventory"] = "AddResourcesToInventory",
        ["defaultattack"] = "DefaultAttack",
        ["forceareaattacktarget"] = "ForceAreaAttackTarget",
        ["forceareaattackcenter"] = "ForceAreaAttackCenter",
    };

    private static readonly Dictionary<string, ProtoActionTypeEditorProfile> EditorProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Drone"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["persistent"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "maxrange", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase),
            DefaultFlagTags: ["persistent"]),
        ["Build"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["anim", "maxrange", "rate", "typedanim", "typedmaxrange"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase)),
        ["Repair"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["anim", "maxrange", "rate", "typedanim", "typedmaxrange"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase)),
        ["DevoteMajor"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["anim", "devotiontime", "maxrange", "modelattachment", "modelattachmentbone"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase)),
        ["DropOff"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["maxrange", "rate"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase)),
        ["SmartDropsite"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["maxrange", "rate"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase)),
        ["Pickup"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["maxrange", "rate"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase)),
        ["Hunting"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["maxrange", "rate", "typedmaxrange"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase)),
        ["Attaching"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["damage", "maxrange", "rate", "rof", "singleuse"],
            HiddenByDefaultTags: new HashSet<string>(["damagebonus"], StringComparer.OrdinalIgnoreCase)),
        ["AutoConvert"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["maxrange", "persistent", "modifyabstracttype"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase),
            DefaultFlagTags: ["persistent"]),
        ["Inline"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: [],
            HiddenByDefaultTags: new HashSet<string>(["rof", "maxrange", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase),
            DefaultFlagTags: ["isabductdrop"]),
        ["Move"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["persistent"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "maxrange", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase),
            DefaultFlagTags: ["persistent"]),
        ["TrapThrow"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: [],
            HiddenByDefaultTags: new HashSet<string>(["rof", "maxrange", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase),
            DefaultFlagTags: ["persistent", "targetenemy"]),
        ["NoWork"] = new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: ["maxrange", "rate", "typedmaxrange"],
            HiddenByDefaultTags: new HashSet<string>(["rof", "damage", "damagebonus"], StringComparer.OrdinalIgnoreCase)),
    };

    public static ProtoActionFieldDefinition GetFieldDefinition(string tag)
    {
        if (Definitions.TryGetValue(tag, out var definition))
            return definition;

        return new ProtoActionFieldDefinition(tag, HumanizeTag(tag), InferEditorKind(tag));
    }

    public static bool SupportsAutoRender(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        return !DeferredComplexFieldTags.Contains(tag.Trim());
    }

    public static ProtoActionTypeEditorProfile GetEditorProfile(string actionType)
    {
        if (!string.IsNullOrWhiteSpace(actionType) &&
            EditorProfiles.TryGetValue(actionType.Trim(), out var profile))
        {
            return profile;
        }

        return new ProtoActionTypeEditorProfile(
            DefaultVisibleTags: [],
            HiddenByDefaultTags: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> GetKnownFlagTags()
        => KnownFlagTags;

    public static string GetKnownFlagLabel(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return "";

        var normalized = tag.Trim();
        return KnownFlagLabels.TryGetValue(normalized, out var label)
            ? label
            : HumanizeTag(normalized);
    }

    public static IReadOnlyList<ProtoActionFieldDefinition> GetKnownFieldDefinitions()
        => Definitions.Values.ToList();

    public static ProtoActionTypeProfile BuildTypeProfile(ProtoBarData data, string actionType)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (!data.ProtoActionFieldUsageByType.TryGetValue(actionType, out var fieldUsage))
            fieldUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var observedFields = fieldUsage
            .Select(kvp => new ProtoActionObservedField(GetFieldDefinition(kvp.Key), kvp.Value))
            .OrderBy(x => GetDisplayPriority(x.Definition.Tag))
            .ThenByDescending(x => x.Occurrences)
            .ThenBy(x => x.Definition.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actionNames = data.ProtoActionNamesByType.TryGetValue(actionType, out var names)
            ? names
            : [];

        return new ProtoActionTypeProfile
        {
            ActionType = actionType,
            ObservedFields = observedFields,
            ActionNames = actionNames,
        };
    }

    public static Dictionary<string, ProtoActionTypeProfile> BuildTypeProfiles(ProtoBarData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var actionTypes = new HashSet<string>(data.ProtoActionFieldUsageByType.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var actionType in data.ProtoActionNamesByType.Keys)
            actionTypes.Add(actionType);

        return actionTypes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x, x => BuildTypeProfile(data, x), StringComparer.OrdinalIgnoreCase);
    }

    private static int GetDisplayPriority(string tag)
        => DisplayPriority.TryGetValue(tag, out var priority) ? priority : 10_000;

    private static ProtoActionFieldEditorKind InferEditorKind(string tag)
    {
        if (tag.EndsWith("flag", StringComparison.OrdinalIgnoreCase) ||
            tag.EndsWith("logic", StringComparison.OrdinalIgnoreCase) ||
            tag.EndsWith("action", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("active", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("persistent", StringComparison.OrdinalIgnoreCase))
        {
            return ProtoActionFieldEditorKind.Toggle;
        }

        if (tag.Contains("damage", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("rate", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("range", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("timer", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("spread", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("accuracy", StringComparison.OrdinalIgnoreCase))
        {
            return ProtoActionFieldEditorKind.Number;
        }

        return ProtoActionFieldEditorKind.Text;
    }

    private static string HumanizeTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return "";

        var normalized = tag.Replace("_", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("-", " ", StringComparison.OrdinalIgnoreCase);
        normalized = Regex.Replace(normalized, "([a-z0-9])([A-Z])", "$1 $2");
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        if (normalized.Length == 0)
            return tag;

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }
}
