using Sprache;

namespace KarelParser.Tests;

public class KarelParserRegressionTests
{
    // Regression (REG_EX): ';' acts as a statement separator, so it may appear as
    // an empty statement immediately after THEN or ELSE, before the first real
    // statement of the body.
    [Fact]
    public void IfThenElse_WithEmptyStatementSemicolons_Parses()
    {
        var src = "PROGRAM t\nBEGIN\n\tIF (x = 1) THEN; y = 1\n\tELSE ; y = 2\n\tENDIF\nEND t\n";
        var r = KarelProgram.GetParser().TryParse(src);
        Assert.True(r.WasSuccessful, $"{r.Message} @ {r.Remainder.Line}:{r.Remainder.Column}");
    }

    // Regression (LIST_EX): a multi-variable declaration may carry an inline
    // comment after the comma that separates each name.
    [Fact]
    public void VarDeclaration_WithInlineCommentsBetweenNames_Parses()
    {
        var src = "PROGRAM t\nVAR\n\tcases,      -- one\n\tmax_number, -- two\n\tseed : INTEGER\nBEGIN\n\tseed = 1\nEND t\n";
        var r = KarelProgram.GetParser().TryParse(src);
        Assert.True(r.WasSuccessful, $"{r.Message} @ {r.Remainder.Line}:{r.Remainder.Column}");
    }

    // Regression (CHG_DATA): ABS is a built-in function and must be callable in
    // expression position (it must not be treated as a reserved keyword).
    [Theory]
    [InlineData("ABS(x)")]
    [InlineData("(ABS(x) DIV 5) MOD 5")]
    [InlineData("BYNAME('', src_var, indx)")]
    public void ReservedBuiltins_CallableInExpression(string expr)
    {
        var r = KarelExpression.GetParser().End().TryParse(expr);
        Assert.True(r.WasSuccessful, $"{expr} => {r.Message}");
    }

    // Regression (CPY_PTH): a literal single quote inside a string is written as
    // a doubled quote ('' -> ').
    [Fact]
    public void String_DoubledQuoteEscape_Parses()
    {
        var r = KarelExpression.GetParser().End().TryParse("'path''s content'");
        Assert.True(r.WasSuccessful, r.Message);
        Assert.Equal("path's content", Assert.IsType<KarelString>(r.Value).Value);
    }

    // Regression (DOUT_EX): a bare routine call is a valid condition-handler action.
    [Fact]
    public void ConditionHandler_WithRoutineCallAction_Parses()
    {
        var src = "PROGRAM t\nBEGIN\n\tCONDITION[1]:\n\t\tWHEN ABORT DO\n\t\t\tinit_port\n\tENDCONDITION\nEND t\n";
        var r = KarelProgram.GetParser().TryParse(src);
        Assert.True(r.WasSuccessful, $"{r.Message} @ {r.Remainder.Line}:{r.Remainder.Column}");
    }

    // Regression (DCLST_EX): SELECT tolerates an inline comment after OF, a
    // comment-only case body, and "ELSE :" with a space before the colon.
    [Fact]
    public void Select_WithCommentsAndSpacedElse_Parses()
    {
        var src =
            "PROGRAM t\nBEGIN\n\tSELECT k OF          -- pick\n\t\tCASE (1):\t-- nothing yet\n\t\tCASE (2):\n\t\t\tx = 1\n\t\tELSE :\n\t\t\tx = 2\n\tENDSELECT\nEND t\n";
        var r = KarelProgram.GetParser().TryParse(src);
        Assert.True(r.WasSuccessful, $"{r.Message} @ {r.Remainder.Line}:{r.Remainder.Column}");
    }
}
