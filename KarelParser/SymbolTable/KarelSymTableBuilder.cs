using KarelParser.Conditions;
using KarelParser.Instructions;

namespace KarelParser.SymbolTable;

public static class KarelSymbolTableBuilder
{
    public static KarelSymbolTable Build(KarelProgram program)
    {
        var table = new KarelSymbolTable
        {
            ProgramUri = program.Uri,
            ScopeStart = program.Start,
            ScopeEnd = program.End
        };
        table.Resolver = KarelTypeResolver.FromProgram(program);

        foreach (var incl in program.TranslatorDirectives.OfType<KarelIncludeDirective>())
        {
            foreach (var decl in incl.Declarations)
            {
                TraverseDeclaration(decl, table, incl.Uri);
            }
            foreach (var routine in incl.Routines)
            {
                TraverseRoutine(routine, table, incl.Uri);
            }
        }
        foreach (var decl in program.Declarations)
        {
            TraverseDeclaration(decl, table, program.Uri!);
        }
        foreach (var routine in program.Routines)
        {
            TraverseRoutine(routine, table, program.Uri!);
        }
        foreach (var stmt in program.Statements)
        {
            TraverseStatement(stmt, table, program.Uri!);
        }

        return table;
    }

    private static void TraverseDeclaration(KarelDeclaration decl, KarelSymbolTable table, Uri contextUri)
    {
        switch (decl)
        {
            case KarelVariableDeclaration d:
                foreach (var v in d.Variable)
                {
                    table.AddSymbol(v.Identifier, contextUri, KarelSymbolKind.Variable, v.Type, v.Start);
                    if (v.Type is KarelTypeName typeName)
                    {
                        table.AddReference(typeName.Identifier, typeName.Start, contextUri);
                    }
                }
                break;
            case KarelConstantDeclaration d:
                foreach (var c in d.Constants)
                {
                    table.AddSymbol(c.Identifier, contextUri, KarelSymbolKind.Constant, new KarelTypeName("INTEGER", 0), c.Start);
                }
                break;
            case KarelTypeDeclaration d:
                foreach (var t in d.Type)
                {
                    table.AddSymbol(t.Identifier, contextUri, KarelSymbolKind.Type, t.Type, t.Start);
                    TraverseUserType(t.Type, t.Identifier, table, contextUri);
                }
                break;
        }
    }

    private static void TraverseUserType(KarelUserType userType, string owningTypeName, KarelSymbolTable table, Uri contextUri)
    {
        switch (userType)
        {
            case KarelStructure s:
                foreach (var field in s.Fields)
                {
                    table.AddFieldSymbol(owningTypeName, field.Identifier, contextUri, field.Type, field.Start);
                    if (field.Type is KarelTypeName typeName)
                    {
                        table.AddReference(typeName.Identifier, typeName.Start, contextUri);
                    }
                }
                break;
            case KarelDataType _:
                break;
        }
    }

    private static void TraverseRoutine(KarelRoutine routine, KarelSymbolTable table, Uri contextUri)
    {
        table.AddSymbol(routine.Identifier, contextUri, KarelSymbolKind.Routine, routine.ReturnType, routine.Start);
        var scope = table.CreateRoutine(routine.Start, routine.End, contextUri);
        foreach (var arg in routine.Args)
        {
            scope.AddSymbol(arg.Identifier, contextUri, KarelSymbolKind.Variable, arg.Type, arg.Start);
        }
        switch (routine.Body)
        {
            case KarelRoutineBody body:
                foreach (var decl in body.Locals)
                {
                    TraverseDeclaration(decl, scope, contextUri);
                }
                foreach (var stmt in body.Body)
                {
                    TraverseStatement(stmt, scope, contextUri);
                }
                break;
            case KarelFromBody _:
                break;
        }
    }

