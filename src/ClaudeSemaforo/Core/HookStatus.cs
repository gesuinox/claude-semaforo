using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeSemaforo.Core;

/// <summary>
/// Confere, apenas lendo, se os hooks que alimentam o alerta estão registrados em
/// <c>~/.claude/settings.json</c>. Sem eles a barra nunca fica sabendo que o Claude
/// parou esperando uma resposta — e o tooltip avisa disso.
/// </summary>
public static class HookStatus
{
    /// <summary>
    /// <c>Notification</c> levanta o alerta; os outros dois o baixam, cobrindo tanto o
    /// caso de o usuário responder quanto o de o turno terminar.
    /// </summary>
    public static readonly string[] Events = ["Notification", "Stop", "UserPromptSubmit"];

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "settings.json");

    public static bool Configured()
    {
        JsonObject? root;
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }

        if (root?["hooks"] is not JsonObject hooks) return false;

        return Events.All(e => hooks[e] is JsonArray list && list.Any(CallsUs));
    }

    private static bool CallsUs(JsonNode? group)
    {
        if (group?["hooks"] is not JsonArray handlers) return false;

        return handlers.Any(h =>
            h?["command"]?.GetValue<string>() is { } cmd
            && cmd.EndsWith("ClaudeSemaforo.exe", StringComparison.OrdinalIgnoreCase));
    }
}
