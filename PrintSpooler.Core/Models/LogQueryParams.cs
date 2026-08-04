namespace PrintSpooler.Core.Models;

public class LogQueryParams
{
    public string? SearchTerms { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public JobAction? ActionFilter { get; set; }
    public ByWho? PerformedBy { get; set; }
    public OrderByField OrderByField { get; set; } = OrderByField.Timestamp;
    public SortDirection SortDirection { get; set; } = SortDirection.Desc;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public enum OrderByField
{
    JobAction,
    ByWho,
    Timestamp,
    Details,
}

public enum SortDirection
{
    Asc,
    Desc,
}
