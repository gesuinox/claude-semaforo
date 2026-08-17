namespace ClaudeSemaforo.Core;

public enum ActivityState
{
    /// <summary>Não deu para classificar (sem transcrição legível).</summary>
    Unknown,

    /// <summary>Amarelo: há um turno em andamento — ferramenta rodando ou resposta sendo gerada.</summary>
    Working,

    /// <summary>
    /// Vermelho piscando: o Claude parou e espera o usuário — autorização de ferramenta,
    /// uma pergunta ou o prompt ocioso. Vem dos hooks, não da transcrição.
    /// </summary>
    Waiting,

    /// <summary>Verde: o último turno terminou (end_turn).</summary>
    Done,

    /// <summary>Vermelho: 429/rate_limit na transcrição, ou a janela de 5h em 100%.</summary>
    Blocked,
}

public enum UsageFreshness
{
    /// <summary>Nunca houve amostra: o Claude Desktop não gravou o histórico.</summary>
    Missing,

    Fresh,

    /// <summary>Velha o bastante para o número já ter subido sem a barra saber.</summary>
    Stale,

    /// <summary>Mais velha que a própria janela de 5 horas: não diz mais nada.</summary>
    Expired,
}

public sealed record StatusSnapshot
{
    public ActivityState State { get; init; } = ActivityState.Unknown;

    /// <summary>Falso quando nenhum processo do Claude Code está vivo — a luz acende atenuada.</summary>
    public bool LiveSession { get; init; }

    public int ActiveSessions { get; init; }

    /// <summary>Nome da pasta do projeto — é o que aparece na barra.</summary>
    public string? ProjectName { get; init; }

    /// <summary>
    /// Apelido que o próprio Claude Code dá à sessão ("civilcalc-4f"): a pasta mais um
    /// sufixo que separa sessões simultâneas. Fica só no tooltip.
    /// </summary>
    public string? SessionName { get; init; }

    /// <summary>Texto do bloqueio, com a hora do reset quando o Claude a informa.</summary>
    public string? BlockedMessage { get; init; }

    public DateTime? LastActivityUtc { get; init; }

    /// <summary>Uso da janela de 5 horas, 0–100. Nulo se o histórico do Desktop não existe.</summary>
    public int? SessionUsage { get; init; }

    /// <summary>Uso dos 7 dias, 0–100.</summary>
    public int? WeeklyUsage { get; init; }

    /// <summary>Quando o Claude coletou a amostra. A gravação é irregular: pode ficar horas parada.</summary>
    public DateTime? UsageSampledUtc { get; init; }

    public TimeSpan? UsageAge =>
        UsageSampledUtc is { } at ? DateTime.UtcNow - at : null;

    /// <summary>
    /// Passadas 5 horas a janela de uso já virou e o número não descreve mais nada; antes
    /// disso ele ainda serve como piso, mas precisa aparecer como velho.
    /// </summary>
    public UsageFreshness Freshness => UsageAge switch
    {
        null => UsageFreshness.Missing,
        { TotalHours: >= 5 } => UsageFreshness.Expired,
        { TotalMinutes: >= 20 } => UsageFreshness.Stale,
        _ => UsageFreshness.Fresh,
    };

    /// <summary>O <c>notification_type</c> que levantou o alerta, quando há um.</summary>
    public string? WaitingKind { get; init; }

    /// <summary>Falso quando os hooks não estão registrados: aí o alerta nunca acende.</summary>
    public bool AlertsConfigured { get; init; }

    public string StateLabel => State switch
    {
        ActivityState.Working => "Trabalhando",
        ActivityState.Waiting => WaitingKind switch
        {
            "permission_prompt" => "Esperando autorização",
            "elicitation_dialog" or "agent_needs_input" => "Esperando resposta",
            "idle_prompt" => "Parado esperando você",
            _ => "Precisa de você",
        },
        ActivityState.Done => "Concluído",
        ActivityState.Blocked => "Bloqueado",
        _ => "Sem sessão",
    };
}
