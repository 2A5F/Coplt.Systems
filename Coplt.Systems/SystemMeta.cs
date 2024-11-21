using System;

namespace Coplt.Systems;

public record SystemMeta
{
    public long Partition { get; set; }
    public Type? Group { get; set; }
    public Type[] Before { get; set; } = [];
    public Type[] After { get; set; } = [];
    public bool Parallel { get; set; } = false;
    public bool Setup { get; set; } = true;
    public bool Update { get; set; } = true;
}
