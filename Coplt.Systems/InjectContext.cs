using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Coplt.Systems;

public readonly struct InjectContext(Systems systems)
{
    public void Set<T>(T value) => systems.SetResource(value);
    public T Get<T>() => systems.GetResource<T>();
    
    public InjectRef<T> GetRef<T>() => systems.GetResourceRef<T>();
}
