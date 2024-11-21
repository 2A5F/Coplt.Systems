using System.Collections.Generic;

namespace Coplt.Systems;

public readonly struct GroupUpdateContext
{
    internal readonly List<Systems.Stage> Stages;

    internal GroupUpdateContext(List<Systems.Stage> stages)
    {
        Stages = stages;
    }

    public void Update()
    {
        foreach (var stage in Stages)
        {
            stage.Update();
        }
    }
}
