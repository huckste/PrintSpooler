namespace PrintSpooler.Web.Services;

public class FormatTime
{
    private const int DaySeconds = 86400;
    private const int HourSeconds = 3600;
    private const int MinuteSeconds = 60;
    private const int JustNowSeconds = 5;

    public static string Short(DateTime time)
    {
        var age = DateTime.UtcNow - time;
        return age.TotalHours < 24 ? Ago(time) : time.ToLocalTime().ToString("MMM d");
    }

    public static string Full(DateTime time) =>
        time.ToLocalTime().ToString("MMM d, yyyy h:mm:ss tt");

    public static string Ago(DateTime time)
    {
        var age = DateTime.UtcNow - time;

        return age.TotalSeconds switch
        {
            < JustNowSeconds => "Just Now",
            < MinuteSeconds => $"{(int)age.TotalSeconds}s ago",
            < HourSeconds => $"{(int)age.TotalMinutes}m ago",
            < DaySeconds => $"{(int)age.TotalHours}h ago",
            _ => $"{(int)age.TotalDays}d ago",
        };
    }
}
