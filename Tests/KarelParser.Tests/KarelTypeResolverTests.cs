using KarelParser.SymbolTable;
using ParserUtils;
using Sprache;

namespace KarelParser.Tests;

public class KarelTypeResolverTests
{
    private const string Src =
        "PROGRAM t\n" +
        "TYPE\n" +
        "inner = STRUCTURE\n" +
        "val : INTEGER\n" +
        "ENDSTRUCTURE\n" +
        "outer = STRUCTURE\n" +
        "child : inner\n" +
        "items : ARRAY[3] OF inner\n" +
        "ENDSTRUCTURE\n" +
        "VAR\n" +
        "o : outer\n" +
        "BEGIN\n" +
        "o.child.val = 1\n" +
        "END t\n";

    private static readonly Func<string, KarelDataType?> LookupO =
        name => name.Equals("o", StringComparison.OrdinalIgnoreCase)
            ? new KarelTypeName("outer", null)
            : null;

    private static KarelTypeResolver Resolver()
        => KarelTypeResolver.FromProgram(KarelProgram.GetParser().Parse(Src));

    [Fact]
    public void ResolveAccessType_NestedField_ReturnsLeafType()
    {
        var access = KarelVariableAccess.GetParser().Parse("o.child.val");
        var type = Resolver().ResolveAccessType(access, LookupO);
        Assert.Equal("INTEGER", Assert.IsType<KarelTypeName>(type).Identifier);
    }

    [Fact]
    public void ResolveAccessType_ArrayElementField_ReturnsLeafType()
    {
        var access = KarelVariableAccess.GetParser().Parse("o.items[1].val");
        var type = Resolver().ResolveAccessType(access, LookupO);
        Assert.Equal("INTEGER", Assert.IsType<KarelTypeName>(type).Identifier);
    }

    [Fact]
    public void ResolveFieldOwner_NestedField_ReturnsDeclaringStruct()
    {
        var access = (KarelFieldAccess)KarelVariableAccess.GetParser().Parse("o.child.val");
        var owner = Resolver().ResolveFieldOwner(access, LookupO);
        Assert.Equal(("inner", "val"), owner);
    }

    [Fact]
    public void ResolveFieldOwner_TopLevelField_ReturnsParentStruct()
    {
        var access = (KarelFieldAccess)KarelVariableAccess.GetParser().Parse("o.child");
        var owner = Resolver().ResolveFieldOwner(access, LookupO);
        Assert.Equal(("outer", "child"), owner);
    }

    [Fact]
    public void ResolveFieldOwner_UnknownBase_ReturnsNull()
    {
        var access = (KarelFieldAccess)KarelVariableAccess.GetParser().Parse("nope.child");
        var owner = Resolver().ResolveFieldOwner(access, LookupO);
        Assert.Null(owner);
    }

    [Fact]
    public void ResolveFieldOwner_PathNodeField_ReturnsNodeDataStruct()
    {
        var src =
            "PROGRAM t\n" +
            "TYPE\n" +
            "mynode = STRUCTURE\n" +
            "px : REAL\n" +
            "ENDSTRUCTURE\n" +
            "VAR\n" +
            "p : PATH NODEDATA = mynode\n" +
            "BEGIN\n" +
            "END t\n";
        var resolver = KarelTypeResolver.FromProgram(KarelProgram.GetParser().Parse(src));
        Func<string, KarelDataType?> lookupP = name =>
            name.Equals("p", StringComparison.OrdinalIgnoreCase)
                ? new KarelTypePath(string.Empty, "mynode")
                : null;

        // Manually construct: p[1..2].px (path access followed by field access)
        var pathAccess = new KarelPathAccess(
            new KarelIdentifier("p"),
            new KarelInteger(1),
            new KarelInteger(2));
        var fieldAccess = new KarelFieldAccess(pathAccess, "px");

        var owner = resolver.ResolveFieldOwner(fieldAccess, lookupP);
        Assert.Equal(("mynode", "px"), owner);
    }

    [Fact]
    public void ResolveFieldOwner_NonStructBase_ReturnsNull()
    {
        var resolver = KarelTypeResolver.FromProgram(
            KarelProgram.GetParser().Parse("PROGRAM t\nBEGIN\nx = 1\nEND t\n"));
        Func<string, KarelDataType?> lookupInt = _ => new KarelTypeName("INTEGER", null);

        var access = (KarelFieldAccess)KarelVariableAccess.GetParser().Parse("x.foo");
        Assert.Null(resolver.ResolveFieldOwner(access, lookupInt));
    }

    [Fact]
    public void RelativePathsToField_TopLevelField_ReturnsFieldName()
    {
        var paths = Resolver().RelativePathsToField(new KarelTypeName("outer", null), "outer", "child");
        Assert.Equal(["child"], paths.ToArray());
    }

    [Fact]
    public void RelativePathsToField_NestedAndArrayReachedField_ReturnsEveryDottedPath()
    {
        // "val" lives on "inner", which "outer" reaches both directly (child : inner)
        // and through an array (items : ARRAY OF inner). Both dotted paths qualify.
        var paths = Resolver().RelativePathsToField(new KarelTypeName("outer", null), "inner", "val");
        Assert.Equal(["child.val", "items.val"], paths.OrderBy(p => p).ToArray());
    }

    [Fact]
    public void RelativePathsToField_FieldNotInGraph_ReturnsEmpty()
    {
        Assert.Empty(Resolver().RelativePathsToField(new KarelTypeName("outer", null), "inner", "missing"));
        Assert.Empty(Resolver().RelativePathsToField(new KarelTypeName("outer", null), "nostruct", "val"));
    }

    [Fact]
    public void RelativePathsToField_IsCaseInsensitive()
    {
        var paths = Resolver().RelativePathsToField(new KarelTypeName("OUTER", null), "INNER", "VAL");
        Assert.Equal(["child.val", "items.val"], paths.OrderBy(p => p).ToArray());
    }

    [Fact]
    public void RelativePathsToField_SelfReferentialStruct_TerminatesAtShallowestPath()
    {
        // "link" contains a field of its own type; enumeration must stop at the
        // first cycle rather than emitting next.val, next.next.val, ...
        var src =
            "PROGRAM t\n" +
            "TYPE\n" +
            "link = STRUCTURE\n" +
            "next : link\n" +
            "val : INTEGER\n" +
            "ENDSTRUCTURE\n" +
            "VAR\n" +
            "n : link\n" +
            "BEGIN\n" +
            "END t\n";
        var resolver = KarelTypeResolver.FromProgram(KarelProgram.GetParser().Parse(src));

        Assert.Equal(["val"], resolver.RelativePathsToField(new KarelTypeName("link", null), "link", "val").ToArray());
    }
}
