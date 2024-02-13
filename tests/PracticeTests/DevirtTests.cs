namespace DistIL.PracticeTests;

using DistIL.Attributes;

[Optimize, CheckCodeGenAfterRun]
public class DevirtTests
{
    [Fact]
    public void Delegate_CallVirt()
    {
        var vfn = new Func<int>(new Deriv1().Foo);
        Assert.Equal(150, vfn.Invoke());

        var dfn = new Deriv1().GetBaseFoo();
        Assert.Equal(100, dfn.Invoke());

        // CHECK-NOT: newobj Func`1
        // CHECK: callvirt DevirtTests+Base::Foo
        // CHECK-NOT: newobj Func`1
        // CHECK: call DevirtTests+Base::Foo
    }

    class Base {
        public virtual int Foo() => 100;
    }
    class Deriv1 : Base {
        public override int Foo() => base.Foo() + 50;

        [Optimize]
        public Func<int> GetBaseFoo() => base.Foo;
    }
}