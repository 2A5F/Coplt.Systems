using System;
using System.Runtime.CompilerServices;
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

    public static ref byte UnsafeUnbox(object obj) => ref Unsafe.As<UnboxHelper>(obj)._first;
    
    public static ref T UnsafeUnboxAs<T>(object obj) => ref Unsafe.As<byte, T>(ref UnsafeUnbox(obj));

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
