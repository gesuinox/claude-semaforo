using System.Drawing.Drawing2D;
using ClaudeSemaforo.Core;
using Timer = System.Windows.Forms.Timer;

namespace ClaudeSemaforo.Ui;

/// <summary>
/// A barra em si: sem borda, sempre no topo, com a altura de uma barra de ferramentas.
/// Três luzes à esquerda, o rótulo no meio e o anel de uso da janela de 5 horas à direita.
/// </summary>
internal sealed class StatusBarForm : Form
{
    private const int BaseWidth = 236;
    private const int BaseHeight = 30;
    private const int ScreenMargin = 12;

    private readonly StatusMonitor _monitor = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ToolTip _tip = new() { InitialDelay = 250, ReshowDelay = 100 };

    // Só anima quando está trabalhando; parado, a barra não consome CPU à toa.
    private readonly Timer _pulse = new() { Interval = 60 };

    private readonly Font _fontLabel = new("Segoe UI", 8.25f);
    private readonly Font _fontSecondary = new("Segoe UI", 7.5f);
    private readonly Font _fontRing = new("Segoe UI Semibold", 7.5f);
    private readonly Font _fontRingSmall = new("Segoe UI Semibold", 6.5f);

    private Timer? _demo;
    private NotifyIcon _tray = null!;
    private ToolStripMenuItem _topMostItem = null!;
    private ToolStripMenuItem _autoStartItem = null!;

    private StatusSnapshot _snapshot = new();
    private float _pulsePhase;

    public StatusBarForm(bool demo = false)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Palette.Background;
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

