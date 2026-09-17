namespace MineRailMonitor.Simulator.Models;

/// <summary>
/// Holds the ordered RFID inputs for one manual/automatic simulator scene.
/// It deliberately keeps duplicate values because the simulator models raw
/// slot observations; production recognition rules decide how duplicates count.
/// </summary>
public sealed class ScenarioPlaybackState
{
    public const int SlotCapacity = 14;

    private readonly ushort[] _slots = new ushort[SlotCapacity];
    private ushort[] _sequence = Array.Empty<ushort>();
    private ushort? _currentRfid;

    public IReadOnlyList<ushort> Sequence => _sequence;

    public IReadOnlyList<ushort> Slots => _slots;

    public int CurrentIndex { get; private set; }

    public bool IsPlaying { get; private set; }

    public ushort? CurrentRfid => _currentRfid;

    public ushort? NextRfid => CurrentIndex < _sequence.Length ? _sequence[CurrentIndex] : null;

    public void Load(IEnumerable<ushort> sequence)
    {
        if (sequence is null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        var values = sequence.ToArray();
        if (values.Length > SlotCapacity)
        {
            throw new ArgumentException($"A simulator scene cannot contain more than {SlotCapacity} RFID values.", nameof(sequence));
        }

        _sequence = values;
        Reset();
    }

    public bool Start()
    {
        if (CurrentIndex >= _sequence.Length)
        {
            IsPlaying = false;
            return false;
        }

        IsPlaying = true;
        return true;
    }

    public void Pause() => IsPlaying = false;

    public void Reset()
    {
        IsPlaying = false;
        CurrentIndex = 0;
        _currentRfid = null;
        Array.Clear(_slots, 0, _slots.Length);
    }

    public ushort? Step()
    {
        if (CurrentIndex >= _sequence.Length || CurrentIndex >= _slots.Length)
        {
            IsPlaying = false;
            return null;
        }

        var rfid = _sequence[CurrentIndex];
        _slots[CurrentIndex] = rfid;
        CurrentIndex++;
        _currentRfid = rfid;
        if (CurrentIndex >= _sequence.Length)
        {
            IsPlaying = false;
        }

        return rfid;
    }
}
