using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public enum MapAnnotationKind
{
    MapPoint,
    RfidStation,
    MapLabel
}

public sealed class MapAnnotationEditor
{
    private readonly AdminModeService _session;
    private readonly Stack<StationConfig> _history = new();
    private readonly IReadOnlyList<StationConfig> _allYards;
    private readonly IReadOnlyList<RfidStationConfig>? _rfidStations;
    private StationConfig _originalSnapshot;
    private StationConfig _workingCopy;

    public MapAnnotationEditor(
        StationConfig source,
        AdminModeService session,
        IEnumerable<StationConfig>? allYards = null,
        IEnumerable<RfidStationConfig>? rfidStations = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        _session = session;
        _originalSnapshot = Clone(source);
        _workingCopy = Clone(_originalSnapshot);
        _allYards = (allYards ?? new[] { source })
            .Where(item => item is not null)
            .ToArray();
        if (!_allYards.Any(item => string.Equals(item.Id, source.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _allYards = _allYards.Concat(new[] { source }).ToArray();
        }
        _rfidStations = rfidStations?.Where(item => item is not null).ToArray();
    }

    public StationConfig WorkingCopy => Clone(_workingCopy);

    public bool HasUnsavedChanges => _history.Count > 0;

    public string? LastBindingError { get; private set; }

    public bool TryAddMapPoint(MapPoint point)
    {
        if (!_session.IsAdmin || point is null || HasId(_workingCopy.Points, point.Id))
        {
            return false;
        }

        PushHistory();
        var points = new List<MapPoint>(_workingCopy.Points)
        {
            Clone(point)
        };
        _workingCopy.Points = points;
        return true;
    }

    public bool TryAddRfidStation(DeviceConfig station)
    {
        LastBindingError = null;
        if (!_session.IsAdmin || station is null || HasId(_workingCopy.Devices, station.Id))
        {
            return false;
        }

        var copy = Clone(station);
        copy.Type = DeviceType.RfidStation;
        if (!string.IsNullOrWhiteSpace(copy.RfidStationId))
        {
            var candidate = Clone(_workingCopy);
            candidate.Devices = new List<DeviceConfig>(candidate.Devices) { copy };
            if (!CanApplyRfidBinding(candidate, copy.Id, copy.RfidStationId, out var error))
            {
                LastBindingError = error;
                return false;
            }
        }

        PushHistory();
        var devices = new List<DeviceConfig>(_workingCopy.Devices)
        {
            copy
        };
        _workingCopy.Devices = devices;
        return true;
    }

    public bool TryAddMapLabel(MapLabel label)
    {
        if (!_session.IsAdmin || label is null || HasId(_workingCopy.Labels, label.Id))
        {
            return false;
        }

        PushHistory();
        var labels = new List<MapLabel>(_workingCopy.Labels)
        {
            Clone(label)
        };
        _workingCopy.Labels = labels;
        return true;
    }

    public bool TryUpdateMapPoint(string id, string name, double cadX, double cadY, bool enabled)
    {
        if (!_session.IsAdmin)
        {
            return false;
        }

        var point = Find(_workingCopy.Points, id);
        if (point is null)
        {
            return false;
        }

        PushHistory();
        point.Name = name;
        point.CadX = cadX;
        point.CadY = cadY;
        point.Enabled = enabled;
        return true;
    }

    public bool TryUpdateRfidStation(
        string id,
        string name,
        double cadX,
        double cadY,
        string? rfidStationId,
        bool enabled)
    {
        LastBindingError = null;
        if (!_session.IsAdmin)
        {
            return false;
        }

        var station = Find(_workingCopy.Devices, id, device => device.Type == DeviceType.RfidStation);
        if (station is null)
        {
            return false;
        }

        var normalizedRfidStationId = rfidStationId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedRfidStationId))
        {
            var candidate = Clone(_workingCopy);
            var candidateStation = Find(candidate.Devices, id, device => device.Type == DeviceType.RfidStation);
            if (candidateStation is null)
            {
                return false;
            }

            candidateStation.RfidStationId = normalizedRfidStationId;
            if (!CanApplyRfidBinding(candidate, id, normalizedRfidStationId, out var error))
            {
                LastBindingError = error;
                return false;
            }
        }

        PushHistory();
        station.Type = DeviceType.RfidStation;
        station.Name = name;
        station.CadX = cadX;
        station.CadY = cadY;
        station.RfidStationId = normalizedRfidStationId;
        station.Enabled = enabled;
        return true;
    }

    public bool TryUpdateMapLabel(
        string id,
        string text,
        double cadX,
        double cadY,
        double rotation,
        double textHeight,
        bool enabled)
    {
        if (!_session.IsAdmin)
        {
            return false;
        }

        var label = Find(_workingCopy.Labels, id);
        if (label is null)
        {
            return false;
        }

        PushHistory();
        label.Text = text;
        label.CadX = cadX;
        label.CadY = cadY;
        label.Rotation = rotation;
        label.TextHeight = textHeight;
        label.Enabled = enabled;
        return true;
    }

    public bool TryMove(MapAnnotationKind kind, string id, CadPoint point)
    {
        if (!_session.IsAdmin)
        {
            return false;
        }

        var annotation = FindAnnotation(kind, id);
        if (annotation is null)
        {
            return false;
        }

        PushHistory();
        switch (annotation)
        {
            case MapPoint mapPoint:
                mapPoint.CadX = point.X;
                mapPoint.CadY = point.Y;
                break;
            case DeviceConfig station:
                station.CadX = point.X;
                station.CadY = point.Y;
                break;
            case MapLabel label:
                label.CadX = point.X;
                label.CadY = point.Y;
                break;
            default:
                return false;
        }

        return true;
    }

