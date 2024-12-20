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

public class SystemRefResourceProvider(Systems Systems) : ResourceProvider<SystemRefAttribute>
{
    public override ResRef<T> GetRef<T>(SystemRefAttribute data, ResReq req = default)
    {
        if (typeof(T) == typeof(SystemHandle))
        {
            if (data.System is null)
            {
                if (req.SrcSystem is { } handle) return new(handle, ResRefType.ReadRef);
                throw new ArgumentException("No system type provided");
            }
            else
            {
                var handle = new SystemHandle(Systems.m_system_instances.GetOrAdd(data.System));
                return new(handle, ResRefType.ReadRef);
            }
        }
        if (typeof(T).IsAssignableTo(typeof(ISystemBase)))
        {
            return Systems.m_system_instances.GetOrAdd<T>();
        }
        throw new ArgumentException($"{typeof(T)} is not a system or SystemHandle");
    }
}
