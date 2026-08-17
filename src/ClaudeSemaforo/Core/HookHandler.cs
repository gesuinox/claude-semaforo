using System.Text.Json;

namespace ClaudeSemaforo.Core;

/// <summary>
/// Roda como <c>ClaudeSemaforo.exe --hook</c>, chamado pelo Claude Code com o evento em
/// JSON no stdin. Levanta ou baixa o alerta da sessão conforme o que chegou.
/// </summary>
public static class HookHandler
{
    /// <summary>
    /// Tipos de notificação em que o Claude está parado esperando o usuário. Os demais
    /// (<c>auth_success</c>, <c>agent_completed</c>, <c>elicitation_complete</c>) não pedem
    /// nada e servem para baixar o alerta.
    /// </summary>
    private static readonly HashSet<string> NeedsUser = new(StringComparer.OrdinalIgnoreCase)
    {
        "permission_prompt",
        "idle_prompt",
        "elicitation_dialog",
        "agent_needs_input",
    };

    public static int Run()
    {
        string payload;
        try
        {
            // OpenStandardInput em vez de Console.In: o app é WinExe e, sem console
            // anexado, Console.In vira um leitor vazio mesmo com o stdin redirecionado.
            using var stdin = Console.OpenStandardInput();
            using var reader = new StreamReader(stdin);
            payload = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(payload)) return 0;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var sessionId = Text(root, "session_id");
            if (sessionId is null) return 0;

            var e = Text(root, "hook_event_name");

            if (string.Equals(e, "Notification", StringComparison.OrdinalIgnoreCase))
            {
                var kind = Text(root, "notification_type") ?? "";
                if (NeedsUser.Contains(kind)) AlertStore.Raise(sessionId, kind);
                else AlertStore.Clear(sessionId);
                return 0;
            }

            // Stop e UserPromptSubmit: o turno acabou ou o usuário já respondeu.
            AlertStore.Clear(sessionId);
            return 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
