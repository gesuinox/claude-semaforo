# Claude Semáforo

Barra fina para Windows que fica sempre visível mostrando o que o Claude Code está
fazendo agora e quanto da janela de uso já foi gasta.

```
●○○  Bloqueado   · resets 11:50pm    (100)   ← limite atingido
○●○  Trabalhando · civilcalc         ( 48)   ← turno em andamento (a luz pulsa)
○○●  Concluído   · civilcalc         ( 72)   ← o turno terminou
```

O anel da direita é o **uso da janela de 5 horas** — o mesmo número que o `/usage`
mostra. Passe o mouse sobre a barra para ver o uso semanal, quantas sessões estão
rodando e quando a medida foi feita.

## De onde vêm os dados

Tudo é lido de arquivos locais. O app **não faz nenhuma chamada de rede**, não usa
credencial e não escreve nada dentro de `~/.claude`.

| Sinal | Arquivo | O que é lido |
|---|---|---|
| Uso do plano | `%APPDATA%\Claude\plan-usage-history.json` | Última amostra `{"u":{"fh":48,"sd":50}}` — `fh` é a janela de 5 h, `sd` são os 7 dias |
| Sessões rodando | `~/.claude/sessions/<pid>.json` | `pid`, `sessionId` e `cwd`; cada pid é confirmado contra o processo vivo |
| Estado do turno | `~/.claude/projects/**/<sessionId>.jsonl` | Última entrada: `stop_reason` `tool_use` → trabalhando, `end_turn` → concluído |
| Bloqueio | o mesmo `.jsonl` | `error: rate_limit` / `apiErrorStatus: 429`, cujo texto traz a hora do reset |

As transcrições passam de 40 MB, então só os últimos 96 KB de cada arquivo são lidos,
e só quando a data de modificação muda.

## Limitações conhecidas

- **O anel congela com o Claude Desktop fechado.** Quem grava `plan-usage-history.json`
  é o app do Claude, a cada poucos minutos. Com ele fechado o número para no tempo — a
  barra apaga o anel e o tooltip avisa há quanto tempo a medida está velha.
- **Não dá para embutir o widget na barra de tarefas do Windows.** A API de DeskBand foi
  descontinuada pela Microsoft. A barra é uma janela sem borda sempre no topo, que se
  arrasta com o mouse e gruda onde você largar.
- Quando nenhuma sessão está rodando, a luz continua acesa porém **fraca**: ela mostra
  como terminou a última conversa.

## Uso

Arraste a barra com o botão esquerdo. O botão direito (ou o ícone na bandeja) abre o
menu: sempre no topo, iniciar com o Windows, levar para o canto, atualizar agora e sair.
A posição é guardada em `%APPDATA%\ClaudeSemaforo\settings.json`.

## Compilar

Precisa do SDK do .NET 10.

```bash
dotnet build src/ClaudeSemaforo/ClaudeSemaforo.csproj -c Release
```

O executável **não** sai dentro do repositório. Como o projeto fica no Google Drive, que
mantém os arquivos mapeados e faz o `CreateAppHost` falhar ao gravar o `.exe`, o
`Directory.Build.props` manda `obj/` e `bin/` para um caminho local:

```
%LOCALAPPDATA%\ClaudeSemaforo-build\bin\ClaudeSemaforo\Release\net10.0-windows\ClaudeSemaforo.exe
```

Para conferir o visual dos quatro estados sem esperar cada situação acontecer:

```bash
ClaudeSemaforo.exe --demo
```

## Estrutura

```
src/ClaudeSemaforo/
  Core/    ClaudePaths · UsageReader · SessionScanner · TranscriptReader · StatusMonitor
  Ui/      StatusBarForm · Palette · AppSettings · Native
```

`Core/` não depende de nada da interface além do `Timer` do WinForms usado pelo
`StatusMonitor`, e é a camada onde mora toda a leitura dos arquivos do Claude.
