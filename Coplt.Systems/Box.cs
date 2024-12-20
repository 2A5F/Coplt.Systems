using System;
using System.Runtime.CompilerServices;

namespace Coplt.Systems;

public abstract class DynBox : IStrongBox
{
    public abstract ref T GetRef<T>();
    public abstract T Get<T>();

    public abstract ref T TryGetMutRef<T>();
    public abstract ref T TryGetImmRef<T>();
    public abstract bool TryGet<T>(out T value);
    public abstract object? DynValue { get; set; }

    object? IStrongBox.Value
    {
        get => DynValue;
        set => DynValue = value;
    }
}

public class Box<T> : DynBox
{
    public T? Value;

    public override ref R GetRef<R>()
    {
        if (typeof(T) == typeof(R)) return ref Unsafe.As<T, R>(ref Value!);
        throw new InvalidCastException($"Unable to convert reference(pointer) type {typeof(T)} to {typeof(R)}");
    }
    public override R Get<R>()
    {
        if (typeof(T) == typeof(R)) return Unsafe.As<T, R>(ref Value!);
        else if (typeof(T).IsAssignableTo(typeof(R))) return (R)(object)Value!;
        throw new InvalidCastException($"Cannot cast {typeof(T)} to {typeof(R)}");
    }
    public override ref R TryGetMutRef<R>()
    {
        if (typeof(T) == typeof(R)) return ref Unsafe.As<T, R>(ref Value!);
        return ref Unsafe.NullRef<R>();
    }
    public override ref R TryGetImmRef<R>()
    {
        if (typeof(T) == typeof(R)) return ref Unsafe.As<T, R>(ref Value!);
        return ref Unsafe.NullRef<R>();
    }
    public override bool TryGet<R>(out R value)
    {
        if (typeof(T) == typeof(R))
        {
            value = Unsafe.As<T, R>(ref Value!);
            return true;
        }
        else if (typeof(T).IsAssignableTo(typeof(R)))
        {
            value = (R)(object)Value!;
            return true;
        }
        else
        {
            value = default!;
            return false;
        }
    }

    public override object? DynValue
    {
        get => Value;
        set => Value = (T)value!;
    }
}
