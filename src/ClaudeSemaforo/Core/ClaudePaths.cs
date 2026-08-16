namespace ClaudeSemaforo.Core;

/// <summary>Onde o Claude Code e o Claude Desktop guardam o que a barra lê.</summary>
public static class ClaudePaths
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Um arquivo por processo do Claude Code vivo: pid, sessionId, cwd.</summary>
    public static string SessionsDir => Path.Combine(Home, ".claude", "sessions");

    /// <summary>Transcrições .jsonl, uma pasta por projeto.</summary>
    public static string ProjectsDir => Path.Combine(Home, ".claude", "projects");

    /// <summary>Histórico de uso do plano gravado pelo Claude Desktop (fh = 5h, sd = 7 dias).</summary>
    public static string UsageHistoryFile => Path.Combine(AppData, "Claude", "plan-usage-history.json");

    public static string SettingsFile => Path.Combine(AppData, "ClaudeSemaforo", "settings.json");
}
