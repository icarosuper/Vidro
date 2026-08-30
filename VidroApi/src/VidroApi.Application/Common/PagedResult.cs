namespace VidroApi.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);
