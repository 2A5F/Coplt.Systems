using System;
using System.Reflection;

namespace Coplt.Systems;

public record SystemMeta
{
    public long Partition { get; set; }
    public Type? Group { get; set; }
    public Type[] Before { get; set; } = [];
    public Type[] After { get; set; } = [];
    public bool Setup { get; set; } = true;
    public bool Update { get; set; } = true;
    
    public static SystemMeta FromType(Type type)
    {
        var attr = type.GetCustomAttribute<SystemAttribute>();
        return new()
        {
            Partition = attr?.Partition ?? 0,
            Group = attr?.Group,
            Before = attr?.Before ?? [],
            After = attr?.After ?? [],
            Setup = type.GetCustomAttribute<SkipSetupAttribute>() is null,
            Update = type.GetCustomAttribute<SkipUpdateAttribute>() is null
        };
    }
}
