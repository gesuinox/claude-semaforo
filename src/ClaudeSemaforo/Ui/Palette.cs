using System.Drawing;

namespace ClaudeSemaforo.Ui;

internal static class Palette
{
    public static readonly Color Background = Color.FromArgb(22, 22, 26);
    public static readonly Color Border = Color.FromArgb(48, 48, 56);
    public static readonly Color Track = Color.FromArgb(46, 46, 54);
    public static readonly Color LightOff = Color.FromArgb(38, 38, 45);

    public static readonly Color Text = Color.FromArgb(228, 228, 231);
    public static readonly Color TextDim = Color.FromArgb(138, 138, 148);

    public static readonly Color Red = Color.FromArgb(229, 72, 77);
    public static readonly Color Amber = Color.FromArgb(245, 165, 36);
    public static readonly Color Green = Color.FromArgb(48, 164, 108);

    /// <summary>Cor do anel de uso: sobe de verde a vermelho conforme a janela enche.</summary>
    public static Color ForUsage(int percent) => percent switch
    {
        >= 85 => Red,
        >= 60 => Amber,
        _ => Green,
    };

    public static Color Dim(Color color, float factor) => Color.FromArgb(
        (int)(color.R * factor), (int)(color.G * factor), (int)(color.B * factor));
}
