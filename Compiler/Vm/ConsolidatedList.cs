using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zurfur.Vm;

/// <summary>
/// Consolidate re-used objects into a list.  Items are found by name (i.e. item.ToString())
/// </summary>
public class ConsolidatedList<T> : IEnumerable<T>
{
    List<T> _list = new();
    Dictionary<string, int> _map = new();

    /// <summary>
    /// Add to list or re-use the one that's already there.
    /// Uses item.ToString() to find and compare items.
    /// Returns the index of the item.
    /// </summary>
    public int AddOrFind(T item)
    {
        if (_map.TryGetValue(item!.ToString()!, out var index))
            return index;
        _map[item.ToString()!] = _list.Count;
        _list.Add(item);
        return _list.Count - 1;
    }

    public List<T>.Enumerator GetEnumerator() => _list.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();


    public int Count => _list.Count;
    public T this[int i]
    {
        get { return _list[i]; }
        set { _list[i] = value; }
    }
}

public static class Util
{
    public static T Pop<T>(this List<T> list)
    { 
        var item = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        return item;
    }

    public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T>? item)
    {
        return item ?? Enumerable.Empty<T>();
    }

}
