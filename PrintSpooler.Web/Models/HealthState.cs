namespace PrintSpooler.Web.Models;

public enum HealthState
{
  Unknown,
  Online,
  Offline
}

public static class HealthStateExtensions
{
  public static string Label(this HealthState state) => state switch
  {
    HealthState.Online  => "ONLINE",
    HealthState.Offline => "OFFLINE",
    _                   => "UNKNOWN",
  };

  // Modifier class only for the non-default states. The base `.system-status`
  // is the neutral (unknown) style, so unknown needs no extra class.
  public static string CssClass(this HealthState state) => state switch
  {
    HealthState.Online  => "system-status-online",
    HealthState.Offline => "system-status-offline",
    _                   => string.Empty,
  };
}
