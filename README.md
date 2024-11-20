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
sys.AddSystem<Sys1<int>>();
sys.AddSystemGroup<SysGroup1>();
sys.Setup();

// Call update every frame
sys.Update();

// Resources can be modified and retrieved at any time (thread-safe)
var inc = sys.GetResource<Data>().inc;
Console.WriteLine(inc);
Assert.That(inc, Is.EqualTo(1));
```

---

Parallel systems of the same order can automatically execute in parallel

```csharp
[System(Parallel = true)]
public readonly partial struct Sys2
{
    private void Update(ref Data data) => Interlocked.Increment(ref data.inc);
}

[System(Parallel = true)]
public readonly partial struct Sys3
{
    private void Update(ref Data data) => Interlocked.Decrement(ref data.inc);
}

var sys = new Systems();
sys.SetResource(new Data { inc = 6 });
sys.AddSystem<Sys2>();
sys.AddSystem<Sys3>();
sys.Setup();

sys.Update();

var inc = sys.GetResource<Data>().inc;
Console.WriteLine(inc);
Assert.That(inc, Is.EqualTo(6));
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
sys.AddSystemGroup<SysGroup1>();
sys.AddSystem<Sys4>();
sys.AddSystem<Sys5>();
sys.AddSystem<Sys6>();
sys.Setup();

sys.Update();

var inc = sys.GetResource<Data>().inc;
Console.WriteLine(inc);
Assert.That(inc, Is.EqualTo(39));
```
