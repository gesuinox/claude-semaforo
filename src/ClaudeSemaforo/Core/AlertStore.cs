using System.Text.Json;

namespace ClaudeSemaforo.Core;

public sealed record Alert(string SessionId, string Kind, DateTime RaisedUtc);

/// <summary>
/// Caixa de correio entre os hooks do Claude Code e a barra: um arquivo por sessão que
/// está esperando o usuário. Quem escreve é o <see cref="HookHandler"/>, chamado pelo
/// próprio Claude Code; quem lê é o <see cref="StatusMonitor"/>.
/// </summary>
public static class AlertStore
{
    /// <summary>Rede de segurança: se um hook de limpeza falhar, o alerta não fica eterno.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeSemaforo", "alerts");

    public static void Raise(string sessionId, string kind)
    {
        var path = PathFor(sessionId);
        if (path is null) return;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, JsonSerializer.Serialize(new { kind, at = DateTime.UtcNow }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Perder um alerta não pode derrubar o hook nem travar o Claude Code.
        }
    }

    public static void Clear(string sessionId)
    {
        var path = PathFor(sessionId);
        if (path is null) return;

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static List<Alert> Active()
    {
        var alerts = new List<Alert>();

        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(Directory, "*.json");
        }
        catch (Exception e) when (e is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return alerts;
        }

        foreach (var file in files)
        {
            try
            {
                var raised = File.GetLastWriteTimeUtc(file);
                if (DateTime.UtcNow - raised > MaxAge)
                {
                    File.Delete(file);
                    continue;
                }

                var kind = "";
                using (var doc = JsonDocument.Parse(File.ReadAllText(file)))
                    if (doc.RootElement.TryGetProperty("kind", out var k))
                        kind = k.GetString() ?? "";

                alerts.Add(new Alert(Path.GetFileNameWithoutExtension(file), kind, raised));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }

        return alerts;
    }

    /// <summary>O id vem de fora, então só GUID passa — nada de virar caminho.</summary>
    private static string? PathFor(string sessionId) =>
        Guid.TryParse(sessionId, out _)
            ? Path.Combine(Directory, sessionId + ".json")
            : null;
}
