using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Coplt.Systems.Utilities;

namespace Coplt.Systems;

public enum ResRefType : uint
{
    /// <summary>
    /// Indicates that the object is boxed struct
    /// </summary>
    BoxedStruct,
    /// <summary>
    /// Indicates that the object is boxed struct and read only
    /// </summary>
    BoxedStructReadOnly,
    /// <summary>
    /// Indicates that the object is class object
    /// </summary>
    Object,
    /// <summary>
    /// Indicates that the object is a nested ResRef
    /// </summary>
    Indirect,
    /// <summary>
    /// Indicates that the object is a nested <see cref="Box{T}"/>
    /// </summary>
    IndirectBox,
    /// <summary>
    /// Indicates that the object is a nested <see cref="StrongBox{T}"/>
    /// </summary>
    IndirectStrongBox,
    /// <summary>
    /// Indicates that the object is a nested <see cref="DynBox"/>
    /// </summary>
    IndirectDynBox,
    /// <summary>
    /// Indicates that object is an <see cref="T:object[]"/>. Unsafe, you need to manually ensure the object type at the index in the array
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
    /// <summary>
    /// Indicates that object is <see cref="List{T}"/>
    /// </summary>
    ValueList,
    /// <summary>
    /// Indicates that object is <see cref="T:List&lt;object&gt;"/>. Unsafe, you need to manually ensure the object type at the index in the list
    /// </summary>
    ObjectList,
}

