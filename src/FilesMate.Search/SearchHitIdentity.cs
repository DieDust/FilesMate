namespace FilesMate.Search;

// File targets retain Windows path comparison. Application targets also carry
// their entity and arguments: several launchers can share the same executable.
internal readonly struct SearchHitIdentity : IEquatable<SearchHitIdentity>
{
    private readonly string _entity;
    private readonly string? _launchPath;
    private readonly string? _arguments;

    private SearchHitIdentity(string entity, string? launchPath, string? arguments)
        => (_entity, _launchPath, _arguments) = (entity, launchPath, arguments);

    public static SearchHitIdentity From(NameHit hit) => hit.Application is { } app
        ? new(app.Identity, app.LaunchPath, app.Arguments) : new(hit.Path, null, null);

    public bool Equals(SearchHitIdentity other) =>
        StringComparer.OrdinalIgnoreCase.Equals(_entity, other._entity)
        && StringComparer.OrdinalIgnoreCase.Equals(_launchPath, other._launchPath)
        && StringComparer.Ordinal.Equals(_arguments, other._arguments);

    public override bool Equals(object? obj) => obj is SearchHitIdentity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(_entity ?? ""),
        _launchPath is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(_launchPath),
        _arguments is null ? 0 : StringComparer.Ordinal.GetHashCode(_arguments));

    public static bool operator ==(SearchHitIdentity left, SearchHitIdentity right) => left.Equals(right);
    public static bool operator !=(SearchHitIdentity left, SearchHitIdentity right) => !left.Equals(right);
}
