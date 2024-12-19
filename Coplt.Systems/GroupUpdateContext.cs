using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Coplt.Systems;

public readonly struct GroupUpdateContext : IList<GroupManualUpdate>
{
    internal readonly Systems.Node GroupNode;

    internal GroupUpdateContext(Systems.Node group_node)
    {
        GroupNode = group_node;
    }

    /// <summary>
    /// Synchronously update the systems in the group
    /// </summary>
    public void Update()
    {
        if (GroupNode.SortedSubNodes is null) return;
        foreach (var node in GroupNode.SortedSubNodes)
        {
            node.Update();
        }
    }

    /// <summary>
    /// Update systems in a group in parallel
    /// </summary>
    public void UpdateParallel()
    {
        if (GroupNode.SortedSubNodes is null) return;
        Parallel.ForEach(GroupNode.SortedSubNodes, static node => node.Update());
    }

    #region Manual Update

    public int Count => GroupNode.SortedSubNodes?.Count ?? 0;
    public GroupManualUpdate this[int index]
    {
        get => GroupNode.SortedSubNodes is null
            ? throw new IndexOutOfRangeException()
            : new(GroupNode.SortedSubNodes[index]);
        set => throw new NotSupportedException();
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<GroupManualUpdate> IEnumerable<GroupManualUpdate>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(GroupUpdateContext ctx) : IEnumerator<GroupManualUpdate>
    {
        private List<Systems.Node>.Enumerator m_enumerator = ctx.GroupNode.SortedSubNodes?.GetEnumerator() ?? default;
        public bool MoveNext()
        {
            if (ctx.GroupNode.SortedSubNodes is null) return false;
            return m_enumerator.MoveNext();
        }
        public void Reset()
        {
            if (ctx.GroupNode.SortedSubNodes is null) return;
            m_enumerator = ctx.GroupNode.SortedSubNodes.GetEnumerator();
        }
        public GroupManualUpdate Current => ctx.GroupNode.SortedSubNodes is null ? default : new(m_enumerator.Current);
        object? IEnumerator.Current => Current;
        void IDisposable.Dispose() { }
    }

    bool ICollection<GroupManualUpdate>.IsReadOnly => true;
    void ICollection<GroupManualUpdate>.Add(GroupManualUpdate item) => throw new NotSupportedException();
    void ICollection<GroupManualUpdate>.Clear() => throw new NotSupportedException();
    bool ICollection<GroupManualUpdate>.Contains(GroupManualUpdate item) => throw new NotSupportedException();
    void ICollection<GroupManualUpdate>.CopyTo(GroupManualUpdate[] array, int arrayIndex) =>
        throw new NotSupportedException();
    bool ICollection<GroupManualUpdate>.Remove(GroupManualUpdate item) => throw new NotSupportedException();
    int IList<GroupManualUpdate>.IndexOf(GroupManualUpdate item) => throw new NotSupportedException();
    void IList<GroupManualUpdate>.Insert(int index, GroupManualUpdate item) => throw new NotSupportedException();
    void IList<GroupManualUpdate>.RemoveAt(int index) => throw new NotSupportedException();

    #endregion
}

public readonly struct GroupManualUpdate
{
    internal readonly Systems.Node Node;

    internal GroupManualUpdate(Systems.Node node)
    {
        Node = node;
    }

    public void Update() => Node.Update();
}