public readonly struct ResRef<T>
{
    internal readonly object? m_obj;
    internal readonly ResRefType m_type;
    internal readonly int m_index;

    public object? Object => m_obj;
    public ResRefType Type => m_type;
    public int Index => m_index;

    public ResRef(Box<T> obj) : this(obj, ResRefType.IndirectBox) { }
    public ResRef(StrongBox<T> obj) : this(obj, ResRefType.IndirectStrongBox) { }
    public ResRef(DynBox obj) : this(obj, ResRefType.IndirectDynBox) { }
    public ResRef(T[] obj, int index) : this(obj, ResRefType.ValueArray, index) { }
    public ResRef(ResRef<T>[] obj, int index) : this(obj, ResRefType.IndirectArray, index) { }

    public ResRef(object? obj, ResRefType type, int index = -1)
    {
        if (Systems.DebugEnabled)
        {
            switch (type)
            {
                case ResRefType.BoxedStruct:
                case ResRefType.BoxedStructReadOnly:
                    if (!typeof(T).IsValueType) throw new ArgumentException($"{typeof(T)} is not a value type");
                    if (obj is not T) throw new ArgumentException($"obj must be {typeof(T)}");
                    break;
                case ResRefType.Object:
                    if (typeof(T).IsValueType) throw new ArgumentException($"{typeof(T)} is not a reference type");
                    if (obj is not T) throw new ArgumentException($"obj must be {typeof(T)}");
                    break;
                case ResRefType.Indirect:
                    if (obj is not (null or ResRef<T>))
                        throw new ArgumentException($"obj must be null or ResRef<{typeof(T)}>", nameof(obj));
                    break;
                case ResRefType.IndirectBox:
                    if (obj is not (null or Box<T>))
                        throw new ArgumentException($"obj must be null or Box<{typeof(T)}>", nameof(obj));
                    break;
                case ResRefType.IndirectStrongBox:
                    if (obj is not (null or StrongBox<T>))
                        throw new ArgumentException($"obj must be null or StrongBox<{typeof(T)}>", nameof(obj));
                    break;
                case ResRefType.IndirectDynBox:
                    if (obj is not (null or DynBox))
                        throw new ArgumentException($"obj must be null or DynBox", nameof(obj));
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
                        throw new ArgumentException($"obj must be null or {typeof(T)}[]", nameof(obj));
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
                        throw new ArgumentException($"obj must be null or ResRef<{typeof(T)}>[]", nameof(obj));
                    if (obj is ResRef<T>[] arr)
                    {
                        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
                        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, arr.Length, nameof(index));
                    }
                    break;
                }
                case ResRefType.ValueList:
                {
                    if (obj is not (null or List<T>))
                        throw new ArgumentException($"obj must be null or {typeof(List<T>)}", nameof(obj));
                    if (obj is List<T> arr)
                    {
                        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
                        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, arr.Count, nameof(index));
                    }
                    break;
                }
                case ResRefType.ObjectList:
                {
                    if (obj is not (null or List<object>))
                        throw new ArgumentException($"obj must be null or {typeof(List<object>)}", nameof(obj));
                    if (obj is List<object> arr)
                    {
                        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));
                        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, arr.Count, nameof(index));
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
        else if (m_type is ResRefType.IndirectBox) return ref Unsafe.As<Box<T>>(m_obj).Value!;
        else if (m_type is ResRefType.IndirectStrongBox) return ref Unsafe.As<StrongBox<T>>(m_obj).Value!;
        else if (m_type is ResRefType.IndirectDynBox) return ref Unsafe.As<DynBox>(m_obj).GetRef<T>()!;
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
        else if (m_type is ResRefType.ValueList)
        {
            return ref CollectionsMarshal.AsSpan(Unsafe.As<List<T>>(m_obj))[m_index];
        }
        else if (m_type is ResRefType.ObjectList)
        {
            return ref Unsafe.As<object, T>(ref CollectionsMarshal.AsSpan(Unsafe.As<List<object>>(m_obj))[m_index]);
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
        if (m_type is ResRefType.Object or ResRefType.BoxedStructReadOnly)
            throw new NotSupportedException("This resource reference does not support mutable references");
        return ref UnsafeGetRef();
    }

    [UnscopedRef]
    public ref readonly T GetImmRef() => ref UnsafeGetRef();

    public T Get()
    {
        if (m_obj is null) return default!;
        if (m_type is ResRefType.Indirect) return UnsafeGetIndirectRef().Get();
        else if (m_type is ResRefType.IndirectBox) return Unsafe.As<Box<T>>(m_obj).Value!;
        else if (m_type is ResRefType.IndirectStrongBox) return Unsafe.As<StrongBox<T>>(m_obj).Value!;
        else if (m_type is ResRefType.IndirectDynBox) return Unsafe.As<DynBox>(m_obj).Get<T>()!;
        else if (m_type is ResRefType.ObjectArray)
        {
            ref var slot = ref Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(Unsafe.As<object?[]>(m_obj)), m_index
            );
            if (typeof(T).IsValueType)
            {
                var obj = slot;
                if (obj is null) return default!;
                return SystemsUtils.UnsafeUnbox<T>(obj);
            }
            return Unsafe.As<object?, T>(ref slot);
        }
        else if (m_type is ResRefType.ValueArray)
        {
            return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Unsafe.As<T[]>(m_obj)), m_index);
        }
        else if (m_type is ResRefType.IndirectArray)
        {
            return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Unsafe.As<ResRef<T>[]>(m_obj)), m_index)
                .Get();
        }
        else if (m_type is ResRefType.ValueList)
        {
            return Unsafe.As<List<T>>(m_obj)[m_index];
        }
        else if (m_type is ResRefType.ObjectList)
        {
            return Unsafe.As<object, T>(ref CollectionsMarshal.AsSpan(Unsafe.As<List<object>>(m_obj))[m_index]);
        }
        else
        {
            return (T)m_obj;
        }
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
        else if (m_type is ResRefType.IndirectBox) return Unsafe.As<DynBox>(m_obj).DynValue;
        else if (m_type is ResRefType.IndirectStrongBox) return Unsafe.As<IStrongBox>(m_obj).Value;
        else if (m_type is ResRefType.IndirectDynBox) return Unsafe.As<DynBox>(m_obj).DynValue;
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
        else if (m_type is ResRefType.ValueList)
        {
            return Unsafe.As<IList>(m_obj)[m_index];
        }
        else if (m_type is ResRefType.ObjectList)
        {
            return Unsafe.As<List<object>>(m_obj)[m_index];
        }
        else
        {
            return m_obj;
        }
    }
}
