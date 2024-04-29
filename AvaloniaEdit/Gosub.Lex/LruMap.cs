
namespace Gosub.Lex;

// https://stackoverflow.com/questions/754233/is-it-there-any-lru-implementation-of-idictionary
internal class LruMap<K, V>
    where K : notnull
{
    private int capacity;
    private Dictionary<K, LinkedListNode<LRUCacheItem<K, V>>> cacheMap = new();
    private LinkedList<LRUCacheItem<K, V>> lruList = new();

    public LruMap(int capacity)
    {
        this.capacity = capacity;
    }

    public V? Get(K key)
    {
        if (cacheMap.TryGetValue(key, out var node))
        {
            V value = node.Value.value;
            lruList.Remove(node);
            lruList.AddLast(node);
            return value;
        }
        return default(V);
    }

    public void Add(K key, V val)
    {
        if (cacheMap.TryGetValue(key, out var existingNode))
            lruList.Remove(existingNode);

        var cacheItem = new LRUCacheItem<K, V>(key, val);
        var node = new LinkedListNode<LRUCacheItem<K, V>>(cacheItem);
        lruList.AddLast(node);
        cacheMap[key] = node;

        if (cacheMap.Count >= capacity)
            RemoveOldest();
    }

    private void RemoveOldest()
    {
        var node = lruList.First;
        lruList.RemoveFirst();
        cacheMap.Remove(node!.Value.key);
    }
}

class LRUCacheItem<K, V>
{
    public LRUCacheItem(K k, V v)
    {
        key = k;
        value = v;
    }
    public K key;
    public V value;
}
