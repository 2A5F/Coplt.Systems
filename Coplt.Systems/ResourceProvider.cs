using System;
using System.Collections.Concurrent;

namespace Coplt.Systems;

public record struct ResReq
{
    public SystemHandle? SrcSystem { get; set; }
}

public abstract class ResourceProviderAttribute<T, D> : Attribute where T : ResourceProvider<D>
{
    public abstract D GetData();
}

public abstract class ResourceProvider
{
    public virtual void Clear() { }
}

public abstract class ResourceProvider<D> : ResourceProvider
{
    public abstract ResRef<T> GetRef<T>(D data, ResReq req = default);
}

public class DefaultResourceProvider : ResourceProvider<Unit>
{
    internal readonly ResourceContainer m_resources = new();

    public override ResRef<T> GetRef<T>(Unit data, ResReq req = default) => m_resources.GetOrAdd<T>();

    public override void Clear() => m_resources.Clear();
}
