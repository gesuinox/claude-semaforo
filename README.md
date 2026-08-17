# Claude Semáforo

Barra fina para Windows que fica sempre visível mostrando o que o Claude Code está
fazendo agora e quanto da janela de uso já foi gasta.

A barra tem 94 × 30 px e nenhum texto além do número do uso:

```
●○○  (100)   ← vermelho fixo: limite atingido
◐○○  ( 83)   ← vermelho piscando: o Claude espera você (autorização ou resposta)
○●○  ( 48)   ← amarelo: turno em andamento (o halo pulsa)
○○●  ( 72)   ← verde: o turno terminou
```

O anel da direita é o **uso da janela de 5 horas** — o mesmo número que o `/usage`
mostra. Passe o mouse sobre a barra para ver o estado por extenso, o projeto, o uso
semanal, quantas sessões estão rodando e quando a medida foi feita.

## De onde vêm os dados

Tudo é lido de arquivos locais. O app **não faz nenhuma chamada de rede**, não usa
credencial e não escreve nada dentro de `~/.claude`.

| Sinal | Arquivo | O que é lido |
|---|---|---|
| Uso do plano | `%APPDATA%\Claude\plan-usage-history.json` | Última amostra `{"u":{"fh":48,"sd":50}}` — `fh` é a janela de 5 h, `sd` são os 7 dias |
| Sessões rodando | `~/.claude/sessions/<pid>.json` | `pid`, `sessionId` e `cwd`; cada pid é confirmado contra o processo vivo |
| Estado do turno | `~/.claude/projects/**/<sessionId>.jsonl` | Última entrada: `stop_reason` `tool_use` → trabalhando, `end_turn` → concluído |
| Bloqueio | o mesmo `.jsonl` | `error: rate_limit` / `apiErrorStatus: 429`, cujo texto traz a hora do reset |
| Alerta | `%APPDATA%\ClaudeSemaforo\alerts\<sessão>.json` | escrito pelos hooks do Claude Code (veja abaixo) |

As transcrições passam de 40 MB, então só os últimos 96 KB de cada arquivo são lidos,
e só quando a data de modificação muda.

## Limitações conhecidas

- **O anel depende de uma medida que o Claude grava quando quer.** Quem escreve o
  `plan-usage-history.json` é o app do Claude, consultando `/api/organizations/<org>/usage`
  a cada ~15 min. Essa consulta falha (503) ou simplesmente para com alguma frequência —
  observei o histórico ficar 3 horas parado com o app aberto e em uso. Por isso a barra
  nunca finge que o número é de agora:

  | Idade da medida | O que a barra faz |
  |---|---|
  | até 20 min | anel sólido, número normal |
  | 20 min a 5 h | anel **pontilhado**, número esmaecido, tooltip diz "pelo menos X%" |
  | mais de 5 h | anel vazio com `–`: a janela de 5 h já virou e o número não diz mais nada |

  No instante em que a medida volta a ser gravada, a barra mostra o número novo: além do
  polling de 5 s, um `FileSystemWatcher` avisa na hora que o arquivo mudou. Medido: três
  segundos entre a gravação e o anel voltar a sólido.
- **Não dá para embutir o widget na barra de tarefas do Windows.** A API de DeskBand foi
  descontinuada pela Microsoft. A barra é uma janela sem borda sempre no topo, que se
  arrasta com o mouse e gruda onde você largar.
- Quando nenhuma sessão está rodando, a luz continua acesa porém **fraca**: ela mostra
  como terminou a última conversa.
- **O ícone da bandeja é sempre o mesmo**, em qualquer estado — quem muda é só o texto que
  aparece ao passar o mouse. O estado se lê na barra, que é o ponto do app. Trocar a
  imagem do ícone a cada mudança era o que espalhava cópias na bandeja quando o processo
  era encerrado à força entre uma troca e outra.
- Cópias que **já** ficaram na bandeja de versões anteriores somem ao passar o mouse sobre
  elas: o shell confere que a janela dona não existe mais e limpa.

## Instalação

