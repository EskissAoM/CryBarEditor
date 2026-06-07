using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CryBar;
using CryBar.Bar;
using CryBar.Sound;
using CryBarEditor.Classes;
using Xunit;
using Xunit.Abstractions;

namespace CryBar.Tests;

/// <summary>
/// Integration tests over real game data that guard the FMOD path-resolution feature: that
/// FMOD bank subsound names resolve to file paths via soundmanifest.xml + soundsets_*.soundset,
/// and that FMOD event variants are backed by actual subsounds in the same bank. Skipped when
/// the game isn't installed.
/// </summary>
[Collection("Integration")]
public class FmodPathDiagnosticTests
{
    readonly ITestOutputHelper _out;
    public FmodPathDiagnosticTests(ITestOutputHelper output) => _out = output;

    static string GamePath => Environment.GetEnvironmentVariable("AOMR_GAME_PATH")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game";

    static string SoundBarPath => Path.Combine(GamePath, "sound", "Sound.bar");
    static string BanksDir => Path.Combine(GamePath, "sound", "banks", "Desktop");

    static Dictionary<string, SoundManifestEntry> LoadManifest()
    {
        using var stream = File.OpenRead(SoundBarPath);
        var bar = new BarFile(stream);
        Assert.True(bar.Load(out var err), "Sound.bar load failed: " + err);

        var entry = bar.Entries!.First(e =>
            e.Name.Equals("soundmanifest.xml.XMB", StringComparison.OrdinalIgnoreCase));
        var raw = entry.ReadDataRaw(stream);
        var decompressed = BarCompression.EnsureDecompressed(raw, out _);
        var xml = ConversionHelper.ConvertXmbToXmlText(decompressed.Span)!;
        return SoundsetParser.ParseSoundManifest(xml);
    }

    static List<SoundsetDefinition> LoadSoundsets()
    {
        using var stream = File.OpenRead(SoundBarPath);
        var bar = new BarFile(stream);
        Assert.True(bar.Load(out _));

        var allDefs = new List<SoundsetDefinition>();
        foreach (var entry in bar.Entries!.Where(e =>
                     e.Name.StartsWith("soundsets_", StringComparison.OrdinalIgnoreCase) &&
                     e.Name.Contains(".soundset", StringComparison.OrdinalIgnoreCase)))
        {
            var raw = entry.ReadDataRaw(stream);
            var dec = BarCompression.EnsureDecompressed(raw, out _);
            var xml = ConversionHelper.GetTextContent(dec.Span, entry.Name);
            allDefs.AddRange(SoundsetParser.ParseSoundsetXml(xml));
        }
        return allDefs;
    }

    [SkippableFact]
    public void Manifest_ParsesManyEntries()
    {
        Skip.IfNot(File.Exists(SoundBarPath), "Sound.bar not found: " + SoundBarPath);

        var manifest = LoadManifest();
        _out.WriteLine($"soundmanifest entries: {manifest.Count}");
        Assert.True(manifest.Count > 1000, $"expected many manifest entries, got {manifest.Count}");
    }

    [SkippableFact]
    public void Subsounds_ResolveAgainstCombinedIndex()
    {
        Skip.IfNot(File.Exists(SoundBarPath), "Sound.bar not found");
        Skip.IfNot(Directory.Exists(BanksDir), "Banks dir not found");

        var index = SoundPathIndex.BuildFrom(LoadManifest().Values, LoadSoundsets());

        var bankFiles = Directory.GetFiles(BanksDir, "*.bank")
            .Where(f => !Path.GetFileName(f).StartsWith("Master", StringComparison.OrdinalIgnoreCase));

        int grandTotal = 0, grandMatched = 0;
        foreach (var bankPath in bankFiles)
        {
            using var bank = FMODBank.LoadBank(bankPath);
            var subs = bank!.Subsounds;
            if (subs.Length == 0) continue;

            int matched = subs.Count(s => index.Resolve(s.Name) != null);
            grandTotal += subs.Length;
            grandMatched += matched;
            _out.WriteLine($"{Path.GetFileName(bankPath),-22} {matched}/{subs.Length}");
        }

        double pct = 100.0 * grandMatched / grandTotal;
        _out.WriteLine($"TOTAL {grandMatched}/{grandTotal} ({pct:F1}%)");
        Assert.True(pct >= 90.0, $"combined subsound path coverage regressed: {pct:F1}% (< 90%)");
    }

    [SkippableFact]
    public void EventVariants_AreBackedBySubsoundsInSameBank()
    {
        Skip.IfNot(File.Exists(SoundBarPath), "Sound.bar not found");
        Skip.IfNot(Directory.Exists(BanksDir), "Banks dir not found");

        var soundsetByName = new Dictionary<string, SoundsetDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in LoadSoundsets()) soundsetByName[d.Name] = d;

        // banks expected to back ALL their events with in-bank subsounds
        foreach (var (bankName, minRatio) in new[]
        {
            ("norse.bank", 1.0), ("egyptian.bank", 1.0),
            ("greek.bank", 0.90), ("chinese.bank", 0.90), ("atlantean.bank", 0.90),
        })
        {
            var bankPath = Path.Combine(BanksDir, bankName);
            if (!File.Exists(bankPath)) continue;

            using var bank = FMODBank.LoadBank(bankPath);
            var subNames = new HashSet<string>(bank!.Subsounds.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

            int resolvable = 0, fullyBacked = 0;
            foreach (var ev in bank.Events)
            {
                var name = SoundsetParser.ExtractEventName(ev.Path);
                if (name == null || !soundsetByName.TryGetValue(name, out var def)) continue;
                resolvable++;

                bool all = def.Sounds.All(s =>
                    subNames.Contains(Path.GetFileNameWithoutExtension(s.Filename.Replace('/', '\\'))));
                if (all) fullyBacked++;
            }

            double ratio = resolvable == 0 ? 0 : (double)fullyBacked / resolvable;
            _out.WriteLine($"{bankName,-16} events fully backed by subsounds: {fullyBacked}/{resolvable} ({ratio:P0})");
            Assert.True(ratio >= minRatio,
                $"{bankName}: only {fullyBacked}/{resolvable} events fully backed (< {minRatio:P0})");
        }
    }
}
