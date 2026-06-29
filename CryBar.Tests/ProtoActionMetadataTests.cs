using System.Xml.Linq;
using CryBarEditor.Classes;

namespace CryBar.Tests;

public class ProtoActionMetadataTests
{
    [Fact]
    public void SetProtoActions_PreservesAdditionalProtoActionElements()
    {
        const string xml = """
            <protomods>
              <unit name="Valkyrie">
                <protoaction>
                  <name>BurstHeal</name>
                  <type>BurstHeal</type>
                  <anim>BurstHeal</anim>
                  <rof>4.0</rof>
                  <chargeaction>1</chargeaction>
                  <maxrange>18.000000</maxrange>
                  <rate type="LogicalTypeHealed">60.0</rate>
                  <modelattachment>vfx\glow\caladria_burst_heal.xml</modelattachment>
                  <modelattachmentbone>bonethatdoesntexist</modelattachmentbone>
                  <modelattachmenttimer>2222</modelattachmenttimer>
                </protoaction>
              </unit>
            </protomods>
            """;

        var (_, root) = ProtoXmlHandler.ParseProtoXmlString(xml);
        var unit = ProtoXmlHandler.GetUnitElement(root, "Valkyrie");

        Assert.NotNull(unit);

        var actions = ProtoXmlHandler.GetProtoActions(unit!);
        var action = Assert.Single(actions);
        action.Rof = "5.5";
        action.MaxRange = "20.0";

        ProtoXmlHandler.SetProtoActions(unit!, actions);

        var savedAction = Assert.Single(unit!.Elements("protoaction"));
        Assert.Equal("5.5", (string?)savedAction.Element("rof"));
        Assert.Equal("20.0", (string?)savedAction.Element("maxrange"));
        Assert.Equal("BurstHeal", (string?)savedAction.Element("anim"));
        Assert.Equal("1", (string?)savedAction.Element("chargeaction"));
        Assert.Equal("60.0", (string?)savedAction.Element("rate"));
        Assert.Equal("LogicalTypeHealed", (string?)savedAction.Element("rate")?.Attribute("type"));
        Assert.Equal(@"vfx\glow\caladria_burst_heal.xml", (string?)savedAction.Element("modelattachment"));
        Assert.Equal("bonethatdoesntexist", (string?)savedAction.Element("modelattachmentbone"));
        Assert.Equal("2222", (string?)savedAction.Element("modelattachmenttimer"));
    }

    [Fact]
    public void BuildTypeProfiles_UsesObservedProtoActionData()
    {
        const string xml = """
            <protomods>
              <unit name="UnitA">
                <protoaction>
                  <name>HandAttack</name>
                  <type>Attack</type>
                  <rof>1.0</rof>
                  <maxrange>0.75</maxrange>
                  <damage type="Hack">9</damage>
                </protoaction>
                <protoaction>
                  <name>GatherBerries</name>
                  <type>Gather</type>
                  <rate type="Food">0.8</rate>
                  <anim>Gather</anim>
                </protoaction>
              </unit>
              <unit name="UnitB">
                <protoaction>
                  <name>RangedAttack</name>
                  <type>Attack</type>
                  <rof>2.0</rof>
                  <maxrange>12</maxrange>
                  <damage type="Pierce">12</damage>
                  <damagebonus type="Hero">1.5</damagebonus>
                </protoaction>
              </unit>
            </protomods>
            """;

        var (data, _) = ProtoDataExtractor.ExtractProtoData(xml);
        var profiles = ProtoActionMetadataCatalog.BuildTypeProfiles(data);

        Assert.True(profiles.ContainsKey("Attack"));
        Assert.True(profiles.ContainsKey("Gather"));

        var attackProfile = profiles["Attack"];
        Assert.Contains("HandAttack", attackProfile.ActionNames);
        Assert.Contains("RangedAttack", attackProfile.ActionNames);
        Assert.Equal(2, attackProfile.ObservedFields.Single(x => x.Definition.Tag == "rof").Occurrences);
        Assert.Equal(2, attackProfile.ObservedFields.Single(x => x.Definition.Tag == "maxrange").Occurrences);
        Assert.Equal(2, attackProfile.ObservedFields.Single(x => x.Definition.Tag == "damage").Occurrences);
        Assert.Equal(1, attackProfile.ObservedFields.Single(x => x.Definition.Tag == "damagebonus").Occurrences);
        Assert.Contains("rof", attackProfile.GetRecommendedFieldTags());
        Assert.Contains("maxrange", attackProfile.GetRecommendedFieldTags());

        var gatherProfile = profiles["Gather"];
        Assert.Contains("GatherBerries", gatherProfile.ActionNames);
        Assert.Equal(1, gatherProfile.ObservedFields.Single(x => x.Definition.Tag == "rate").Occurrences);
        Assert.Equal("Rate", gatherProfile.ObservedFields.Single(x => x.Definition.Tag == "rate").Definition.Label);
        Assert.Equal(ProtoActionFieldEditorKind.StructuredList, gatherProfile.ObservedFields.Single(x => x.Definition.Tag == "rate").Definition.EditorKind);
    }