    private static void TraverseStatement(KarelStatement stmt, KarelSymbolTable table, Uri contextUri)
    {
        switch (stmt)
        {
            case KarelAssignment s:
                TraverseExpression(s.Variable, table, contextUri);
                TraverseExpression(s.Expr, table, contextUri);
                break;
            case KarelCall s:
                table.AddReference(s.Identifier, s.Start, contextUri);
                foreach (var arg in s.Args)
                {
                    TraverseExpression(arg, table, contextUri);
                }
                break;
            case KarelIfThenElse s:
                TraverseExpression(s.Expr, table, contextUri);
                foreach (var s2 in s.Body)
                {
                    TraverseStatement(s2, table, contextUri);
                }

                foreach (var s2 in s.Else)
                {
                    TraverseStatement(s2, table, contextUri);
                }
                break;
            case KarelIfThen s:
                TraverseExpression(s.Expr, table, contextUri);
                foreach (var s2 in s.Body)
                {
                    TraverseStatement(s2, table, contextUri);
                }
                break;
            case KarelFor s:
                TraverseExpression(s.InitialValue, table, contextUri);
                TraverseExpression(s.TargetValue, table, contextUri);
                foreach (var s2 in s.Body)
                {
                    TraverseStatement(s2, table, contextUri);
                }
                break;
            case KarelRepeat s:
                foreach (var s2 in s.Body)
                {
                    TraverseStatement(s2, table, contextUri);
                }
                TraverseExpression(s.Expr, table, contextUri);
                break;
            case KarelWhile s:
                TraverseExpression(s.Expr, table, contextUri);
                foreach (var s2 in s.Body)
                {
                    TraverseStatement(s2, table, contextUri);
                }
                break;
            case KarelSelect s:
                TraverseExpression(s.Expr, table, contextUri);
                foreach (var c in s.Cases)
                {
                    TraverseCase(c, table, contextUri);
                }
                if (s.ElseCase != null)
                {
                    TraverseCase(s.ElseCase, table, contextUri);
                }
                break;
            case KarelUsing s:
                foreach (var v in s.Variables)
                {
                    TraverseExpression(v, table, contextUri);
                }
                foreach (var s2 in s.Body)
                {
                    TraverseStatement(s2, table, contextUri);
                }
                break;
            case KarelReturn s:
                if (s.Expr != null)
                {
                    TraverseExpression(s.Expr, table, contextUri);
                }
                break;
            case KarelPause s:
                if (s.TaskNumber != null)
                {
                    TraverseExpression(s.TaskNumber, table, contextUri);
                }
                break;
            case KarelDelay s:
                TraverseExpression(s.Expr, table, contextUri);
                break;
            case KarelSignal s:
                TraverseExpression(s.Number, table, contextUri);
                break;
            case KarelPulse s:
                TraverseExpression(s.Index, table, contextUri);
                TraverseExpression(s.Time, table, contextUri);
                break;
            case KarelWait s:
                TraverseCondition(s.Condition, table, contextUri);
                break;
            case KarelConnectTimer s:
                table.AddReference(s.Identifier, s.Start, contextUri);
                break;
            case KarelDisconnectTimer s:
                table.AddReference(s.Identifier, s.Start, contextUri);
                break;
            case KarelCondition s:
                TraverseExpression(s.HandlerNumber, table, contextUri);
                if (s.With != null)
                {
                    foreach (var a in s.With.Assignments)
                    {
                        TraverseExpression(a.Expr, table, contextUri);
                    }
                }
                foreach (var w in s.When)
                {
                    TraverseWhen(w, table, contextUri);
                }
                break;
            case KarelEnable s:
                TraverseExpression(s.Expr, table, contextUri);
                break;
            case KarelDisable s:
                TraverseExpression(s.Expr, table, contextUri);
                break;
            case KarelPurge s:
                TraverseExpression(s.Expr, table, contextUri);
                break;
            case KarelRead s:
                if (s.Variable != null)
                {
                    TraverseExpression(s.Variable, table, contextUri);
                }
                foreach (var item in s.Items)
                {
                    TraverseItem(item, table, contextUri);
                }
                break;
            case KarelWrite s:
                if (s.Variable != null)
                {
                    TraverseExpression(s.Variable, table, contextUri);
                }
                foreach (var item in s.Items)
                {
                    TraverseItem(item, table, contextUri);
                }
                break;
            case KarelOpenFile s:
                TraverseExpression(s.File, table, contextUri);
                TraverseExpression(s.Usage, table, contextUri);
                TraverseExpression(s.Spec, table, contextUri);
                break;
            case KarelOpenHand s:
                TraverseExpression(s.Hand, table, contextUri);
                break;
            case KarelCloseFile s:
                TraverseExpression(s.File, table, contextUri);
                break;
            case KarelCloseHand s:
                TraverseExpression(s.Hand, table, contextUri);
                break;
            case KarelRelaxHand s:
                TraverseExpression(s.Hand, table, contextUri);
                break;
            case KarelCancelFile s:
                TraverseExpression(s.File, table, contextUri);
                break;
            case KarelAbort _:
            case KarelCancel _:
            case KarelHold _:
            case KarelUnhold _:
            case KarelStop _:
            case KarelResume _:
            case KarelRelease _:
            case KarelAttach _:
            case KarelGoto _:
            case KarelLabel _:
                break;
        }
    }

