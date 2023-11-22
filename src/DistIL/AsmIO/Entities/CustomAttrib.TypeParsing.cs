namespace DistIL.AsmIO;

using System.Reflection;
using System.Text.RegularExpressions;

partial class CustomAttrib
{
    //Adapted from AstParser.ParseType(), the syntax is roughly:
    //  Type := Identifier  ("+"  Identifier)*  ("["  Seq{Type}  "]")?  ("[]" | "*" | "&")*
    //e.g. "NS.A`1+B`1[int[], int][]&"  ->  "NS.A.B<int[], int>[]&"
    //
    //Rant: I can't help but wonder why on earth the CLI designers decided it would
    //be a good idea to stringify type names for CAs instead of just using entity handles.
    //I guess it avoids depending on entity tables, which seems to make laziness easier(?)
    //Parsing things like these is always a huge pain (and there's basically zero docs)...
    public static TypeDesc ParseSerializedType(ModuleDef module, string str)
    {
        //Note that assembly qualified names can be nested as well (what a surprise!):
        //  "System.Collections.Generic.List`1+Enumerator[[System.String, System.Runtime, Version=6.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a]][], System.Collections, Version=6.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
        int pos = 0;
        return ParseFullyQualifiedType(module);

        TypeDesc ParseFullyQualifiedType(ModuleDef scope)
        {
            int startPos = pos;
            var asmName = SkipFullyQualifiedType(getAsmName: true);
            int endPos = pos;
            pos = startPos;

            if (asmName != null) {
                scope = scope.Resolver.Resolve(asmName, throwIfNotFound: true);
            }
            var parsed = ParseType(scope);
            pos = endPos;
            return parsed;
        }

        TypeDesc? Resolve(ModuleDef scope, string name)
        {
            int nsIdx = name.LastIndexOf('.');
            string? typeNs = nsIdx < 0 ? null : name[0..nsIdx];
            string typeName = nsIdx < 0 ? name : name[(nsIdx + 1)..];
            return scope.FindType(typeNs, typeName);
        }
        TypeDesc ParseType(ModuleDef scope)
        {
            int numGenArgs = 0;
            string name = ScanName(ref numGenArgs);
            var type = Resolve(scope, name);

            //Nested types
            while (Match('+')) {
                string childName = ScanName(ref numGenArgs);
                type = (type as TypeDef)?.FindNestedType(childName);
            }
            if (type == null) {
                throw Error("Specified type could not be found");
            }
            //Generic arguments
            if (numGenArgs > 0 && Match('[')) {
                var args = ImmutableArray.CreateBuilder<TypeDesc>();
                for (int i = 0; i < numGenArgs; i++) {
                    if (i != 0) Expect(',');
                    args.Add(ParseNestedType(scope));
                }
                Expect(']');
                type = ((TypeDef)type).GetSpec(args.DrainToImmutable());
            }
            //Compound types (array, pointer, byref)
            while (true) {
                if (Match('[')) {
                    //TODO: multi dim arrays
                    Expect(']');
                    type = type.CreateArray();
                } else if (Match('*')) {
                    type = type.CreatePointer();
                } else if (Match('&')) {
                    type = type.CreateByref();
                } else break;
            }
            return type;
        }
        TypeDesc ParseNestedType(ModuleDef scope)
        {
            if (Match('[')) {
                var parsed = ParseFullyQualifiedType(scope);
                Expect(']');
                return parsed;
            }
            return ParseType(scope);
        }
        AssemblyName? SkipFullyQualifiedType(bool getAsmName = false)
        {
            int numGenArgs = 0;
            string name = ScanName(ref numGenArgs);

            //Nested types
            while (Match('+')) {
                ScanName(ref numGenArgs);
            }
            //Generic arguments
            if (numGenArgs > 0 && Match('[')) {
                for (int i = 0; i < numGenArgs; i++) {
                    if (i != 0) Expect(',');
                    SkipNested();
                }
                Expect(']');
            }
            //Compound types (array, pointer, byref)
            while (true) {
                if (Match('[')) {
                    Expect(']');
                } else if (Match('*')) {
                } else if (Match('&')) {
                } else break;
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

                return getAsmName ? new AssemblyName(str[asmNameStart..pos]) : null;
            }
            return null;
        }
        void SkipNested()
        {
            bool isFullyQualified = Match('[');
            SkipFullyQualifiedType();
            if (isFullyQualified) Expect(']');
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
            Debug.Assert(str[len - 1] != '\\'); //TODO: handle escaping

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
            //Escape rare special characters
            if (str.AsSpan().IndexOfAny("[],&*+=") >= 0) {
                str = Regex.Replace(str, @"[\[\]\,\&\*\+\=]", @"\$0");
            }
            sb.Append(str);
        }
    }
}