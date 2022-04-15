using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace IRTests
{
    public class Test1
    {
        public static float SmoothStep2(float x)
        {
            return (x * x) * (3 - 2 * x);
        }
        public static float SmoothStep(float x)
        {
            return MathF.Pow(x, 2) * (3 - 2 * x);
        }
        public static int Add(int x, int y)
        {
            return x + y;
        }
        public static int LocalCall(int x, int y)
        {
            return Add(SimpleIf(x), y - 1) * 3;
        }
        public static int LocalSideEffect(int x)
        {
            return Math.Abs(x++);
        }
        public static int ArraySideEffect(int[] x, int i)
        {
            return x[i]++;
        }
        public static int MethodRefSideEffect1(int x)
        {
            int k = x;
            Inc(ref x);
            return k;
        }
        public static unsafe void PinTest1(ref int val)
        {
            fixed (int* p = &val) {
                MutPointer(p);
            }
        }
        public static unsafe void PinTest2(int[] arr)
        {
            fixed (int* p = arr) {
                MutPointer(p);
            }
        }
        public static unsafe void MutPointer(int* ptr)
        {
            *ptr = *ptr + 3;
            *ptr = *ptr * (int)ptr;
        }

        public static void InfLoop(int x)
        {
            while (true) {
                if (x > 0) {
                    x--;
                }
            }
        }

        public static int SimpleIf(int x)
        {
            if (x < 0) {
                x = 0;
            }
            return x;
        }
        public static int SimpleIf2(int x, int y)
        {
            if (x > 0) {
                int tmp = y;
                y = x;
                x = tmp;
            } else {
                x = 0;
                y = 0;
            }
            return x * y;
        }
        public static int SimpleIf3(int x, int y)
        {
            if (x > 0) {
                if (x > 4) {
                    x = 4;
                } else {
                    x++;
                }
            } else {
                x = 1;
            }
            return x * y;
        }

        public static int Loops1(int x, int y)
        {
            int a = x;
            int b = y;
            
            if (x > 0) {
                for (int i = 1; i < 16; i++) {
                    if (a * a > b) {
                        a *= y;
                    } else {
                        b *= x;
                    }
                    x /= y * i;
                }
            } else {
                x = 0;
                y = 0;
            }
            
            return a + b - x;
        }
        
        public static int Ternary_Spill(int x, int y)
        {
            return (x < y ? x : y) + 1;
        }
        
        public static int SSA0(int x, int y)
        {
            int c = x * y;
            x = x - 1;
            y = y - 2;
            c = c - x;
            return c + y;
        }
        public static int SSA1(int x, int y)
        {
            int a = x + 1;
            int b = y;
            y += 100 / x;
            int c = a + b;
            return c * a - y * y;
        }
        public static int SSA2(int x, int y)
        {
            if (x != 0) {
                x++;
            } else {
                y--;
            }
            return x + y;
        }
        public static int SSA3(int x, int y)
        {
            int tmp;
            if (x > 0) {
                tmp = x;
            } else {
                tmp = y;
            }
            return tmp;
        }
        public static int SSA4(int x)
        {
            int y = 12;
            x = y + 1;
            
            if (y > 0) {
                y = 4;
                x *= 2;
            } else {
                x = 8;
            }
            return x - 1;
        }
        public static int SSA5(int x, int y)
        {
            int r = 0;
            for (int i = 1; i < y; i++) {
                r += x;
            }
            return r;
        }
        public static int SSA6(int x)
        {
            //Predecessor on entry block
            while (true) {
                x++;
                if (x * x > 64) return x;
            }
        }

        public static long Factorial_Itr(int x)
        {
            long n = 1;
            while (x > 0) {
                n *= x;
                x--;
            }
            return n;
        }

        public static long Factorial_Recursive_If(int x)
        {
            if (x <= 0) return 1;
            return x * Factorial_Recursive_If(x - 1);
        }
        public static long Factorial_Recursive_Ternary(int x)
        {
            return x <= 0 ? 1 : x * Factorial_Recursive_Ternary(x - 1);
        }

        public static float SumAbsDiff(float[] a, float[] b)
        {
            float r = 0;
            for (int i = 0; i < a.Length; i++) {
                r += Math.Abs(a[i] - b[i]);
            }
            return r;
        }

        public static int LoopBranch1(int[] a, int[] b)
        {
            int r = 0;
            for (int i = 0; i < a.Length; i++) {
                r += a[i] > b[i] ? 1 : 0;
                if (a[i] > b[i]) {
                    int tmp = b[i];
                    b[i] = a[i];
                    a[i] = tmp;
                }
            }
            for (int i = a.Length; i < b.Length; i++) {
                b[i] = 0;
            }
            return r;
        }
        
        public static int Switch1(int m, int x, int y)
        {
            switch (m) {
                case 1: return x * 10 + y;
                case 2: return x * 20 - y * 3;
                case 3: return x * x + y * y;
                default: return x;
            }
        }

        public static void Cond1(int x, int y)
        {
            Nop(x > y ? 1 : 0); //cgt x, y
            Nop(x > y ? 2 : 4); //4 - (cgt x, y) * 2
            Nop(x > y ? 3 : 4); //4 - (cgt x, y)
            Nop(x > y ? 4 : 3); //(cgt x, y) + 3
            Nop(x > y ? -4 : 4); //4 - (cgt x, y) * 8
            Nop(x > y ? 2 : -2); //(cgt x, y) * 4 - 2
            Nop(x > y ? 15 : 4); //diff not a pow2; not simplified

            int r1, r2;
            if (x > y) {
                (r1, r2) = (2, 3);
            } else {
                (r1, r2) = (3, 4);
            }
            Nop(r1);
            Nop(r2);
        }
        //using Nop() because we don't handle multiple returns yet
        public static int Cond2(bool c)
        {
            return Nop(c ? 1 : 0); //cne x, 0
        }
        public static bool Cond3(int x, int y)
        {
            return Nop(x != 0 || y != 0); //(x | y) != 0
        }
        public static int Nop(int x) => x;
        public static bool Nop(bool x) => x;

        public static int DCE1(int x, int y)
        {
            int z = Nop(0);
            if (x * z > 0) {
                y += x * x - 1;
                if (y < 0) y = -y;
                if (y > 100) y = 100;
                y *= 4;
            } else {
                y *= 12;
            }
            return y;
        }
        public static void DblRoots(double a, double b, double c, ref double r1, ref double r2)
        {
            r1 = (-b + Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
            r2 = (-b - Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
        }

        private static int s_Int;
        public static unsafe void AddrOf(int[] arr)
        {
            ref int src = ref arr[0];
            ref int dst = ref s_Int;
            delegate* managed<int[], void> a = &ArrStore;
            delegate* unmanaged[Cdecl]<int*, nint, long> ac = &UnmanagedSum;

            dst = src;
        }
        public static void ArrStore(int[] arr)
        {
            arr[0] = 1;
        }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static unsafe long UnmanagedSum(int* p, nint len)
        {
            long sum = 0;
            for (int i = 0; i < len; i++) {
                sum += p[i];
            }
            return sum;
        }

        public static void ObjArray(string[] arr)
        {
            arr[0] = "hello";
            arr[1] = string.Empty;
            arr[2] = null;
            ref string s = ref arr[0];
            s += "x";
        }
        public static unsafe DateTime StructPtrs(DateTime* val)
        {
            val[0] = val[0].AddSeconds(-5);
            return val[0];
        }

        public static byte[] NewArray()
        {
            var barr = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            barr[0]++;
            return barr;
        }

        public struct Foo { public int bar; }

        public static int NestedAddr( Foo foo)
        {
            ref int x = ref foo.bar;
            return x;
        }

        public static int Inc(ref int x)
        {
            return x++;
        }

        public static int Try1(string s)
        {
            try {
                return int.Parse(s);
            } catch {
                return 0;
            }
        }
        public static int Try2(string s)
        {
            try {
                return int.Parse(s);
            } finally {
                Console.WriteLine("finally");
            }
        }

        public static int[] Linq1(int[] arr)
        {
            return arr.Where(x => x > 0)
                      .Select(x => x * 2)
                      .ToArray();
        }
        //public static async Task Async1()
        //{
        //    await Task.Delay(1);
        //}
        //public static void Lambda1()
        //{
        //    UseLambda1(x => { });
        //}
        //public static void Lambda2()
        //{
        //    int x = 1;
        //    UseLambda1(y => x = x * y - 1);
        //    Console.WriteLine(x);
        //}
        //public static void Lambda3()
        //{
        //    UseLambda1(Console.WriteLine);
        //}
        //public static void UseLambda1(Action<int> v)
        //{
        //    v(1);
        //}

        public static int ObjAccess(int[] arr1, int[] arr2, int startIndex, int seed)
        {
            var bar = new Bar();
            bar.i = startIndex;
            while (bar.MoveNext(arr1)) {
                arr2[bar.r & 15] = seed;
                seed = (seed * 8121 + 28411) % 134456;
            }
            return bar.r;
        }
        
        public class Bar
        {
            public int i, r;

            public bool MoveNext(int[] a)
            {
                if (i < 16) {
                    r += a[i++] < 8 ? 1 : 0;
                    return true;
                }
                return false;
            }
        }
    }
}
