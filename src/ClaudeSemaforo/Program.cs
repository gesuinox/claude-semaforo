using ClaudeSemaforo.Core;
using ClaudeSemaforo.Ui;

namespace ClaudeSemaforo;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Chamado pelo Claude Code como hook, com o evento em JSON no stdin. Não abre
        // janela: só levanta ou baixa o alerta da sessão e sai.
        if (args.Contains("--hook"))
        {
            HookHandler.Run();
            return;
        }

        // "--demo" cicla os três estados para conferir o visual sem esperar cada situação.
        var demo = args.Contains("--demo");

        // Uma instância só: uma segunda barra na tela não faria sentido.
        using var mutex = new Mutex(true, @"Local\ClaudeSemaforo.SingleInstance", out var first);
        if (!first && !demo) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new StatusBarForm(demo));
    }
}
