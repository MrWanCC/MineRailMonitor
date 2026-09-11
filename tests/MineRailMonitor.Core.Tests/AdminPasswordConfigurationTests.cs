namespace MineRailMonitor.Core.Tests;

public sealed class AdminPasswordConfigurationTests
{
    [Fact]
    public void Application_configuration_uses_the_requested_default_admin_password()
    {
        var configPath = LocateSourceFile("src", "MineRailMonitor", "App.config");
        var config = File.ReadAllText(configPath);

        Assert.Contains("<add key=\"AdminPassword\" value=\"admin123\" />", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_password_input_centers_entered_password_content()
    {
        var dialogPath = LocateSourceFile("src", "MineRailMonitor", "Pages", "AdminPasswordDialog.xaml");
        var dialogMarkup = File.ReadAllText(dialogPath);

        Assert.Contains("<PasswordBox x:Name=\"PasswordInput\"", dialogMarkup, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Center\"", dialogMarkup, StringComparison.Ordinal);
    }

    private static string LocateSourceFile(params string[] segments)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException($"Could not locate source file: {Path.Combine(segments)}");
    }
}
