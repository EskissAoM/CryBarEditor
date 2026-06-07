using CryBar.Sound;

namespace CryBar.Tests;

public class SoundPathIndexTests
{
    [Fact]
    public void Resolve_MatchesByStem_CaseInsensitive()
    {
        var idx = new SoundPathIndex();
        idx.Add(@"music\aotg_theme\aotg_theme_calm_loop.wav");

        Assert.Equal(@"music\aotg_theme\aotg_theme_calm_loop.wav", idx.Resolve("aotg_theme_calm_loop"));
        Assert.Equal(@"music\aotg_theme\aotg_theme_calm_loop.wav", idx.Resolve("AOTG_THEME_CALM_LOOP"));
        // resolving by a name that still carries an extension also works
        Assert.Equal(@"music\aotg_theme\aotg_theme_calm_loop.wav", idx.Resolve("aotg_theme_calm_loop.wav"));
    }

    [Fact]
    public void Add_NormalizesForwardSlashes()
    {
        var idx = new SoundPathIndex();
        idx.Add("greek/vo/minotaur/minotaur_select4.wav");

        Assert.Equal(@"greek\vo\minotaur\minotaur_select4.wav", idx.Resolve("minotaur_select4"));
    }

    [Fact]
    public void Resolve_ReturnsNullForMissing()
    {
        var idx = new SoundPathIndex();
        idx.Add(@"a\b.wav");
        Assert.Null(idx.Resolve("nope"));
        Assert.Empty(idx.ResolveAll("nope"));
        Assert.False(idx.IsAmbiguous("nope"));
    }

    [Fact]
    public void Ambiguous_WhenMultiplePathsShareStem()
    {
        var idx = new SoundPathIndex();
        idx.Add(@"campaign\a\music2.mp3");
        idx.Add(@"campaign\b\music2.mp3");

        Assert.True(idx.IsAmbiguous("music2"));
        Assert.Equal(2, idx.ResolveAll("music2").Count);
        // first registered wins for the single-path Resolve
        Assert.Equal(@"campaign\a\music2.mp3", idx.Resolve("music2"));
    }

    [Fact]
    public void Add_DedupsIdenticalPaths()
    {
        var idx = new SoundPathIndex();
        idx.Add(@"x\y.wav");
        idx.Add(@"x/y.wav");   // same path, different separators
        idx.Add(@"X\Y.WAV");   // same path, different case

        Assert.Single(idx.ResolveAll("y"));
        Assert.False(idx.IsAmbiguous("y"));
    }

    [Fact]
    public void Add_IgnoresBlankAndExtensionlessPaths()
    {
        var idx = new SoundPathIndex();
        idx.Add("");
        idx.Add("   ");
        Assert.Equal(0, idx.Count);
    }

    [Fact]
    public void BuildFrom_MergesManifestAndSoundsets()
    {
        var manifest = new[]
        {
            new SoundManifestEntry { Filename = @"music\theme.wav" },
            new SoundManifestEntry { Filename = @"ui\click.wav" },
        };
        var soundsets = new List<SoundsetDefinition>
        {
            new()
            {
                Name = "GreekSelect",
                Sounds = new List<SoundsetSound>
                {
                    new() { Filename = @"greek\vo\minotaur\minotaur_select4.wav" },
                },
            },
        };

        var idx = SoundPathIndex.BuildFrom(manifest, soundsets);

        Assert.Equal(3, idx.Count);
        Assert.Equal(@"music\theme.wav", idx.Resolve("theme"));
        Assert.Equal(@"greek\vo\minotaur\minotaur_select4.wav", idx.Resolve("minotaur_select4"));
    }

    [Fact]
    public void BuildFrom_HandlesNullSources()
    {
        var idx = SoundPathIndex.BuildFrom(null, null);
        Assert.Equal(0, idx.Count);
    }

    [Fact]
    public void ResolveBest_PicksClosestDuration()
    {
        var idx = new SoundPathIndex();
        idx.Add(@"campaign\a\music2.mp3", 38817);
        idx.Add(@"campaign\b\music2.mp3", 19017);
        idx.Add(@"campaign\c\music2.mp3", 45609);

        Assert.Equal(@"campaign\b\music2.mp3", idx.ResolveBest("music2", 19017));
        Assert.Equal(@"campaign\c\music2.mp3", idx.ResolveBest("music2", 45600)); // within tolerance
        Assert.Equal(@"campaign\a\music2.mp3", idx.ResolveBest("music2", 38817));
    }

    [Fact]
    public void ResolveBest_FallsBackToFirst_WhenNoCloseMatchOrNoDuration()
    {
        var idx = new SoundPathIndex();
        idx.Add(@"campaign\a\x.mp3", 3030);
        idx.Add(@"shared\b\x.wav", 3947);

        // far from both -> no candidate within tolerance -> first
        Assert.Equal(@"campaign\a\x.mp3", idx.ResolveBest("x", 9000));
        // no usable duration provided -> first
        Assert.Equal(@"campaign\a\x.mp3", idx.ResolveBest("x", 0));

        var noLen = new SoundPathIndex();
        noLen.Add(@"a\y.wav");
        noLen.Add(@"b\y.wav");
        Assert.Equal(@"a\y.wav", noLen.ResolveBest("y", 1234)); // no durations -> first
    }

    [Fact]
    public void ResolveBest_SingleCandidate_ReturnsIt()
    {
        var idx = new SoundPathIndex();
        idx.Add(@"a\z.wav", 1000);
        Assert.Equal(@"a\z.wav", idx.ResolveBest("z", 999999));
    }

    [Fact]
    public void Add_LaterDurationEnrichesExistingPath()
    {
        var idx = new SoundPathIndex();
        idx.Add(@"a\w.wav");           // from soundset (no duration)
        idx.Add(@"a\w.wav", 2000);     // from manifest (with duration)
        idx.Add(@"b\w.wav", 5000);

        Assert.Single(idx.ResolveAll("w"), p => p.EndsWith(@"a\w.wav"));
        Assert.Equal(@"a\w.wav", idx.ResolveBest("w", 2000)); // enriched duration is used
    }
}
