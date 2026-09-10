using System.Collections;

namespace MineRailMonitor.Core.Models;

/// <summary>
/// Compatibility view for older callers indexed by protocol address.
/// Values contains every station; the indexer returns the first station when
/// protocol addresses are shared, while endpoint-aware code uses the full key.
/// </summary>
internal sealed class RfidProtocolAddressCollectionView<T> : IReadOnlyDictionary<byte, T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly IReadOnlyDictionary<byte, T> _firstByProtocol;
    private readonly Func<T, byte> _protocolSelector;

    public RfidProtocolAddressCollectionView(IEnumerable<T> items, Func<T, byte> protocolSelector)
    {
        _items = (items ?? throw new ArgumentNullException(nameof(items))).ToArray();
        _protocolSelector = protocolSelector ?? throw new ArgumentNullException(nameof(protocolSelector));
        _firstByProtocol = _items
            .GroupBy(_protocolSelector)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public IEnumerable<byte> Keys => _firstByProtocol.Keys;

    public IEnumerable<T> Values => _items;

    public int Count => _items.Count;

    public T this[byte key] => _firstByProtocol[key];

    public bool ContainsKey(byte key) => _firstByProtocol.ContainsKey(key);

    public bool TryGetValue(byte key, out T value) => _firstByProtocol.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<byte, T>> GetEnumerator() => _firstByProtocol.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
