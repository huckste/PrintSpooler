namespace PrintSpooler.Web.Services;

public class FormatTime
{
    private const int Day = 1440;
    private const int Hour = 60;
    private const int Minute = 1;

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

        return age.TotalMinutes switch
        {
            < Minute => "Just Now",
            < Hour => $"{(int)age.TotalMinutes}m ago",
            < Day => $"{(int)age.TotalHours}h ago",
            _ => $"{(int)age.TotalDays}d ago",
        };
    }
}
