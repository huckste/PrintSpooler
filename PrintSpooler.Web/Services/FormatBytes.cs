namespace PrintSpooler.Web.Services;

public class FormatBytes
{
    private const long Kb = 1024;
    private const long Mb = Kb * 1024;

    public static string Short(long bytes) => bytes switch
    {
        < Kb => $"{bytes} B",
        < Mb => $"{bytes / (double)Kb:0.#} KB",
        _ => $"{bytes / (double)Mb:0.#} MB",
    };
}