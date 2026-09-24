using Culprit.Core.Attribution;
using Xunit;

namespace Culprit.Tests;

public class AttributionTests
{
    [Theory]
    [InlineData("PlatePeek.Plugin::OnUpdate", "PlatePeek.Plugin")]
    [InlineData("Some.Deep.Namespace.Type+Nested::Handler", "Some.Deep.Namespace.Type+Nested")]
    public void HandlerKey_TypeName_TakesEverythingBeforeTheSeparator(string key, string expected)
    {
        Assert.Equal(expected, HandlerKeyParser.TypeName(key));
    }

    [Theory]
    [InlineData("::StaticHandler")]
    [InlineData("NoSeparatorAtAll")]
    [InlineData("")]
    public void HandlerKey_TypeName_IsNullForStaticOrMalformed(string key)
    {
        Assert.Null(HandlerKeyParser.TypeName(key));
    }

    [Fact]
    public void TypeIndex_ResolvesKnownTypeToPlugin()
    {
        var index = new TypeIndex();
        index.Add("PlatePeek.Plugin", "PlatePeek");
        index.Add("PlatePeek.Services", "PlatePeek");

        Assert.Equal("PlatePeek", index.ResolveType("PlatePeek.Services"));
        Assert.Equal("PlatePeek", index.ResolveHandlerKey("PlatePeek.Plugin::OnUpdate"));
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void TypeIndex_UnknownTypeAndStaticHandler_AreNull()
    {
        var index = new TypeIndex();
        index.Add("PlatePeek.Plugin", "PlatePeek");

        Assert.Null(index.ResolveType("Other.Plugin"));
        Assert.Null(index.ResolveHandlerKey("Other.Plugin::OnUpdate"));
        Assert.Null(index.ResolveHandlerKey("::Static"));
    }

    [Fact]
    public void TypeIndex_Clear_ForgetsEverything()
    {
        var index = new TypeIndex();
        index.Add("A.B", "A");
        index.Clear();

        Assert.Equal(0, index.Count);
        Assert.Null(index.ResolveType("A.B"));
    }

    [Fact]
    public void TypeIndex_ConflictingAdd_IsAmbiguous()
    {
        var index = new TypeIndex();
        index.Add("Shared.Type", "First");
        index.Add("Shared.Type", "Second");

        Assert.Null(index.ResolveType("Shared.Type"));
        Assert.Null(index.ResolveHandlerKey("Shared.Type::OnUpdate"));

        var same = new TypeIndex();
        same.Add("PlatePeek.Plugin", "PlatePeek");
        same.Add("PlatePeek.Plugin", "PlatePeek");

        Assert.Equal("PlatePeek", same.ResolveType("PlatePeek.Plugin"));
    }

    [Theory]
    [InlineData("UiBuilder(PlatePeek)", HitchKind.Draw, "PlatePeek")]
    [InlineData("UiBuilder(Some Plugin With Spaces)", HitchKind.Draw, "Some Plugin With Spaces")]
    [InlineData("PlatePeek.Plugin::OnUpdate", HitchKind.Update, "PlatePeek.Plugin::OnUpdate")]
    [InlineData("FrameworkUpdate", HitchKind.Frame, "FrameworkUpdate")]
    [InlineData("GameNetworkUp", HitchKind.Unknown, "GameNetworkUp")]
    [InlineData("", HitchKind.Unknown, "")]
    public void HitchName_Parse(string name, HitchKind kind, string target)
    {
        var parsed = HitchNameParser.Parse(name);

        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(target, parsed.Target);
    }
}
