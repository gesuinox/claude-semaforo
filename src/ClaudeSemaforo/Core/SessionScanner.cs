using System.Diagnostics;
using System.Text.Json;

namespace ClaudeSemaforo.Core;

public sealed record ClaudeSession(int Pid, string SessionId, string Cwd, string? Name);

/// <summary>
/// Lista as sessões do Claude Code que estão realmente rodando. Cada processo grava
/// <c>~/.claude/sessions/&lt;pid&gt;.json</c>, mas os arquivos de sessões encerradas ficam
/// para trás — por isso todo pid é confirmado contra o processo vivo.
/// </summary>
public static class SessionScanner
{
    public static List<ClaudeSession> Scan()
    {
        var sessions = new List<ClaudeSession>();

        string[] files;
        try
        {
            files = Directory.GetFiles(ClaudePaths.SessionsDir, "*.json");
        }
        catch (DirectoryNotFoundException)
        {
            return sessions;
        }

        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                var pid = root.GetProperty("pid").GetInt32();
                var id = root.GetProperty("sessionId").GetString();
                if (id is null) continue;

                var procStart = root.TryGetProperty("procStart", out var ps)
                    ? ParseFileTime(ps)
                    : 0;
                if (!IsAlive(pid, procStart)) continue;

                sessions.Add(new ClaudeSession(
                    Pid: pid,
                    SessionId: id,
                    Cwd: root.TryGetProperty("cwd", out var cwd) ? cwd.GetString() ?? "" : "",
                    Name: root.TryGetProperty("name", out var n) ? n.GetString() : null));
            }
            catch (Exception e) when (e is JsonException or IOException or KeyNotFoundException)
            {
                // Arquivo sendo escrito ou de um formato mais novo: ignora essa sessão.
            }
        }

        return sessions;
    }

    private static long ParseFileTime(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => long.TryParse(el.GetString(), out var v) ? v : 0,
        JsonValueKind.Number => el.TryGetInt64(out var v) ? v : 0,
        _ => 0,
    };

    /// <summary>
    /// Confirma o pid. Como o Windows recicla pids, o horário de início do processo é
    /// comparado com o <c>procStart</c> registrado; quando não dá para lê-lo (permissão),
    /// cai para uma checagem pelo nome do processo.
    /// </summary>
    private static bool IsAlive(int pid, long procStart)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return false;

            try
            {
                if (procStart > 0)
                {
                    var delta = Math.Abs(p.StartTime.ToFileTime() - procStart);
                    return delta < 20_000_000; // 2 s de tolerância
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Sem acesso ao StartTime: usa o nome como desempate.
            }

            var name = p.ProcessName;
            return name.Contains("claude", StringComparison.OrdinalIgnoreCase)
                || name.Contains("node", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
