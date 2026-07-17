using ParserUtils;
using Sprache;

namespace KarelParser.Tests;

public class KarelVariableAccessTests
{
    [Fact]
    public void FieldAccess_CapturesFieldPosition()
    {
        var access = KarelVariableAccess.GetParser().Parse("a.field");
        var fa = Assert.IsType<KarelFieldAccess>(access);

        Assert.Equal("field", fa.Field);
        Assert.Equal(0, fa.FieldStart.Line);
        Assert.Equal(2, fa.FieldStart.Column);
    }

    [Fact]
    public void ChainedFieldAccess_CapturesEachFieldPosition()
    {
        var access = KarelVariableAccess.GetParser().Parse("a.b.cee");
        var outer = Assert.IsType<KarelFieldAccess>(access);
        var inner = Assert.IsType<KarelFieldAccess>(outer.Variable);

        Assert.Equal("cee", outer.Field);
        Assert.Equal(4, outer.FieldStart.Column);
        Assert.Equal("b", inner.Field);
        Assert.Equal(2, inner.FieldStart.Column);
    }
}
