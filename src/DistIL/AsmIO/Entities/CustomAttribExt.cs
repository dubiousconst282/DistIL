namespace DistIL.AsmIO;

public static class CustomAttribExt
{
    internal static IList<CustomAttrib> GetOrInitList(ref IList<CustomAttrib>? list, bool readOnly)
    {
        var attribs = list ?? Array.Empty<CustomAttrib>();

        if (!readOnly && list is not List<CustomAttrib>) {
            return list = new List<CustomAttrib>(attribs);
        }
        return attribs;
    }

    public static CustomAttrib? Find(this IList<CustomAttrib> list, string? ns, string className)
    {
        return list.FirstOrDefault(ca => {
            var declType = ca.Constructor.DeclaringType;
            return declType.Name == className && declType.Namespace == ns;
        });
    }
}