using System;

namespace Coplt.Systems;

public interface ISystemBase: IDisposable
{
    /// <summary>
    /// Creation, manual implementation is not recommended
    /// </summary>
    public static abstract void Create(SetupContext ctx, ref object slot);
    /// <summary>
    /// Update the system, manual implementation is not recommended
    /// </summary>
    public void Update();
}

public interface ISystem : ISystemBase;

public interface ISystemGroup : ISystemBase
{
    /// <summary>
    /// Will be called after calling Update
    /// </summary>
    /// <returns>Should the subsystem within the group be updated?</returns>
    public bool ShouldUpdate() => true;
}
