using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Coplt.Systems.Utilities;

namespace Coplt.Systems;

public class ResourceContainer : IEnumerable<UnTypedResRef>
{
    #region ChunkSize

    // must be pow of 2
    internal const int ChunkSize = 512;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexInChunk(int index) => index & (ChunkSize - 1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOfChunk(int index) => index >> BitOperations.TrailingZeroCount(ChunkSize);

    #endregion

    #region Static

    private static readonly object[] s_placeholder = [];

    #endregion

    #region Fields

    internal readonly ConcurrentDictionary<Type, int> m_resource_to_index = new();
    internal readonly ConcurrentQueue<int> m_recycled_indexes = new();
    internal readonly ReaderWriterLockSlim m_chunks_grow_lock = new();
    internal object[]?[] m_chunks = [null, null, null, null];
    internal volatile int m_chunks_count = 0;
    internal int m_index_inc = -1;

    #endregion

    #region Alloc

    internal int AllocIndex() => m_recycled_indexes.TryDequeue(out var i) ? i : Interlocked.Increment(ref m_index_inc);

    internal void EnsureChunk(int index_of_chunk)
    {
        {
            re:
            var chunk_count = m_chunks_count;
            if (index_of_chunk >= chunk_count)
            {
                Interlocked.CompareExchange(ref m_chunks_count, chunk_count + 1, chunk_count);
                goto re;
            }
        }

        {
            re:
            var chunk_count = m_chunks_count;
            if (chunk_count >= m_chunks.Length)
            {
                TryGrow(chunk_count);
                goto re;
            }
        }

        if (m_chunks[index_of_chunk] == null)
        {
            CreateChunk(index_of_chunk);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void CreateChunk(int index_of_chunk)
    {
        m_chunks_grow_lock.EnterReadLock();
        try
        {
            ref var slot = ref m_chunks[index_of_chunk];
            if (Volatile.Read(ref slot) == null)
            {
                if (Interlocked.CompareExchange(ref slot, s_placeholder, null) == null)
                {
                    Volatile.Write(ref slot, new object[ChunkSize]);
                }
                while (Volatile.Read(ref slot) == s_placeholder) ;
            }
        }
        finally
        {
            m_chunks_grow_lock.ExitReadLock();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void TryGrow(int required_size)
    {
        if (!m_chunks_grow_lock.TryEnterWriteLock(0)) return;
        try
        {
            if (required_size < m_chunks.Length) return;
            var new_arr = new object[]?[m_chunks.Length * 2];
            Array.Copy(m_chunks, new_arr, m_chunks.Length);
            m_chunks = new_arr;
        }
        finally
        {
            m_chunks_grow_lock.ExitWriteLock();
        }
    }

    #endregion

    #region GetOrAdd

    public ResRef<T> GetOrAdd<T>()
    {
        var index = m_resource_to_index.GetOrAdd(typeof(T), static (_, self) => self.AllocIndex(), this);
        var index_of_chunk = IndexOfChunk(index);
        EnsureChunk(index_of_chunk);
        return GetByIndex<T>(index_of_chunk, IndexInChunk(index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ResRef<T> GetByIndex<T>(int index_of_chunk, int index_in_chunk)
    {
        if (typeof(T).IsValueType)
        {
            ref var slot = ref m_chunks[index_of_chunk]![index_in_chunk];
            Interlocked.CompareExchange(ref slot, default(T)!, null!);
            return new(slot, ResRefType.BoxedStruct);
        }
        return new(m_chunks[index_of_chunk]!, ResRefType.ObjectArray, index_in_chunk);
    }

    public UnTypedResRef GetOrAdd(Type type)
    {
        var index = m_resource_to_index.GetOrAdd(type, static (_, self) => self.AllocIndex(), this);
        var index_of_chunk = IndexOfChunk(index);
        EnsureChunk(index_of_chunk);
        return GetByIndex(type, index_of_chunk, IndexInChunk(index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal UnTypedResRef GetByIndex(Type type, int index_of_chunk, int index_in_chunk)
    {
        if (type.IsValueType)
        {
            ref var slot = ref m_chunks[index_of_chunk]![index_in_chunk];
            var obj = SystemsUtils.CreateBoxedDefaultValueType(type);
            Interlocked.CompareExchange(ref slot, obj, null!);
            return new(slot, ResRefType.BoxedStruct);
        }
        return new(m_chunks[index_of_chunk]!, ResRefType.ObjectArray, index_in_chunk);
    }

    #endregion

    #region TryRemove

    public bool TryRemove<T>()
    {
        if (!m_resource_to_index.TryRemove(typeof(T), out var index)) return false;
        m_recycled_indexes.Enqueue(index);
        return true;
    }

    #endregion

    #region Clear

    /// <summary>
    /// Clear is not thread safe
    /// </summary>
    public void Clear()
    {
        m_chunks_grow_lock.ExitWriteLock();
        try
        {
            m_resource_to_index.Clear();
            m_recycled_indexes.Clear();
            Array.Clear(m_chunks, 0, m_chunks_count);
            m_chunks_count = 0;
            m_index_inc = -1;
        }
        finally
        {
            m_chunks_grow_lock.ExitWriteLock();
        }
    }

    #endregion

    #region Enumerator

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<UnTypedResRef> IEnumerable<UnTypedResRef>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public readonly struct Enumerator(ResourceContainer self) : IEnumerator<UnTypedResRef>
    {
        private readonly IEnumerator<KeyValuePair<Type, int>> m_enumerator = self.m_resource_to_index.GetEnumerator();

        public bool MoveNext() => m_enumerator.MoveNext();
        public void Reset() => m_enumerator.Reset();
        object IEnumerator.Current => Current;
        public UnTypedResRef Current
        {
            get
            {
                var (type, index) = m_enumerator.Current;
                var chunk = self.m_chunks[IndexOfChunk(index)];
                if (chunk == null) return default;
                if (type.IsValueType)
                {
                    ref var slot = ref chunk[IndexInChunk(index)];
                    if (slot == null!)
                    {
                        var obj = SystemsUtils.CreateBoxedDefaultValueType(type);
                        Interlocked.CompareExchange(ref slot!, obj, null!);
                    }
                    return new(slot, ResRefType.BoxedStruct);
                }
                return new(chunk, ResRefType.ObjectArray, IndexInChunk(index));
            }
        }
        public void Dispose() => m_enumerator.Dispose();
    }

    #endregion
}
