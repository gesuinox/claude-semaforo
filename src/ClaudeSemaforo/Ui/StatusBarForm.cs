using System.Drawing.Drawing2D;
using ClaudeSemaforo.Core;
using Timer = System.Windows.Forms.Timer;

namespace ClaudeSemaforo.Ui;

/// <summary>
/// A barra em si: sem borda, sempre no topo, com a altura de uma barra de ferramentas.
/// Três luzes à esquerda, o rótulo no meio e o anel de uso da janela de 5 horas à direita.
/// Arrasta com o botão esquerdo; o duplo clique chama a janela do Claude.
/// </summary>
internal sealed class StatusBarForm : Form
{
    private const int BaseWidth = 94;
    private const int BaseHeight = 30;
    private const int ScreenMargin = 12;

    private readonly StatusMonitor _monitor = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ToolTip _tip = new() { InitialDelay = 250, ReshowDelay = 100 };

    // Só anima quando está trabalhando; parado, a barra não consome CPU à toa.
    private readonly Timer _pulse = new() { Interval = 60 };

    private readonly Font _fontRing = new("Segoe UI Semibold", 7.5f);
    private readonly Font _fontRingSmall = new("Segoe UI Semibold", 6.5f);

    private Timer? _demo;
    private NotifyIcon _tray = null!;
    private ToolStripMenuItem _topMostItem = null!;
    private ToolStripMenuItem _autoStartItem = null!;
    private ToolStripMenuItem _lockItem = null!;
    private readonly List<ToolStripMenuItem> _themeItems = [];

    private Theme _theme;
    private StatusSnapshot _snapshot = new();
    private float _pulsePhase;
    private bool _pressed;
    private Point _pressOrigin;

