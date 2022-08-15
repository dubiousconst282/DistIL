namespace DistIL.IR.Utils.Parser;

internal class TypeResolver
{
    readonly List<(ModuleDef Mod, string? Ns)> _imports = new();
    readonly Dictionary<TypeNode, TypeDesc> _cache = new();

    public void ImportNamespace(ModuleDef mod, string ns)
    {
        _imports.Add((mod, ns));
    }

    public TypeDesc? Resolve(TypeNode node, [DoesNotReturnIf(true)] bool throwOnFailure = false)
    {
        if (_cache.TryGetValue(node, out var type)) {
            return type;
        }
        type = node switch {
            BasicTypeNode nodeC => Resolve(nodeC),
            NestedTypeNode nodeC => Resolve(nodeC),
            TypeSpecNode nodeC => Resolve(nodeC),
            ArrayTypeNode nodeC => ResolveAndWrap(nodeC.ElemType, et => new ArrayType(et)),
            PointerTypeNode nodeC => ResolveAndWrap(nodeC.ElemType, et => new PointerType(et)),
            ByrefTypeNode nodeC => ResolveAndWrap(nodeC.ElemType, et => new ByrefType(et)),
            _ => throw new InvalidOperationException()
        };
        if (type != null) {
            _cache[node] = type;
        } else if (throwOnFailure) {
            throw new InvalidOperationException("Failed to resolve type");
        }
        return type;
    }

    private TypeDesc? Resolve(BasicTypeNode node)
    {
        var name = node.Name;
        int lastDot = name.LastIndexOf('.');
        if (lastDot < 0) {
            var prim = PrimType.GetFromAlias(name);
            if (prim != null) {
                return prim;
            }
            foreach (var (mod, ns) in _imports) {
                var type = mod.FindType(ns, name);
                if (type != null) {
                    return type;
                }
            }
        } else {
            throw new NotImplementedException("Fully qualified type name");
        }
        return null;
    }

    private TypeDef? Resolve(NestedTypeNode node)
    {
        var parent = (TypeDef?)Resolve(node.Parent);
        return parent?.GetNestedType(node.ChildName);
    }

    private TypeSpec? Resolve(TypeSpecNode node)
    {
        var def = (TypeDef?)Resolve(node.Definition);

        return def?.GetSpec(
            node.ArgTypes
                .Select(t => Resolve(t, true))
                .ToImmutableArray()
        );
    }

    private TType? ResolveAndWrap<TType>(TypeNode type, Func<TypeDesc, TType> wrapper) where TType : TypeDesc
    {
        var resolvedType = Resolve(type);
        return resolvedType == null ? null : wrapper(resolvedType);
    }
}