using System;

namespace Coplt.Systems;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class SystemAttribute : Attribute
{
    /// <summary>
    /// The order of system update will be based on the partition order, e.g. -1 may be earlier than the default, 1 may be later
    /// </summary>
    public long Partition { get; set; }
    public Type? Group { get; set; }
    public Type[] Before { get; set; } = [];
    public Type[] After { get; set; } = [];
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SetupAttribute : Attribute
{
    public int Order { get; set; }
    public bool Exclude { get; set; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class UpdateAttribute : Attribute
{
    public int Order { get; set; }
    public bool Exclude { get; set; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectAttribute : Attribute
{
    public bool Exclude { get; set; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SkipSetupAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SkipUpdateAttribute : Attribute;

/// <summary>
/// Inject references to other or local systems
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SystemRefAttribute : ResourceProviderAttribute<SystemRefResourceProvider, SystemRefAttribute>
{
    /// <summary>
    /// Inject references to other or local systems
    /// </summary>
    /// <param name="system">If the injected type is SystemHandle and no system type is provided the result will be the current system</param>
    public SystemRefAttribute(Type? system = null)
    {
        System = system;
    }
    /// <summary>
    /// If the injected type is SystemHandle and no system type is provided the result will be the current system
    /// </summary>
    public Type? System { get; }
    public override SystemRefAttribute GetData() => this;
}
