namespace TRPGMaster.Assets.Models;

public sealed class AssetManifest
{
    public int Version { get; init; } = 1;
    public Dictionary<string, AssetEntry> Assets { get; init; } = new();
}
