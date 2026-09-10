using System.Windows;
using MineRailMonitor.Simulator.Acceptance;

namespace MineRailMonitor.Simulator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var options = SimulatorCommandLineOptions.Parse(e.Args);
            if (!options.TestMode)
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                var window = new MainWindow();
                MainWindow = window;
                window.Show();
                return;
            }

            var scenario = AcceptanceScenario.Parse(options.Scenario!);
            _ = RunAcceptanceAsync(options, scenario);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Shutdown(2);
        }
    }

    private async Task RunAcceptanceAsync(SimulatorCommandLineOptions options, AcceptanceScenarioDefinition scenario)
    {
        var exitCode = 0;
        try
        {
            var runner = new SimulatorScenarioRunner(scenario, options.Port, options.ResultPath!, options.ReadyFile!);
            await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            Console.Error.WriteLine(exception);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => Shutdown(exitCode));
        }
    }
}
