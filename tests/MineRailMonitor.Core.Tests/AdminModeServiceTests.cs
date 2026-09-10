using System.ComponentModel;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AdminModeServiceTests
{
    private static readonly string TestCredential = Guid.NewGuid().ToString("N");

    [Fact]
    public void Does_not_enable_when_no_password_is_configured()
    {
        var service = new AdminModeService();

        Assert.False(service.EnterAdminMode("any-password"));
        Assert.False(service.IsAdmin);
    }

    [Fact]
    public void Starts_non_admin_and_rejects_an_incorrect_password()
    {
        var service = new AdminModeService(TestCredential);

        Assert.False(service.IsAdmin);
        Assert.False(service.EnterAdminMode("wrong-password"));
        Assert.False(service.IsAdmin);
    }

    [Fact]
    public void Correct_password_enters_admin_mode_and_exit_leaves_it()
    {
        var service = new AdminModeService(TestCredential);

        Assert.True(service.EnterAdminMode(TestCredential));
        Assert.True(service.IsAdmin);

        service.ExitAdminMode();

        Assert.False(service.IsAdmin);
        Assert.False(new AdminModeService(TestCredential).IsAdmin);
    }

    [Fact]
    public void Raises_property_changed_when_admin_state_changes()
    {
        var service = new AdminModeService(TestCredential);
        var changedProperties = new List<string?>();
        service.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        service.EnterAdminMode(TestCredential);
        service.ExitAdminMode();

        Assert.Equal(
            new[] { nameof(service.IsAdmin), nameof(service.IsAdmin) },
            changedProperties);
    }
}
