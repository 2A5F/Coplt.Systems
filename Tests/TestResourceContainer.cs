using Coplt.Systems;

namespace Tests;

public class TestResourceContainer
{
    [Test]
    public void Test1()
    {
        var rc = new ResourceContainer();
        var a = rc.GetOrAdd<int>();
        Assert.That(a.Get(), Is.Zero);
        a.GetMutRef() = 123;
        Console.WriteLine(a.Get());
        Assert.That(a.Get(), Is.EqualTo(123));
    }
    
    [Test]
    public void Test2()
    {
        var rc = new ResourceContainer();
        var a = rc.GetOrAdd<string>();
        Assert.That(a.Get(), Is.Null);
        a.GetMutRef() = "asd";
        Console.WriteLine(a.Get());
        Assert.That(a.Get(), Is.EqualTo("asd"));
    }
}
