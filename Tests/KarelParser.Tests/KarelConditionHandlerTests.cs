using Sprache;

namespace KarelParser.Tests;

public class KarelConditionHandlerTests
{
    // Regression: a WHEN ... DO <assignment> ... ENDCONDITION handler used to
    // stack-overflow at parser-construction time. KarelAssignmentAction.GetParser
    // called KarelPortAssignmentAction.GetParser()/KarelVarAssignmentAction.GetParser(),
    // but those only existed as explicit static interface members, so the calls
    // bound to the inherited base KarelAssignmentAction.GetParser() and recursed
    // forever. KarelAction is only reached through WHEN, so WHEN was the trigger.
    [Theory]
    [InlineData("PROGRAM t\nBEGIN\n\tCONDITION[1]:\n\t\tWHEN ABORT DO\n\t\t\tx = 1\n\tENDCONDITION\nEND t\n")]
    [InlineData("PROGRAM t\nBEGIN\n\tCONDITION[1]:\n\t\tWHEN DIN[1] DO\n\t\t\tDOUT[2] = 1\n\tENDCONDITION\nEND t\n")]
    public void ConditionHandler_WithAssignmentAction_Parses(string src)
    {
        var r = KarelProgram.GetParser().TryParse(src);
        Assert.True(r.WasSuccessful, $"{r.Message} @ {r.Remainder.Line}:{r.Remainder.Column}");
    }

    // Regression: a keyword-terminated action value (TRUE/FALSE) skips its
    // trailing newline via Token(), so ENDCONDITION must not depend on a literal
    // line break being present after the last action.
    [Fact]
    public void ConditionHandler_BooleanAction_Parses()
    {
        var src = "PROGRAM t\nBEGIN\n\tCONDITION[1]:\n\t\tWHEN ABORT DO\n\t\t\tx = TRUE\n\tENDCONDITION\nEND t\n";
        var r = KarelProgram.GetParser().TryParse(src);
        Assert.True(r.WasSuccessful, $"{r.Message} @ {r.Remainder.Line}:{r.Remainder.Column}");
    }

    // Regression: the WHEN body may contain interspersed comments and blank
    // lines between DO and the actions (as in the manual's PTH_MOVE example).
    [Fact]
    public void ConditionHandler_WithCommentsAndBlankLines_Parses()
    {
        var src =
            "PROGRAM t\nBEGIN\n\tCONDITION[1]:\n\t\tWHEN ABORT DO          -- trailing comment\n\n\t\t\t-- a standalone comment line\n\n\t\t\tx = 1\n\tENDCONDITION\nEND t\n";
        var r = KarelProgram.GetParser().TryParse(src);
        Assert.True(r.WasSuccessful, $"{r.Message} @ {r.Remainder.Line}:{r.Remainder.Column}");
    }
}