    public bool TryDelete(MapAnnotationKind kind, string id)
    {
        if (!_session.IsAdmin)
        {
            return false;
        }

        switch (kind)
        {
            case MapAnnotationKind.MapPoint:
            {
                var points = new List<MapPoint>(_workingCopy.Points);
                var index = IndexOf(points, id);
                if (index < 0)
                {
                    return false;
                }

                PushHistory();
                points.RemoveAt(index);
                _workingCopy.Points = points;
                return true;
            }
            case MapAnnotationKind.RfidStation:
            {
                var devices = new List<DeviceConfig>(_workingCopy.Devices);
                var index = IndexOf(devices, id, device => device.Type == DeviceType.RfidStation);
                if (index < 0)
                {
                    return false;
                }

                PushHistory();
                devices.RemoveAt(index);
                _workingCopy.Devices = devices;
                return true;
            }
            case MapAnnotationKind.MapLabel:
            {
                var labels = new List<MapLabel>(_workingCopy.Labels);
                var index = IndexOf(labels, id);
                if (index < 0)
                {
                    return false;
                }

                PushHistory();
                labels.RemoveAt(index);
                _workingCopy.Labels = labels;
                return true;
            }
            default:
                return false;
        }
    }

    public bool TryUndo()
    {
        if (!_session.IsAdmin || _history.Count == 0)
        {
            return false;
        }

        _workingCopy = _history.Pop();
        return true;
    }

    public bool TryGetSaveSnapshot(out StationConfig snapshot)
    {
        if (!_session.IsAdmin)
        {
            snapshot = null!;
            return false;
        }

        snapshot = Clone(_workingCopy);
        return true;
    }

    public void MarkSaved()
    {
        if (!_session.IsAdmin)
        {
            return;
        }

        _originalSnapshot = Clone(_workingCopy);
        _history.Clear();
    }

    public void DiscardChanges()
    {
        if (!_session.IsAdmin)
        {
            return;
        }

        _workingCopy = Clone(_originalSnapshot);
        _history.Clear();
    }

    private void PushHistory()
    {
        _history.Push(Clone(_workingCopy));
    }

    private bool CanApplyRfidBinding(
        StationConfig candidate,
        string deviceId,
        string? rfidStationId,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(rfidStationId))
        {
            return true;
        }

        var yards = _allYards
            .Select(yard => string.Equals(yard.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : yard)
            .ToArray();
        var validation = RfidStationBindingRules.ValidateBinding(
            yards,
            _rfidStations,
            candidate.Id,
            deviceId,
            rfidStationId);
        error = validation.Message;
        return validation.Succeeded;
    }

    private object? FindAnnotation(MapAnnotationKind kind, string id)
    {
        return kind switch
        {
            MapAnnotationKind.MapPoint => Find(_workingCopy.Points, id),
            MapAnnotationKind.RfidStation => Find(
                _workingCopy.Devices,
                id,
                device => device.Type == DeviceType.RfidStation),
            MapAnnotationKind.MapLabel => Find(_workingCopy.Labels, id),
            _ => null
        };
    }

    private static bool HasId<T>(IEnumerable<T> items, string id)
        where T : class
    {
        return !string.IsNullOrWhiteSpace(id) && items.Any(item => GetId(item) == id);
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, string id, Func<T, bool>? predicate = null)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return -1;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (GetId(items[index]) == id && (predicate is null || predicate(items[index])))
            {
                return index;
            }
        }

        return -1;
    }

    private static T? Find<T>(IReadOnlyList<T> items, string id, Func<T, bool>? predicate = null)
        where T : class
    {
        var index = IndexOf(items, id, predicate);
        return index < 0 ? null : items[index];
    }

    private static string GetId<T>(T item)
        where T : class
    {
        return item switch
        {
            MapPoint point => point.Id,
            DeviceConfig device => device.Id,
            MapLabel label => label.Id,
            _ => string.Empty
        };
    }

    private static StationConfig Clone(StationConfig source)
    {
        var points = new List<MapPoint>();
        foreach (var point in source.Points)
        {
            points.Add(Clone(point));
        }

        var labels = new List<MapLabel>();
        foreach (var label in source.Labels)
        {
            labels.Add(Clone(label));
        }

        var devices = new List<DeviceConfig>();
        foreach (var device in source.Devices)
        {
            devices.Add(Clone(device));
        }

        return new StationConfig
        {
            Id = source.Id,
            Name = source.Name,
            BackgroundImage = source.BackgroundImage,
            CadMinX = source.CadMinX,
            CadMaxX = source.CadMaxX,
            CadMinY = source.CadMinY,
            CadMaxY = source.CadMaxY,
            Points = points,
            Labels = labels,
            Devices = devices
        };
    }

    private static MapPoint Clone(MapPoint source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        CadX = source.CadX,
        CadY = source.CadY,
        Enabled = source.Enabled
    };

    private static MapLabel Clone(MapLabel source) => new()
    {
        Id = source.Id,
        Text = source.Text,
        CadX = source.CadX,
        CadY = source.CadY,
        Rotation = source.Rotation,
        TextHeight = source.TextHeight,
        Enabled = source.Enabled
    };

    private static DeviceConfig Clone(DeviceConfig source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Type = source.Type,
        StationId = source.StationId,
        RfidStationId = source.RfidStationId,
        CadX = source.CadX,
        CadY = source.CadY,
        ProtocolAddress = source.ProtocolAddress,
        Enabled = source.Enabled
    };
}
