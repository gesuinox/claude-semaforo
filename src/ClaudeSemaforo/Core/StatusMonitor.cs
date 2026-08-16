using Timer = System.Windows.Forms.Timer;

namespace ClaudeSemaforo.Core;

/// <summary>
/// Junta as três fontes num único <see cref="StatusSnapshot"/> e avisa quando ele muda.
/// Roda na thread da UI: cada tique lê ~100 KB de cauda de arquivo, nada que justifique
/// uma thread própria.
/// </summary>
public sealed class StatusMonitor : IDisposable
{
    private static readonly TimeSpan UsageInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleScanInterval = TimeSpan.FromSeconds(30);

    private readonly Timer _timer = new() { Interval = 1000 };
    private readonly UsageReader _usage = new();
    private readonly TranscriptReader _transcripts = new();

    private UsageSample? _lastUsage;
    private DateTime _usageCheckedAt = DateTime.MinValue;
    private string? _idleTranscript;
    private DateTime _idleScannedAt = DateTime.MinValue;

    public event Action<StatusSnapshot>? Updated;

    public StatusSnapshot Current { get; private set; } = new();

    public StatusMonitor()
    {
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    public void Refresh()
    {
        var snapshot = Build();
        if (snapshot == Current) return;

        Current = snapshot;
        Updated?.Invoke(snapshot);
    }

    private StatusSnapshot Build()
    {
        var now = DateTime.UtcNow;

        if (now - _usageCheckedAt >= UsageInterval)
        {
            _lastUsage = _usage.Read();
            _usageCheckedAt = now;
        }

        var sessions = SessionScanner.Scan();

        // Entre várias sessões vivas, a mais grave manda: bloqueado > trabalhando > concluído.
        var state = ActivityState.Unknown;
        string? blockedMessage = null;
        string? project = null;
        DateTime? lastActivity = null;

        foreach (var session in sessions)
        {
            var read = _transcripts.Read(session);
            if (read is null) continue;

            if (read.TimestampUtc > lastActivity || lastActivity is null)
                lastActivity = read.TimestampUtc;

            if (Severity(read.State) <= Severity(state)) continue;

            state = read.State;
            blockedMessage = read.Message;
            project = session.Name ?? ProjectNameOf(session.Cwd);
        }

        var live = sessions.Count > 0;

        // Nada rodando: mostra em que pé ficou a última conversa, com a luz atenuada.
        if (state == ActivityState.Unknown)
        {
            if (now - _idleScannedAt >= IdleScanInterval || _idleTranscript is null)
            {
                _idleTranscript = TranscriptReader.MostRecentTranscript();
                _idleScannedAt = now;
            }

            if (_idleTranscript is not null)
            {
                var read = _transcripts.ReadFile(_idleTranscript);
                if (read is not null)
                {
                    state = read.State;
                    blockedMessage = read.Message;
                    lastActivity ??= read.TimestampUtc;
                    project ??= ProjectNameOf(Path.GetDirectoryName(_idleTranscript) ?? "");
                }
            }
        }

        // 100% da janela de 5h é bloqueio, mesmo antes do primeiro 429 aparecer.
        if (_lastUsage?.FiveHour >= 100 && state != ActivityState.Working)
        {
            state = ActivityState.Blocked;
            blockedMessage ??= "Janela de 5 horas em 100%";
        }

        return new StatusSnapshot
        {
            State = state,
            LiveSession = live,
            ActiveSessions = sessions.Count,
            ProjectName = project,
            BlockedMessage = blockedMessage,
            LastActivityUtc = lastActivity,
            SessionUsage = _lastUsage?.FiveHour,
            WeeklyUsage = _lastUsage?.SevenDay,
            UsageSampledUtc = _lastUsage?.SampledUtc,
        };
    }

    private static int Severity(ActivityState state) => state switch
    {
        ActivityState.Blocked => 3,
        ActivityState.Working => 2,
        ActivityState.Done => 1,
        _ => 0,
    };

    private static string? ProjectNameOf(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public void Dispose() => _timer.Dispose();
}
