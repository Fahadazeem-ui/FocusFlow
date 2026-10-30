using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FocusFlow.Engine;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;

namespace FocusFlow;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly EngineService _engine;
    private Forms.NotifyIcon? _tray;
    private bool _reallyClosing;
    private bool _suppressAutoStartEvent;

    public MainWindow()
    {
        _engine = new EngineService(_settings);
        _engine.Recover();
        InitializeComponent();

        BuildProfileChips();
        RefreshWhitelistUi();
        SetupTray();

        _suppressAutoStartEvent = true;
        AutoStartBox.IsChecked = AutoStart.IsEnabled();
        _settings.AutoStart = AutoStartBox.IsChecked == true;
        _suppressAutoStartEvent = false;

        _engine.StateChanged += () => Dispatcher.Invoke(Refresh);
        Closed += OnClosed;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                if (_tray != null) _tray.Visible = true;
            }
        };

        if (_settings.AutoStart && AutoStart.IsEnabled())
        {
            Loaded += (_, _) =>
            {
                WindowState = WindowState.Minimized;
                Hide();
                if (_tray != null) _tray.Visible = true;
            };
        }

        Refresh();
    }

    private void BuildProfileChips()
    {
        ProfilePanel.Children.Clear();
        foreach (var p in _settings.Profiles)
        {
            var rb = new System.Windows.Controls.RadioButton
            {
                Content = p.Name,
                Style = (Style)FindResource("ProfileChip"),
                GroupName = "profiles",
                IsChecked = p.Name == _settings.ActiveProfile
            };
            rb.Checked += (_, _) =>
            {
                if (rb.IsChecked == true)
                {
                    _engine.SetProfile(p.Name);
                    RefreshProfileSettings();
                    Refresh();
                }
            };
            ProfilePanel.Children.Add(rb);
        }
    }

    private void RefreshWhitelistUi()
    {
        WhitelistList.ItemsSource = null;
        WhitelistList.ItemsSource = _settings.Whitelist.ToList();
    }

    private void RefreshProfileSettings()
    {
        var p = _engine.Profile;
        EcoQosValue.Text = p.EcoQos ? "ON" : "OFF";
        EcoQosValue.Foreground = p.EcoQos
            ? (MediaBrush)FindResource("Green")
            : (MediaBrush)FindResource("TextMuted");

        if (p.NetKbps > 0)
        {
            NetValue.Text = p.NetKbps >= 1024
                ? $"{p.NetKbps / 1024.0:0.#} Mbps"
                : $"{p.NetKbps} Kbps";
            NetValue.Foreground = (MediaBrush)FindResource("Cyan");
        }
        else
        {
            NetValue.Text = "OFF";
            NetValue.Foreground = (MediaBrush)FindResource("TextMuted");
        }

        RamValue.Text = p.TrimRam ? "ON" : "OFF";
        RamValue.Foreground = p.TrimRam
            ? (MediaBrush)FindResource("Magenta")
            : (MediaBrush)FindResource("TextMuted");
    }

    private void Refresh()
    {
        bool on = _engine.IsRunning;
        StatusLabel.Text = on ? "Focus Mode ON" : "Focus Mode OFF";
        StatusLabel.Foreground = on
            ? (MediaBrush)FindResource("Cyan")
            : (MediaBrush)FindResource("TextPrimary");

        FocusedAppText.Text = on
            ? $"Focused:  {_engine.ForegroundName}"
            : "Waiting for Start…";

        StatsText.Text = on
            ? $"Profile: {_engine.Profile.Name}   ·   Throttled: {_engine.ThrottledCount}"
            : "";

        if (on)
        {
            var names = _engine.ThrottledNames;
            LiveCountText.Text = names.Count + " apps";
            LiveListText.Text = names.Count > 0
                ? string.Join("  ·  ", names.Select(n => n + ".exe"))
                : "No background apps throttled yet";
        }
        else
        {
            LiveCountText.Text = "";
            LiveListText.Text = "Start Focus Mode to see live list…";
        }

        if (on)
        {
            OrbCenter.Color = MediaColor.FromRgb(0, 229, 255);
            OrbEdge.Color = MediaColor.FromRgb(0, 80, 110);
            OrbGlow.Color = MediaColor.FromRgb(0, 229, 255);
            OrbGlow.Opacity = 0.7;
        }
        else
        {
            OrbCenter.Color = MediaColor.FromRgb(58, 69, 88);
            OrbEdge.Color = MediaColor.FromRgb(26, 34, 51);
            OrbGlow.Color = MediaColor.FromRgb(90, 106, 138);
            OrbGlow.Opacity = 0.4;
        }

        if (on)
        {
            ToggleBtn.Content = "Stop Focus Mode";
            ToggleBtn.Background = new LinearGradientBrush(
                MediaColor.FromRgb(255, 46, 151), MediaColor.FromRgb(168, 85, 247), 0);
            ToggleBtn.Foreground = System.Windows.Media.Brushes.White;
        }
        else
        {
            ToggleBtn.Content = "Start Focus Mode";
            ToggleBtn.Background = new LinearGradientBrush(
                MediaColor.FromRgb(0, 229, 255), MediaColor.FromRgb(0, 180, 216), 0);
            ToggleBtn.Foreground = new SolidColorBrush(MediaColor.FromRgb(4, 16, 24));
        }

        RefreshProfileSettings();

        if (_tray != null)
            _tray.Text = on
                ? $"FocusFlow — ON ({_engine.ForegroundName})"
                : "FocusFlow — OFF";
    }

    private void AddWhitelist_Click(object sender, RoutedEventArgs e)
    {
        string name = (WhitelistInput.Text ?? "").Trim().ToLowerInvariant();
        if (name.EndsWith(".exe")) name = name[..^4];
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_settings.Whitelist.Any(w => w.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            WhitelistInput.Text = "";
            return;
        }

        _settings.Whitelist.Add(name);
        _settings.Save();
        _engine.RefreshWhitelist();
        WhitelistInput.Text = "";
        RefreshWhitelistUi();
    }

    private void RemoveWhitelist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string name) return;
        _settings.Whitelist.RemoveAll(w => w.Equals(name, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        _engine.RefreshWhitelist();
        RefreshWhitelistUi();
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAutoStartEvent) return;
        bool enable = AutoStartBox.IsChecked == true;
        AutoStart.Set(enable);
        _settings.AutoStart = enable;
        _settings.Save();
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = "FocusFlow",
            Visible = false,
            Icon = System.Drawing.SystemIcons.Application
        };

        var menu = new Forms.ContextMenuStrip();

        var toggleItem = new Forms.ToolStripMenuItem("Start Focus Mode");
        toggleItem.Click += (_, _) =>
        {
            if (_engine.IsRunning) _engine.Stop(); else _engine.Start();
            Refresh();
            toggleItem.Text = _engine.IsRunning ? "Stop Focus Mode" : "Start Focus Mode";
        };
        menu.Items.Add(toggleItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var showItem = new Forms.ToolStripMenuItem("Show Window");
        showItem.Click += (_, _) => ShowFromTray();
        menu.Items.Add(showItem);

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _reallyClosing = true;
            Close();
        };
        menu.Items.Add(exitItem);

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_tray != null) _tray.Visible = false;
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning) _engine.Stop(); else _engine.Start();
        Refresh();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _reallyClosing = true;
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _engine.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClosing)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }
        base.OnClosing(e);
    }
}
