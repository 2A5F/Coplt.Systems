using System;
using System.Collections.Concurrent;

namespace Coplt.Systems;

public abstract class ResourceProviderAttribute<T> : Attribute where T : ResourceProvider;

public abstract class ResourceProvider
{
    public abstract ResRef<T> GetRef<T>();
    public virtual void Clear() { }
}

public class DefaultResourceProvider : ResourceProvider
{
    internal ResourceContainer m_resources = new();

    public override ResRef<T> GetRef<T>() => m_resources.GetOrAdd<T>();

    public override void Clear() => m_resources = new();
}
