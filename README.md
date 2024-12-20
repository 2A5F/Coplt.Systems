# Coplt.Systems

Automatic system (the S in ECS) scheduling and dependency injection

## Examples

```csharp
[System]
public partial class SysGroup1 : ISystemGroup;
    
public struct Data
{
    public int inc;
}

[System]
public readonly partial struct Sys1<T>(ref float a, int b) // di on constructor parameters
{
    // di on partial properties
    public partial ref Data Data { get; }
    public partial ref readonly Data Data2 { get; }
    
    private readonly float some = a + b;
    
    public void Update(ref int a) // di on parameters
    {
        Data.inc++;
    }
}

// use

// Build systems
var sys = new Systems();
sys.SetResource(new Data()); // Resources will be automatically dependency injected
sys.Add<Sys1<int>>();
sys.Add<SysGroup1>();

// Call update every frame
sys.Update();

// Resources can be modified and retrieved at any time (thread-safe)
var inc = sys.GetResource<Data>().inc;
Console.WriteLine(inc);
Assert.That(inc, Is.EqualTo(1));
```

---

Setting groups and order via attributes

```csharp
[System(Group = typeof(SysGroup1))]
public readonly partial struct Sys4
{
    private void Update(ref Data data) => data.inc *= 2;
}

[System(Group = typeof(SysGroup1), Before = [typeof(Sys4)])]
public readonly partial struct Sys5
{
    private void Update(ref Data data) => data.inc *= 3;
}

[System(After = [typeof(SysGroup1)])]
public readonly partial struct Sys6
{
    private void Update(ref Data data) => data.inc += 3;
}

var sys = new Systems();
sys.SetResource(new Data { inc = 6 });
sys.Add<SysGroup1>();
sys.Add<Sys4>();
sys.Add<Sys5>();
sys.Add<Sys6>();

sys.Update();

var inc = sys.GetResource<Data>().inc;
Console.WriteLine(inc);
Assert.That(inc, Is.EqualTo(39));
```

---

Custom resource providers

```csharp
public class NullResourceProvider : ResourceProvider<NullAttribute>
{
    public override ResRef<T> GetRef<T>(NullAttribute data, ResReq req = default)
    {
        Console.WriteLine(data.Msg);
        return default;
    }
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class NullAttribute(string Msg = "") : ResourceProviderAttribute<NullResourceProvider, NullAttribute>
{
    public string Msg { get; } = Msg;

    public override NullAttribute GetData() => this;
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class NullAttribute(string Msg = "") : ResourceProviderAttribute<NullResourceProvider>
{
    public string Msg { get; } = Msg;
}

[System]
public partial class NullResourceProviderSystem([Null] object? a) : ISystem
{
    [Null("Some")]
    public partial object? Some { get; set; }
    public partial object? Foo { get; set; }

    public void Update([Null] object? b) { }
}

var sys = new Systems();
sys.SetResourceProvider(new NullResourceProvider());
sys.Add<NullResourceProviderSystem>();
sys.Update();

// print: Some
```
