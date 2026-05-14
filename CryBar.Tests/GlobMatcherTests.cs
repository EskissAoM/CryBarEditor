using CryBar.Utilities;

namespace CryBar.Tests;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("anything", "")]
    [InlineData("art/dwarf.tmm", "")]
    public void EmptyPattern_MatchesEverything(string input, string pattern)
    {
        Assert.True(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("art/dwarf.tmm", "art")]
    [InlineData("art/dwarf.tmm", "DWARF")]
    [InlineData("art/dwarf.tmm", ".tmm")]
    [InlineData("models/scn/foo.bar", "scn")]
    public void NoStar_DoesCaseInsensitiveSubstring(string input, string pattern)
    {
        Assert.True(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("art/dwarf.tmm", "elf")]
    [InlineData("foo.bar", "baz")]
    public void NoStar_NoMatch(string input, string pattern)
    {
        Assert.False(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("intermediate/modelcache/atlantean/units/economic/villager_atlantean/villager_atlantean_female.tmm", "villager*female")]
    [InlineData("villager_atlantean_female.tmm", "villager*female")]
    [InlineData("a/b/c/villager_x_female.txt", "villager*female")]
    public void StarBetween_MatchesAcrossFixedParts(string input, string pattern)
    {
        Assert.True(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("villager_male.tmm", "villager*female")]
    [InlineData("female_villager.tmm", "villager*female")]
    public void StarBetween_NoMatchWhenOrderWrong(string input, string pattern)
    {
        Assert.False(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("intermediate/foo/test_align_char_01.tmm", "test*")]
    [InlineData("models/foo/test_x.tmm", "test*")]
    [InlineData("a/b/TEST_x", "test*")]
    public void StarTrailing_MatchesAnywhere(string input, string pattern)
    {
        Assert.True(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("art/dwarf.tmm", "test*")]
    [InlineData("a/b/c.tmm", "elf*")]
    public void StarTrailing_NoMatchWhenSubstringAbsent(string input, string pattern)
    {
        Assert.False(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("art/dwarf.tmm", "*.tmm")]
    [InlineData("dwarf.TMM", "*.tmm")]
    [InlineData("a/b/c/d.tmm", "*.tmm")]
    [InlineData("file.tmm", "*")]
    [InlineData("", "*")]
    public void Star_BasicCases(string input, string pattern)
    {
        Assert.True(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("art/dwarf.tma", "*.tmm")]
    public void Star_NoMatch(string input, string pattern)
    {
        Assert.False(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("models/v1.0/file.tmm", "v1.0")]
    [InlineData("path/(test)/x.tmm", "(test)")]
    [InlineData("a+b/c.tmm", "a+b")]
    public void RegexMetacharsAreLiteral(string input, string pattern)
    {
        Assert.True(GlobMatcher.IsMatch(input, pattern));
    }

    [Theory]
    [InlineData("models/v10/file.tmm", "v1.0")]
    public void Dot_IsLiteralNotAnyChar(string input, string pattern)
    {
        Assert.False(GlobMatcher.IsMatch(input, pattern));
    }
}
