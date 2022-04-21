namespace DistIL.AsmIO;

using System.Reflection.Metadata;

internal static class MetadataReaderEx
{
    public static string? GetOptString(this MetadataReader reader, StringHandle handle) 
        => handle.IsNil ? null : reader.GetString(handle);
}