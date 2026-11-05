using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using WebTrayHost.Configuration;

namespace WebTrayHost;

public partial class MainForm : Form
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly AppSettings _settings;
    private readonly WebView2 _webView;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _trayContextMenu;
    private readonly string _expandedDataFolder;
    private readonly string _cachedFaviconIcoPath;
    private readonly string _cachedFaviconPngPath;

    private bool _isExiting;
    private string _lastNotificationFingerprint = string.Empty;
    private DateTime _lastNotificationTime = DateTime.MinValue;
    private Icon? _currentCustomIcon;

    public MainForm(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _expandedDataFolder = Environment.ExpandEnvironmentVariables(_settings.Web.UserDataFolder);
        _cachedFaviconIcoPath = Path.Combine(_expandedDataFolder, "cached_favicon.ico");
        _cachedFaviconPngPath = Path.Combine(_expandedDataFolder, "cached_favicon.png");

        InitializeComponentWindow();

        _trayContextMenu = new ContextMenuStrip();
        _notifyIcon = new NotifyIcon();
        _webView = new WebView2 { Dock = DockStyle.Fill };

        Controls.Add(_webView);

        ConfigureSystemTray();
        ConfigureToastActivation();
        LoadCachedFaviconIfExists();
        EnsureStartMenuShortcut();
    }

    private void InitializeComponentWindow()
    {
        Text = _settings.Window.Title;
        ClientSize = new Size(_settings.Window.Width, _settings.Window.Height);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        if (_settings.Window.StartMinimized)
        {
            WindowState = FormWindowState.Minimized;
        }
    }

    private void ConfigureSystemTray()
    {
        var openItem = new ToolStripMenuItem("Abrir / Restaurar", null, (s, e) => ShowAndActivate());
        var reloadItem = new ToolStripMenuItem("Recarregar Página", null, (s, e) => _webView.CoreWebView2?.Reload());
        var exitItem = new ToolStripMenuItem("Sair do Aplicativo", null, (s, e) => ExitApplication());

        _trayContextMenu.Items.Add(openItem);
        _trayContextMenu.Items.Add(reloadItem);
        _trayContextMenu.Items.Add(new ToolStripSeparator());
        _trayContextMenu.Items.Add(exitItem);

        _notifyIcon.Icon = SystemIcons.Application;
        _notifyIcon.Text = _settings.Window.Title.Length > 63 ? _settings.Window.Title[..63] : _settings.Window.Title;
        _notifyIcon.ContextMenuStrip = _trayContextMenu;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (s, e) => ToggleVisibility();
    }

    private void LoadCachedFaviconIfExists()
    {
        try
        {
            if (File.Exists(_cachedFaviconIcoPath))
            {
                using var fs = new FileStream(_cachedFaviconIcoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var cachedIcon = new Icon(fs);
                ApplyDynamicIcon(cachedIcon);

                // Garante que o arquivo PNG de alta resolução exista para o Toast
                if (!File.Exists(_cachedFaviconPngPath))
                {
                    using var bmp = cachedIcon.ToBitmap();
                    using var highResBmp = CreateHighResBitmap(bmp, 96);
                    highResBmp.Save(_cachedFaviconPngPath, ImageFormat.Png);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Falha ao carregar favicon do cache: {ex.Message}");
        }
    }

    private void ConfigureToastActivation()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(ShowAndActivate));
                }
                else
                {
                    ShowAndActivate();
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Falha ao registrar ativação de Toast: {ex.Message}");
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_settings.Window.StartHiddenInTray)
        {
            BeginInvoke(new Action(Hide));
        }

        await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            Directory.CreateDirectory(_expandedDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _expandedDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);

            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            if (!string.IsNullOrWhiteSpace(_settings.Web.UserAgent))
            {
                _webView.CoreWebView2.Settings.UserAgent = _settings.Web.UserAgent;
            }

            // Registro do evento dinâmico de Favicon
            _webView.CoreWebView2.FaviconChanged += OnFaviconChangedAsync;

            // Injeção da ponte de notificações
            await InjectNotificationBridgeScriptAsync();
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            _webView.CoreWebView2.Navigate(_settings.Web.Url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Falha ao inicializar o runtime WebView2:\n\n{ex.Message}",
                "Erro de Inicialização",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void OnFaviconChangedAsync(object? sender, object e)
    {
        try
        {
            using var stream = await _webView.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream == null || stream.Length == 0) return;

            using var rawBitmap = new Bitmap(stream);

            // 1. Salva PNG redimensionado com alta qualidade (96x96) para o Action Center do Windows
            try
            {
                Directory.CreateDirectory(_expandedDataFolder);
                using var highResBmp = CreateHighResBitmap(rawBitmap, 96);
                highResBmp.Save(_cachedFaviconPngPath, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Erro ao salvar PNG: {ex.Message}");
            }

            // 2. Extrai e atribui o ícone Win32 nativo
            IntPtr hIcon = rawBitmap.GetHicon();
            try
            {
                using var tempIcon = Icon.FromHandle(hIcon);
                var clonedIcon = (Icon)tempIcon.Clone();

                ApplyDynamicIcon(clonedIcon);
                SaveFaviconIcoToDisk(clonedIcon);
            }
            finally
            {
                DestroyIcon(hIcon);
            }

            // 3. Atualiza o atalho do Start Menu com o novo ícone
            EnsureStartMenuShortcut();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Erro ao processar favicon dinâmico: {ex.Message}");
        }
    }

    private static Bitmap CreateHighResBitmap(Image source, int targetSize)
    {
        var result = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(source, new Rectangle(0, 0, targetSize, targetSize));
        return result;
    }

    private void ApplyDynamicIcon(Icon newIcon)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyDynamicIcon(newIcon));
            return;
        }

        var oldIcon = _currentCustomIcon;
        _currentCustomIcon = newIcon;

        Icon = newIcon;
        _notifyIcon.Icon = newIcon;

        if (oldIcon != null && oldIcon != SystemIcons.Application)
        {
            oldIcon.Dispose();
        }
    }

    private void SaveFaviconIcoToDisk(Icon icon)
    {
        try
        {
            Directory.CreateDirectory(_expandedDataFolder);
            using var fs = new FileStream(_cachedFaviconIcoPath, FileMode.Create, FileAccess.Write, FileShare.None);
            icon.Save(fs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Erro ao persistir ICO de favicon: {ex.Message}");
        }
    }

    private void EnsureStartMenuShortcut()
    {
        try
        {
            string programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string shortcutPath = Path.Combine(programsPath, $"{_settings.Window.Title}.lnk");

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = Environment.ProcessPath ?? Application.ExecutablePath;
                    shortcut.WorkingDirectory = AppContext.BaseDirectory;
                    if (File.Exists(_cachedFaviconIcoPath))
                    {
                        shortcut.IconLocation = $"{_cachedFaviconIcoPath},0";
                    }
                    shortcut.Save();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Não foi possível registrar atalho no menu Iniciar: {ex.Message}");
        }
    }

    private async Task InjectNotificationBridgeScriptAsync()
    {
        string suppressDefaultLiteral = _settings.Notifications.SuppressBrowserDefaultNotifications ? "true" : "false";

        string script = $$"""
        (function() {
            if (window.__webTrayInjected) return;
            window.__webTrayInjected = true;

            const suppressDefault = {{suppressDefaultLiteral}};
            const OriginalNotification = window.Notification;

            // 1. Interceptação da Web Notifications API
            window.Notification = function(title, options) {
                try {
                    const body = (options && options.body) ? options.body : '';
                    window.chrome.webview.postMessage({
                        type: 'notification',
                        title: title || 'Nova Notificação',
                        body: body
                    });
                } catch(e) {
                    console.error('[WebTrayHost] Erro ao despachar postMessage:', e);
                }

                // Se NÃO for para suprimir a notificação do browser, despacha para a engine padrão
                if (!suppressDefault && OriginalNotification) {
                    return new OriginalNotification(title, options);
                }

                // Retorna objeto mock conforme a especificação W3C para evitar exceções em SPAs
                return {
                    close: function() {},
                    addEventListener: function() {},
                    removeEventListener: function() {},
                    dispatchEvent: function() { return false; }
                };
            };

            window.Notification.permission = 'granted';
            window.Notification.requestPermission = function() {
                return Promise.resolve('granted');
            };

            // 2. Monitoramento de mutações no título do documento
            let lastTitle = document.title;
            const titleObserver = new MutationObserver(function() {
                if (document.title !== lastTitle) {
                    lastTitle = document.title;
                    try {
                        window.chrome.webview.postMessage({
                            type: 'titleChanged',
                            title: document.title
                        });
                    } catch(e) {}
                }
            });

            const titleEl = document.querySelector('title');
            if (titleEl) {
                titleObserver.observe(titleEl, { subtree: true, characterData: true, childList: true });
            }
        })();
        """;

        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return;

            string type = typeProp.GetString() ?? string.Empty;

            if (type.Equals("notification", StringComparison.OrdinalIgnoreCase))
            {
                string title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "Nova Notificação" : "Nova Notificação";
                string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;

                HandleIncomingNotification(title, body);
            }
            else if (type.Equals("titleChanged", StringComparison.OrdinalIgnoreCase) && _settings.Notifications.MonitorDocumentTitle)
            {
                string newTitle = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
                HandleTitleChange(newTitle);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Erro ao desserializar mensagem web: {ex.Message}");
        }
    }

    private void HandleIncomingNotification(string title, string body)
    {
        if (!_settings.Notifications.EnableCustomToastNotifications) return;

        // Anti-duplicação básica (debouncing de 1.5s para notificações com o mesmo conteúdo)
        string fingerprint = $"{title}|{body}";
        if (fingerprint == _lastNotificationFingerprint && (DateTime.UtcNow - _lastNotificationTime).TotalMilliseconds < 1500)
        {
            return;
        }

        _lastNotificationFingerprint = fingerprint;
        _lastNotificationTime = DateTime.UtcNow;

        DispatchWindowsNotification(title, body);
    }

    private void HandleTitleChange(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        if (!Visible || WindowState == FormWindowState.Minimized)
        {
            var match = Regex.Match(title, _settings.Notifications.TitleNotificationRegex);
            if (match.Success)
            {
                string unreadCount = match.Groups[1].Value;
                _notifyIcon.Text = $"{_settings.Window.Title} ({unreadCount} novas)"[..Math.Min(63, $"{_settings.Window.Title} ({unreadCount} novas)".Length)];
            }
        }
    }

    private void DispatchWindowsNotification(string title, string body)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body);

            // Injeta o logotipo de alta resolução no corpo do Toast (Action Center)
            if (File.Exists(_cachedFaviconPngPath))
            {
                builder.AddAppLogoOverride(new Uri(_cachedFaviconPngPath), ToastGenericAppLogoCrop.Circle);
            }

            if (!_settings.Notifications.PlaySound)
            {
                builder.AddAudio(new ToastAudio { Silent = true });
            }

            builder.Show();
        }
        catch
        {
            _notifyIcon.ShowBalloonTip(4000, title, body, ToolTipIcon.Info);
        }
    }

    public void ShowAndActivate()
    {
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        BringToFront();
        Activate();
        _notifyIcon.Text = _settings.Window.Title.Length > 63 ? _settings.Window.Title[..63] : _settings.Window.Title;
    }

    private void ToggleVisibility()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            Hide();
        }
        else
        {
            ShowAndActivate();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExiting && _settings.Window.CloseToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_settings.Window.MinimizeToTray && WindowState == FormWindowState.Minimized)
        {
            Hide();
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
            _trayContextMenu.Dispose();
            _webView.Dispose();
            _currentCustomIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
