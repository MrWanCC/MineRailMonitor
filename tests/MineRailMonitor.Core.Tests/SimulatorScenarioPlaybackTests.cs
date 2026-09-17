using MineRailMonitor.Simulator.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorScenarioPlaybackTests
{
    private static readonly ushort[] Normal11 =
    {
        0x0001, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015,
        0x0016, 0x0017, 0x0018, 0x0019, 0x001A
    };

    [Fact]
    public void Loading_scenario_starts_with_empty_slots_and_first_next_rfid()
    {
        var playback = new ScenarioPlaybackState();

        playback.Load(Normal11);

        Assert.Equal(0, playback.CurrentIndex);
        Assert.Equal((ushort)0x0001, playback.NextRfid);
        Assert.Null(playback.CurrentRfid);
        Assert.All(playback.Slots, slot => Assert.Equal((ushort)0, slot));
    }

    [Fact]
    public void Step_adds_only_one_rfid_in_sequence_order()
    {
        var playback = new ScenarioPlaybackState();
        playback.Load(Normal11);

        Assert.Equal((ushort)0x0001, playback.Step());
        Assert.Equal(1, playback.CurrentIndex);
        Assert.Equal((ushort)0x0001, playback.CurrentRfid);
        Assert.Equal((ushort)0x0011, playback.NextRfid);
        Assert.Equal(new ushort[] { 0x0001 }, playback.Slots.Take(1));
        Assert.All(playback.Slots.Skip(1), slot => Assert.Equal((ushort)0, slot));

        Assert.Equal((ushort)0x0011, playback.Step());
        Assert.Equal(2, playback.CurrentIndex);
        Assert.Equal(new ushort[] { 0x0001, 0x0011 }, playback.Slots.Take(2));
        Assert.All(playback.Slots.Skip(2), slot => Assert.Equal((ushort)0, slot));
    }

    [Fact]
    public void Pause_does_not_change_step_state_and_reset_allows_second_run()
    {
        var playback = new ScenarioPlaybackState();
        playback.Load(Normal11);
        playback.Start();
        playback.Step();
        playback.Pause();

        Assert.False(playback.IsPlaying);
        Assert.Equal(1, playback.CurrentIndex);

        playback.Reset();

        Assert.False(playback.IsPlaying);
        Assert.Equal(0, playback.CurrentIndex);
        Assert.Null(playback.CurrentRfid);
        Assert.All(playback.Slots, slot => Assert.Equal((ushort)0, slot));

        playback.Start();
        Assert.Equal((ushort)0x0001, playback.Step());
        Assert.Equal(1, playback.CurrentIndex);
    }

    [Fact]
    public void Duplicate_sequence_items_are_kept_as_separate_slot_inputs()
    {
        var playback = new ScenarioPlaybackState();
        playback.Load(new ushort[] { 0x0001, 0x0031, 0x0032, 0x0032 });

        while (playback.Step().HasValue)
        {
        }

        Assert.Equal(4, playback.CurrentIndex);
        Assert.Equal(new ushort[] { 0x0001, 0x0031, 0x0032, 0x0032 }, playback.Slots.Take(4));
    }
}
