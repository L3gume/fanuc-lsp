using ParserUtils;
using Sprache;

namespace KarelParser.SymbolTable;

public enum KarelSymbolKind
{
    Variable,
    Routine,
    Type,
    StructField,
    Constant,
}

public record ProgramPosition(TokenPosition Position, Uri ProgramUri);

public record KarelSymbol
{
    public string Name { get; init; }

    // The dotted path that addresses this symbol from a program-level variable
    // (without the program name), e.g. "Var.Field1.Field2". This mirrors the way
    // TP programs reference Karel data ($[PROG]Var.Field1.Field2), so it is the
    // key used to find a Karel symbol's references in TP files. For symbols that
    // aren't reached through a variable (types, routines, per-TYPE fields), it is
    // simply the symbol name.
    public string FullName { get; init; }
    public KarelSymbolKind Kind { get; init; }
    public ProgramPosition DeclarationPosition { get; init; }
    public List<ProgramPosition> ReferencePositions { get; init; }
    public KarelUserType? Type { get; init; }

    public KarelSymbol(string name, Uri uri, KarelSymbolKind kind, KarelUserType? type, TokenPosition declarationPosition)
        : this(name, name, uri, kind, type, declarationPosition)
    {
    }

    public KarelSymbol(string name, string fullName, Uri uri, KarelSymbolKind kind, KarelUserType? type, TokenPosition declarationPosition)
    {
        Name = name;
        FullName = fullName;
        Kind = kind;
        Type = type;
        DeclarationPosition = new ProgramPosition(declarationPosition, uri);
        ReferencePositions = new();
    }
}

// KarelSymbolTable needs to be a recursive structure to properly represent lexical scoping
public class KarelSymbolTable
{
    private ReaderWriterLockSlim Lock { get; } = new();

    private KarelSymbolTable? Parent { get; set; } = null;
    // There should never be more than one level of children realistically
    private List<KarelSymbolTable> Routines { get; set;} = [];

    private KarelSymbolTable Root => Parent?.Root ?? this;

    public Uri? ProgramUri { get; init; }
    public TokenPosition ScopeStart { get; set; } = new(0,0);
    public TokenPosition ScopeEnd { get; set; } = new(0,0);

    // Struct fields are program-global; the registry and store live on the root
    // table and every scope delegates to it.
    public KarelTypeResolver? Resolver
    {
        get => Root._resolver;
        set => Root._resolver = value;
    }

    private readonly Dictionary<string, KarelSymbol> _symbols = new();
    private readonly Dictionary<string, KarelSymbol> _fields = new();
    private KarelTypeResolver? _resolver;

    public KarelSymbolTable CreateRoutine(TokenPosition start, TokenPosition end, Uri programUri)
    {
        if (!(IsPositionInScope(start) && (IsPositionInScope(end))))
        {
            throw new InvalidOperationException("Nested scope isn't in parent scope");
        }
        var childTbl = new KarelSymbolTable
        {
            Parent = this,
            ProgramUri = programUri,
            ScopeStart = start,
            ScopeEnd = end,
        };
        Routines.Add(childTbl);

        return childTbl;
    }

    public void AddSymbol(string name, Uri uri, KarelSymbolKind kind, TokenPosition declarationPosition)
        => LockedWrite(() => {
            var symName = name.ToLower();
            if (!_symbols.ContainsKey(symName))
            {
                _symbols[symName] = new KarelSymbol(symName, uri, kind, null, declarationPosition);
            }
        });

    public void AddSymbol(string name, Uri uri, KarelSymbolKind kind, KarelUserType? type, TokenPosition declarationPosition)
        => LockedWrite(() => {
            var symName = name.ToLower();
            if (!_symbols.ContainsKey(symName))
            {
                _symbols[symName] = new KarelSymbol(symName, uri, kind, type, declarationPosition);
            }
        });

    public void AddReference(string name, TokenPosition refPosition, Uri programUri)
        => LockedWrite(() => {
            if (_symbols.TryGetValue(name.ToLower(), out var symbol))
            {
                symbol.ReferencePositions.Add(new (refPosition, programUri));
            }

            if (Parent?.GetTopLevelSymbol(name) is { } parentSym)
            {
                parentSym.ReferencePositions.Add(new (refPosition, programUri));
            }
        });

    public KarelSymbol? GetTopLevelSymbol(string name)
        => LockedRead(() => _symbols.GetValueOrDefault(name.ToLower()));

    public KarelSymbol? GetSymbol(string name, TokenPosition position)
        => LockedRead<KarelSymbol?>(() => {
            var symName = name.ToLower();
            if (!IsPositionInScope(position))
            {
                return null;
            }

            foreach (var childTbl in Routines)
            {
                if (childTbl.GetSymbol(symName, position) is {} scoped)
                {
                    return scoped;
                }
            }

            return _symbols.GetValueOrDefault(symName);
        });

    public List<ProgramPosition> GetSymbolReferences(string name)
        => GetTopLevelSymbol(name) switch
        {
            { } symbol => symbol.ReferencePositions,
            _ => []
        };

    public List<ProgramPosition> GetSymbolReferences(string name, TokenPosition position)
        => GetSymbol(name, position) switch
        {
            { } symbol => symbol.ReferencePositions,
            _ => []
        };

