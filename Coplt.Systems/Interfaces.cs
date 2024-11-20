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
    /// The system group can control whether or how often a subsystem is updated.
    /// <br/>
    /// <para>Do not call update in parallel</para>
    /// <para>This method has no dependency injection</para>
    /// </summary>
    public void UpdateSybSystems(GroupUpdateContext ctx) => ctx.Update();
}