    [Fact]
    public void SetProtoActionSimpleFieldValue_UpdatesAndRemovesAdditionalElements()
    {
        var action = new ProtoAction();

        ProtoXmlHandler.SetProtoActionSimpleFieldValue(action, "anim", "Attack");
        ProtoXmlHandler.SetProtoActionSimpleFieldValue(action, "persistent", "1");

        Assert.Equal("Attack", ProtoXmlHandler.GetProtoActionSimpleFieldValue(action, "anim"));
        Assert.Equal("1", ProtoXmlHandler.GetProtoActionSimpleFieldValue(action, "persistent"));

        ProtoXmlHandler.SetProtoActionSimpleFieldValue(action, "anim", "Reload");
        Assert.Equal("Reload", ProtoXmlHandler.GetProtoActionSimpleFieldValue(action, "anim"));

        ProtoXmlHandler.SetProtoActionSimpleFieldValue(action, "persistent", "");
        Assert.Equal("", ProtoXmlHandler.GetProtoActionSimpleFieldValue(action, "persistent"));
        Assert.DoesNotContain(action.AdditionalElements, x => x.Name.LocalName == "persistent");
    }

    [Fact]
    public void SetProtoActionStructuredFieldEntries_UpdatesAndRemovesRepeatableLeafElements()
    {
        var action = new ProtoAction();

        var first = new ProtoActionStructuredFieldEntry { Value = "1.0" };
        first.Attributes["type"] = "Food";

        var second = new ProtoActionStructuredFieldEntry { Value = "0.5" };
        second.Attributes["type"] = "Gold";
        second.Attributes["resource"] = "Gold";

        ProtoXmlHandler.SetProtoActionStructuredFieldEntries(action, "rate", [first, second]);

        var entries = ProtoXmlHandler.GetProtoActionStructuredFieldEntries(action, "rate");
        Assert.Equal(2, entries.Count);
        Assert.Equal("Food", entries[0].Attributes["type"]);
        Assert.Equal("1.0", entries[0].Value);
        Assert.Equal("Gold", entries[1].Attributes["type"]);
        Assert.Equal("Gold", entries[1].Attributes["resource"]);
        Assert.Equal("0.5", entries[1].Value);

        ProtoXmlHandler.SetProtoActionStructuredFieldEntries(action, "rate", []);
        Assert.Empty(ProtoXmlHandler.GetProtoActionStructuredFieldEntries(action, "rate"));
        Assert.DoesNotContain(action.AdditionalElements, x => x.Name.LocalName == "rate");
    }

    [Fact]
    public void ParseProtoActionLikeElement_ParsesTacticsStyleInheritedFields()
    {
        var actionElement = XElement.Parse("""
            <action>
              <name>Heal</name>
              <type>Heal</type>
              <anim>Heal</anim>
              <modelattachment>effects\heal.xml</modelattachment>
              <modelattachmentbone>Bip01 Spine</modelattachmentbone>
              <rate type="LogicalTypeHealed">12</rate>
            </action>
            """);

        var action = ProtoXmlHandler.ParseProtoActionLikeElement(actionElement);

        Assert.Equal("Heal", action.Name);
        Assert.Equal("Heal", action.Type);
        Assert.Equal("Heal", ProtoXmlHandler.GetProtoActionSimpleFieldValue(action, "anim"));
        Assert.Equal(@"effects\heal.xml", ProtoXmlHandler.GetProtoActionSimpleFieldValue(action, "modelattachment"));
        Assert.Equal("Bip01 Spine", ProtoXmlHandler.GetProtoActionSimpleFieldValue(action, "modelattachmentbone"));

        var rates = ProtoXmlHandler.GetProtoActionStructuredFieldEntries(action, "rate");
        var rate = Assert.Single(rates);
        Assert.Equal("LogicalTypeHealed", rate.Attributes["type"]);
        Assert.Equal("12", rate.Value);
    }

