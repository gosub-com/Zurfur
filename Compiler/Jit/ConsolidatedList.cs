using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zurfur.Jit
{
    /// <summary>
    /// Consolidate re-used objects into a list.  Items are found by name (i.e. item.ToString())
    /// </summary>
    public class ConsolidatedList<T> : IEnumerable<T>
    {
        List<T> mList = new();
        Dictionary<string, int> mMap = new();

        /// <summary>
        /// Add to list or re-use the one that's already there.
        /// Uses item.ToString() to find and compare items.
        /// Returns the index of the item.
        /// </summary>
        public int AddOrFind(T item)
        {
            if (mMap.TryGetValue(item!.ToString()!, out var index))
                return index;
            mMap[item.ToString()!] = mList.Count;
            mList.Add(item);
            return mList.Count - 1;
        }

        public List<T>.Enumerator GetEnumerator() => mList.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();


        public int Count => mList.Count;
        public T this[int i]
        {
            get { return mList[i]; }
            set { mList[i] = value; }
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
}
