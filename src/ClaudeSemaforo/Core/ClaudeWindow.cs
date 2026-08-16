using System.Diagnostics;
using ClaudeSemaforo.Ui;

namespace ClaudeSemaforo.Core;

/// <summary>
/// Traz a janela do Claude para a frente. Vários processos se chamam "claude" — as próprias
/// sessões do Claude Code são um deles —, mas só o app do Desktop tem janela principal.
/// </summary>
public static class ClaudeWindow
{
    public static bool Activate()
    {
        var handle = Find();
        if (handle == IntPtr.Zero) return false;

        if (Native.IsIconic(handle)) Native.ShowWindow(handle, Native.SW_RESTORE);

        // O Windows só deixa o processo que está em primeiro plano trocar o primeiro plano.
        // Grudar nossa fila de entrada na da janela ativa nos dá esse direito emprestado.
        var foreground = Native.GetForegroundWindow();
        var target = Native.GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var mine = Native.GetCurrentThreadId();
        var attached = target != 0 && target != mine && Native.AttachThreadInput(mine, target, true);

        try
        {
            Native.BringWindowToTop(handle);
            return Native.SetForegroundWindow(handle);
        }
        finally
        {
            if (attached) Native.AttachThreadInput(mine, target, false);
        }
    }

    private static IntPtr Find()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (!p.ProcessName.Contains("claude", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.ProcessName.Equals("ClaudeSemaforo", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Processo morreu no meio da varredura.
        }

        return IntPtr.Zero;
    }
}
