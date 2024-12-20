using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Coplt.Systems.Utilities;

namespace Coplt.Systems;

public enum ResRefType : uint
{
    ReadWriteRef,
    ReadRef,
    /// <summary>
    /// Indicates that the object is a nested ResRef
    /// </summary>
    Indirect,
    /// <summary>
    /// Indicates that the object is a nested <see cref="StrongBox{T}"/>
    /// </summary>
    IndirectStrongBox,
    /// <summary>
    /// Indicates that object is an <see cref="T:object[]"/>
    /// </summary>
    ObjectArray,
    /// <summary>
    /// Indicates that object is an array of T
    /// </summary>
    ValueArray,
    /// <summary>
    /// Indicates that object is an array of <see cref="Coplt.Systems.ResRef{T}"/>
    /// </summary>
    IndirectArray,
}

public readonly struct ResRef<T>
{
    internal readonly object? m_obj;
    internal readonly ResRefType m_type;
    internal readonly int m_index;

    public object? Object => m_obj;
    public ResRefType Type => m_type;
    public int Index => m_index;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ResRef(object? obj, ResRefType type, int index = -1)
    {
        if (Systems.DebugEnabled)
        {
            switch (type)
            {
                case ResRefType.ReadWriteRef:
                case ResRefType.ReadRef:
                    break;
                case ResRefType.Indirect:
                    if (obj is not (null or ResRef<T>))
                        throw new ArgumentException("obj must be null or ResRef<T>", nameof(obj));
                    break;
                case ResRefType.IndirectStrongBox:
                    if (obj is not (null or StrongBox<T>))
                        throw new ArgumentException("obj must be null or ResRef<T>", nameof(obj));
                    break;
                case ResRefType.ObjectArray:
                {
                    if (obj is not (null or object[]))
                        throw new ArgumentException("obj must be null or object[]", nameof(obj));
                    if (obj is object[] arr)
                    {
                        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
                        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, arr.Length, nameof(index));
                    }
                    break;
                }
                case ResRefType.ValueArray:
                {
                    if (obj is not (null or T[]))
                        throw new ArgumentException("obj must be null or T[]", nameof(obj));
                    if (obj is T[] arr)
                    {
                        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
                        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, arr.Length, nameof(index));
                    }
                    break;
                }
                case ResRefType.IndirectArray:
                {
                    if (obj is not (null or ResRef<T>[]))
                        throw new ArgumentException("obj must be null or ResRef<T>[]", nameof(obj));
                    if (obj is ResRef<T>[] arr)
                    {
                        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
                        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, arr.Length, nameof(index));
                    }
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        m_obj = obj;
        m_type = type;
        m_index = index;
    }

    public bool IsNull => m_obj == null;

    #region private

    private ref ResRef<T> UnsafeGetIndirectRef() => ref SystemsUtils.UnsafeUnbox<ResRef<T>>(m_obj!);

    [UnscopedRef]
    private ref T UnsafeGetRef()
    {
        if (m_obj is null) return ref Unsafe.NullRef<T>();
        if (m_type is ResRefType.Indirect) return ref UnsafeGetIndirectRef().UnsafeGetRef();
        else if (m_type is ResRefType.IndirectStrongBox) return ref ((StrongBox<T>)m_obj).Value!;
        else if (m_type is ResRefType.ObjectArray)
        {
            ref var slot = ref Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(Unsafe.As<object?[]>(m_obj)), m_index
            );
            if (typeof(T).IsValueType)
            {
                var obj = slot;
                if (obj is null) return ref Unsafe.NullRef<T>();
                return ref SystemsUtils.UnsafeUnbox<T>(obj);
            }
            return ref Unsafe.As<object?, T>(ref slot);
        }
        else if (m_type is ResRefType.ValueArray)
        {
            return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Unsafe.As<T[]>(m_obj)), m_index);
        }
        else if (m_type is ResRefType.IndirectArray)
        {
            return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Unsafe.As<ResRef<T>[]>(m_obj)), m_index)
                .UnsafeGetRef();
        }
        else
        {
            if (typeof(T).IsValueType) return ref SystemsUtils.UnsafeUnbox<T>(m_obj!);
            return ref Unsafe.As<object, T>(ref Unsafe.AsRef(in m_obj!));
        }
    }

    #endregion

    #region access

    [UnscopedRef]
    public ref T GetMutRef()
    {
        if (m_type == ResRefType.ReadRef)
            throw new NotSupportedException("This resource reference does not support mutable references");
        return ref UnsafeGetRef();
    }

    [UnscopedRef]
    public ref readonly T GetImmRef() => ref UnsafeGetRef();

    public T Get()
    {
        ref readonly var slot = ref GetImmRef();
        if (Unsafe.IsNullRef(in slot)) return default!;
        return slot;
    }

    public void Set(T value) => GetMutRef() = value;

    #endregion

    #region ToString

    public override string ToString() => IsNull ? "null" : $"{Get()}";

    #endregion

    #region ToUnTyped

    public UnTypedResRef UnTyped => new(m_obj, m_type, m_index);

    #endregion
}

public readonly struct UnTypedResRef
{
    internal readonly object? m_obj;
    internal readonly ResRefType m_type;
    internal readonly int m_index;

    public object? Object => m_obj;
    public ResRefType Type => m_type;
    public int Index => m_index;

    // ReSharper disable once ConvertToPrimaryConstructor
    internal UnTypedResRef(object? obj, ResRefType type, int index = -1)
    {
        m_obj = obj;
        m_type = type;
        m_index = index;
    }

    public ResRef<T> UnsafeAs<T>() => new(m_obj, m_type, m_index);

    // UnTypedResRef and ResRef<T> memory layout must be same
    private ref UnTypedResRef UnsafeGetIndirectRef() => ref SystemsUtils.UnsafeUnboxAs<UnTypedResRef>(m_obj!);

    public object? GetObject()
    {
        if (m_obj is null) return null;
        if (m_type is ResRefType.Indirect) return UnsafeGetIndirectRef().GetObject();
        else if (m_type is ResRefType.IndirectStrongBox) return ((IStrongBox)m_obj).Value;
        else if (m_type is ResRefType.ObjectArray)
        {
            ref var slot = ref Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(Unsafe.As<object?[]>(m_obj)), m_index
            );
            return slot;
        }
        else if (m_type is ResRefType.ValueArray)
        {
            return ((Array)m_obj).GetValue(m_index);
        }
        else if (m_type is ResRefType.IndirectArray)
        {
            return Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(Unsafe.As<UnTypedResRef[]>(m_obj)), m_index
            ).GetObject();
        }
        else
        {
            return m_obj;
        }
    }
}
