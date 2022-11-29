namespace DistIL.AsmIO;

using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

/// <summary> Base class for all method entities. </summary>
public abstract class MethodDesc : MemberDesc
{
    public abstract MethodAttributes Attribs { get; }
    public abstract MethodImplAttributes ImplAttribs { get; }

    public abstract TypeSig ReturnSig { get; }
    public abstract IReadOnlyList<TypeSig> ParamSig { get; }
    public abstract IReadOnlyList<TypeDesc> GenericParams { get; }

    public bool IsStatic => (Attribs & MethodAttributes.Static) != 0;
    public bool IsInstance => !IsStatic;
    public bool IsGeneric => GenericParams.Count > 0;

    public TypeDesc ReturnType => ReturnSig.Type;

    public override void Print(PrintContext ctx)
    {
        if (IsStatic) ctx.Print("static ", PrintToner.Keyword);
        ReturnSig.Print(ctx);
        ctx.Print($" {DeclaringType}::{PrintToner.MethodName}{Name}");
        if (IsGeneric) {
            ctx.PrintSequence("<", ">", GenericParams, ctx.Print);
        }
        ctx.PrintSequence("(", ")", ParamSig, p => p.Print(ctx));
    }

    public abstract MethodDesc GetSpec(GenericContext ctx);
}
public class ParamDef
{
    public TypeSig Sig { get; set; }
    public string Name { get; set; }
    public ParameterAttributes Attribs { get; set; }
    public object? DefaultValue { get; set; }
    public byte[]? MarshallingDesc { get; set; }

    public TypeDesc Type => Sig.Type;

    internal IList<CustomAttrib>? _customAttribs;

    public ParamDef(TypeSig sig, string name, ParameterAttributes attribs = default)
    {
        Sig = sig;
        Name = name;
        Attribs = attribs;
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribExt.GetOrInitList(ref _customAttribs, readOnly);

    public override string ToString() => Sig.ToString();
}

public abstract class MethodDefOrSpec : MethodDesc, ModuleEntity
{
    /// <summary> Returns the parent definition if this is a MethodSpec, or the current instance if already a MethodDef. </summary>
    public abstract MethodDef Definition { get; }
    public ModuleDef Module => Definition.DeclaringType.Module;

    public abstract override TypeDefOrSpec DeclaringType { get; }
}
public class MethodDef : MethodDefOrSpec
{
    public override MethodDef Definition => this;
    public override TypeDef DeclaringType { get; }
    public override string Name { get; }

    public override MethodAttributes Attribs { get; }
    public override MethodImplAttributes ImplAttribs { get; }

    public override TypeSig ReturnSig => ReturnParam.Sig;
    public override IReadOnlyList<TypeSig> ParamSig => _paramSig ??= new() { Method = this };
    public override IReadOnlyList<GenericParamType> GenericParams { get; }

    /// <summary> Represents a placeholder for the return value, which may contain custom attributes. </summary>
    public ParamDef ReturnParam { get; }

    //TODO: expose `IROList<TypeSig> ParamSig` instead of `ImmutArray<ParamDef> Params`
    public ImmutableArray<ParamDef> Params { get; }
    public ReadOnlySpan<ParamDef> StaticParams => Params.AsSpan(IsStatic ? 0 : 1);

    private ParamSigProxyList? _paramSig;
    internal IList<CustomAttrib>? _customAttribs;

    public ILMethodBody? ILBody { get; set; }
    public IR.MethodBody? Body { get; set; }

    public MethodDef(
        TypeDef declaringType,
        TypeSig retSig, ImmutableArray<ParamDef> pars, string name,
        MethodAttributes attribs = default, MethodImplAttributes implAttribs = default,
        ImmutableArray<GenericParamType> genericParams = default)
    {
        DeclaringType = declaringType;
        ReturnParam = new ParamDef(retSig, "", ParameterAttributes.Retval);
        Params = pars;
        Name = name;
        Attribs = attribs;
        ImplAttribs = implAttribs;
        GenericParams = genericParams.EmptyIfDefault();

        Ensure.That(
            IsStatic || !declaringType.IsGeneric || pars[0].Type is TypeSpec or ByrefType { ElemType: TypeSpec },
            "`this` parameter for generic type must be specialized with the default parameters");
    }

