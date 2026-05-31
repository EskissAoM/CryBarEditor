using System;
using System.IO;
using System.Linq;
using CryBarEditor.Classes;
using Xunit;

namespace CryBar.Tests;

/// <summary>
/// Verifies that asset/streaming banks (e.g. music.bank) expose their embedded FSB5
/// audio as <see cref="FMODSubsound"/>s even though they define no FMOD events.
/// </summary>
[Collection("Integration")]
public class FmodSubsoundTests
{
    static string GamePath => Environment.GetEnvironmentVariable("AOMR_GAME_PATH")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game";

    static string BanksDir => Path.Combine(GamePath, "sound", "banks", "Desktop");

    [SkippableFact]
    public void MusicBank_HasNoEvents_ButExposesSubsounds()
    {
        var path = Path.Combine(BanksDir, "music.bank");
        Skip.IfNot(File.Exists(path), "music.bank not found: " + path);

        using var bank = FMODBank.LoadBank(path);
        Assert.NotNull(bank);

        // music.bank is an asset bank: audio lives in FSB5, not in events.
        Assert.Empty(bank!.Events);
        Assert.NotEmpty(bank.Subsounds);

        Assert.All(bank.Subsounds, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
        Assert.Contains(bank.Subsounds, s => s.LengthMs > 1000);
    }

    // Regression: campaign.bank (~492MB) carries a coincidental "FSB5" byte match at
    // offset 0 ahead of its real container at 124. Feeding that bogus offset to
    // createSound previously crashed the runtime (ExecutionEngineException). Loading it
    // here would hard-crash the test host if the header validation regressed.
    [SkippableFact]
    public void LargeAssetBank_LoadsWithoutCrashing()
    {
        var path = Path.Combine(BanksDir, "campaign.bank");
        Skip.IfNot(File.Exists(path), "campaign.bank not found: " + path);

        using var bank = FMODBank.LoadBank(path);
        Assert.NotNull(bank);
        Assert.NotEmpty(bank!.Subsounds);
    }

    [SkippableFact]
    public void EventBank_StillLoadsEvents()
    {
        var path = Path.Combine(BanksDir, "greek.bank");
        Skip.IfNot(File.Exists(path), "greek.bank not found: " + path);

        using var bank = FMODBank.LoadBank(path);
        Assert.NotNull(bank);
        Assert.NotEmpty(bank!.Events);
    }

    // Regression: subsound Export must reproduce the full clip. Two past bugs: (1) real-time
    // NRT rendering looped a small window into minutes; (2) decoding without ACCURATETIME
    // dropped the codec's encoder delay/padding off the head and tail. Both are most visible
    // on a SHORT clip, so use campaign.bank's shortest subsound and require a tight match.
    [SkippableFact]
    public void Subsound_Export_DurationMatchesReportedLength()
    {
        var path = Path.Combine(BanksDir, "campaign.bank");
        Skip.IfNot(File.Exists(path), "campaign.bank not found: " + path);

        using var bank = FMODBank.LoadBank(path);
        var sub = bank!.Subsounds.Where(s => s.LengthMs > 1000).OrderBy(s => s.LengthMs).First();

        var outPath = Path.Combine(Path.GetTempPath(), "crybar_subsound_export_test.wav");
        try
        {
            sub.Export(outPath);
            Assert.True(File.Exists(outPath));

            // The head/tail-loss bug dropped ~5-7% on short clips; a correct decode matches
            // the reported length within ~1%. 0.97 cleanly separates the two.
            var durationMs = ReadWavDurationMs(outPath);
            Assert.InRange(durationMs, sub.LengthMs * 0.97, sub.LengthMs * 1.03);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    static double ReadWavDurationMs(string wavPath)
    {
        using var fs = File.OpenRead(wavPath);
        using var br = new BinaryReader(fs);

        br.ReadBytes(12); // "RIFF" + size + "WAVE"

        int channels = 1, sampleRate = 44100, bits = 16, dataSize = 0;
        while (fs.Position + 8 <= fs.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
            int size = br.ReadInt32();
            if (id == "fmt ")
            {
                br.ReadInt16();           // audio format
                channels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32();           // byte rate
                br.ReadInt16();           // block align
                bits = br.ReadInt16();
                if (size > 16) br.ReadBytes(size - 16);
            }
            else if (id == "data")
            {
                dataSize = size;
                break;
            }
            else
            {
                br.ReadBytes(size);
            }
        }

        double bytesPerSec = sampleRate * channels * (bits / 8.0);
        return dataSize / bytesPerSec * 1000.0;
    }
}
