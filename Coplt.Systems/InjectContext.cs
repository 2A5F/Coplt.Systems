using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Coplt.Systems;

public readonly struct InjectContext(Systems systems)
{
    public ResourceProvider<Unit> DefaultResourceProvider => systems.DefaultResourceProvider;

    public T? TryGetResourceProvider<T>() where T : ResourceProvider => systems.TryGetResourceProvider<T>();
    public T GetResourceProvider<T>() where T : ResourceProvider => systems.GetResourceProvider<T>();
}
