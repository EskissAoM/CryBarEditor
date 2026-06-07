using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CryBar;
using CryBar.Bar;
using CryBar.Sound;
using CryBarEditor.Classes;
using Xunit;
using Xunit.Abstractions;

namespace CryBar.Tests;

/// <summary>
/// Guards duration-based disambiguation: when a subsound's name maps to several manifest paths,
/// <see cref="SoundPathIndex.ResolveBest"/> must pick the path whose manifest length matches the
/// subsound's own duration. (numsamples can't be used - the FSB5 is re-encoded at another rate.)
/// </summary>
[Collection("Integration")]
public class FmodDisambiguationDiag
{
    readonly ITestOutputHelper _out;
    public FmodDisambiguationDiag(ITestOutputHelper o) => _out = o;

    static string GamePath => Environment.GetEnvironmentVariable("AOMR_GAME_PATH")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game";
    static string SoundBarPath => Path.Combine(GamePath, "sound", "Sound.bar");
    static string BanksDir => Path.Combine(GamePath, "sound", "banks", "Desktop");

    static (SoundPathIndex index, Dictionary<string, SoundManifestEntry> manifest) LoadIndex()
    {
        using var stream = File.OpenRead(SoundBarPath);
        var bar = new BarFile(stream);
        bar.Load(out _);

        var mEntry = bar.Entries!.First(e => e.Name.Equals("soundmanifest.xml.XMB", StringComparison.OrdinalIgnoreCase));
        var mDec = BarCompression.EnsureDecompressed(mEntry.ReadDataRaw(stream), out _);
        var manifest = SoundsetParser.ParseSoundManifest(ConversionHelper.ConvertXmbToXmlText(mDec.Span)!);

        var defs = new List<SoundsetDefinition>();
        foreach (var e in bar.Entries!.Where(e =>
                     e.Name.StartsWith("soundsets_", StringComparison.OrdinalIgnoreCase) &&
                     e.Name.Contains(".soundset", StringComparison.OrdinalIgnoreCase)))
        {
            var dec = BarCompression.EnsureDecompressed(e.ReadDataRaw(stream), out _);
            defs.AddRange(SoundsetParser.ParseSoundsetXml(ConversionHelper.GetTextContent(dec.Span, e.Name)));
        }

        return (SoundPathIndex.BuildFrom(manifest.Values, defs), manifest);
    }

    [SkippableFact]
    public void AmbiguousSubsounds_ResolveToDurationMatchedPath()
    {
        Skip.IfNot(File.Exists(SoundBarPath), "Sound.bar not found");
        Skip.IfNot(Directory.Exists(BanksDir), "Banks dir not found");

        var (index, manifest) = LoadIndex();

        var bankPath = Path.Combine(BanksDir, "campaign.bank");
        Skip.IfNot(File.Exists(bankPath), "campaign.bank not found");
        using var bank = FMODBank.LoadBank(bankPath);

        int ambiguous = 0, durationMatched = 0;
        foreach (var sub in bank!.Subsounds)
        {
            if (!index.IsAmbiguous(sub.Name)) continue;
            ambiguous++;

            var best = index.ResolveBest(sub.Name, sub.LengthMs);
            if (best != null && manifest.TryGetValue(best, out var e) && e.Length is double s
                && Math.Abs(s * 1000 - sub.LengthMs) <= 150)
                durationMatched++;
        }

        double pct = ambiguous == 0 ? 1 : (double)durationMatched / ambiguous;
        _out.WriteLine($"campaign.bank ambiguous subsounds: {ambiguous}, duration-matched: {durationMatched} ({pct:P1})");

        Assert.True(ambiguous > 100, $"expected many ambiguous subsounds, got {ambiguous}");
        // The subsounds ARE the manifest clips, so almost all should hit an exact-duration path.
        Assert.True(pct >= 0.95, $"duration disambiguation regressed: only {pct:P1} matched");
    }
}
