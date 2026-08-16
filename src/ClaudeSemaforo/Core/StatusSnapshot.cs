namespace ClaudeSemaforo.Core;

public enum ActivityState
{
    /// <summary>Não deu para classificar (sem transcrição legível).</summary>
    Unknown,

    /// <summary>Amarelo: há um turno em andamento — ferramenta rodando ou resposta sendo gerada.</summary>
    Working,

    /// <summary>Verde: o último turno terminou (end_turn).</summary>
    Done,

    /// <summary>Vermelho: 429/rate_limit na transcrição, ou a janela de 5h em 100%.</summary>
    Blocked,
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

    /// <summary>Quando o Desktop coletou a amostra — ele só grava enquanto está aberto.</summary>
    public DateTime? UsageSampledUtc { get; init; }

    public string StateLabel => State switch
    {
        ActivityState.Working => "Trabalhando",
        ActivityState.Done => "Concluído",
        ActivityState.Blocked => "Bloqueado",
        _ => "Sem sessão",
    };
}
