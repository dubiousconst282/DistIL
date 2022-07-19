namespace DistIL.AsmIO;

using System.Reflection.Metadata;

internal static class MetadataReaderEx
{
    public static string? GetOptString(this MetadataReader reader, StringHandle handle) 
        => handle.IsNil ? null : reader.GetString(handle);

    public static object? DecodeConst(this MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil) {
            return null;
        }
        var cst = reader.GetConstant(handle);
        var blob = reader.GetBlobReader(cst.Value);

        #pragma warning disable format
        return cst.TypeCode switch {
            ConstantTypeCode.Boolean    => blob.ReadBoolean(),
            ConstantTypeCode.Char       => blob.ReadChar(),
            ConstantTypeCode.SByte      => blob.ReadSByte(),
            ConstantTypeCode.Byte       => blob.ReadByte(),
            ConstantTypeCode.Int16      => blob.ReadInt16(),
            ConstantTypeCode.UInt16     => blob.ReadUInt16(),
            ConstantTypeCode.Int32      => blob.ReadInt32(),
            ConstantTypeCode.UInt32     => blob.ReadUInt32(),
            ConstantTypeCode.Int64      => blob.ReadInt64(),
            ConstantTypeCode.UInt64     => blob.ReadUInt64(),
            ConstantTypeCode.Single     => blob.ReadSingle(),
            ConstantTypeCode.Double     => blob.ReadDouble(),
            ConstantTypeCode.String     => blob.ReadUTF16(blob.Length),
            ConstantTypeCode.NullReference => null,
            _ => throw new NotSupportedException()
        };
        #pragma warning restore format
    }
}