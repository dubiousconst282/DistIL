namespace DistIL.AsmIO;

using System.Reflection.Metadata.Ecma335;

/// <summary> A token specifying the location of an instruction. </summary>
public readonly struct SourceLocation
{
    // This struct will be added to every instruction instance, so it's desirable to make it as small as possible.
    //
    // Metadata tables are limited to at most (2^24)-2 entries, per ECMA spec. This leaves us with
    // 40 bits from a total of 64.  An even split to 20 bits limits us to at most 1 million modules,
    // and 1MB of IL, which seems more than reasonable.
    // Realistically there'd be far worse problems than wrong sequence points for an 1MB+ method.
    readonly ulong _data;

    /// <summary> Indicates if this location has no parent method, or if it is unknown. </summary>
    /// <remarks> A null location may still be assigned an offset. </remarks>
    public bool IsNull => ModuleId == 0;

    public int Offset => (int)(_data >> 0) & 0xFFFFF;       // 20 bits
    private int ModuleId => (int)(_data >> 44) & 0xFFFFF;   // 20 bits
    private int MethodRid => (int)(_data >> 20) & 0xFFFFFF; // 24 bits

    public SourceLocation(MethodDef method, int offset)
    {
        int moduleId = method.Module._resolverIndex + 1;
        int methodRid = MetadataTokens.GetRowNumber(method._handle);

        // If we can't represent moduleId in 20 bits, create a null location instead.
        moduleId = methodRid == 0 || moduleId >= 0xFFFFF ? 0 : moduleId;

        _data = (ulong)moduleId << 44 | (ulong)methodRid << 20 | Math.Min((ulong)offset, 0xFFFFF);

        Debug.Assert(MethodRid == methodRid);
    }
    private SourceLocation(ulong data) => _data = data;

    public MethodDef? GetMethod(ModuleResolver resolver)
    {
        if (IsNull) return null;

        var module = resolver._loadedModules[ModuleId - 1];
        return module._loader?.GetMethod(MetadataTokens.MethodDefinitionHandle(MethodRid));
    }

    /// <summary> Returns a location within the current method, at the specified offset. </summary>
    public SourceLocation WithOffset(int newOffset)
    {
        return new SourceLocation((_data & ~0xFFFFFul) | Math.Min((ulong)newOffset, 0xFFFFF));
    }

    public override string ToString() => $"IL_{Offset:X4} (at {ModuleId}+{MethodRid})";
}