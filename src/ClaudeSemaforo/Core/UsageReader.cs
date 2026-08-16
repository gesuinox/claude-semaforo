using System.Text.RegularExpressions;

namespace ClaudeSemaforo.Core;

public sealed record UsageSample(int FiveHour, int SevenDay, DateTime SampledUtc);

/// <summary>
/// Lê a última amostra de <c>plan-usage-history.json</c>, o arquivo que o Claude Desktop
/// atualiza a cada poucos minutos com o mesmo número que o <c>/usage</c> mostra.
/// Cada amostra é <c>{"t":epochMs,"org":"...","u":{"fh":48,"sd":50}}</c>.
/// </summary>
public sealed partial class UsageReader
{
    // O arquivo é append-only e passa de 180 KB; ler só a cauda mantém o custo constante.
    private const int TailBytes = 16 * 1024;

    private DateTime _lastWrite;
    private UsageSample? _cache;

    [GeneratedRegex("""
        "t"\s*:\s*(\d+).{0,200}?"fh"\s*:\s*(\d+)\s*,\s*"sd"\s*:\s*(\d+)
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex SampleRegex { get; }

    public UsageSample? Read()
    {
        var path = ClaudePaths.UsageHistoryFile;

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
            if (info.LastWriteTimeUtc == _lastWrite) return _cache;
        }
        catch (IOException)
        {
            return _cache;
        }

        var tail = ReadTail(path);
        if (tail is null) return _cache;

        // O último match é a amostra mais recente.
        Match? last = null;
        foreach (Match m in SampleRegex.Matches(tail)) last = m;
        if (last is null) return _cache;

        var epochMs = long.Parse(last.Groups[1].Value);
        _cache = new UsageSample(
            FiveHour: int.Parse(last.Groups[2].Value),
            SevenDay: int.Parse(last.Groups[3].Value),
            SampledUtc: DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime);
        _lastWrite = info.LastWriteTimeUtc;
        return _cache;
    }

    private static string? ReadTail(string path)
    {
        try
        {
            // O Desktop mantém o arquivo aberto: só dá para ler compartilhando escrita.
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, fs.Length - TailBytes);
            fs.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