    public override MethodDesc GetSpec(GenericContext ctx)
    {
        return IsGeneric || DeclaringType.IsGeneric
            ? new MethodSpec(DeclaringType.GetSpec(ctx), this, ctx.FillParams(GenericParams))
            : this;
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribExt.GetOrInitList(ref _customAttribs, readOnly);

    internal static MethodDef Decode(ModuleLoader loader, MethodDefinition info)
    {
        var declaringType = loader.GetType(info.GetDeclaringType());
        var genericParams = loader.CreateGenericParams(info.GetGenericParameters(), true);
        string name = loader._reader.GetString(info.Name);

        var genCtx = new GenericContext(declaringType.GenericParams, genericParams);

        //II.2.3.2.1 MethodDefSig
        var decoder = new SignatureDecoder(loader, info.Signature, genCtx);

        var header = decoder.Reader.ReadSignatureHeader();
        Ensure.That(header.Kind == SignatureKind.Method);
        Debug.Assert(!header.HasExplicitThis); //not impl
        Debug.Assert(header.IsInstance == !info.Attributes.HasFlag(MethodAttributes.Static));

        if (header.IsGeneric) {
            Ensure.That(decoder.Reader.ReadCompressedInteger() == genericParams.Length);
        }
        int numParams = decoder.Reader.ReadCompressedInteger();
        var retSig = decoder.DecodeTypeSig();

        var pars = ImmutableArray.CreateBuilder<ParamDef>(numParams + (header.IsInstance ? 1 : 0));

        if (header.IsInstance) {
            var instanceType = declaringType.GetSpec(GenericContext.Empty);
            pars.Add(new ParamDef(instanceType.IsValueType ? instanceType.CreateByref() : instanceType, "this"));
        }
        for (int i = 0; i < numParams; i++) {
            pars.Add(new ParamDef(decoder.DecodeTypeSig(), "", 0));
        }
        return new MethodDef(
            declaringType, retSig, pars.MoveToImmutable(),
            name, info.Attributes, info.ImplAttributes, genericParams
        );
    }
    internal void Load3(ModuleLoader loader, MethodDefinition info)
    {
        var reader = loader._reader;
        foreach (var parHandle in info.GetParameters()) {
            var parInfo = reader.GetParameter(parHandle);

            int index = parInfo.SequenceNumber;
            ParamDef par;

            if (index > 0 && index <= Params.Length) {
                par = Params[index - (IsStatic ? 1 : 0)]; //we always have a `this` param
                par.Name = reader.GetString(parInfo.Name);
            } else {
                par = ReturnParam;
            }
            par.Attribs = parInfo.Attributes;

            if (par.Attribs.HasFlag(ParameterAttributes.HasDefault)) {
                par.DefaultValue = reader.DecodeConst(parInfo.GetDefaultValue());
            }
            if (par.Attribs.HasFlag(ParameterAttributes.HasFieldMarshal)) {
                par.MarshallingDesc = reader.GetBlobBytes(parInfo.GetMarshallingDescriptor());
            }
            par._customAttribs = loader.DecodeCustomAttribs(parInfo.GetCustomAttributes());
        }
        if (info.RelativeVirtualAddress != 0) {
            ILBody = new ILMethodBody(loader, info.RelativeVirtualAddress);
        }
        loader.FillGenericParams(GenericParams, info.GetGenericParameters());
        _customAttribs = loader.DecodeCustomAttribs(info.GetCustomAttributes());
    }

    class ParamSigProxyList : IReadOnlyList<TypeSig>
    {
        public MethodDef Method = null!;

        public TypeSig this[int index] => Method.Params[index].Sig;
        public int Count => Method.Params.Length;

        public IEnumerator<TypeSig> GetEnumerator() => Method.Params.Select(p => p.Sig).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary> Represents a generic method instantiation. </summary>
public class MethodSpec : MethodDefOrSpec
{
    public override MethodDef Definition { get; }
    public override TypeDefOrSpec DeclaringType { get; }
    public override string Name => Definition.Name;

    public override MethodAttributes Attribs => Definition.Attribs;
    public override MethodImplAttributes ImplAttribs => Definition.ImplAttribs;

    public override TypeSig ReturnSig { get; }
    public override IReadOnlyList<TypeSig> ParamSig { get; }
    public override IReadOnlyList<TypeDesc> GenericParams { get; }

    /// <summary> Returns whether this meth generic parameters from this spec are different from its definition. </summary>
    public bool IsBoundGeneric => IsGeneric && GenericParams != Definition.GenericParams;

    internal MethodSpec(TypeDefOrSpec declaringType, MethodDef def, ImmutableArray<TypeDesc> genArgs = default)
    {
        Definition = def;
        DeclaringType = declaringType;
        Ensure.That(genArgs.IsDefaultOrEmpty || def.IsGeneric);
        GenericParams = genArgs.IsDefault ? def.GenericParams : genArgs;

        var genCtx = new GenericContext(this);
        ReturnSig = def.ReturnSig.GetSpec(genCtx);
        ParamSig = GetParamsSpec(genCtx);
    }

    private TypeSig[] GetParamsSpec(GenericContext genCtx)
    {
        if (Definition.Params.Length == 0) {
            return Array.Empty<TypeSig>();
        }
        var types = new TypeSig[Definition.Params.Length];
        int index = 0;

        if (Definition.IsInstance) {
            types[index++] = DeclaringType.IsValueType ? DeclaringType.CreateByref() : DeclaringType;
        }
        foreach (var par in Definition.StaticParams) {
            types[index++] = par.Sig.GetSpec(genCtx);
        }
        return types;
    }

    public override MethodDesc GetSpec(GenericContext ctx)
    {
        return new MethodSpec(DeclaringType.GetSpec(ctx), Definition, ctx.FillParams(GenericParams));
    }
}

public class ILMethodBody
{
    public required ArraySegment<ILInstruction> Instructions { get; set; }
    public Variable[] Locals { get; set; } = Array.Empty<Variable>();
    public ExceptionRegion[] ExceptionRegions { get; set; } = Array.Empty<ExceptionRegion>();
    public int MaxStack { get; set; }
    public bool InitLocals { get; set; }

    [SetsRequiredMembers]
    internal ILMethodBody(ModuleLoader loader, int rva)
    {
        var block = loader._pe.GetMethodBody(rva);
        Instructions = DecodeInsts(loader, block.GetILReader());
        Locals = DecodeLocals(loader, block);
        ExceptionRegions = DecodeExceptionRegions(loader, block);
        MaxStack = block.MaxStack;
        InitLocals = block.LocalVariablesInitialized;
    }

    public ILMethodBody()
    {
    }

    private static ILInstruction[] DecodeInsts(ModuleLoader loader, BlobReader reader)
    {
        //Doing two passes over the raw data is slightly more efficient than using a list,
        //since it avoids extra allocations and copies due to resizes.
        var insts = new ILInstruction[CountInsts(reader)];
        for (int i = 0; i < insts.Length; i++) {
            insts[i] = DecodeInst(loader, ref reader);
        }
        return insts;
    }

    private static int CountInsts(BlobReader reader)
    {
        int count = 0;
        while (reader.Offset < reader.Length) {
            var opcode = DecodeOpcode(ref reader);

            int operSize = opcode switch {
                ILCode.Switch => reader.ReadInt32() * 4,
                _ => opcode.GetOperandType().GetSize()
            };
            reader.Offset += operSize;
            count++;
        }
        return count;
    }

    private static ILInstruction DecodeInst(ModuleLoader loader, ref BlobReader reader)
    {
        var inst = new ILInstruction() {
            Offset = reader.Offset,
            OpCode = DecodeOpcode(ref reader)
        };
        inst.Operand = inst.OperandType switch {
            ILOperandType.BrTarget => reader.ReadInt32() + reader.Offset,
            ILOperandType.Field or
            ILOperandType.Method or
            ILOperandType.Tok or
            ILOperandType.Type
                => loader.GetEntity(MetadataTokens.EntityHandle(reader.ReadInt32())),
            ILOperandType.Sig
                //`StandaloneSignature` is only used by calli.
                //We use `FuncPtrType` instead of `MethodSig` because it inherits `Value`.
                //TODO: fix generic context (reuse generic parameters)
                => loader.DecodeMethodSig(MetadataTokens.StandaloneSignatureHandle(reader.ReadInt32())),
            ILOperandType.String => loader._reader.GetUserString(MetadataTokens.UserStringHandle(reader.ReadInt32())),
            ILOperandType.I => reader.ReadInt32(),
            ILOperandType.I8 => reader.ReadInt64(),
            ILOperandType.R => reader.ReadDouble(),
            ILOperandType.Switch => ReadJumpTable(ref reader),
            ILOperandType.Var => (int)reader.ReadUInt16(),
            ILOperandType.ShortBrTarget => (int)reader.ReadSByte() + reader.Offset,
            ILOperandType.ShortI => (int)reader.ReadSByte(),
            ILOperandType.ShortR => reader.ReadSingle(),
            ILOperandType.ShortVar => (int)reader.ReadByte(),
            _ => null
        };
        return inst;

        static int[] ReadJumpTable(ref BlobReader reader)
        {
            int count = reader.ReadInt32();
            int baseOffset = reader.Offset + count * 4;
            var targets = new int[count];

            for (int i = 0; i < count; i++) {
                targets[i] = baseOffset + reader.ReadInt32();
            }
            return targets;
        }
    }

    private static ILCode DecodeOpcode(ref BlobReader reader)
    {
        int b = reader.ReadByte();
        return (ILCode)(b == 0xFE ? 0xFE00 | reader.ReadByte() : b);
    }

    private static ExceptionRegion[] DecodeExceptionRegions(ModuleLoader loader, MethodBodyBlock block)
    {
        if (block.ExceptionRegions.Length == 0) {
            return Array.Empty<ExceptionRegion>();
        }
        var regions = new ExceptionRegion[block.ExceptionRegions.Length];
        for (int i = 0; i < regions.Length; i++) {
            var region = block.ExceptionRegions[i];

            regions[i] = new() {
                Kind = region.Kind,
                CatchType = region.CatchType.IsNil ? null : (TypeDefOrSpec)loader.GetEntity(region.CatchType),
                HandlerStart = region.HandlerOffset,
                HandlerEnd = region.HandlerOffset + region.HandlerLength,
                TryStart = region.TryOffset,
                TryEnd = region.TryOffset + region.TryLength,
                FilterStart = region.FilterOffset
            };
        }
        return regions;
    }

    private static Variable[] DecodeLocals(ModuleLoader loader, MethodBodyBlock block)
    {
        if (block.LocalSignature.IsNil) {
            return Array.Empty<Variable>();
        }
        var info = loader._reader.GetStandaloneSignature(block.LocalSignature);
        return new SignatureDecoder(loader, info.Signature).DecodeLocals();
    }
}
public class ExceptionRegion
{
    public ExceptionRegionKind Kind { get; set; }

    /// <summary> The catch type if the region represents a catch handler, or null otherwise. </summary>
    public TypeDefOrSpec? CatchType { get; set; }

    /// <summary> Gets the starting IL offset of the exception handler. </summary>
    public int HandlerStart { get; set; }
    /// <summary> Gets the ending IL offset of the exception handler. </summary>
    public int HandlerEnd { get; set; }

    /// <summary> Gets the starting IL offset of the try region. </summary>
    public int TryStart { get; set; }
    /// <summary> Gets the ending IL offset of the try region. </summary>
    public int TryEnd { get; set; }

    /// <summary> Gets the starting IL offset of the filter region, or -1 if the region is not a filter. </summary>
    public int FilterStart { get; set; } = -1;
    /// <summary> Gets the ending IL offset of the filter region. This is an alias for `HandlerStart`. </summary>
    public int FilterEnd => HandlerStart;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"Try(IL_{TryStart:X4}-IL_{TryEnd:X4}) ");
        sb.Append(Kind);
        if (Kind == ExceptionRegionKind.Catch) {
            sb.Append($"<{CatchType}>");
        }
        sb.Append($"(IL_{HandlerStart:X4}-IL_{HandlerEnd:X4})");
        if (FilterStart >= 0) {
            sb.Append($" Filter IL_{FilterStart:X4}");
        }
        return sb.ToString();
    }
}