    private static void TraverseExpression(KarelExpression expr, KarelSymbolTable table, Uri contextUri)
    {
        switch (expr)
        {
            case KarelIdentifier id:
                table.AddReference(id.Identifier, id.Start, contextUri);
                break;
            case KarelFieldAccess fa:
                TraverseExpression(fa.Variable, table, contextUri);
                if (table.Resolver?.ResolveFieldOwner(fa, table.ResolveVariableType) is { } owner)
                {
                    table.AddFieldReference(owner.OwningType, owner.Field, fa.FieldStart, contextUri);
                }
                break;
            case KarelArrayAccess aa:
                TraverseExpression(aa.Variable, table, contextUri);
                foreach (var idx in aa.Indices)
                {
                    TraverseExpression(idx, table, contextUri);
                }
                break;
            case KarelPathAccess pa:
                TraverseExpression(pa.Variable, table, contextUri);
                TraverseExpression(pa.StartNode, table, contextUri);
                TraverseExpression(pa.EndNode, table, contextUri);
                break;
            case KarelFunctionCall f:
                table.AddReference(f.Identifier, f.Start, contextUri);
                foreach (var arg in f.Args)
                {
                    TraverseExpression(arg, table, contextUri);
                }
                break;
            case KarelComparisonExpression c:
                TraverseExpression(c.Lhs, table, contextUri);
                TraverseExpression(c.Rhs, table, contextUri);
                break;
            case KarelSumBinary s:
                TraverseExpression(s.Lhs, table, contextUri);
                TraverseExpression(s.Rhs, table, contextUri);
                break;
            case KarelProductBinary p:
                TraverseExpression(p.Lhs, table, contextUri);
                TraverseExpression(p.Rhs, table, contextUri);
                break;
            case KarelPositionBinary p:
                TraverseExpression(p.Lhs, table, contextUri);
                TraverseExpression(p.Rhs, table, contextUri);
                break;
            case KarelNotExpression n:
                TraverseExpression(n.Expr, table, contextUri);
                break;
            case KarelUnaryMinus m:
                TraverseExpression(m.Expr, table, contextUri);
                break;
            case KarelInteger _:
            case KarelReal _:
            case KarelString _:
            case KarelBool _:
                break;
        }
    }

    private static void TraverseCondition(KarelGlobalCondition cond, KarelSymbolTable table, Uri contextUri)
    {
        switch (cond)
        {
            case KarelErrorCondition c:
                TraverseExpression(c.Number, table, contextUri);
                break;
            case KarelEventCondition c:
                TraverseExpression(c.Number, table, contextUri);
                break;
            case KarelSemaphoreCondition c:
                TraverseExpression(c.Number, table, contextUri);
                break;
            case KarelAbortCondition c:
                if (c.ProgramNumber != null)
                {
                    TraverseExpression(c.ProgramNumber, table, contextUri);
                }
                break;
            case KarelPauseCondition c:
                if (c.ProgramNumber != null)
                {
                    TraverseExpression(c.ProgramNumber, table, contextUri);
                }
                break;
            case KarelContinueCondition c:
                if (c.ProgramNumber != null)
                {
                    TraverseExpression(c.ProgramNumber, table, contextUri);
                }
                break;
            case KarelPowerUpCondition _:
                break;
            case KarelComparisonCondition c:
                TraverseExpression(c.Variable, table, contextUri);
                TraverseExpression(c.Expr, table, contextUri);
                break;
            case KarelPortCondition c:
                TraverseExpression(c.Index, table, contextUri);
                break;
            case KarelAndCondition c:
                foreach (var sub in c.Conditions)
                {
                    TraverseCondition(sub, table, contextUri);
                }
                break;
            case KarelOrCondition c:
                foreach (var sub in c.Conditions)
                {
                    TraverseCondition(sub, table, contextUri);
                }
                break;
        }
    }

