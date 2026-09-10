namespace MineRailMonitor.Simulator.Models;

public sealed class SimulatorStationCatalog
{
    private readonly SimulatorStation?[] _stations = new SimulatorStation?[6];

    public int Capacity => _stations.Length;

    public IEnumerable<SimulatorStation> ConfiguredStations => _stations.Where(station => station is not null)!;

    public void Clear() => Array.Clear(_stations, 0, _stations.Length);

    public void Set(int index, SimulatorStation station)
    {
        if (index < 0 || index >= _stations.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (station is null)
        {
            throw new ArgumentNullException(nameof(station));
        }
        if (_stations.Where((_, itemIndex) => itemIndex != index).Any(item => item?.Address == station.Address))
        {
            throw new ArgumentException("Simulator station addresses must be unique.", nameof(station));
        }
        _stations[index] = station;
    }
}
