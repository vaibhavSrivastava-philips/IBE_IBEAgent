namespace Philips.IBE.IBEAgent.Configuration;

public sealed record ConfigurationMigrationReport
{
    public int SourceSchemaVersion { get; init; }
    public int TargetSchemaVersion { get; init; } = ConfigurationSchemaVersion.Current;
    public IReadOnlyList<string> AppliedChanges { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool IsSuccessful => Errors.Count == 0;

    public static ConfigurationMigrationReport Success(
        int sourceSchemaVersion,
        IEnumerable<string>? warnings = null,
        IEnumerable<string>? appliedChanges = null) => new()
    {
        SourceSchemaVersion = sourceSchemaVersion,
        AppliedChanges = appliedChanges?.ToArray() ?? [],
        Warnings = warnings?.ToArray() ?? [],
    };

    public static ConfigurationMigrationReport Failed(
        int sourceSchemaVersion,
        IEnumerable<string> errors,
        IEnumerable<string>? warnings = null,
        IEnumerable<string>? appliedChanges = null) => new()
    {
        SourceSchemaVersion = sourceSchemaVersion,
        AppliedChanges = appliedChanges?.ToArray() ?? [],
        Warnings = warnings?.ToArray() ?? [],
        Errors = errors.ToArray(),
    };
}
