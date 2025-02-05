using System.Reflection;
using System.Runtime.CompilerServices;

using DistIL.AsmIO;

using Shouldly;

namespace DistIL.Testing;

[ShouldlyMethods]
public static class TypeAssertions
{
    public static void ShouldHaveName(this TypeDef type, string name)
    {
        type.Name.ShouldBe(name);
    }

    public static void ShouldHaveNamespace(this TypeDef type, string ns)
    {
        type.Namespace.ShouldBe(ns);
    }

    public static void ShouldHaveBaseType(this TypeDef type, string ns, string name)
    {
        type.BaseType.ShouldNotBeNull();
        type.BaseType!.Namespace.ShouldBe(ns);
        type.BaseType!.Name.ShouldBe(name);
    }

    public static void ShouldBeEnum(this TypeDef type)
    {
        type.IsEnum.ShouldBeTrue();
    }

    public static void ShouldBeEnumWithFlags(this TypeDef type)
    {
        type.IsEnum.ShouldBeTrue();
        type.HasCustomAttrib(typeof(FlagsAttribute));
    }

    public static void ShouldBeClass(this TypeDef type)
    {
        type.IsClass.ShouldBeTrue();
    }

    public static void ShouldBeInterface(this TypeDef type)
    {
        type.IsInterface.ShouldBeTrue();
    }

    public static void ShouldBeValueType(this TypeDef type)
    {
        type.IsValueType.ShouldBeTrue();
    }

    public static void ShouldBeAbstract(this TypeDef type)
    {
        type.Attribs.HasFlag(TypeAttributes.Abstract).ShouldBeTrue();
    }

    public static void ShouldBeSealed(this TypeDef type)
    {
        type.Attribs.HasFlag(TypeAttributes.Sealed).ShouldBeTrue();
    }

    public static void ShouldBePublic(this TypeDef type)
    {
        type.Attribs.HasFlag(TypeAttributes.Public).ShouldBeTrue();
    }

    public static void ShouldBePrivate(this TypeDef type)
    {
        type.Attribs.HasFlag(TypeAttributes.NotPublic).ShouldBeTrue();
    }

    public static void ShouldBeInternal(this TypeDef type)
    {
        type.Attribs.HasFlag(TypeAttributes.NestedAssembly).ShouldBeTrue();
    }

    public static void ShouldBeProtected(this TypeDef type)
    {
        type.Attribs.HasFlag(TypeAttributes.NestedFamily).ShouldBeTrue();
    }

    public static void ShouldImplement(this TypeDef type, string ns, string name)
    {
        type.Interfaces.ShouldContain(i => i.Namespace == ns && i.Name == name);
    }

    public static void ShouldImplement<T>(this TypeDef type)
    {
        type.Interfaces.ShouldContain(i => i.Namespace == typeof(T).Namespace && i.Name == typeof(T).Name);
    }

    public static void ShouldHaveField(this TypeDef type, string name, string fieldType)
    {
        type.Fields.ShouldContain(f => f.Name == name && f.Type.Namespace == fieldType);
    }

    public static void ShouldHaveProperty(this TypeDef type, string name)
    {
        type.Properties.ShouldContain(p => p.Name == name);
    }

    public static void ShouldHaveMethod(this TypeDef type, string name)
    {
        type.Methods.ShouldContain(m => m.Name == name);
    }

    public static void ShouldHaveMethod(this TypeDef type, string name, string returnType)
    {
        type.Methods.ShouldContain(m => m.Name == name && m.ReturnType.Namespace == returnType);
    }
}
