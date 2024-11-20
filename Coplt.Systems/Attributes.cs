using System;

namespace Coplt.Systems;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public class SystemAttribute : Attribute
{
    public Type? Group { get; set; }
    public Type[] Before { get; set; } = [];
    public Type[] After { get; set; } = [];
    /// <summary>
    /// Allows the system to execute in parallel
    /// </summary>
    public bool Parallel { get; set; } = false;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class UpdateAttribute : Attribute
{
    public int Order { get; set; }
    public bool Exclude { get; set; }
}

[AttributeUsage(AttributeTargets.Property)]
public class InjectAttribute : Attribute
{
    public bool Exclude { get; set; }
}
