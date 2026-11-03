namespace Sengoku.Tests;

public sealed class MetadataProbe
{
    public int UserId { get; init; }
    public string Password => throw new InvalidOperationException("Secret getters must never be evaluated.");
    public string ApiKey => throw new InvalidOperationException("Secret getters must never be evaluated.");
}
