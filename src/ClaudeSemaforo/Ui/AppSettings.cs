using System.Text.Json;
using ClaudeSemaforo.Core;
using Microsoft.Win32;

namespace ClaudeSemaforo.Ui;

internal sealed class AppSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ClaudeSemaforo";

    /// <summary>Nulo na primeira execução: aí a barra vai sozinha para o canto.</summary>
    public int? X { get; set; }

    public int? Y { get; set; }
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Fixada: o arrasto é ignorado, para não sair do lugar sem querer.</summary>
    public bool Locked { get; set; }

    /// <summary>Id do tema: "claude", "dark" ou "light".</summary>
    public string Theme { get; set; } = Ui.Theme.Dark.Id;

    public static AppSettings Load()
    {
        try
        {
            var path = ClaudePaths.SettingsFile;
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var path = ClaudePaths.SettingsFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Preferência de janela não vale travar o app.
        }
    }

    public static bool AutoStartEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is not null;
        }
    }

    public static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe is not null) key.SetValue(RunValue, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            // Sem permissão de escrita no registro: o menu volta ao estado real na próxima abertura.
        }
    }
}
