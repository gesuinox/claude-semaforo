using System.Runtime.InteropServices;

namespace ClaudeSemaforo.Ui;

internal static partial class Native
{
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTCAPTION = 0x0002;

    /// <summary>Chega quando o usuário solta a barra: hora de prender à tela e salvar.</summary>
    public const int WM_EXITSIZEMOVE = 0x0232;

    /// <summary>Fora do Alt+Tab: a barra não é uma janela de aplicativo.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr handle);
}