    private void StartDemo()
    {
        StatusSnapshot[] samples =
        [
            new()
            {
                State = ActivityState.Working, LiveSession = true, ActiveSessions = 2,
                ProjectName = "civilcalc", SessionUsage = 48, WeeklyUsage = 50,
                UsageSampledUtc = DateTime.UtcNow,
            },
            new()
            {
                State = ActivityState.Done, LiveSession = true, ActiveSessions = 1,
                ProjectName = "civilcalc", SessionUsage = 72, WeeklyUsage = 50,
                UsageSampledUtc = DateTime.UtcNow,
            },
            new()
            {
                State = ActivityState.Blocked, LiveSession = true, ActiveSessions = 1,
                ProjectName = "civilcalc", SessionUsage = 100, WeeklyUsage = 61,
                UsageSampledUtc = DateTime.UtcNow,
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

        var working = snapshot.State == ActivityState.Working && snapshot.LiveSession;
        if (working && !_pulse.Enabled) _pulse.Start();
        else if (!working && _pulse.Enabled) { _pulse.Stop(); _pulsePhase = 0; }

        _tip.SetToolTip(this, BuildTooltip(snapshot));
        UpdateTrayIcon(snapshot);
        Invalidate();
    }

    private static Color ColorOf(ActivityState state) => state switch
    {
        ActivityState.Blocked => Palette.Red,
        ActivityState.Working => Palette.Amber,
        ActivityState.Done => Palette.Green,
        _ => Palette.LightOff,
    };

    private static string BuildTooltip(StatusSnapshot s)
    {
        var lines = new List<string> { s.StateLabel + (s.ProjectName is null ? "" : $" · {s.ProjectName}") };

        if (s.BlockedMessage is not null) lines.Add(s.BlockedMessage);

        lines.Add(s.ActiveSessions switch
        {
            0 => "Nenhuma sessão do Claude Code rodando",
            1 => "1 sessão ativa",
            _ => $"{s.ActiveSessions} sessões ativas",
        });

        if (s.SessionUsage is { } fh)
        {
            lines.Add($"Uso da sessão (5h): {fh}%");
            if (s.WeeklyUsage is { } sd) lines.Add($"Uso semanal (7d): {sd}%");

            if (s.UsageSampledUtc is { } at)
            {
                var age = DateTime.UtcNow - at;
                lines.Add(age.TotalMinutes < 20
                    ? $"Medido há {Math.Max(0, (int)age.TotalMinutes)} min"
                    : $"Desatualizado há {FormatAge(age)} — o Claude Desktop precisa estar aberto");
            }
        }
        else
        {
            lines.Add("Uso indisponível: abra o Claude Desktop ao menos uma vez");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatAge(TimeSpan age) =>
        age.TotalHours >= 1 ? $"{(int)age.TotalHours} h" : $"{(int)age.TotalMinutes} min";

    /// <summary>Depois do estado, o mais útil: a hora do reset quando bloqueado, o projeto quando não.</summary>
    private static string? SecondaryText(StatusSnapshot s)
    {
        if (s.State == ActivityState.Blocked && s.BlockedMessage is { } msg)
        {
            var at = msg.IndexOf("resets", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                var reset = msg[at..];
                var paren = reset.IndexOf(" (", StringComparison.Ordinal);
                if (paren > 0) reset = reset[..paren];
                return reset.Trim();
            }
        }

        return s.ProjectName;
    }

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

        using (var border = new Pen(Palette.Border))
        using (var path = RoundedRect(new RectangleF(0.5f, 0.5f, w - 1, h - 1), h * 0.22f))
            g.DrawPath(border, path);

        DrawTrafficLight(g, pad, (h - lightD) / 2f, lightD, gap);

        var ringX = w - pad - ringD;
        DrawUsageRing(g, new RectangleF(ringX, (h - ringD) / 2f, ringD, ringD), Math.Max(2f, h * 0.10f));

        var textX = pad + 3 * lightD + 2 * gap + gap * 1.4f;
        DrawLabel(g, new RectangleF(textX, 0, ringX - gap - textX, h));
    }

    private void DrawTrafficLight(Graphics g, float x, float y, float d, float gap)
    {
        var active = _snapshot.State;
        var live = _snapshot.LiveSession;

        // Ordem de semáforo de rua: vermelho, amarelo, verde.
        ReadOnlySpan<ActivityState> order =
            [ActivityState.Blocked, ActivityState.Working, ActivityState.Done];

        for (var i = 0; i < order.Length; i++)
        {
            var cx = x + i * (d + gap);
            var lit = order[i] == active;
            var color = ColorOf(order[i]);

            if (!lit)
            {
                using var off = new SolidBrush(Palette.LightOff);
                g.FillEllipse(off, cx, y, d, d);
                continue;
            }

            // Sem processo vivo, a luz fica acesa mas fraca: é memória do último turno.
            var intensity = live ? 1f : 0.45f;
            if (_pulse.Enabled) intensity *= 0.7f + 0.3f * (float)Math.Sin(_pulsePhase);

            var shown = Palette.Dim(color, Math.Clamp(intensity, 0.25f, 1f));

            using (var glow = new SolidBrush(Color.FromArgb(live ? 55 : 25, color)))
                g.FillEllipse(glow, cx - d * 0.35f, y - d * 0.35f, d * 1.7f, d * 1.7f);

            using var brush = new SolidBrush(shown);
            g.FillEllipse(brush, cx, y, d, d);
        }
    }

    private void DrawLabel(Graphics g, RectangleF area)
    {
        if (area.Width <= 4) return;

        var label = _snapshot.StateLabel;
        var secondary = SecondaryText(_snapshot);

        using var primary = new SolidBrush(_snapshot.LiveSession ? Palette.Text : Palette.TextDim);
        using var dim = new SolidBrush(Palette.TextDim);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };

        var labelWidth = g.MeasureString(label, _fontLabel).Width;
        g.DrawString(label, _fontLabel, primary, area, format);

        if (secondary is null) return;

        var rest = new RectangleF(
            area.X + labelWidth, area.Y, area.Width - labelWidth, area.Height);
        if (rest.Width > 10) g.DrawString("· " + secondary, _fontSecondary, dim, rest, format);
    }

    private void DrawUsageRing(Graphics g, RectangleF rect, float thickness)
    {
        using (var track = new Pen(Palette.Track, thickness))
            g.DrawEllipse(track, rect);

        if (_snapshot.SessionUsage is not { } percent)
        {
            using var unknown = new SolidBrush(Palette.TextDim);
            using var fmt = Centered();
            g.DrawString("–", _fontRing, unknown, rect, fmt);
            return;
        }

        var stale = _snapshot.UsageSampledUtc is { } at
            && DateTime.UtcNow - at > TimeSpan.FromMinutes(20);

        var color = Palette.ForUsage(percent);
        if (stale) color = Palette.Dim(color, 0.55f);

        if (percent > 0)
        {
            using var pen = new Pen(color, thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawArc(pen, rect, -90f, 360f * Math.Clamp(percent, 0, 100) / 100f);
        }

        using var text = new SolidBrush(stale ? Palette.TextDim : Palette.Text);
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

    private void SavePosition()
    {
        _settings.X = Location.X;
        _settings.Y = Location.Y;
        _settings.Save();
    }

    private void MoveToCorner()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - Width - ScreenMargin, area.Top + ScreenMargin);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        // Arrasta a barra inteira como se fosse a barra de título.
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
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

        var menu = new ContextMenuStrip();
        menu.Items.Add(_topMostItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Levar para o canto", null, (_, _) => { MoveToCorner(); SavePosition(); });
        menu.Items.Add("Atualizar agora", null, (_, _) => _monitor.Refresh());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Close());

        ContextMenuStrip = menu;

        _tray = new NotifyIcon
        {
            Text = "Claude Semáforo",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = SystemIcons.Application,
        };
        _tray.DoubleClick += (_, _) =>
        {
            Visible = !Visible;
            if (Visible) BringToFront();
        };
    }

    private void UpdateTrayIcon(StatusSnapshot s)
    {
        var color = ColorOf(s.State);
        if (!s.LiveSession) color = Palette.Dim(color, 0.5f);

        var old = _tray.Icon;
        _tray.Icon = MakeDotIcon(color);
        _tray.Text = Truncate($"Claude: {s.StateLabel}"
            + (s.SessionUsage is { } fh ? $" · {fh}% da sessão" : ""), 63);

        if (old is not null && !ReferenceEquals(old, SystemIcons.Application)) old.Dispose();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    private static Icon MakeDotIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }

        var handle = bmp.GetHicon();
        try
        {
            // Clone para poder liberar o HICON sem invalidar o ícone.
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
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
            _fontLabel.Dispose();
            _fontSecondary.Dispose();
            _fontRing.Dispose();
            _fontRingSmall.Dispose();
            _tray.Icon?.Dispose();
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }
}
