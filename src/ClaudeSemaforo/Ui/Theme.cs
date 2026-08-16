using System.Drawing;

namespace ClaudeSemaforo.Ui;

/// <summary>
/// Paleta da barra. As três luzes mantêm vermelho/amarelo/verde em todos os temas — num
/// semáforo a cor é o significado —, mas os tons são ajustados para o fundo de cada um.
/// </summary>
internal sealed record Theme(
    string Id,
    string Label,
    Color Background,
    Color Border,
    Color Track,
    Color LightOff,
    Color Text,
    Color TextDim,
    Color Red,
    Color Amber,
    Color Green)
{
    /// <summary>Creme e laranja da identidade do Claude; "trabalhando" acende no laranja da marca.</summary>
    public static readonly Theme Claude = new(
        Id: "claude",
        Label: "Claude (laranja)",
        Background: Color.FromArgb(240, 238, 230),
        Border: Color.FromArgb(217, 119, 87),
        Track: Color.FromArgb(222, 216, 202),
        LightOff: Color.FromArgb(214, 208, 193),
        Text: Color.FromArgb(61, 61, 58),
        TextDim: Color.FromArgb(124, 119, 108),
        // Vermelho puxado para o frio: no tema Claude o "trabalhando" já é laranja,
        // e um vermelho alaranjado deixaria as duas luzes parecidas demais.
        Red: Color.FromArgb(166, 36, 42),
        Amber: Color.FromArgb(217, 119, 87),
        Green: Color.FromArgb(47, 133, 90));

    public static readonly Theme Dark = new(
        Id: "dark",
        Label: "Escuro",
        Background: Color.FromArgb(22, 22, 26),
        Border: Color.FromArgb(48, 48, 56),
        Track: Color.FromArgb(46, 46, 54),
        LightOff: Color.FromArgb(38, 38, 45),
        Text: Color.FromArgb(228, 228, 231),
        TextDim: Color.FromArgb(138, 138, 148),
        Red: Color.FromArgb(229, 72, 77),
        Amber: Color.FromArgb(245, 165, 36),
        Green: Color.FromArgb(48, 164, 108));

    public static readonly Theme Light = new(
        Id: "light",
        Label: "Claro",
        Background: Color.FromArgb(255, 255, 255),
        Border: Color.FromArgb(213, 213, 219),
        Track: Color.FromArgb(230, 230, 236),
        LightOff: Color.FromArgb(221, 221, 227),
        Text: Color.FromArgb(29, 29, 31),
        TextDim: Color.FromArgb(108, 108, 118),
        Red: Color.FromArgb(203, 46, 51),
        Amber: Color.FromArgb(191, 126, 12),
        Green: Color.FromArgb(30, 132, 84));

    public static readonly Theme[] All = [Claude, Dark, Light];

    public static Theme ById(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? Dark;

    public Color ForUsage(int percent) => percent switch
    {
        >= 85 => Red,
        >= 60 => Amber,
        _ => Green,
    };

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
