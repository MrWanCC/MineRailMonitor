using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class MapAnnotationEditorTests
{
    private static readonly string TestCredential = Guid.NewGuid().ToString("N");

    [Fact]
    public void Rejects_all_editing_and_save_operations_when_not_admin()
    {
        var source = CreateSource();
        var editor = new MapAnnotationEditor(source, new AdminModeService());

        Assert.False(editor.TryAddMapPoint(new MapPoint { Id = "point-2", Name = "Point 2" }));
        Assert.False(editor.TryAddRfidStation(new DeviceConfig { Id = "rfid-2", Name = "RFID 2" }));
        Assert.False(editor.TryAddMapLabel(new MapLabel { Id = "label-2", Text = "Label 2" }));
        Assert.False(editor.TryUpdateMapPoint("point-1", "Updated", 1, 2, false));
        Assert.False(editor.TryUpdateRfidStation("rfid-1", "Updated", 3, 4, "RFID-UPDATED", false));
        Assert.False(editor.TryUpdateMapLabel("label-1", "Updated", 5, 6, 7, 8, false));
        Assert.False(editor.TryMove(MapAnnotationKind.MapPoint, "point-1", new CadPoint(9, 10)));
        Assert.False(editor.TryDelete(MapAnnotationKind.MapLabel, "label-1"));
        Assert.False(editor.TryUndo());
        Assert.False(editor.TryGetSaveSnapshot(out _));

        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal("Point 1", source.Points[0].Name);
        Assert.Single(editor.WorkingCopy.Points);
    }

    [Fact]
    public void Admin_can_add_each_annotation_type_with_the_required_model_fields()
    {
        var editor = CreateAdminEditor();

        Assert.True(editor.TryAddMapPoint(new MapPoint
        {
            Id = "point-2",
            Name = "Point 2",
            CadX = 11,
            CadY = 12,
            Enabled = false
        }));
        Assert.True(editor.TryAddRfidStation(new DeviceConfig
        {
            Id = "rfid-2",
            Name = "RFID 2",
            Type = DeviceType.BaseStation,
            StationId = "560",
            RfidStationId = "RFID-02",
            CadX = 13,
            CadY = 14,
            ProtocolAddress = null,
            Enabled = false
        }));
        Assert.True(editor.TryAddMapLabel(new MapLabel
        {
            Id = "label-2",
            Text = "Label 2",
            CadX = 15,
            CadY = 16,
            Rotation = 17,
            TextHeight = 18,
            Enabled = false
        }));

        var point = Assert.Single(editor.WorkingCopy.Points, item => item.Id == "point-2");
        var station = Assert.Single(editor.WorkingCopy.Devices, item => item.Id == "rfid-2");
        var label = Assert.Single(editor.WorkingCopy.Labels, item => item.Id == "label-2");

        Assert.Equal("Point 2", point.Name);
        Assert.Equal(11, point.CadX);
        Assert.False(point.Enabled);
        Assert.Equal(DeviceType.RfidStation, station.Type);
        Assert.Null(station.ProtocolAddress);
        Assert.Equal(13, station.CadX);
        Assert.False(station.Enabled);
        Assert.Equal("Label 2", label.Text);
        Assert.Equal(17, label.Rotation);
        Assert.Equal(18, label.TextHeight);
        Assert.False(label.Enabled);
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public void Edits_working_copy_without_mutating_source_or_snapshot_state()
    {
        var source = CreateSource();
        var editor = new MapAnnotationEditor(source, CreateAdminSession());

        Assert.NotSame(source, editor.WorkingCopy);
        Assert.NotSame(source.Points[0], editor.WorkingCopy.Points[0]);
        Assert.True(editor.TryUpdateMapPoint("point-1", "Updated", 101, 102, false));

        Assert.Equal("Point 1", source.Points[0].Name);
        Assert.Equal(1, source.Points[0].CadX);
        Assert.True(source.Points[0].Enabled);

        Assert.True(editor.TryGetSaveSnapshot(out var snapshot));
        Assert.NotSame(editor.WorkingCopy, snapshot);
        Assert.NotSame(editor.WorkingCopy.Points[0], snapshot.Points[0]);

        snapshot.Points[0].Name = "Changed outside editor";

        Assert.Equal("Updated", editor.WorkingCopy.Points[0].Name);
    }

    [Fact]
    public void Working_copy_snapshot_cannot_bypass_gates_or_mutate_editor_state()
    {
        var source = CreateSource();
        var session = new AdminModeService(TestCredential);
        var editor = new MapAnnotationEditor(source, session);
        var snapshot = editor.WorkingCopy;

        snapshot.Name = "Bypassed station";
        snapshot.Points[0].Name = "Bypassed point";
        snapshot.Labels[0].Text = "Bypassed label";
        snapshot.Devices[0].RfidStationId = "BYPASSED";

        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal("560", source.Id);
        Assert.Equal("Point 1", source.Points[0].Name);
        Assert.Equal("Point 1", editor.WorkingCopy.Points[0].Name);
        Assert.Equal("Label 1", editor.WorkingCopy.Labels[0].Text);
        Assert.Equal("RFID-01", editor.WorkingCopy.Devices[0].RfidStationId);

        Assert.True(session.EnterAdminMode(TestCredential));
        Assert.True(editor.TryGetSaveSnapshot(out var saveSnapshot));
        Assert.Equal("560", saveSnapshot.Id);
        Assert.Equal("Point 1", saveSnapshot.Points[0].Name);
        Assert.Equal("Label 1", saveSnapshot.Labels[0].Text);
        Assert.Equal("RFID-01", saveSnapshot.Devices[0].RfidStationId);
    }

    [Fact]
    public void Updates_each_annotation_type_in_the_working_copy()
    {
        var editor = CreateAdminEditor();

        Assert.True(editor.TryUpdateMapPoint("point-1", "Point updated", 21, 22, false));
        Assert.True(editor.TryUpdateRfidStation("rfid-1", "RFID updated", 23, 24, "RFID-UPDATED", false));
        Assert.True(editor.TryUpdateMapLabel("label-1", "Label updated", 25, 26, 27, 28, false));

        Assert.Equal("Point updated", editor.WorkingCopy.Points[0].Name);
        Assert.Equal(21, editor.WorkingCopy.Points[0].CadX);
        Assert.Equal(22, editor.WorkingCopy.Points[0].CadY);
        Assert.False(editor.WorkingCopy.Points[0].Enabled);
        Assert.Equal("RFID updated", editor.WorkingCopy.Devices[0].Name);
        Assert.Equal(23, editor.WorkingCopy.Devices[0].CadX);
        Assert.Equal(24, editor.WorkingCopy.Devices[0].CadY);
        Assert.Equal("RFID-UPDATED", editor.WorkingCopy.Devices[0].RfidStationId);
        Assert.Equal("A0", editor.WorkingCopy.Devices[0].ProtocolAddress);
        Assert.False(editor.WorkingCopy.Devices[0].Enabled);
        Assert.Equal("Label updated", editor.WorkingCopy.Labels[0].Text);
        Assert.Equal(25, editor.WorkingCopy.Labels[0].CadX);
        Assert.Equal(26, editor.WorkingCopy.Labels[0].CadY);
        Assert.Equal(27, editor.WorkingCopy.Labels[0].Rotation);
        Assert.Equal(28, editor.WorkingCopy.Labels[0].TextHeight);
        Assert.False(editor.WorkingCopy.Labels[0].Enabled);
    }

    [Fact]
    public void Moves_and_deletes_annotations_by_kind_and_id()
    {
        var editor = CreateAdminEditor();

        Assert.True(editor.TryMove(MapAnnotationKind.MapPoint, "point-1", new CadPoint(31, 32)));
        Assert.True(editor.TryMove(MapAnnotationKind.RfidStation, "rfid-1", new CadPoint(33, 34)));
        Assert.True(editor.TryMove(MapAnnotationKind.MapLabel, "label-1", new CadPoint(35, 36)));
        Assert.False(editor.TryMove(MapAnnotationKind.MapPoint, "missing", new CadPoint(0, 0)));
        Assert.False(editor.TryMove(MapAnnotationKind.MapPoint, "rfid-1", new CadPoint(0, 0)));

        Assert.Equal(31, editor.WorkingCopy.Points[0].CadX);
        Assert.Equal(32, editor.WorkingCopy.Points[0].CadY);
        Assert.Equal(33, editor.WorkingCopy.Devices[0].CadX);
        Assert.Equal(34, editor.WorkingCopy.Devices[0].CadY);
        Assert.Equal(35, editor.WorkingCopy.Labels[0].CadX);
        Assert.Equal(36, editor.WorkingCopy.Labels[0].CadY);

        Assert.True(editor.TryDelete(MapAnnotationKind.MapPoint, "point-1"));
        Assert.True(editor.TryDelete(MapAnnotationKind.RfidStation, "rfid-1"));
        Assert.True(editor.TryDelete(MapAnnotationKind.MapLabel, "label-1"));
        Assert.False(editor.TryDelete(MapAnnotationKind.MapLabel, "missing"));
        Assert.Empty(editor.WorkingCopy.Points);
        Assert.Empty(editor.WorkingCopy.Devices);
        Assert.Empty(editor.WorkingCopy.Labels);
    }

    [Fact]
    public void Undo_restores_one_previous_working_copy_state_at_a_time()
    {
        var editor = CreateAdminEditor();

        Assert.True(editor.TryAddMapPoint(new MapPoint { Id = "point-2", Name = "Point 2" }));
        Assert.True(editor.TryUpdateMapPoint("point-1", "Point updated", 41, 42, false));

        Assert.True(editor.TryUndo());
        Assert.Equal("Point 1", editor.WorkingCopy.Points[0].Name);
        Assert.Equal(2, editor.WorkingCopy.Points.Count);
        Assert.True(editor.HasUnsavedChanges);

        Assert.True(editor.TryUndo());
        Assert.Equal("Point 1", editor.WorkingCopy.Points[0].Name);
        Assert.Single(editor.WorkingCopy.Points);
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.TryUndo());
    }

    [Fact]
    public void Mark_saved_clears_dirty_state_and_discard_restores_the_saved_baseline()
    {
        var editor = CreateAdminEditor();

        Assert.True(editor.TryUpdateMapLabel("label-1", "Saved label", 51, 52, 53, 54, true));
        editor.MarkSaved();

        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.TryUndo());

        Assert.True(editor.TryUpdateMapLabel("label-1", "Unsaved label", 61, 62, 63, 64, false));
        editor.DiscardChanges();

        Assert.Equal("Saved label", editor.WorkingCopy.Labels[0].Text);
        Assert.Equal(51, editor.WorkingCopy.Labels[0].CadX);
        Assert.True(editor.WorkingCopy.Labels[0].Enabled);
        Assert.False(editor.HasUnsavedChanges);
    }

    private static MapAnnotationEditor CreateAdminEditor() =>
        new(CreateSource(), CreateAdminSession());

    private static AdminModeService CreateAdminSession()
    {
        var session = new AdminModeService(TestCredential);
        session.EnterAdminMode(TestCredential);
        return session;
    }

    private static StationConfig CreateSource() => new()
    {
        Id = "560",
        Name = "-560 站场",
        BackgroundImage = "maps/560.png",
        CadMinX = 0,
        CadMaxX = 100,
        CadMinY = 0,
        CadMaxY = 100,
        Points = new List<MapPoint>
        {
            new()
            {
                Id = "point-1",
                Name = "Point 1",
                CadX = 1,
                CadY = 2,
                Enabled = true
            }
        },
        Labels = new List<MapLabel>
        {
            new()
            {
                Id = "label-1",
                Text = "Label 1",
                CadX = 3,
                CadY = 4,
                Rotation = 5,
                TextHeight = 6,
                Enabled = true
            }
        },
        Devices = new List<DeviceConfig>
        {
            new()
            {
                Id = "rfid-1",
                Name = "RFID 1",
                Type = DeviceType.RfidStation,
                StationId = "560",
                RfidStationId = "RFID-01",
                CadX = 7,
                CadY = 8,
                ProtocolAddress = "A0",
                Enabled = true
            }
        }
    };
}