    private static void TraverseWhen(KarelWhen when, KarelSymbolTable table, Uri contextUri)
    {
        TraverseWhenCondition(when.Condition, table, contextUri);
        foreach (var action in when.Actions)
        {
            TraverseAction(action, table, contextUri);
        }
    }

    private static void TraverseWhenCondition(KarelWhenCondition cond, KarelSymbolTable table, Uri contextUri)
    {
        switch (cond)
        {
            case KarelWhenOr o:
                foreach (var c in o.Conditions)
                {
                    TraverseCondition(c, table, contextUri);
                }
                break;
            case KarelWhenAnd a:
                foreach (var c in a.Conditions)
                {
                    TraverseCondition(c, table, contextUri);
                }
                break;
        }
    }

    private static void TraverseAction(KarelAction action, KarelSymbolTable table, Uri contextUri)
    {
        switch (action)
        {
            case KarelGroupAction a:
                TraverseStatement(a.Statement, table, contextUri);
                break;
            case KarelConditionAction a:
                TraverseStatement(a.Statement, table, contextUri);
                break;
            case KarelEventAction a:
                TraverseStatement(a.Signal, table, contextUri);
                break;
            case KarelSemaphoreAction a:
                TraverseExpression(a.Number, table, contextUri);
                break;
            case KarelPulseDoutAction a:
                TraverseExpression(a.Index, table, contextUri);
                TraverseExpression(a.Time, table, contextUri);
                break;
            case KarelPulseRdoAction a:
                TraverseExpression(a.Index, table, contextUri);
                TraverseExpression(a.Time, table, contextUri);
                break;
            case KarelAbortAction a:
                if (a.ProgramNumber != null)
                {
                    TraverseExpression(a.ProgramNumber, table, contextUri);
                }
                break;
            case KarelPauseAction a:
                if (a.ProgramNumber != null)
                {
                    TraverseExpression(a.ProgramNumber, table, contextUri);
                }
                break;
            case KarelContinueAction a:
                if (a.ProgramNumber != null)
                {
                    TraverseExpression(a.ProgramNumber, table, contextUri);
                }
                break;
            case KarelVarAssignmentAction a:
                TraverseExpression(a.Variable, table, contextUri);
                TraverseExpression(a.Expr, table, contextUri);
                break;
            case KarelPortAssignmentAction a:
                TraverseExpression(a.Index, table, contextUri);
                TraverseExpression(a.Expr, table, contextUri);
                break;
            case KarelNoAbortAction _:
            case KarelNoPauseAction _:
            case KarelUnpauseAction _:
            case KarelNoMessageAction _:
            case KarelRestoreAction _:
                break;
        }
    }

    private static void TraverseCase(KarelCase c, KarelSymbolTable table, Uri contextUri)
    {
        switch (c)
        {
            case KarelValueCase vc:
                foreach (var s in vc.Body) TraverseStatement(s, table, contextUri);
                break;
            case KarelElseCase ec:
                foreach (var s in ec.Body) TraverseStatement(s, table, contextUri);
                break;
        }
    }

    private static void TraverseItem(KarelItem item, KarelSymbolTable table, Uri contextUri)
    {
        switch (item)
        {
            case KarelReadItemExpr i:
                TraverseExpression(i.Expression, table, contextUri);
                foreach (var fmt in i.FormatSpecs) TraverseExpression(fmt, table, contextUri);
                break;
            case KarelReadItemCR i:
                foreach (var fmt in i.FormatSpecs) TraverseExpression(fmt, table, contextUri);
                break;
        }
    }
}