    public IEnumerable<KarelSymbol> GetAllSymbols()
        => LockedRead(() => _symbols.Values);

    // The symbol addressed by a base-rooted access path written in Karel syntax
    // ("var", "var.field", "var.a.b[2].c"). The base identifier resolves to a
    // top-level variable; each field segment resolves through the type resolver to
    // its declaring struct's field symbol; array/path index suffixes are unwrapped
    // to the datum they index. Returns null when the path can't be parsed or a
    // segment doesn't resolve. This is the entry point TP programs use to reach a
    // Karel datum's declaration/type from a $[PROG]var.field reference.
    public KarelSymbol? ResolveAccessSymbol(string accessPath)
        => string.IsNullOrWhiteSpace(accessPath)
            ? null
            : KarelVariableAccess.GetParser().TryParse(accessPath) switch
            {
                { WasSuccessful: true } parsed => ResolveAccessSymbol(parsed.Value),
                _ => null
            };

    private KarelSymbol? ResolveAccessSymbol(KarelVariableAccess access)
        => access switch
        {
            KarelIdentifier id => GetTopLevelSymbol(id.Identifier),
            KarelArrayAccess aa => ResolveAccessSymbol(aa.Variable),
            KarelPathAccess pa => ResolveAccessSymbol(pa.Variable),
            KarelFieldAccess fa => Resolver?.ResolveFieldOwner(fa, ResolveVariableType) is { } owner
                ? GetFieldSymbol(owner.OwningType, owner.Field)
                : null,
            _ => null
        };

    // Variable-rooted dotted paths (no program name) that reach the given struct
    // field through every top-level variable in this scope — e.g. for a field
    // (cfg_t, mode) reached by variables cfg and cfg2: "cfg.mode", "cfg2.mode".
    // These are the paths a TP program would use as $[PROG]<path>, so they drive
    // finding a field declaration's external references. Empty when no resolver is
    // attached or no variable's type reaches the field.
    public IEnumerable<string> GetVariablePathsToField(string owningType, string field)
    {
        if (Resolver is not { } resolver)
        {
            return [];
        }

        return GetAllSymbols()
            .Where(sym => sym is { Kind: KarelSymbolKind.Variable, Type: KarelDataType })
            .SelectMany(sym => resolver
                .RelativePathsToField((KarelDataType)sym.Type!, owningType, field)
                .Select(rel => $"{sym.Name}.{rel}"))
            .ToList();
    }

    private static string FieldKey(string owningType, string field)
        => $"{owningType.ToLower()}.{field.ToLower()}";

    public void AddFieldSymbol(string owningType, string field, Uri uri, KarelUserType? type, TokenPosition declarationPosition)
        => Root.LockedWrite(() =>
        {
            var key = FieldKey(owningType, field);
            if (!Root._fields.ContainsKey(key))
            {
                Root._fields[key] = new KarelSymbol(field, $"{owningType}.{field}", uri, KarelSymbolKind.StructField, type, declarationPosition);
            }
        });

    public void AddFieldReference(string owningType, string field, TokenPosition refPosition, Uri programUri)
        => Root.LockedWrite(() =>
        {
            if (Root._fields.TryGetValue(FieldKey(owningType, field), out var symbol))
            {
                symbol.ReferencePositions.Add(new(refPosition, programUri));
            }
        });

    public KarelSymbol? GetFieldSymbol(string owningType, string field)
        => Root.LockedRead(() => Root._fields.GetValueOrDefault(FieldKey(owningType, field)));

    // The field symbol whose declaration or one of whose references spans the
    // given position (0-based) in the given document. A token covers
    // [start, start + name length). The URI check prevents a position in one
    // file from matching a same-coordinate field declared in an %included file.
    public KarelSymbol? GetFieldSymbolAt(TokenPosition position, Uri? documentUri)
        => Root.LockedRead<KarelSymbol?>(() => Root._fields.Values.FirstOrDefault(sym =>
            Covers(sym.DeclarationPosition, sym.Name, position, documentUri)
            || sym.ReferencePositions.Any(r => Covers(r, sym.Name, position, documentUri))));

    private static bool Covers(ProgramPosition token, string name, TokenPosition position, Uri? documentUri)
        => token.ProgramUri == documentUri
           && position.Line == token.Position.Line
           && position.Column >= token.Position.Column
           && position.Column < token.Position.Column + name.Length;

    // The declared data type of a variable/constant reachable from this scope,
    // searching this scope then ancestors. Used as the base-type lookup for the
    // type resolver.
    public KarelDataType? ResolveVariableType(string name)
        => LockedRead(() => _symbols.TryGetValue(name.ToLower(), out var sym) && sym.Type is KarelDataType dt
            ? dt
            : Parent?.ResolveVariableType(name));

    private bool IsPositionInScope(TokenPosition position)
        => position.Line >= ScopeStart.Line
        && position.Line <= ScopeEnd.Line;

    private void LockedWrite(Action func)
    {
        try
        {
            Lock.EnterWriteLock();
            func();
        }
        catch
        {
            throw;
        }
        finally
        {
            Lock.ExitWriteLock();
        }

    }

    private T LockedRead<T>(Func<T> func)
    {
        try
        {
            Lock.EnterReadLock();
            return func();
        }
        catch
        {
            throw;
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

}

