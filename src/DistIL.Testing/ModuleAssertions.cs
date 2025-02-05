using DistIL.AsmIO;

using Shouldly;

namespace DistIL.Testing;

[ShouldlyMethods]
public static class ModuleAssertions
{
    public static void ShouldHaveName(this ModuleDef module, string name)
    {
        module.AsmName.Name.ShouldBe(name);
    }

    public static void ShouldHaveVersion(this ModuleDef module, Version version)
    {
        module.AsmName.Version.ShouldBe(version);
    }

    public static void ShouldHaveCustomAttribute(this ModuleDef module, string fullname, bool forAssembly = true)
    {
        module.GetCustomAttribs(forAssembly).Any(ca => $"{ca.Type.Namespace}.{ca.Type.Name}" == fullname).ShouldBeTrue();
    }

    public static void ShouldHaveCustomAttribute<TAttribute>(this ModuleDef module, bool forAssembly = true)
        where TAttribute : Attribute
    {
        module.ShouldHaveCustomAttribute(typeof(TAttribute)!.FullName!, forAssembly);
    }

    public static void ShouldContainType(this ModuleDef module, string ns, string name, bool includeExports = true)
    {
        module.FindType(ns, name, includeExports, throwIfNotFound: true).ShouldNotBeNull();
    }
}
