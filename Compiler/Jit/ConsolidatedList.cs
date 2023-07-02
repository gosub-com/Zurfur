using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zurfur.Jit
{
    /// <summary>
    /// Consolidate re-used objects into a list.  Items are found by name (i.e. item.ToString())
    /// </summary>
    public class ConsolidatedList<T>
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
        public int Count => mList.Count;
        public T this[int i]
        {
            get { return mList[i]; }
            set { mList[i] = value; }
        }
    }
}
