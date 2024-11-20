using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Coplt.Systems;

public readonly struct SetupContext(Systems systems)
{
    public T Get<T>() => systems.GetResource<T>();
    
    public InjectRef<T> GetRef<T>() => systems.GetResourceRef<T>();
}
