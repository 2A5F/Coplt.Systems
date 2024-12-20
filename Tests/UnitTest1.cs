using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Coplt.Dropping;
using Coplt.Systems;

namespace Tests;

[StructLayout(LayoutKind.Auto)]
public partial class Tests
{
    [SetUp]
    public void Setup() { }

    public struct Data
    {
        public int inc;
    }


    [System]
    public readonly partial struct Sys1<T>(ref float a, int b)
    {
        public partial ref Data Data { get; }
        public partial ref readonly Data Data2 { get; }
        private readonly float some = a + b;
        public void Setup(int a) { }
        public void Update(ref int a)
        {
            Data.inc++;
        }
    }

    [System]
    public partial class SysGroup1 : ISystemGroup;

    [Test]
    public void Test1()
    {
        var sys = new Systems();
        sys.UnhandledException += e => e.Throw();
        sys.Logger = Systems.ConsoleLogger.Instance;
        sys.SetResource(new Data());
        sys.Add<Sys1<int>>();
        sys.Add<SysGroup1>();
        sys.Update();
        var inc = sys.GetResource<Data>().inc;
        Console.WriteLine(inc);
        Assert.That(inc, Is.EqualTo(1));
    }

    [System(Group = typeof(SysGroup1))]
    public readonly partial struct Sys2
    {
        private void Update(ref Data data) => data.inc *= 2;
    }

    [System(Group = typeof(SysGroup1), Before = [typeof(Sys2)])]
    public readonly partial struct Sys3
    {
        private void Update(ref Data data) => data.inc *= 3;
    }

    [System(After = [typeof(SysGroup1)])]
    public readonly partial struct Sys4
    {
        private void Update(ref Data data) => data.inc += 3;
    }

    [Test]
    public void Test2()
    {
        var sys = new Systems();
        sys.UnhandledException += e => e.Throw();
        sys.Logger = Systems.ConsoleLogger.Instance;
        sys.SetResource(new Data { inc = 6 });
        sys.Add<SysGroup1>();
        sys.Add<Sys2>();
        sys.Add<Sys3>();
        sys.Add<Sys4>();
        sys.Update();
        var inc = sys.GetResource<Data>().inc;
        Console.WriteLine(inc);
        Assert.That(inc, Is.EqualTo(39));
    }

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

    [System]
    public partial class NullResourceProviderSystem([Null] object? a) : ISystem
    {
        [Null("Some")]
        public partial object? Some { get; set; }
        public partial object? Foo { get; set; }
        [SystemRef]
        public partial NullResourceProviderSystem Self { get; }

        public void Update([Null] object? b)
        {
            Console.WriteLine($"{a} {b} {Some} {Foo}");
        }
    }

    [Test]
    public void Test3()
    {
        var sys = new Systems();
        sys.UnhandledException += e => e.Throw();
        sys.Logger = Systems.ConsoleLogger.Instance;
        sys.SetResourceProvider(new NullResourceProvider());
        sys.Add<NullResourceProviderSystem>();
        sys.Update();
    }
}
