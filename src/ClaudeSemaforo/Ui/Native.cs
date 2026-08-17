using System.Runtime.InteropServices;

namespace ClaudeSemaforo.Ui;

internal static partial class Native
{
    public const int WM_NCLBUTTONDOWN = 0x00A1;

    /// <summary>Chega quando o usuário solta a barra: hora de prender à tela e salvar.</summary>
    public const int WM_EXITSIZEMOVE = 0x0232;

    /// <summary>Faz o Windows arrastar a janela como se fosse pela barra de título.</summary>
    public const int HTCAPTION = 0x0002;

    /// <summary>Fora do Alt+Tab: a barra não é uma janela de aplicativo.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    public const int SW_RESTORE = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    /// <summary>
    /// Liga a fila de entrada de duas threads. É o que destrava o
    /// <see cref="SetForegroundWindow"/>, que o Windows recusa quando quem chama não é
    /// o processo em primeiro plano.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint from, uint to, [MarshalAs(UnmanagedType.Bool)] bool attach);
}
