using KarelParser.SymbolTable;
using ParserUtils;
using Sprache;

namespace KarelParser.Tests;

public class KarelSymbolTableTests
{
    private static readonly Uri Uri = new("file:///t.kl");

    private static KarelSymbolTable Table()
        => new() { ProgramUri = Uri, ScopeStart = new(0, 0), ScopeEnd = new(100, 0) };

    [Fact]
    public void FieldStore_AddAndGetByName()
    {
        var table = Table();
        table.AddFieldSymbol("outer", "child", Uri, new KarelTypeName("inner", null), new TokenPosition(6, 0));

        var sym = table.GetFieldSymbol("outer", "child");
        Assert.NotNull(sym);
        Assert.Equal("child", sym!.Name);
        Assert.Equal(KarelSymbolKind.StructField, sym.Kind);
    }

    [Fact]
    public void FieldStore_SameFieldNameDifferentTypes_AreDistinct()
    {
        var table = Table();
        table.AddFieldSymbol("a", "x", Uri, new KarelTypeName("INTEGER", null), new TokenPosition(2, 0));
        table.AddFieldSymbol("b", "x", Uri, new KarelTypeName("INTEGER", null), new TokenPosition(5, 0));

        Assert.NotSame(table.GetFieldSymbol("a", "x"), table.GetFieldSymbol("b", "x"));
        Assert.Equal(2, table.GetFieldSymbol("a", "x")!.DeclarationPosition.Position.Line);
        Assert.Equal(5, table.GetFieldSymbol("b", "x")!.DeclarationPosition.Position.Line);
    }

    [Fact]
    public void GetFieldSymbolAt_MatchesReferenceAndDeclarationSpans()
    {
        var table = Table();
        // declaration token "child" at line 6, col 0 (length 5 -> cols 0..4)
        table.AddFieldSymbol("outer", "child", Uri, null, new TokenPosition(6, 0));
        // reference token "child" at line 11, col 2 (cols 2..6)
        table.AddFieldReference("outer", "child", new TokenPosition(11, 2), Uri);

        var sym = table.GetFieldSymbol("outer", "child");

        Assert.Same(sym, table.GetFieldSymbolAt(new TokenPosition(11, 4), Uri)); // inside the reference token
        Assert.Same(sym, table.GetFieldSymbolAt(new TokenPosition(6, 0), Uri));  // at the declaration token
        Assert.Null(table.GetFieldSymbolAt(new TokenPosition(11, 7), Uri));      // just past the reference token
        Assert.Null(table.GetFieldSymbolAt(new TokenPosition(50, 0), Uri));      // nowhere near
    }

    [Fact]
    public void ResolveVariableType_FindsSymbolInScopeAndParent()
    {
        var root = Table();
        root.AddSymbol("g", Uri, KarelSymbolKind.Variable, new KarelTypeName("outer", null), new TokenPosition(1, 0));
        var child = root.CreateRoutine(new(10, 0), new(20, 0), Uri);
        child.AddSymbol("loc", Uri, KarelSymbolKind.Variable, new KarelTypeName("inner", null), new TokenPosition(11, 0));

        Assert.Equal("inner", Assert.IsType<KarelTypeName>(child.ResolveVariableType("loc")).Identifier);
        Assert.Equal("outer", Assert.IsType<KarelTypeName>(child.ResolveVariableType("g")).Identifier);
        Assert.Null(child.ResolveVariableType("missing"));
    }

    private static KarelSymbolTable Build(string src)
        => KarelSymbolTableBuilder.Build(KarelProgram.GetParser().Parse(src) with { Uri = Uri });

    [Fact]
    public void Build_RecordsNestedFieldReferences()
    {
        // 0:PROGRAM t 1:TYPE 2:inner=STRUCTURE 3:val:INTEGER 4:ENDSTRUCTURE
        // 5:outer=STRUCTURE 6:child:inner 7:ENDSTRUCTURE 8:VAR 9:o:outer
        // 10:BEGIN 11:o.child.val = 5 12:END t
        var src =
            "PROGRAM t\nTYPE\ninner = STRUCTURE\nval : INTEGER\nENDSTRUCTURE\n" +
            "outer = STRUCTURE\nchild : inner\nENDSTRUCTURE\nVAR\no : outer\n" +
            "BEGIN\no.child.val = 5\nEND t\n";
        var table = Build(src);

        Assert.Single(table.GetFieldSymbol("inner", "val")!.ReferencePositions);
        Assert.Single(table.GetFieldSymbol("outer", "child")!.ReferencePositions);

        // "o.child.val": o=col0 .=1 child=2..6 .=7 val=8..10
        Assert.Same(table.GetFieldSymbol("inner", "val"), table.GetFieldSymbolAt(new TokenPosition(11, 9), Uri));
        Assert.Same(table.GetFieldSymbol("outer", "child"), table.GetFieldSymbolAt(new TokenPosition(11, 3), Uri));
    }

