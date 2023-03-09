namespace TestAsm.TypeSys.Generics;

public class GenBase<T>
{
    public virtual T GetFoo<M>(M x) => default(T);
}
public class DerivedA<T> : GenBase<T>
{
    public override T GetFoo<M>(M x) => default(T);
}
public class DerivedB<T> : DerivedA<T> { }
public class DerivedC<T> : DerivedB<(T, int)> { }

public class DerivedB_KnownArg : DerivedA<string> { }
public class DerivedC_KnownArg : DerivedB_KnownArg { }

public static class GenRefs
{
    public static int DerivedC_Str(DerivedC<string> x) => x.GetFoo(123).Item2;
    public static int DerivedC_KA(DerivedC_KnownArg x) => x.GetFoo(123)?.Length ?? 0;
}