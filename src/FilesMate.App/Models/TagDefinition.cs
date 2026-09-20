namespace FilesMate.App.Models;

public sealed record TagDefinition(
    long Id,
    string Name,
    string Color,
    int SortOrder);
