namespace KarelParser.SymbolTable;

// Resolves the data type of a Karel variable access by walking the access AST,
// using a caller-supplied lookup for the type of the base identifier. A named
// struct type resolves only if a struct body for that name is registered in the
// resolver's dictionary (from the program itself or its %included files). Unknown
// or unregistered names—including types defined only in non-included programs—
// resolve to null; nothing throws.
public sealed class KarelTypeResolver
{
    private readonly Dictionary<string, KarelStructure> _structures;

    public KarelTypeResolver(Dictionary<string, KarelStructure> structures)
        => _structures = structures;

    public static KarelTypeResolver FromProgram(KarelProgram program)
    {
        var declarations = program.TranslatorDirectives
            .OfType<KarelIncludeDirective>()
            .SelectMany(incl => incl.Declarations)
            .Concat(program.Declarations);

        var structures = declarations
            .OfType<KarelTypeDeclaration>()
            .SelectMany(decl => decl.Type)
            .Where(type => type.Type is KarelStructure)
            .GroupBy(type => type.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (KarelStructure)group.First().Type,
                StringComparer.OrdinalIgnoreCase);

        return new KarelTypeResolver(structures);
    }

    // The data type an access evaluates to, or null when it can't be determined.
    public KarelDataType? ResolveAccessType(
        KarelVariableAccess access,
        Func<string, KarelDataType?> lookupBase)
        => access switch
        {
            KarelIdentifier id => lookupBase(id.Identifier),
            KarelFieldAccess fa => ResolveFieldType(fa, lookupBase),
            KarelArrayAccess aa => ResolveArrayElement(aa, lookupBase),
            KarelPathAccess pa => ResolveNodeType(pa, lookupBase),
            _ => null,
        };

    // (declaring struct type name, field name) when the base resolves to a known
    // struct that declares the field; otherwise null.
    public (string OwningType, string Field)? ResolveFieldOwner(
        KarelFieldAccess fa,
        Func<string, KarelDataType?> lookupBase)
        => (ResolveAccessType(fa.Variable, lookupBase) is { } baseType
                ? ResolveStructure(baseType)
                : (null, string.Empty)) switch
        {
            ({ } structure, var typeName) when structure.Fields.Any(
                f => f.Identifier.Equals(fa.Field, StringComparison.OrdinalIgnoreCase))
                    => (typeName, fa.Field),
            _ => null,
        };

    private KarelDataType? ResolveFieldType(KarelFieldAccess fa, Func<string, KarelDataType?> lookupBase)
        => ResolveAccessType(fa.Variable, lookupBase) is { } baseType
           && ResolveStructure(baseType) is ({ } structure, _)
            ? structure.Fields
                .FirstOrDefault(f => f.Identifier.Equals(fa.Field, StringComparison.OrdinalIgnoreCase))
                ?.Type
            : null;

    private KarelDataType? ResolveArrayElement(KarelArrayAccess aa, Func<string, KarelDataType?> lookupBase)
        => ResolveAccessType(aa.Variable, lookupBase) is KarelTypeArray array
            ? array.Type
            : null;

    private KarelDataType? ResolveNodeType(KarelPathAccess pa, Func<string, KarelDataType?> lookupBase)
        => ResolveAccessType(pa.Variable, lookupBase) is KarelTypePath path
           && _structures.ContainsKey(path.nodeData)
            ? new KarelTypeName(path.nodeData, null)
            : null;

    public KarelStructure? GetStructure(string typeName)
        => _structures.TryGetValue(typeName, out var s) ? s : null;

    // Dotted, variable-relative paths (no variable root) from rootType to every
    // occurrence of the field (owningType, field) in the type graph — e.g. "mode",
    // "lim.lo", "items.val". A TP program keys Karel references by these paths under
    // a variable ($[PROG]var.<path>), so this enumerates the reachable addresses of a
    // struct field so its external references can be found from its declaration.
    // Array segments are unwrapped (ResolveStructure sees through KarelTypeArray), so
    // "items.val" addresses "items[i].val"; a dotted path can't express a mid-path
    // index, which is an accepted limitation. All comparisons are case-insensitive.
    public IEnumerable<string> RelativePathsToField(KarelDataType rootType, string owningType, string field)
    {
        var results = new List<string>();
        Walk(rootType, string.Empty, new HashSet<string>(StringComparer.OrdinalIgnoreCase), results, owningType, field);
        return results;
    }

    private void Walk(KarelDataType type, string prefix, HashSet<string> visiting, List<string> results, string owningType, string field)
    {
        if (ResolveStructure(type) is not ({ } structure, { } typeName))
        {
            return;
        }

        // A struct type on the current path more than once: stop to avoid cycles.
        if (!visiting.Add(typeName))
        {
            return;
        }

        if (typeName.Equals(owningType, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var f in structure.Fields.Where(f => f.Identifier.Equals(field, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(prefix + f.Identifier);
            }
        }

        foreach (var f in structure.Fields)
        {
            Walk(f.Type, prefix + f.Identifier + ".", visiting, results, owningType, field);
        }

        visiting.Remove(typeName);
    }

    // Unwraps arrays and resolves a named type to its struct, or (null, "").
    private (KarelStructure? Structure, string TypeName) ResolveStructure(KarelDataType type)
        => type switch
        {
            KarelTypeName name when _structures.TryGetValue(name.Identifier, out var s)
                => (s, name.Identifier),
            KarelTypeArray array => ResolveStructure(array.Type),
            _ => (null, string.Empty),
        };
}
