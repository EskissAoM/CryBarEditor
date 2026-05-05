using CryBar.Bar;
using CryBar.Scenario;

namespace CryBar.Tests;

/// <summary>
/// Regression coverage for TR/TRG parsing across every campaign .mythscn and .trg
/// in the game install. Mirrors <see cref="IntegrationTests"/> conventions: only
/// campaign files are used as fixtures (user scenarios are not stable).
/// </summary>
[Collection("Integration")]
public class TriggerDiagnosticTests
{
    static readonly string GamePath =
        Environment.GetEnvironmentVariable("AOMR_GAME_PATH")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game";

    static readonly Lazy<(string fileName, string path, byte[] decompressed)[]> Scenarios = new(() =>
    {
        var campaignDir = Path.Combine(GamePath, "campaign");
        if (!Directory.Exists(campaignDir)) return [];

        return Directory.GetFiles(campaignDir, "*.mythscn", SearchOption.AllDirectories)
            .AsParallel()
            .Select(path =>
            {
                byte[]? data;
                try { data = BarCompression.DecompressL33t(File.ReadAllBytes(path)); }
                catch { data = null; }
                return (Path.GetFileName(path), path, data!);
            })
            .Where(t => t.Item3 != null)
            .ToArray();
    });

    static readonly Lazy<(string fileName, string path, byte[] raw)[]> Trgs = new(() =>
    {
        var campaignDir = Path.Combine(GamePath, "campaign");
        if (!Directory.Exists(campaignDir)) return [];

        return Directory.GetFiles(campaignDir, "*.trg", SearchOption.AllDirectories)
            .Select(p => (Path.GetFileName(p), p, File.ReadAllBytes(p)))
            .ToArray();
    });

    [SkippableFact]
    public void ScenarioParsing_AllFiles()
    {
        Skip.If(Scenarios.Value.Length == 0, "No scenario files found");
        var failures = Scenarios.Value
            .Where(s => !TryParse(s.decompressed))
            .Select(s => s.path)
            .ToList();
        Assert.True(failures.Count == 0,
            $"Scenario parse failures ({failures.Count}/{Scenarios.Value.Length}):\n" + string.Join("\n", failures));

        static bool TryParse(byte[] data)
        {
            try { return new ScenarioFile(data).Parsed; }
            catch { return false; }
        }
    }

    [SkippableFact]
    public void TR_SectionToXml_AllFiles()
    {
        Skip.If(Scenarios.Value.Length == 0, "No scenario files found");

        var failures = new List<string>();
        int withTr = 0;
        foreach (var s in Scenarios.Value)
        {
            var sf = new ScenarioFile(s.decompressed);
            if (!sf.Parsed) continue;
            var tr = sf.FindSection("TR");
            if (tr == null) continue;
            withTr++;

            try { _ = ScenarioFile.SectionToTriggersXml(tr); }
            catch (Exception ex) { failures.Add($"{s.fileName}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Assert.True(failures.Count == 0,
            $"TR->XML failures ({failures.Count}/{withTr}):\n" + string.Join("\n", failures));
    }

    [SkippableFact]
    public void TR_RoundtripBinary_AllFiles()
    {
        Skip.If(Scenarios.Value.Length == 0, "No scenario files found");

        var failures = new List<string>();
        int withTr = 0;
        foreach (var s in Scenarios.Value)
        {
            var sf = new ScenarioFile(s.decompressed);
            if (!sf.Parsed) continue;
            var tr = sf.FindSection("TR");
            if (tr == null) continue;
            withTr++;

            try
            {
                var rt = ScenarioFile.FromXml(sf.ToXml());
                var rtTr = rt.FindSection("TR");
                if (rtTr == null) { failures.Add($"{s.fileName}: rt missing TR"); continue; }
                if (!tr.Data.AsSpan().SequenceEqual(rtTr.Data))
                    failures.Add($"{s.fileName}: TR bytes differ orig={tr.Data.Length} rt={rtTr.Data.Length}");
            }
            catch (Exception ex) { failures.Add($"{s.fileName}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Assert.True(failures.Count == 0,
            $"TR roundtrip failures ({failures.Count}/{withTr}):\n" + string.Join("\n", failures));
    }

    [SkippableFact]
    public void Standalone_Trg_Parse_AllFiles()
    {
        Skip.If(Trgs.Value.Length == 0, "No .trg files found");
        var failures = Trgs.Value.Where(t => !new TriggerFile(t.raw).Parsed).Select(t => t.path).ToList();
        Assert.True(failures.Count == 0,
            $"Standalone .trg parse failures ({failures.Count}/{Trgs.Value.Length}):\n" + string.Join("\n", failures));
    }

    [SkippableFact]
    public void Standalone_Trg_RoundTripBytes_AllFiles()
    {
        Skip.If(Trgs.Value.Length == 0, "No .trg files found");

        var failures = new List<string>();
        foreach (var t in Trgs.Value)
        {
            var tf = new TriggerFile(t.raw);
            if (!tf.Parsed) { failures.Add($"{t.fileName}: parse failed"); continue; }
            if (!t.raw.AsSpan().SequenceEqual(tf.ToBytes()))
                failures.Add($"{t.fileName}: bytes differ");
        }
        Assert.True(failures.Count == 0,
            $"Standalone .trg byte roundtrip failures ({failures.Count}/{Trgs.Value.Length}):\n" + string.Join("\n", failures));
    }

    [SkippableFact]
    public void Standalone_Trg_XmlRoundTrip_AllFiles()
    {
        Skip.If(Trgs.Value.Length == 0, "No .trg files found");

        var failures = new List<string>();
        foreach (var t in Trgs.Value)
        {
            try
            {
                var tf = new TriggerFile(t.raw);
                if (!tf.Parsed) { failures.Add($"{t.fileName}: parse failed"); continue; }
                var rt = TriggerFile.FromXml(tf.ToXml());
                if (!t.raw.AsSpan().SequenceEqual(rt.ToBytes()))
                    failures.Add($"{t.fileName}: bytes differ after XML roundtrip");
            }
            catch (Exception ex) { failures.Add($"{t.fileName}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Assert.True(failures.Count == 0,
            $"Standalone .trg XML roundtrip failures ({failures.Count}/{Trgs.Value.Length}):\n" + string.Join("\n", failures));
    }

    [SkippableFact]
    public void TR_To_Trg_To_Parse_AllFiles()
    {
        Skip.If(Scenarios.Value.Length == 0, "No scenario files found");

        var failures = new List<string>();
        int with = 0;
        foreach (var s in Scenarios.Value)
        {
            var sf = new ScenarioFile(s.decompressed);
            if (!sf.Parsed) continue;
            var tr = sf.FindSection("TR");
            if (tr == null) continue;
            with++;

            try
            {
                var trgBytes = TriggerFile.FromScenarioSection(tr).ToBytes();
                if (!new TriggerFile(trgBytes).Parsed)
                    failures.Add($"{s.fileName}: re-parse trg failed (size {trgBytes.Length})");
            }
            catch (Exception ex) { failures.Add($"{s.fileName}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Assert.True(failures.Count == 0,
            $"TR->trg->parse failures ({failures.Count}/{with}):\n" + string.Join("\n", failures));
    }
}