Baixe o `ClaudeSemaforo-1.0.0-setup.exe` e execute. Ele instala na pasta do usuário,
**sem pedir senha de administrador**, e oferece as opções de iniciar junto com o Windows e
criar atalho na área de trabalho. O executável é self-contained: não exige o .NET
instalado na máquina.

Para gerar o instalador a partir do código (precisa do SDK do .NET 10 e do
[Inno Setup 6](https://jrsoftware.org/isdl.php)):

```bash
powershell -ExecutionPolicy Bypass -File installer\publicar.ps1
```

## Uso

- **Duplo clique** traz a janela do Claude para a frente.
- **Arraste** com o botão esquerdo para mover — a barra nunca sai da área visível da tela.
- **Fixar na tela** trava a posição: com a opção ligada o arrasto é ignorado e a barra não
  sai do lugar sem querer.
- **Botão direito** (ou o ícone na bandeja) abre o menu: abrir o Claude, cor, fixar, sempre
  no topo, iniciar com o Windows, levar para o canto, atualizar agora e sair.

Tudo o que é texto vive no tooltip: o estado por extenso, a pasta do projeto, o apelido
que o Claude Code dá à sessão (`civilcalc-4f` — a pasta mais um sufixo que separa sessões
simultâneas), o uso semanal e a idade da medida.

Preferências e posição ficam em `%APPDATA%\ClaudeSemaforo\settings.json`.

### Alerta de "precisa de você"

A transcrição **não** distingue trabalhar de esperar: nos dois casos ela para num
`tool_use`, porque o Claude fica bloqueado no prompt de autorização sem escrever mais
nada. Quem sabe a diferença é o próprio Claude Code, pelo evento `Notification`.

Registre estes hooks em `~/.claude/settings.json` (ajuste o caminho se instalou em outro
lugar). O `Notification` levanta o alerta; os outros dois o baixam, cobrindo tanto o caso
de você responder quanto o de o turno terminar:

```json
{
  "hooks": {
    "Notification": [
      { "hooks": [ { "type": "command",
                     "command": "C:\\Users\\SEU-USUARIO\\AppData\\Local\\Programs\\Claude Semaforo\\ClaudeSemaforo.exe",
                     "args": ["--hook"], "async": true, "timeout": 5 } ] }
    ],
    "Stop":             [ { "hooks": [ { "type": "command", "command": "…\\ClaudeSemaforo.exe", "args": ["--hook"], "async": true, "timeout": 5 } ] } ],
    "UserPromptSubmit": [ { "hooks": [ { "type": "command", "command": "…\\ClaudeSemaforo.exe", "args": ["--hook"], "async": true, "timeout": 5 } ] } ]
  }
}
```

Os hooks não levam `matcher`: quem decide é o executável, que lê o `notification_type` do
stdin. Levantam alerta `permission_prompt`, `idle_prompt`, `elicitation_dialog` e
`agent_needs_input`; os demais tipos (`auth_success`, `agent_completed`,
`elicitation_complete`) baixam. Se um hook de limpeza falhar, o alerta expira sozinho em
3 horas.

Sem os hooks registrados o alerta simplesmente nunca acende — e o tooltip avisa disso.

### Cores

As três luzes têm tom fixo em qualquer tema — num semáforo a cor é o significado:

| Luz | Cor | Estado |
|---|---|---|
| Vermelho | `#D30000` | parado por limite |
| Amarelo | `#FFED29` | trabalhando |
| Verde | `#CEFF00` | concluído |

O tema muda o fundo, a borda e a trilha do anel:

| Tema | Fundo |
|---|---|
| **Claude (laranja)** | `#D97757`, o laranja da marca — é a cor chapada da tela de abertura do app |
| **Escuro** | `#16161A`, grafite neutro (padrão) |
| **Claro** | Branco com hairline cinza |

O anel de uso segue a mesma escala verde → amarelo → vermelho, mas por ser um traço de
poucos pixels ele é escurecido quando o fundo é claro; senão o amarelo e o verde-limão
sumiriam no branco. Em fundo escuro as cores ficam intactas.

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
