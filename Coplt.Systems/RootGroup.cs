namespace Coplt.Systems;

[SkipSetup]
public struct RootGroup : ISystemGroup
{
    public static void AddToSystems(Systems systems) => systems.Add<RootGroup>();
    public static void Create(InjectContext ctx, SystemHandle handle)
    {
        ref var self = ref handle.UnsafeAs<RootGroup>().GetMutRef();
        self = new();
    }
    public void UpdateSybSystems(GroupUpdateContext ctx) => ctx.Update();
    public void Setup() { }
    public void Update() { }
    public void Dispose() { }
}
