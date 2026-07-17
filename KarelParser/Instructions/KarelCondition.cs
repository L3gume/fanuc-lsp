using ParserUtils;
using Sprache;
using KarelParser;

namespace KarelParser.Instructions;

public sealed record KarelCondition(KarelExpression HandlerNumber, KarelWith? With, List<KarelWhen> When)
    : KarelStatement, IKarelParser<KarelStatement>
{
    // WHEN clauses are separated by line breaks in the source, but line breaks
    // are not reliably present between the last action and ENDCONDITION: a
    // keyword-terminated action value (e.g. `x = TRUE`) skips the trailing
    // newline via Token(). Rather than depend on a literal LineBreak, wrap each
    // WHEN in IgnoreComments (which absorbs surrounding whitespace, blank lines,
    // and comments) and collect clauses until ENDCONDITION.
    public new static Parser<KarelStatement> GetParser()
        => from kw in KarelCommon.Keyword("CONDITION")
           from handler in KarelExpression.GetParser().BetweenBrackets()
           from sep in KarelCommon.Keyword(":")
           from with in KarelWith.GetParser().Optional()
           from cond in KarelWhen.GetParser().IgnoreComments().AtLeastOnce()
           from kww in KarelCommon.Keyword("ENDCONDITION")
           select new KarelCondition(handler, with.GetOrElse(null), cond.ToList());
}

public sealed record KarelWith(List<KarelWithAssignment> Assignments) : WithPosition, IKarelParser<KarelWith>
{
    public static Parser<KarelWith> GetParser()
        => from kw in KarelCommon.Keyword("WITH")
           from assignments in KarelWithAssignment.GetParser()
                .WithPos()
                .DelimitedBy(Parse.Char(',').Then(_ => KarelCommon.LineBreak.Optional()))
           select new KarelWith(assignments.ToList());
}

public sealed record KarelWithAssignment(string Indentifier, KarelExpression Expr)
    : WithPosition, IKarelParser<KarelWithAssignment>
{
    public static Parser<KarelWithAssignment> GetParser()
        => from sysIdent in KarelCommon.Identifier.WithPosition()
           from sep in KarelCommon.Keyword("=")
           from expr in KarelExpression.GetParser()
           select new KarelWithAssignment(sysIdent.Value, expr)
           {
               Start = sysIdent.Start,
               End = sysIdent.End
           };
}

public record KarelWhen(KarelWhenCondition Condition, List<KarelAction> Actions) : WithPosition, IKarelParser<KarelWhen>
{
    // Actions may be separated by commas and/or line breaks, and the body can
    // contain interspersed comments and blank lines (common in the manual's
    // examples). Wrapping each action in IgnoreComments absorbs that trivia; an
    // optional comma between actions covers the comma-separated form.
    public static Parser<KarelWhen> GetParser()
        => from kw in KarelCommon.Keyword("WHEN")
           from cond in KarelWhenCondition.GetParser().WithPos()
           from kww in KarelCommon.Keyword("DO")
           from actions in KarelAction.GetParser().IgnoreComments()
                .DelimitedBy(KarelCommon.Keyword(",").Optional(), 1, null)
           select new KarelWhen(cond, actions.ToList());
}
public record KarelWhenCondition : WithPosition, IKarelParser<KarelWhenCondition>
{
    public static Parser<KarelWhenCondition> GetParser()
        => KarelWhenOr.GetParser()
            .Or(KarelWhenAnd.GetParser());
}

public sealed record KarelWhenOr(List<KarelGlobalCondition> Conditions) : KarelWhenCondition, IKarelParser<KarelWhenCondition>
{
    public new static Parser<KarelWhenCondition> GetParser()
        => from conds in KarelGlobalCondition.GetParser().DelimitedBy(KarelCommon.Keyword("OR"))
           select new KarelWhenOr(conds.ToList());
}

public sealed record KarelWhenAnd(List<KarelGlobalCondition> Conditions) : KarelWhenCondition, IKarelParser<KarelWhenCondition>
{
    public new static Parser<KarelWhenCondition> GetParser()
        => from conds in KarelGlobalCondition.GetParser().DelimitedBy(KarelCommon.Keyword("AND"))
           select new KarelWhenAnd(conds.ToList());
}

