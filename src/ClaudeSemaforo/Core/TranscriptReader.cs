using System.Text;
using System.Text.Json;

namespace ClaudeSemaforo.Core;

public sealed record TranscriptState(ActivityState State, DateTime? TimestampUtc, string? Message);

/// <summary>
/// Classifica o estado de uma sessão pela última entrada útil da transcrição .jsonl:
/// <list type="bullet">
///   <item><c>assistant</c> com <c>stop_reason: tool_use</c>, ou <c>user</c> com
///   <c>tool_result</c> — o turno está em andamento (amarelo);</item>
///   <item><c>assistant</c> com <c>stop_reason: end_turn</c> — o turno acabou (verde);</item>
///   <item><c>error: rate_limit</c> ou <c>apiErrorStatus: 429</c> — limite atingido
///   (vermelho); o texto costuma trazer a hora do reset.</item>
/// </list>
/// Linhas de metadados (<c>ai-title</c>, <c>mode</c>, <c>last-prompt</c>) não têm
/// <c>timestamp</c> e são puladas.
/// </summary>
public sealed class TranscriptReader
{
    // As transcrições passam de 40 MB; só a cauda é lida.
    private const int TailBytes = 96 * 1024;

    private readonly Dictionary<string, string> _pathCache = new();

    public TranscriptState? Read(ClaudeSession session)
    {
        var path = ResolvePath(session);
        return path is null ? null : ReadFile(path);
    }

    public TranscriptState? ReadFile(string path)
    {
        var lines = ReadTailLines(path);
        if (lines is null) return null;

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var state = Classify(lines[i]);
            if (state is not null) return state;
        }

        return null;
    }

    /// <summary>Transcrição mais recente de todas — usada quando nada está rodando.</summary>
    public static string? MostRecentTranscript()
    {
        try
        {
            var best = default(FileInfo);
            foreach (var f in Directory.EnumerateFiles(ClaudePaths.ProjectsDir, "*.jsonl", SearchOption.AllDirectories))
            {
                var info = new FileInfo(f);
                if (best is null || info.LastWriteTimeUtc > best.LastWriteTimeUtc) best = info;
            }

            return best?.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? ResolvePath(ClaudeSession session)
    {
        if (_pathCache.TryGetValue(session.SessionId, out var cached) && File.Exists(cached))
            return cached;

        var direct = Path.Combine(ClaudePaths.ProjectsDir, Slug(session.Cwd), session.SessionId + ".jsonl");
        if (File.Exists(direct))
        {
            _pathCache[session.SessionId] = direct;
            return direct;
        }

        // Se a regra do slug mudar em alguma versão, procura o arquivo pelo id.
        try
        {
            var found = Directory
                .EnumerateFiles(ClaudePaths.ProjectsDir, session.SessionId + ".jsonl", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found is not null) _pathCache[session.SessionId] = found;
            return found;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>"G:\Meu Drive\public\civilcalc" vira "G--Meu-Drive-public-civilcalc".</summary>
    private static string Slug(string cwd)
    {
        var sb = new StringBuilder(cwd.Length);
        foreach (var ch in cwd) sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        return sb.ToString();
    }

    private static List<string>? ReadTailLines(string path)
    {
        try
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, fs.Length - TailBytes);
            fs.Seek(start, SeekOrigin.Begin);

            using var reader = new StreamReader(fs, Encoding.UTF8);
            if (start > 0) reader.ReadLine(); // descarta a linha cortada ao meio

            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                if (line.Length > 0) lines.Add(line);
            return lines;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static TranscriptState? Classify(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("timestamp", out var tsEl)) return null;
            if (!root.TryGetProperty("type", out var typeEl)) return null;

            var type = typeEl.GetString();
            if (type is not ("user" or "assistant" or "system")) return null;

            DateTime? ts = tsEl.TryGetDateTime(out var parsed) ? parsed.ToUniversalTime() : null;

            if (IsRateLimit(root))
                return new TranscriptState(ActivityState.Blocked, ts, FirstText(root));

            // Erro de API que não é limite (500, rede…): não classifica, olha a entrada anterior.
            if (root.TryGetProperty("isApiErrorMessage", out var apiErr)
                && apiErr.ValueKind == JsonValueKind.True)
                return null;

            if (type == "assistant")
            {
                var stop = root.TryGetProperty("message", out var msg)
                    && msg.TryGetProperty("stop_reason", out var sr)
                        ? sr.GetString()
                        : null;

                return stop switch
                {
                    "tool_use" => new TranscriptState(ActivityState.Working, ts, null),
                    "end_turn" or "stop_sequence" or "max_tokens" =>
                        new TranscriptState(ActivityState.Done, ts, null),
                    _ => new TranscriptState(ActivityState.Working, ts, null),
                };
            }

            // Turno do usuário: prompt novo ou retorno de ferramenta — nos dois casos, trabalhando.
            return new TranscriptState(ActivityState.Working, ts, null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRateLimit(JsonElement root)
    {
        if (root.TryGetProperty("error", out var err)
            && err.ValueKind == JsonValueKind.String
            && string.Equals(err.GetString(), "rate_limit", StringComparison.OrdinalIgnoreCase))
            return true;

        return root.TryGetProperty("apiErrorStatus", out var status)
            && status.TryGetInt32(out var code)
            && code == 429;
    }

    /// <summary>Primeiro bloco de texto da mensagem — traz o "resets 11:50pm" do limite.</summary>
    private static string? FirstText(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg)) return null;
        if (!msg.TryGetProperty("content", out var content)) return null;

        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out var t)
                && t.GetString() == "text"
                && block.TryGetProperty("text", out var text))
                return text.GetString();
        }

        return null;
    }
}
