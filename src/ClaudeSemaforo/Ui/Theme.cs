using System.Drawing;

namespace ClaudeSemaforo.Ui;

/// <summary>
/// Paleta da barra. As três luzes têm cor fixa em todos os temas — num semáforo a cor é o
/// significado —; o tema muda o fundo, a borda e a trilha do anel.
/// </summary>
internal sealed record Theme(
    string Id,
    string Label,
    Color Background,
    Color Border,
    Color Track,
    Color LightOff,
    Color Text,
    Color TextDim)
{
    public static readonly Color LightRed = Color.FromArgb(0xD3, 0x00, 0x00);
    public static readonly Color LightAmber = Color.FromArgb(0xFF, 0xED, 0x29);
    public static readonly Color LightGreen = Color.FromArgb(0xCE, 0xFF, 0x00);

    /// <summary>
    /// #D97757 é o laranja da marca — é a cor chapada da tela de abertura do Claude,
    /// conferida no próprio app instalado.
    /// </summary>
    public static readonly Theme Claude = new(
        Id: "claude",
        Label: "Claude (laranja)",
        Background: Color.FromArgb(0xD9, 0x77, 0x57),
        Border: Color.FromArgb(0xA9, 0x52, 0x36),
        Track: Color.FromArgb(0xC2, 0x63, 0x44),
        LightOff: Color.FromArgb(0xC2, 0x67, 0x4A),
        Text: Color.FromArgb(0xFF, 0xFF, 0xFF),
        TextDim: Color.FromArgb(0xF3, 0xD9, 0xCE));

    public static readonly Theme Dark = new(
        Id: "dark",
        Label: "Escuro",
        Background: Color.FromArgb(22, 22, 26),
        Border: Color.FromArgb(48, 48, 56),
        Track: Color.FromArgb(46, 46, 54),
        LightOff: Color.FromArgb(38, 38, 45),
        Text: Color.FromArgb(228, 228, 231),
        TextDim: Color.FromArgb(138, 138, 148));

    public static readonly Theme Light = new(
        Id: "light",
        Label: "Claro",
        Background: Color.FromArgb(255, 255, 255),
        Border: Color.FromArgb(206, 206, 213),
        Track: Color.FromArgb(230, 230, 236),
        LightOff: Color.FromArgb(221, 221, 227),
        Text: Color.FromArgb(29, 29, 31),
        TextDim: Color.FromArgb(108, 108, 118));

    public static readonly Theme[] All = [Claude, Dark, Light];

    public static Theme ById(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? Dark;

    /// <summary>Cor do anel de uso: a mesma escala do semáforo, verde → amarelo → vermelho.</summary>
    public Color ForUsage(int percent) => EnsureContrast(percent switch
    {
        >= 85 => LightRed,
        >= 60 => LightAmber,
        _ => LightGreen,
    });

    private static float Luminance(Color c) =>
        (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;

    /// <summary>
    /// O anel é um traço de poucos pixels: o amarelo e o verde-limão sumiriam num fundo claro,
    /// então ali eles são escurecidos até se separarem do fundo. Em fundo escuro nada muda —
    /// as três cores já saltam, e clarear o vermelho só o deixaria rosa. As luzes, discos
    /// grandes, ficam sempre com o tom exato.
    /// </summary>
    private Color EnsureContrast(Color color)
    {
        var background = Luminance(Background);
        if (background <= 0.5f) return color;

        for (var i = 0; i < 8 && Math.Abs(Luminance(color) - background) < 0.34f; i++)
            color = Color.FromArgb(
                (int)(color.R * 0.86f), (int)(color.G * 0.86f), (int)(color.B * 0.86f));

        return color;
    }

    /// <summary>
    /// Atenua misturando com o fundo do tema, e não escurecendo: num tema claro,
    /// escurecer deixaria a luz mais forte em vez de mais fraca.
    /// </summary>
    public Color Fade(Color color, float amount)
    {
        var a = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(color.R * a + Background.R * (1 - a)),
            (int)(color.G * a + Background.G * (1 - a)),
            (int)(color.B * a + Background.B * (1 - a)));
    }
}
