using System;

namespace Coplt.Systems;

public interface ISystemBase : IDisposable
{
    /// <summary>
    /// Manual implementation is not recommended
    /// </summary>
    public static abstract void AddToSystems(Systems systems);
    /// <summary>
    /// Manual implementation is not recommended
    /// </summary>
    public static abstract void Create(InjectContext ctx, SystemHandle handle);
    /// <summary>
    /// Will only be called once
    /// </summary>
    public void Setup();
    /// <summary>
    /// Update the system, manual implementation is not recommended
    /// </summary>
    public void Update();
}

public interface ISystem : ISystemBase;

public interface ISystemGroup : ISystemBase
{
    /// <summary>
    /// The system group can control whether or how often a subsystem is updated.
    /// <br/>
    /// <para>Do not call update in parallel</para>
    /// <para>This method has no dependency injection</para>
    /// </summary>
    public void UpdateSybSystems(GroupUpdateContext ctx) => ctx.Update();
}
