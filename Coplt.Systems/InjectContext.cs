using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Coplt.Systems;

public readonly struct InjectContext(Systems systems)
{
    public ResourceProvider DefaultResourceProvider => systems.DefaultResourceProvider;
}
