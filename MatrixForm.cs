using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MatrixScreensaver;

public class MatrixForm : Form
{
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;

    private readonly bool _isPreview;
    private Point _initialMousePos;
    private bool _mousePosInitialized;

    private MatrixRain _rain = null!;
    private System.Windows.Forms.Timer _timer = null!;

    public MatrixForm(Rectangle bounds)
    {
        _isPreview = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        BackColor = Color.Black;
        Cursor.Hide();
        DoubleBuffered = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        Init();
    }

    public MatrixForm(IntPtr previewHandle)
    {
        _isPreview = true;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        DoubleBuffered = true;

        SetWindowLong(this.Handle, GWL_STYLE, GetWindowLong(this.Handle, GWL_STYLE) | WS_CHILD);
        SetParent(this.Handle, previewHandle);

        GetClientRect(previewHandle, out RECT r);
        Size = new Size(r.Right - r.Left, r.Bottom - r.Top);
        Location = new Point(0, 0);
        Init();
    }

    private void Init()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        Load += (s, e) =>
        {
            _rain = new MatrixRain(ClientSize.Width, ClientSize.Height);
            _timer = new System.Windows.Forms.Timer { Interval = 50 };
            _timer.Tick += (_, __) => { _rain.Step(); Invalidate(); };
            _timer.Start();
        };

        Paint += (s, e) => _rain?.Draw(e.Graphics);

        if (!_isPreview)
        {
            MouseMove += (s, e) =>
            {
                if (!_mousePosInitialized) { _initialMousePos = e.Location; _mousePosInitialized = true; return; }
                if (Math.Abs(e.X - _initialMousePos.X) > 10 || Math.Abs(e.Y - _initialMousePos.Y) > 10)
                    Application.Exit();
            };
            MouseDown += (s, e) => Application.Exit();
            KeyDown += (s, e) => Application.Exit();
            Click += (s, e) => Application.Exit();
        }

        FormClosed += (s, e) => { _timer?.Stop(); _rain?.Dispose(); if (!_isPreview) Cursor.Show(); };
    }
}

internal sealed class MatrixRain : IDisposable
{
    private static readonly char[] Glyphs;
    static MatrixRain()
    {
        var list = new System.Collections.Generic.List<char>();
        for (char c = '\uFF66'; c <= '\uFF9D'; c++) list.Add(c);
        for (char c = '0'; c <= '9'; c++) list.Add(c);
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ:.\"=*+-<>|_") list.Add(c);
        Glyphs = list.ToArray();
    }

    private const int FontSize = 16;
    private const int CellW = 14;
    private const int CellH = 18;

    private readonly int _w, _h;
    private readonly int _cols, _rows;
    private readonly Column[] _columns;
    private readonly Font _font;
    private readonly Font _fontBold;
    private readonly Random _rng = new();
    private readonly StringFormat _sf;
    private readonly SolidBrush[] _greenBrushes;
    private readonly SolidBrush _headBrush;
    private readonly SolidBrush _fadeOverlay;

    private sealed class Column
    {
        public double Y;
        public double Speed;
        public int Length;
        public char[] Chars = Array.Empty<char>();
        public int[] MutateCounter = Array.Empty<int>();
        public bool Active;
        public int RespawnDelay;
    }

    public MatrixRain(int width, int height)
    {
        _w = Math.Max(1, width);
        _h = Math.Max(1, height);
        _cols = Math.Max(1, _w / CellW);
        _rows = Math.Max(1, _h / CellH) + 2;

        _font = new Font("MS Gothic", FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        _fontBold = new Font("MS Gothic", FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        _sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoClip };

        int steps = 24;
        _greenBrushes = new SolidBrush[steps];
        for (int i = 0; i < steps; i++)
        {
            double t = 1.0 - (i / (double)(steps - 1));
            int g = (int)(40 + 215 * t);
            _greenBrushes[i] = new SolidBrush(Color.FromArgb(0, Math.Min(255, g), 20));
        }
        _headBrush = new SolidBrush(Color.FromArgb(220, 255, 220));
        _fadeOverlay = new SolidBrush(Color.FromArgb(45, 0, 0, 0));

        _columns = new Column[_cols];
        for (int i = 0; i < _cols; i++) _columns[i] = MakeColumn(initial: true);
    }

    private Column MakeColumn(bool initial)
    {
        var c = new Column
        {
            Length = _rng.Next(8, Math.Max(10, _rows - 2)),
            Speed = 0.3 + _rng.NextDouble() * 1.2,
            Active = true,
            RespawnDelay = 0,
            Y = initial ? _rng.Next(-_rows, _rows) : -_rng.Next(1, _rows / 2 + 1),
        };
        c.Chars = new char[_rows + 4];
        c.MutateCounter = new int[c.Chars.Length];
        for (int i = 0; i < c.Chars.Length; i++)
        {
            c.Chars[i] = Glyphs[_rng.Next(Glyphs.Length)];
            c.MutateCounter[i] = _rng.Next(5, 40);
        }
        return c;
    }

    public void Step()
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            var c = _columns[i];
            if (!c.Active)
            {
                if (--c.RespawnDelay <= 0) _columns[i] = MakeColumn(initial: false);
                continue;
            }
            c.Y += c.Speed;
            for (int k = 0; k < c.Chars.Length; k++)
            {
                if (--c.MutateCounter[k] <= 0)
                {
                    c.Chars[k] = Glyphs[_rng.Next(Glyphs.Length)];
                    c.MutateCounter[k] = _rng.Next(8, 60);
                }
            }
            if (c.Y - c.Length > _rows + 2)
            {
                c.Active = false;
                c.RespawnDelay = _rng.Next(0, 40);
            }
        }
    }

    public void Draw(Graphics g)
    {
        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        g.FillRectangle(_fadeOverlay, 0, 0, _w, _h);

        int steps = _greenBrushes.Length;

        for (int i = 0; i < _columns.Length; i++)
        {
            var c = _columns[i];
            if (!c.Active) continue;
            int headRow = (int)Math.Floor(c.Y);
            int x = i * CellW;

            for (int t = c.Length - 1; t >= 1; t--)
            {
                int row = headRow - t;
                if (row < 0 || row >= _rows + 2) continue;
                int idx = ((row % c.Chars.Length) + c.Chars.Length) % c.Chars.Length;
                int brushIdx = Math.Min(steps - 1, (int)((t / (double)c.Length) * (steps - 1)));
                g.DrawString(c.Chars[idx].ToString(), _font, _greenBrushes[brushIdx], x, row * CellH, _sf);
            }

            if (headRow >= 0 && headRow < _rows + 2)
            {
                int idx = ((headRow % c.Chars.Length) + c.Chars.Length) % c.Chars.Length;
                g.DrawString(c.Chars[idx].ToString(), _fontBold, _headBrush, x, headRow * CellH, _sf);
            }
        }
    }

    public void Dispose()
    {
        foreach (var b in _greenBrushes) b.Dispose();
        _headBrush.Dispose();
        _fadeOverlay.Dispose();
        _font.Dispose();
        _fontBold.Dispose();
        _sf.Dispose();
    }
}
