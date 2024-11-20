using System;
using System.Runtime.CompilerServices;

namespace Coplt.Systems;

public abstract class InjectRef
{
    public abstract ref T TryGet<T>();
}

public sealed class InjectRef<T> : InjectRef
{
    internal T m_data = default!;

    public InjectRef() { }
    public InjectRef(T data)
    {
        m_data = data;
    }

    public ref T Value => ref m_data;

    public override ref T1 TryGet<T1>()
    {
        if (typeof(T1) != typeof(T)) return ref Unsafe.NullRef<T1>();
        return ref Unsafe.As<T, T1>(ref Value);
    }
}
