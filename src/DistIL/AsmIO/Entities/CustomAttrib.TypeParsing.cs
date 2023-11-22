namespace DistIL.AsmIO;

using System.Reflection;
using System.Text.RegularExpressions;

partial class CustomAttrib
{
    // Adapted from AstParser.ParseType(). This roughly follows the grammar in:
    // - https://learn.microsoft.com/en-us/dotnet/framework/reflection-and-codedom/specifying-fully-qualified-type-names
    // - https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Reflection/TypeNameParser.cs
    //
    // "NS.A`1+B`1[int[], int][]&"  ->  "NS.A.B<int[], int>[]&"
    // "System.Collections.Generic.List`1+Enumerator[[System.String, System.Runtime, Version=6.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a]][], System.Collections, Version=6.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
    public static TypeDesc ParseSerializedType(ModuleDef module, string str)
    {
        int pos = 0;
        return ParseType().Resolve(module);

        ParsedType ParseType()
        {
            int numGenArgs = 0;
            var type = new ParsedType() { Name = ScanName(ref numGenArgs) };

            // Nested types
            while (Match('+')) {
                string childName = ScanName(ref numGenArgs);
                type = new ParsedType() { Inner = type, Name = childName, Kind = PartKind.Nested };
            }
            // Generic arguments
            if (numGenArgs > 0 && Match('[')) {
                type.GenArgs = new List<ParsedType>();
                for (int i = 0; i < numGenArgs; i++) {
                    if (i != 0) Expect(',');
                    type.GenArgs.Add(ParseArgType());
                }
                Expect(']');
            }
            // Compound types (array, pointer, byref)
            while (true) {
                if (Match('[')) {
                    // TODO: multi dim arrays
                    Expect(']');
                    type = new ParsedType() { Inner = type, Kind = PartKind.Array };
                } else if (Match('*') || Match('&')) {
                    type = new ParsedType() { Inner = type, Kind = (PartKind)str[pos - 1] };
                } else {
                    break;
                }
            }

            if (Match(',')) {
                int asmNameStart = pos;
                do {
                    int dummy = 0;
                    ScanName(ref dummy);

                    if (Match('=')) {
                        ScanName(ref dummy);
                    }
                } while (Match(','));

                type.AsmName = new AssemblyName(str[asmNameStart..pos]);
            }
            return type;
        }
        ParsedType ParseArgType()
        {
            if (Match('[')) {
                var type = ParseType();
                Expect(']');
                return type;
            }
            return ParseType();
        }
        bool Match(char ch)
        {
            if (pos < str.Length && str[pos] == ch) {
                pos++;
                return true;
            }
            return false;
        }
        void Expect(char ch)
        {
            if (pos >= str.Length || str[pos] != ch) {
                throw Error($"Expected '{ch}'");
            }
            pos++;
        }
        string ScanName(ref int numGenArgs)
        {
            int len = str.AsSpan(pos).IndexOfAny("[],&*+=");
            if (len < 0) {
                len = str.Length - pos;
            } else if (len == 0) {
                throw Error("Expected identifier");
            }
            Debug.Assert(str[len - 1] != '\\'); // TODO: handle escaping

            string val = str.Substring(pos, len);
            pos += len;

            int backtickIdx = val.IndexOf('`');
            if (backtickIdx >= 0) {
                numGenArgs += int.Parse(val.AsSpan(backtickIdx + 1));
            }
            return val;
        }
        Exception Error(string msg)
            => new FormatException($"Failed to parse serialized type name: {msg} (for '{str}' at {pos})");
    }

    class ParsedType
    {
        public string Name = null!;
        public AssemblyName? AsmName;
        public ParsedType? Inner;
        public List<ParsedType>? GenArgs;
        public PartKind Kind = PartKind.DefOrSpec;

        public TypeDesc Resolve(ModuleDef scope)
        {
            if (AsmName != null) {
                scope = scope.Resolver.Resolve(AsmName, throwIfNotFound: true);
            }

            if (Kind == PartKind.DefOrSpec) {
                return ResolveSpec(ResolveDef(scope), scope);
            }
            var type = Inner!.Resolve(scope);

            if (Kind == PartKind.Nested) {
                var def = (type as TypeDef)?.FindNestedType(Name);
                return ResolveSpec(def, scope);
            }
            return Kind switch {
                PartKind.Array => type.CreateArray(),
                PartKind.Ref => type.CreateByref(),
                PartKind.Ptr => type.CreatePointer(),
            };
        }

        private TypeDef? ResolveDef(ModuleDef scope)
        {
            int nsIdx = Name.LastIndexOf('.');
            string? typeNs = nsIdx < 0 ? null : Name[0..nsIdx];
            string typeName = nsIdx < 0 ? Name : Name[(nsIdx + 1)..];
            return scope.FindType(typeNs, typeName);
        }
        private TypeDesc ResolveSpec(TypeDef? type, ModuleDef scope)
        {
            if (type == null) {
                throw new FormatException($"Could not find type '{Name}'");
            }
            if (GenArgs != null) {
                var args = GenArgs.Select(p => p.Resolve(scope)).ToImmutableArray();
                return type.GetSpec(args);
            }
            return type;
        }
    }
    enum PartKind { DefOrSpec, Nested = '+', Array = '[', Ref = '&', Ptr = '*' }

    public static string SerializeType(TypeDesc type)
    {
        var sb = new StringBuilder();
        Print(type);
        return sb.ToString();

        void Print(TypeDesc type)
        {
            switch (type) {
                case TypeDef def: {
                    if (def.DeclaringType != null) {
                        Print(def.DeclaringType);
                        sb.Append('+');
                    } else if (type.Namespace != null) {
                        PrintEscaped(type.Namespace);
                        sb.Append('.');
                    }
                    PrintEscaped(type.Name);
                    break;
                }
                case TypeSpec spec: {
                    Print(spec.Definition);
                    Ensure.That(spec.Name.Contains('`'), "Generic type name must end with a backtick followed by the number of generic parameters.");
                    sb.Append('[');
                    for (int i = 0; i < spec.GenericParams.Count; i++) {
                        if (i != 0) sb.Append(',');
                        Print(spec.GenericParams[i]);
                    }
                    sb.Append(']');
                    break;
                }
                case CompoundType compound: {
                    Print(compound.ElemType);
                    sb.Append(type switch { ArrayType => "[]", ByrefType => "&", PointerType => "*" });
                    break;
                }
                default: throw new NotSupportedException();
            }
        }
        void PrintEscaped(string str)
        {
            // Escape rare special characters
            if (str.AsSpan().IndexOfAny("[],&*+=") >= 0) {
                str = Regex.Replace(str, @"[\[\]\,\&\*\+\=]", @"\$0");
            }
            sb.Append(str);
        }
    }
}