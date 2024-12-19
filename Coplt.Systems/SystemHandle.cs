namespace Coplt.Systems;

/// <summary>
/// The reference held by SystemHandle guarantees that the reference type has a reference location
/// </summary>
public readonly struct SystemHandle(UnTypedResRef SystemRef)
{
    public UnTypedResRef SystemRef { get; } = SystemRef;

    public ResRef<S> UnsafeAs<S>() where S : ISystemBase => SystemRef.UnsafeAs<S>();
}
