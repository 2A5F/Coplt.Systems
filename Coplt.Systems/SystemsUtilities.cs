using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using InlineIL;
using static InlineIL.IL.Emit;

namespace Coplt.Systems.Utilities;

public static class SystemsUtils
{
    public static ref T UnsafeUnbox<T>(object obj)
    {
        Ldarg_0();
        Unbox<T>();
        return ref IL.ReturnRef<T>();
    }

    private class UnboxHelper
    {
        public byte _first = 0;
    }

    public static ref byte UncheckedUnbox(object obj) => ref Unsafe.As<UnboxHelper>(obj)._first;

    public static ref T UncheckedUnbox<T>(object obj) => ref Unsafe.As<byte, T>(ref UncheckedUnbox(obj));

    public static object CreateBoxedDefaultValueType(Type type)
    {
        if (!type.IsValueType) throw new NotSupportedException($"{type} is not a value type");
#pragma warning disable IL2067
        // GetUninitializedObject returns zeroed memory
        var obj = RuntimeHelpers.GetUninitializedObject(type);
#pragma warning restore IL2067
        return obj;
    }
}

internal record struct RwLock<T>(T Value)
{
    public T Value = Value;
    public ReaderWriterLockSlim Lock = new();

    [UnscopedRef]
    public ReadGuard EnterRead()
    {
        Lock.EnterReadLock();
        return new(ref Value, Lock);
    }

    [UnscopedRef]
    public WriteGuard EnterWrite()
    {
        Lock.EnterWriteLock();
        return new(ref Value, Lock);
    }

    internal ref struct ReadGuard(ref T Value, ReaderWriterLockSlim Lock)
    {
        public ref T Value = ref Value;
        public void Dispose()
        {
            Lock.ExitReadLock();
        }
    }

    internal ref struct WriteGuard(ref T Value, ReaderWriterLockSlim Lock)
    {
        public ref T Value = ref Value;
        public void Dispose()
        {
            Lock.ExitWriteLock();
        }
    }
}
