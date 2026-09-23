using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Infrastructure.Configuration;

public interface IProjectConfigService
{
    Task<ProjectConfigLoadResult> LoadAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default);

    Task<ProjectConfigSaveResult> SaveStationAsync(
        string projectDirectory,
        StationConfig station,
        CancellationToken cancellationToken = default);

    Task<ProjectConfigSaveResult> SaveRfidSettingsAsync(
        string projectDirectory,
        RfidSettings settings,
        CancellationToken cancellationToken = default);

    Task<ProjectConfigSaveResult> SaveYardCommunicationsAsync(
        string projectDirectory,
        IEnumerable<YardCommunicationConfig> configurations,
        CancellationToken cancellationToken = default);

    Task<ProjectConfigSaveResult> SaveYardAlarmForwardsAsync(
        string projectDirectory,
        IEnumerable<YardAlarmForwardConfig> configurations,
        CancellationToken cancellationToken = default);

    Task<ProjectConfigSaveResult> SaveRfidStationsAsync(
        string projectDirectory,
        IEnumerable<RfidStationConfig> stations,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectConfigLoadResult
{
    private ProjectConfigLoadResult(ProjectConfig? project, IReadOnlyList<string> errors)
    {
        Project = project;
        Errors = errors;
    }

    public ProjectConfig? Project { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool Succeeded => Project is not null && Errors.Count == 0;

    public static ProjectConfigLoadResult Success(ProjectConfig project) =>
        new(project, []);

    public static ProjectConfigLoadResult Failure(IEnumerable<string> errors) =>
        new(null, errors.ToArray());
}

public sealed class ProjectConfigSaveResult
{
    private ProjectConfigSaveResult(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    public bool Succeeded { get; }

    public IReadOnlyList<string> Errors { get; }

    public static ProjectConfigSaveResult Success() => new(true, []);

    public static ProjectConfigSaveResult Failure(IEnumerable<string> errors) =>
        new(false, errors.ToArray());
}