    [Fact]
    public void ResolveAccessSymbol_ResolvesBaseVariableFieldsArraysAndNesting()
    {
        var src =
            "PROGRAM t\nTYPE\ninner = STRUCTURE\nval : INTEGER\nENDSTRUCTURE\n" +
            "outer = STRUCTURE\nchild : inner\nitems : ARRAY[3] OF inner\nENDSTRUCTURE\n" +
            "VAR\no : outer\nBEGIN\nEND t\n";
        var table = Build(src);

        // Base variable.
        var baseSym = table.ResolveAccessSymbol("o");
        Assert.Equal(KarelSymbolKind.Variable, baseSym!.Kind);
        Assert.Equal("o", baseSym.Name);

        // Direct field, then nested field.
        Assert.Equal(KarelSymbolKind.StructField, table.ResolveAccessSymbol("o.child")!.Kind);
        Assert.Equal("child", table.ResolveAccessSymbol("o.child")!.Name);
        Assert.Equal("val", table.ResolveAccessSymbol("o.child.val")!.Name);

        // Array element is unwrapped to the field it addresses.
        Assert.Equal("val", table.ResolveAccessSymbol("o.items[1].val")!.Name);

        // Unresolvable paths.
        Assert.Null(table.ResolveAccessSymbol("nope"));
        Assert.Null(table.ResolveAccessSymbol("o.missing"));
        Assert.Null(table.ResolveAccessSymbol(""));
    }

    [Fact]
    public void GetVariablePathsToField_TwoVariablesOfSameType_PrependEachVariableName()
    {
        var src =
            "PROGRAM t\nTYPE\ninner = STRUCTURE\nval : INTEGER\nENDSTRUCTURE\n" +
            "outer = STRUCTURE\nchild : inner\nitems : ARRAY[3] OF inner\nENDSTRUCTURE\n" +
            "VAR\no : outer\no2 : outer\nBEGIN\nEND t\n";
        var table = Build(src);

        var paths = table.GetVariablePathsToField("inner", "val").OrderBy(p => p).ToArray();
        Assert.Equal(["o.child.val", "o.items.val", "o2.child.val", "o2.items.val"], paths);
    }

    [Fact]
    public void GetVariablePathsToField_TopLevelField_UsesVariableRootedPath()
    {
        var src =
            "PROGRAM t\nTYPE\nouter = STRUCTURE\nmode : INTEGER\nENDSTRUCTURE\n" +
            "VAR\ncfg : outer\nBEGIN\nEND t\n";
        var table = Build(src);

        Assert.Equal(["cfg.mode"], table.GetVariablePathsToField("outer", "mode").ToArray());
    }

    [Fact]
    public void GetVariablePathsToField_NoVariableOfType_ReturnsEmpty()
    {
        var src =
            "PROGRAM t\nTYPE\nunused = STRUCTURE\nval : INTEGER\nENDSTRUCTURE\n" +
            "used = STRUCTURE\nn : INTEGER\nENDSTRUCTURE\nVAR\nu : used\nBEGIN\nEND t\n";
        var table = Build(src);

        Assert.Empty(table.GetVariablePathsToField("unused", "val"));
    }

    [Fact]
    public void Build_SameFieldNameInTwoStructs_ReferencesDoNotCollide()
    {
        var src =
            "PROGRAM t\nTYPE\na = STRUCTURE\nx : INTEGER\nENDSTRUCTURE\n" +
            "b = STRUCTURE\nx : INTEGER\nENDSTRUCTURE\nVAR\nva : a\nvb : b\n" +
            "BEGIN\nva.x = 1\nvb.x = 2\nEND t\n";
        var table = Build(src);

        var ax = table.GetFieldSymbol("a", "x")!;
        var bx = table.GetFieldSymbol("b", "x")!;
        Assert.Single(ax.ReferencePositions);
        Assert.Single(bx.ReferencePositions);
        Assert.Equal(12, ax.ReferencePositions[0].Position.Line); // va.x
        Assert.Equal(13, bx.ReferencePositions[0].Position.Line); // vb.x
    }

