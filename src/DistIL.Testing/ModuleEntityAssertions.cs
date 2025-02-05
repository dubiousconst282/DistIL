using DistIL.AsmIO;

using Shouldly;

namespace DistIL.Testing;

[ShouldlyMethods]
public static class ModuleEntityAssertions
{
    public static void ShouldHaveCustomAttribute(this ModuleEntity entity, string fullname)
    {
        entity.GetCustomAttribs(true).Any(ca => $"{ca.Type.Namespace}.{ca.Type.Name}" == fullname).ShouldBeTrue();
    }

    public static void ShouldHaveCustomAttribute<TAttribute>(this ModuleEntity entity)
        where TAttribute : Attribute
    {
        entity.ShouldHaveCustomAttribute(typeof(TAttribute)!.FullName!);
    }
}