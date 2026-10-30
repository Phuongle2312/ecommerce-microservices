namespace BuildingBlocks.Pagination;

public sealed record PaginationRequest(int PageIndex = 1, int PageSize = 10)
{
    public const int MaxPageSize = 100;

    public int SafePageIndex => Math.Max(1, PageIndex);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (SafePageIndex - 1) * SafePageSize;
}

public sealed record PaginatedResult<T>(int PageIndex, int PageSize, long TotalCount, IReadOnlyList<T> Items)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
