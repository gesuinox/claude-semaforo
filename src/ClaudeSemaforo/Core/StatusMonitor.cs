using Timer = System.Windows.Forms.Timer;

namespace ClaudeSemaforo.Core;

/// <summary>
/// Junta as três fontes num único <see cref="StatusSnapshot"/> e avisa quando ele muda.
/// Roda na thread da UI: cada tique lê ~100 KB de cauda de arquivo, nada que justifique
/// uma thread própria.
/// </summary>
public sealed class StatusMonitor : IDisposable
{
    // Curto de propósito: quando o Claude enfim grava a medida, a barra mostra o número
    // novo em segundos. A leitura é barata — o UsageReader só abre o arquivo se o
    // horário de modificação mudou.
    private static readonly TimeSpan UsageInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleScanInterval = TimeSpan.FromSeconds(30);

    private readonly Timer _timer = new() { Interval = 1000 };
    private readonly UsageReader _usage = new();
    private readonly TranscriptReader _transcripts = new();

    private FileSystemWatcher? _usageWatcher;
    private volatile bool _usageDirty = true;
    private UsageSample? _lastUsage;
    private DateTime _usageCheckedAt = DateTime.MinValue;
    private string? _idleTranscript;
    private DateTime _idleScannedAt = DateTime.MinValue;
    private bool _alertsConfigured;
    private DateTime _hooksCheckedAt = DateTime.MinValue;

    public event Action<StatusSnapshot>? Updated;

    public StatusSnapshot Current { get; private set; } = new();

    public StatusMonitor()
    {
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        WatchUsageFile();
        Refresh();
        _timer.Start();
    }

    /// <summary>
    /// Avisa no instante em que o Claude regrava o histórico de uso, sem esperar o
    /// próximo tique. O polling continua como rede de segurança — o watcher pode perder
    /// eventos se o arquivo for substituído em vez de reescrito.
    /// </summary>
    private void WatchUsageFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(ClaudePaths.UsageHistoryFile);
            if (directory is null || !Directory.Exists(directory)) return;

            _usageWatcher = new FileSystemWatcher(directory, Path.GetFileName(ClaudePaths.UsageHistoryFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };

            _usageWatcher.Changed += (_, _) => _usageDirty = true;
            _usageWatcher.Created += (_, _) => _usageDirty = true;
            _usageWatcher.Renamed += (_, _) => _usageDirty = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Sem watcher o polling de 5 s ainda dá conta.
        }
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

        if (_usageDirty || now - _usageCheckedAt >= UsageInterval)
        {
            _usageDirty = false;
            _lastUsage = _usage.Read();
            _usageCheckedAt = now;
        }

        if (now - _hooksCheckedAt >= IdleScanInterval)
        {
            _alertsConfigured = HookStatus.Configured();
            _hooksCheckedAt = now;
        }

        var sessions = SessionScanner.Scan();
        var alerts = AlertStore.Active();

        // Entre várias sessões vivas manda a mais grave:
        // bloqueado > esperando o usuário > trabalhando > concluído.
        var state = ActivityState.Unknown;
        string? blockedMessage = null;
        string? project = null;
        string? sessionName = null;
        string? waitingKind = null;
        DateTime? lastActivity = null;

        foreach (var session in sessions)
        {
            var read = _transcripts.Read(session);
            var alert = alerts.FirstOrDefault(a => a.SessionId == session.SessionId);

            // Rede de segurança: se a conversa andou depois do alerta, alguém já respondeu.
            // Sem isso, um hook de limpeza que falhe deixaria a luz piscando à toa.
            if (alert is not null && read?.TimestampUtc > alert.RaisedUtc)
            {
                AlertStore.Clear(alert.SessionId);
                alert = null;
            }

            // Um alerta pendente supera o que a transcrição diz — ela para no tool_use e
            // parece "trabalhando" justamente enquanto o Claude espera a autorização.
            var sessionState = read?.State == ActivityState.Blocked
                ? ActivityState.Blocked
                : alert is not null
                    ? ActivityState.Waiting
                    : read?.State ?? ActivityState.Unknown;

            if (sessionState == ActivityState.Unknown) continue;

            if (read?.TimestampUtc is { } ts && (lastActivity is null || ts > lastActivity))
                lastActivity = ts;

            if (Severity(sessionState) <= Severity(state)) continue;

            state = sessionState;
            waitingKind = alert?.Kind;
            blockedMessage = read?.Message;
            project = ProjectNameOf(session.Cwd) ?? session.Name;
            sessionName = session.Name;
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
            SessionName = sessionName,
            WaitingKind = waitingKind,
            AlertsConfigured = _alertsConfigured,
            BlockedMessage = blockedMessage,
            LastActivityUtc = lastActivity,
            SessionUsage = _lastUsage?.FiveHour,
            WeeklyUsage = _lastUsage?.SevenDay,
            UsageSampledUtc = _lastUsage?.SampledUtc,
            UsageAgeMinutes = _lastUsage is { } u
                ? Math.Max(0, (int)(now - u.SampledUtc).TotalMinutes)
                : null,
        };
    }

    /// <summary>
    /// Esperando o usuário vem antes de tudo, inclusive do limite atingido: com várias
    /// sessões abertas, a que precisa de uma confirmação é a única em que ele pode agir,
    /// e é a que trava o trabalho enquanto ninguém responde.
    /// </summary>
    private static int Severity(ActivityState state) => state switch
    {
        ActivityState.Waiting => 4,
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

    public void Dispose()
    {
        _timer.Dispose();
        _usageWatcher?.Dispose();
    }
}
