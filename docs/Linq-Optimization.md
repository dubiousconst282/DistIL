# Linq Optimization

Linq optimization is one of the main reasons DistIL exists, ~~the other is gross underestimatation of how much time and effort it would require~~.

The pass is defined in `InlineLinq`, and contains most of the query identification logic. The actual code generation is made by `QuerySynthesizer` and `Stage.Synth()`.

The synthesizer does not actually inline lambdas (calls are made through Invoke()). This step is left for another pass to deal with (InlineLambdas), in order to handle cases that will arrive with basic method inlining.

Since loop analysis is not _yet_ implemented, only a few reduction stages (e.g. ToArray, Count) can be handled.

## Easy cases: reduction queries

```cs
//Original code:
int[] ArrayTransform(int[] arr) {
    return arr.Where(x => x > 0)
              .Select(x => x * 2)
              .ToArray();
}
//Roslyinified:
[CompilerGenerated]
class _PrivData {
    static _PrivData Instance = new();
    static Func<int, int> PredCache;
    static Func<int, bool> MapperCache;
    
    bool Pred(int x) => x > 0; 
    int Mapper(int x) => x * 2;
}
int[] ArrayTransform_Lowered(int[] arr) {
    // `field ??= value` gets lowered into `var tmp = field; if (tmp == null) { field = tmp = new Func(...); }`
    //It's fairly easy to recognize in the IR.
    var pred = _PrivData.PredCache ??= new Func<int, bool>(_PrivData.Instance, &_PrivData.Pred);
    var stage1 = Enumerable.Where(arr, pred);
    var mapper = _PrivData.MapperCache ??= new Func<int, bool>(_PrivData.Instance, &_PrivData.Mapper);
    var stage2 = Enumerable.Select(stage1, mapper);
    return stage2.ToArray();
}
//Expected _distillation_ (before lambda inlining)
int[] ArrayTransform_Distilled(int[] arr) {
    var pred = _PrivData.PredCache ??= new Func<int, bool>(_PrivData.Instance, &_PrivData.Pred);
    var mapper = _PrivData.MapperCache ??= new Func<int, bool>(_PrivData.Instance, &_PrivData.Mapper);

    var result = new int[arr.Length];
    int j = 0;
    for (int i = 0; i < arr.Length; i++) {
        int currItem = arr[i];
        if (pred.Invoke(currItem)) {
            result[j++] = mapper.Invoke(currItem);
        }
    }
    if (j != arr.Length) {
        var newResult = new int[j];
        Array.Copy(result, newResult, j);
        result = newResult;
    }
    return result;
}
```