    [Fact]
    public void TypedAnim_UsesTypeAttributeAndValuePayload()
    {
        var definition = ProtoActionMetadataCatalog.GetFieldDefinition("typedanim");
        Assert.Equal(ProtoActionFieldEditorKind.StructuredList, definition.EditorKind);
        Assert.Equal(["type"], definition.XmlAttributeNames);

        var action = new ProtoAction();
        var entry = new ProtoActionStructuredFieldEntry { Value = "Sow" };
        entry.Attributes["type"] = "AbstractFarm";

        ProtoXmlHandler.SetProtoActionStructuredFieldEntries(action, "typedanim", [entry]);

        var saved = Assert.Single(action.AdditionalElements, x => x.Name.LocalName == "typedanim");
        Assert.Equal("AbstractFarm", (string?)saved.Attribute("type"));
        Assert.Equal("Sow", saved.Value);

        var roundTripped = Assert.Single(ProtoXmlHandler.GetProtoActionStructuredFieldEntries(action, "typedanim"));
        Assert.Equal("AbstractFarm", roundTripped.Attributes["type"]);
        Assert.Equal("Sow", roundTripped.Value);
    }

    [Fact]
    public void SupportsAutoRender_IsFalseForDeferredComplexFields()
    {
        Assert.False(ProtoActionMetadataCatalog.SupportsAutoRender("onhiteffect"));
        Assert.True(ProtoActionMetadataCatalog.SupportsAutoRender("anim"));
        Assert.True(ProtoActionMetadataCatalog.SupportsAutoRender("rate"));
    }

    [Fact]
    public void GetEditorProfile_Drone_HidesCombatDefaultsAndShowsPersistent()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("Drone");

        Assert.Contains("persistent", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("persistent", profile.DefaultFlagTags ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maxrange", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetKnownFlagTags_IncludesPersistent()
    {
        var flags = ProtoActionMetadataCatalog.GetKnownFlagTags();

        Assert.Contains("persistent", flags);
        Assert.Contains("active", flags);
        Assert.Contains("attackaction", flags);
        Assert.Contains("chargeaction", flags);
        Assert.Contains("defaultattack", flags);
        Assert.Contains("forceareaattacktarget", flags);
        Assert.Equal("Persistent", ProtoActionMetadataCatalog.GetKnownFlagLabel("persistent"));
        Assert.Equal("SingleUse", ProtoActionMetadataCatalog.GetKnownFlagLabel("singleuse"));
        Assert.Equal("AttackAction", ProtoActionMetadataCatalog.GetKnownFlagLabel("attackaction"));
        Assert.Equal("ChargeAction", ProtoActionMetadataCatalog.GetKnownFlagLabel("chargeaction"));
        Assert.Equal("DefaultAttack", ProtoActionMetadataCatalog.GetKnownFlagLabel("defaultattack"));
        Assert.Equal("IsAbductDrop", ProtoActionMetadataCatalog.GetKnownFlagLabel("isabductdrop"));
    }

    [Fact]
    public void GetEditorProfile_DropOff_ShowsMaxRangeAndRate()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("DropOff");

        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rate", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_Build_ShowsBuildFields()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("Build");

        Assert.Contains("anim", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rate", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("typedanim", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("typedmaxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_Repair_MatchesBuildFields()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("Repair");

        Assert.Contains("anim", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rate", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("typedanim", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("typedmaxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_DevoteMajor_ShowsConfiguredFields()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("DevoteMajor");

        Assert.Contains("anim", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("devotiontime", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("modelattachment", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("modelattachmentbone", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_SmartDropsite_ShowsMaxRangeAndRate()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("SmartDropsite");

        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rate", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_Hunting_ShowsMaxRangeRateAndTypedMaxRange()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("Hunting");

        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rate", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("typedmaxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_Pickup_ShowsMaxRangeAndRate()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("Pickup");

        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rate", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_Attaching_ShowsSuggestedFieldsAndSingleUse()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("Attaching");

        Assert.Contains("damage", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rate", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("singleuse", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_AutoConvert_ShowsConfiguredFields()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("AutoConvert");

        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("persistent", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("modifyabstracttype", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("persistent", profile.DefaultFlagTags ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_Move_MatchesDroneDefaults()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("Move");

        Assert.Contains("persistent", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("persistent", profile.DefaultFlagTags ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maxrange", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_TrapThrow_DefaultsPersistentAndTargetEnemy()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("TrapThrow");

        Assert.Empty(profile.DefaultVisibleTags);
        Assert.Contains("persistent", profile.DefaultFlagTags ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("targetenemy", profile.DefaultFlagTags ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maxrange", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("TargetEnemy", ProtoActionMetadataCatalog.GetKnownFlagLabel("targetenemy"));
    }

    [Fact]
    public void GetEditorProfile_Inline_HidesCombatDefaultsAndDefaultsIsAbductDrop()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("Inline");

        Assert.Empty(profile.DefaultVisibleTags);
        Assert.Contains("isabductdrop", profile.DefaultFlagTags ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maxrange", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEditorProfile_NoWork_ShowsMaxRangeRateAndTypedMaxRange()
    {
        var profile = ProtoActionMetadataCatalog.GetEditorProfile("NoWork");

        Assert.Contains("maxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rate", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("typedmaxrange", profile.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rof", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damage", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("damagebonus", profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
    }
}