    public StatusBarForm(bool demo = false)
    {
        _theme = Theme.ById(_settings.Theme);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = _theme.Background;
        DoubleBuffered = true;
        TopMost = _settings.AlwaysOnTop;
        Text = "Claude Semáforo";

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        ApplySize();
        RestorePosition();
        BuildMenu();

        _pulse.Tick += (_, _) =>
        {
            _pulsePhase += 0.06f;
            Invalidate();
        };

        if (demo)
        {
            StartDemo();
            return;
        }

        _monitor.Updated += OnStatus;
        _monitor.Start();
        OnStatus(_monitor.Current);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    // ---- estado ----------------------------------------------------------

    private void OnStatus(StatusSnapshot snapshot)
    {
        _snapshot = snapshot;

        // Só há o que animar trabalhando (halo) ou esperando (piscar).
        var animated = snapshot.LiveSession
            && snapshot.State is ActivityState.Working or ActivityState.Waiting;

        if (animated && !_pulse.Enabled) _pulse.Start();
        else if (!animated && _pulse.Enabled) { _pulse.Stop(); _pulsePhase = 0; }

        _tip.SetToolTip(this, BuildTooltip(snapshot));
        UpdateTrayText(snapshot);
        Invalidate();
    }

    private Color ColorOf(ActivityState state) => state switch
    {
        ActivityState.Blocked or ActivityState.Waiting => Theme.LightRed,
        ActivityState.Working => Theme.LightAmber,
        ActivityState.Done => Theme.LightGreen,
        _ => _theme.LightOff,
    };

    /// <summary>Meio segundo aceso, meio apagado — pelo relógio, para não depender do timer.</summary>
    private static bool BlinkOn() => Environment.TickCount64 / 450 % 2 == 0;

    private string BuildTooltip(StatusSnapshot s)
    {
        var lines = new List<string> { s.StateLabel + (s.ProjectName is null ? "" : $" · {s.ProjectName}") };

        if (s.BlockedMessage is not null) lines.Add(s.BlockedMessage);
        if (s.SessionName is not null) lines.Add($"Sessão: {s.SessionName}");

        if (!s.AlertsConfigured)
            lines.Add("Alerta desligado: os hooks do Claude Code não estão registrados");

        lines.Add(s.ActiveSessions switch
        {
            0 => "Nenhuma sessão do Claude Code rodando",
            1 => "1 sessão ativa",
            _ => $"{s.ActiveSessions} sessões ativas",
        });

        if (s.SessionUsage is { } fh && s.Freshness != UsageFreshness.Missing)
        {
            var idade = s.UsageAge is { } age ? FormatAge(age) : "?";
            var quando = s.UsageSampledLocal?.ToString("dd/MM HH:mm") ?? "?";

            lines.Add(s.Freshness switch
            {
                UsageFreshness.Fresh => $"Uso da sessão (5h): {fh}% · medido há {idade}",
                UsageFreshness.Stale => $"Uso da sessão (5h): pelo menos {fh}% · medido às {quando}",
                _ => $"Uso da sessão (5h): sem medida — a última foi às {quando}",
            });

            if (s.WeeklyUsage is { } sd)
                lines.Add(s.Freshness == UsageFreshness.Fresh
                    ? $"Uso semanal (7d): {sd}%"
                    : $"Uso semanal (7d): pelo menos {sd}%");

            // Quem mede é o Claude, não esta barra: passado tanto tempo, o que resolve é
            // reiniciar o app do Claude, e não mexer aqui.
            if (s.Freshness == UsageFreshness.Expired)
                lines.Add($"O Claude parou de medir há {idade} — reinicie o app do Claude"
                    + " para voltar a medir");
            else if (s.Freshness == UsageFreshness.Stale)
                lines.Add("O Claude mede de tempos em tempos; o número exato sai no /usage");
        }
        else
        {
            lines.Add("Uso indisponível: abra o Claude Desktop ao menos uma vez");
        }

        lines.Add(_settings.Locked
            ? "Duplo clique abre o Claude · fixada na tela"
            : "Duplo clique abre o Claude · arraste para mover");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatAge(TimeSpan age) =>
        age.TotalHours >= 1 ? $"{(int)age.TotalHours} h" : $"{(int)age.TotalMinutes} min";

    // ---- desenho ---------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float w = ClientSize.Width;
        float h = ClientSize.Height;
        var pad = h * 0.30f;
        var lightD = h * 0.30f;
        var gap = h * 0.13f;
        var ringD = h * 0.72f;

        using (var border = new Pen(_theme.Border))
        using (var path = RoundedRect(new RectangleF(0.5f, 0.5f, w - 1, h - 1), h * 0.22f))
            g.DrawPath(border, path);

        DrawTrafficLight(g, pad, (h - lightD) / 2f, lightD, gap);

        // O anel fica encostado na direita; a folga que sobra separa o uso dos sinaleiros.
        var ringX = w - pad - ringD;
        DrawUsageRing(g, new RectangleF(ringX, (h - ringD) / 2f, ringD, ringD), Math.Max(2f, h * 0.10f));
    }

    private void DrawTrafficLight(Graphics g, float x, float y, float d, float gap)
    {
        var active = _snapshot.State;
        var live = _snapshot.LiveSession;
        var waiting = active == ActivityState.Waiting;

        // Esperando o usuário acende a mesma luz do bloqueio, mas piscando.
        var litState = waiting ? ActivityState.Blocked : active;

        // Ordem de semáforo de rua: vermelho, amarelo, verde.
        ReadOnlySpan<ActivityState> order =
            [ActivityState.Blocked, ActivityState.Working, ActivityState.Done];

        for (var i = 0; i < order.Length; i++)
        {
            var cx = x + i * (d + gap);
            var lit = order[i] == litState && !(waiting && !BlinkOn());
            var color = ColorOf(order[i]);

            if (!lit)
            {
                using var off = new SolidBrush(_theme.LightOff);
                g.FillEllipse(off, cx, y, d, d);
                continue;
            }

            // Quem pulsa é o halo, não o disco: assim a luz mantém o tom exato do semáforo.
            // Esperando, o halo fica firme — quem chama atenção ali é o piscar.
            var glowAlpha = live ? 55 : 25;
            if (_pulse.Enabled && !waiting)
                glowAlpha = (int)(26 + 52 * (0.5f + 0.5f * (float)Math.Sin(_pulsePhase)));

            using (var glow = new SolidBrush(Color.FromArgb(glowAlpha, color)))
                g.FillEllipse(glow, cx - d * 0.35f, y - d * 0.35f, d * 1.7f, d * 1.7f);

            // Sem processo vivo, a luz fica acesa mas fraca: é memória do último turno.
            using (var brush = new SolidBrush(live ? color : _theme.Fade(color, 0.45f)))
                g.FillEllipse(brush, cx, y, d, d);

            // Aro fino: sem ele o amarelo e o verde-limão se perdem no fundo branco.
            using var rim = new Pen(_theme.Border);
            g.DrawEllipse(rim, cx, y, d, d);
        }
    }

    private void DrawUsageRing(Graphics g, RectangleF rect, float thickness)
    {
        using (var track = new Pen(_theme.Track, thickness))
            g.DrawEllipse(track, rect);

        var freshness = _snapshot.Freshness;

        // O traço fica só para quando nunca houve medida. Havendo uma, ela aparece mesmo
        // velha: um número marcado como velho informa mais que um anel vazio.
        if (_snapshot.SessionUsage is not { } percent || freshness == UsageFreshness.Missing)
        {
            using var unknown = new SolidBrush(_theme.TextDim);
            using var fmt = Centered();
            g.DrawString("–", _fontRing, unknown, rect, fmt);
            return;
        }

        var stale = freshness != UsageFreshness.Fresh;
        var color = stale ? _theme.Fade(_theme.ForUsage(percent), 0.55f) : _theme.ForUsage(percent);

        if (percent > 0)
        {
            // Arco pontilhado quando a medida está velha: o número é um piso, não o valor de agora.
            using var pen = new Pen(color, thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                DashStyle = stale ? DashStyle.Dot : DashStyle.Solid,
            };
            g.DrawArc(pen, rect, -90f, 360f * Math.Clamp(percent, 0, 100) / 100f);
        }

        using var text = new SolidBrush(stale ? _theme.TextDim : _theme.Text);
        using var format = Centered();
        g.DrawString(
            percent >= 100 ? "100" : percent.ToString(),
            percent >= 100 ? _fontRingSmall : _fontRing,
            text, rect, format);

        static StringFormat Centered() => new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ---- janela ----------------------------------------------------------

    private void ApplySize()
    {
        ClientSize = new Size(LogicalToDeviceUnits(BaseWidth), LogicalToDeviceUnits(BaseHeight));
        UpdateShape();
    }

    private void UpdateShape()
    {
        using var path = RoundedRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height),
            ClientSize.Height * 0.22f);
        Region = new Region(path);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplySize();
    }