    [Fact]
    public void Build_ArrayOfStructField_IsRecorded()
    {
        var src =
            "PROGRAM t\nTYPE\ninner = STRUCTURE\nval : INTEGER\nENDSTRUCTURE\n" +
            "outer = STRUCTURE\nitems : ARRAY[3] OF inner\nENDSTRUCTURE\nVAR\no : outer\n" +
            "BEGIN\no.items[1].val = 5\nEND t\n";
        var table = Build(src);

        Assert.Single(table.GetFieldSymbol("inner", "val")!.ReferencePositions);
    }

    [Fact]
    public void Build_SelfReferentialStruct_DoesNotLoop()
    {
        // link has a field of its own type; accessing one level must terminate.
        // ("node" can't be used here: it is a reserved Karel keyword.)
        var src =
            "PROGRAM t\nTYPE\nlink = STRUCTURE\nnext : link\nval : INTEGER\nENDSTRUCTURE\n" +
            "VAR\nn : link\nBEGIN\nn.next.val = 1\nEND t\n";
        var table = Build(src);

        Assert.Single(table.GetFieldSymbol("link", "val")!.ReferencePositions);
        Assert.Single(table.GetFieldSymbol("link", "next")!.ReferencePositions);
    }

    [Fact]
    public void Build_FieldFromIncludedStructType_IsRecorded()
    {
        // TestPrograms isn't copied to the test output directory (see
        // KarelParserIntegrationTests, which reads from this same absolute path),
        // so a path relative to the test assembly's working directory won't resolve.
        var mainPath = Environment.ExpandEnvironmentVariables(
            @"%UserProfile%\Projects\fanuc-lsp\Tests\KarelParser.Tests\TestPrograms\Include\sym_field_main.kl");
        var mainUri = new Uri(mainPath, UriKind.Absolute);
        var result = KarelProgram.ProcessAndParse(mainUri.ToString());
        Assert.True(result.WasSuccessful, result.Message);

        var table = result.Value.SymTable;
        Assert.Single(table.GetFieldSymbol("pose", "x")!.ReferencePositions);
    }

    [Fact]
    public void GetFieldSymbolAt_DoesNotMatchAcrossDocumentUris()
    {
        var mainPath = Environment.ExpandEnvironmentVariables(
            @"%UserProfile%\Projects\fanuc-lsp\Tests\KarelParser.Tests\TestPrograms\Include\sym_field_main.kl");
        var mainUri = new Uri(mainPath, UriKind.Absolute);
        var result = KarelProgram.ProcessAndParse(mainUri.ToString());
        Assert.True(result.WasSuccessful, result.Message);

        var table = result.Value.SymTable;
        var pose_x = table.GetFieldSymbol("pose", "x")!;
        var includedDeclPos = pose_x.DeclarationPosition.Position;

        // Declared in the included file; querying the MAIN document at those coords must NOT match it.
        Assert.NotEqual(mainUri, pose_x.DeclarationPosition.ProgramUri);
        Assert.Null(table.GetFieldSymbolAt(includedDeclPos, result.Value.Uri));
    }

    // Regression: ProcessAndParse must not trim each line before parsing.
    // Trimming stripped the leading indentation, so every declaration and
    // reference collapsed to column 0 relative to the real file, breaking every
    // LSP position (go-to-definition, hover, references).
    [Fact]
    public void ProcessAndParse_PreservesDeclarationColumns()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kl_pos_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "indent.kl");
        try
        {
            // "status" is indented by four spaces, so its declaration token
            // starts at column 4 (0-based).
            File.WriteAllText(file,
                "PROGRAM t\r\nVAR\r\n    status : INTEGER\r\nBEGIN\r\n    status = 1\r\nEND t\r\n");

            var result = KarelProgram.ProcessAndParse(new Uri(file).ToString());
            Assert.True(result.WasSuccessful, result.Message);

            var sym = result.Value.SymTable.GetTopLevelSymbol("status")!;
            Assert.Equal(2, sym.DeclarationPosition.Position.Line);
            Assert.Equal(4, sym.DeclarationPosition.Position.Column);

            // The reference on line 4 is also indented by four spaces.
            var reference = Assert.Single(sym.ReferencePositions);
            Assert.Equal(4, reference.Position.Line);
            Assert.Equal(4, reference.Position.Column);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetFieldSymbolAt_MatchesRealParsedFieldDeclaration()
    {
        var src =
            "PROGRAM t\nTYPE\nrec = STRUCTURE\nfld : INTEGER\nENDSTRUCTURE\n" +
            "VAR\nr : rec\nBEGIN\nr.fld = 1\nEND t\n";
        var table = Build(src);
        var sym = table.GetFieldSymbol("rec", "fld")!;
        var declPos = sym.DeclarationPosition.Position;
        Assert.Same(sym, table.GetFieldSymbolAt(declPos, Uri));
    }
}
