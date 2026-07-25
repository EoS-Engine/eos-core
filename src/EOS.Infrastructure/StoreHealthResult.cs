namespace EOS.Infrastructure;

public sealed record StoreHealthResult(string StoreName, bool Healthy, string? Error);