    private void RestorePosition()
    {
        if (_settings is { X: { } x, Y: { } y }
            && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(
                new Rectangle(x, y, Width, Height))))
        {
            Location = new Point(x, y);
            ClampToScreen();
            return;
        }

        MoveToCorner();
    }

    /// <summary>
    /// Mantém a barra dentro da área útil da tela em que ela está: sem isso, um arrasto
    /// para baixo a esconde atrás da barra de tarefas e ela parece ter sumido.
    /// </summary>
    private void ClampToScreen()
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        var x = Math.Clamp(Location.X, area.Left, Math.Max(area.Left, area.Right - Width));
        var y = Math.Clamp(Location.Y, area.Top, Math.Max(area.Top, area.Bottom - Height));

        if (x != Location.X || y != Location.Y) Location = new Point(x, y);
    }

    private void MoveToCorner()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - Width - ScreenMargin, area.Top + ScreenMargin);
    }

    private void SavePosition()
    {
        _settings.X = Location.X;
        _settings.Y = Location.Y;
        _settings.Save();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _settings.Locked) return;

        _pressed = true;
        _pressOrigin = e.Location;
    }

    /// <summary>
    /// O arrasto só começa depois que o mouse anda um pouco. Disparar no clique jogaria a
    /// janela num laço modal de movimentação, e o segundo clique do duplo clique se perderia.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_pressed) return;

        var drag = SystemInformation.DragSize;
        if (Math.Abs(e.X - _pressOrigin.X) < drag.Width
            && Math.Abs(e.Y - _pressOrigin.Y) < drag.Height) return;

        _pressed = false;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) ClaudeWindow.Activate();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // Fim do arrasto: prende a barra à tela e guarda onde ela ficou.
        if (m.Msg == Native.WM_EXITSIZEMOVE)
        {
            ClampToScreen();
            SavePosition();
        }
    }

    // ---- menu e bandeja --------------------------------------------------

    private void BuildMenu()
    {
        _topMostItem = new ToolStripMenuItem("Sempre no topo") { Checked = TopMost, CheckOnClick = true };
        _topMostItem.CheckedChanged += (_, _) =>
        {
            TopMost = _topMostItem.Checked;
            _settings.AlwaysOnTop = TopMost;
            _settings.Save();
        };

        _autoStartItem = new ToolStripMenuItem("Iniciar com o Windows")
        {
            Checked = AppSettings.AutoStartEnabled,
            CheckOnClick = true,
        };
        _autoStartItem.CheckedChanged += (_, _) => AppSettings.SetAutoStart(_autoStartItem.Checked);

        var colors = new ToolStripMenuItem("Cor");
        foreach (var theme in Theme.All)
        {
            var item = new ToolStripMenuItem(theme.Label) { Checked = theme.Id == _theme.Id };
            item.Click += (_, _) => ApplyTheme(theme);
            _themeItems.Add(item);
            colors.DropDownItems.Add(item);
        }

        _lockItem = new ToolStripMenuItem("Fixar na tela")
        {
            Checked = _settings.Locked,
            CheckOnClick = true,
        };
        _lockItem.CheckedChanged += (_, _) =>
        {
            _settings.Locked = _lockItem.Checked;
            _settings.Save();
            _pressed = false;
            _tip.SetToolTip(this, BuildTooltip(_snapshot));
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir o Claude", null, (_, _) => ClaudeWindow.Activate());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(colors);
        menu.Items.Add(_lockItem);
        menu.Items.Add(_topMostItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Levar para o canto", null, (_, _) => { MoveToCorner(); SavePosition(); });
        menu.Items.Add("Atualizar agora", null, (_, _) => _monitor.Refresh());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Close());

        ContextMenuStrip = menu;

        Icon = LoadAppIcon(SystemInformation.IconSize);

        _tray = new NotifyIcon
        {
            Text = "Claude Semáforo",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = LoadAppIcon(SystemInformation.SmallIconSize),
        };
        _tray.DoubleClick += (_, _) =>
        {
            Visible = !Visible;
            if (Visible) BringToFront();
        };

        // Sem isso, uma saída que não passe pelo fechamento da janela deixa o ícone
        // órfão na bandeja — ele só some quando o usuário passa o mouse por cima.
        Application.ApplicationExit += RemoveTrayIcon;
        AppDomain.CurrentDomain.ProcessExit += RemoveTrayIcon;
    }

    private void RemoveTrayIcon(object? sender, EventArgs e)
    {
        try
        {
            _tray.Visible = false;
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ApplyTheme(Theme theme)
    {
        _theme = theme;
        _settings.Theme = theme.Id;
        _settings.Save();

        foreach (var item in _themeItems) item.Checked = item.Text == theme.Label;

        BackColor = theme.Background;
        Invalidate();
    }

    /// <summary>
    /// O ícone da bandeja nunca muda — é sempre o do app. Só o texto acompanha o estado.
    /// Trocar a imagem a cada mudança é o que espalhava cópias na bandeja quando o
    /// processo era encerrado à força entre uma troca e outra.
    /// </summary>
    private void UpdateTrayText(StatusSnapshot s) =>
        _tray.Text = Truncate($"Claude: {s.StateLabel}"
            + (s.SessionUsage is { } fh ? $" · {fh}% da sessão" : ""), 63);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    /// <summary>Carrega o app.ico embutido no tamanho que o Windows usa na bandeja.</summary>
    private static Icon LoadAppIcon(Size size)
    {
        try
        {
            var assembly = typeof(StatusBarForm).Assembly;
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));

            if (name is not null)
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is not null) return new Icon(stream, size);
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    // ---- demonstração ----------------------------------------------------

    private void StartDemo()
    {
        StatusSnapshot[] samples =
        [
            new()
            {
                State = ActivityState.Working, LiveSession = true, ActiveSessions = 2,
                ProjectName = "civilcalc", SessionUsage = 48, WeeklyUsage = 50,
                UsageSampledUtc = DateTime.UtcNow, UsageAgeMinutes = 0,
            },
            new()
            {
                State = ActivityState.Done, LiveSession = true, ActiveSessions = 1,
                ProjectName = "civilcalc", SessionUsage = 72, WeeklyUsage = 50,
                UsageSampledUtc = DateTime.UtcNow, UsageAgeMinutes = 0,
            },
            new()
            {
                State = ActivityState.Waiting, LiveSession = true, ActiveSessions = 1,
                ProjectName = "civilcalc", SessionUsage = 83, WeeklyUsage = 55,
                UsageSampledUtc = DateTime.UtcNow, UsageAgeMinutes = 0, WaitingKind = "permission_prompt",
            },
            new()
            {
                State = ActivityState.Blocked, LiveSession = true, ActiveSessions = 1,
                ProjectName = "civilcalc", SessionUsage = 100, WeeklyUsage = 61,
                UsageSampledUtc = DateTime.UtcNow, UsageAgeMinutes = 0,
                BlockedMessage = "You've hit your session limit · resets 11:50pm (America/Sao_Paulo)",
            },
            new() { State = ActivityState.Done, SessionUsage = 12, WeeklyUsage = 50, UsageSampledUtc = DateTime.UtcNow },
        ];

        var index = 0;
        _demo = new Timer { Interval = 2500 };
        _demo.Tick += (_, _) => OnStatus(samples[++index % samples.Length]);
        _demo.Start();
        OnStatus(samples[0]);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SavePosition();
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.Dispose();
            _demo?.Dispose();
            _pulse.Dispose();
            _tip.Dispose();
            _fontRing.Dispose();
            _fontRingSmall.Dispose();
            _tray.Icon?.Dispose();
            _tray.Dispose();
            Icon?.Dispose();
        }

        base.Dispose(disposing);
    }
}
