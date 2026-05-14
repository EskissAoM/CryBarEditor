using CryBar.Export;

namespace CryBar.Tests;

public class MaterialExporterTests
{
    [Theory]
    [InlineData("Masks")]
    [InlineData("Mask1")]
    [InlineData("Masks1")]
    [InlineData("masks")]
    [InlineData("MASKS1")]
    public void IsMasks1Role_AcceptsKnownVariants(string name)
    {
        Assert.True(MaterialExporter.IsMasks1Role(name));
    }

    [Theory]
    [InlineData("Masks2")]
    [InlineData("Mask2")]
    [InlineData("masks2")]
    [InlineData("MASK2")]
    public void IsMasks2Role_AcceptsKnownVariants(string name)
    {
        Assert.True(MaterialExporter.IsMasks2Role(name));
    }

    [Theory]
    [InlineData("BaseColor")]
    [InlineData("Diffuse")]
    [InlineData("Normals")]
    [InlineData("Normal")]
    [InlineData("")]
    [InlineData("Unknown")]
    public void IsMasks1Role_RejectsOtherRoles(string name)
    {
        Assert.False(MaterialExporter.IsMasks1Role(name));
    }

    [Theory]
    [InlineData("BaseColor")]
    [InlineData("Normals")]
    [InlineData("Masks")]
    [InlineData("Mask1")]
    [InlineData("Masks1")]
    public void IsMasks2Role_RejectsOtherRoles(string name)
    {
        Assert.False(MaterialExporter.IsMasks2Role(name));
    }
}
