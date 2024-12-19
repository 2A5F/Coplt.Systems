using System;

namespace Coplt.Systems;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public class SystemAttribute : Attribute
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
public class SetupAttribute : Attribute
{
    public int Order { get; set; }
    public bool Exclude { get; set; }
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

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class SkipSetupAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class SkipUpdateAttribute : Attribute;
