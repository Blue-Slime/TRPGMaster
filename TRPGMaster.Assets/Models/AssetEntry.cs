namespace TRPGMaster.Assets.Models;

public sealed class AssetEntry
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Sha256 { get; init; }
    public required string FileName { get; init; }
    public required string FileExtension { get; init; }
    public required string RelativePath { get; init; }
    public string? ThumbnailRelativePath { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
