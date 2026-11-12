using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WebTrayHost;

/// <summary>
/// Gerencia badges nativos na Barra de Tarefas do Windows via interface COM oficial ITaskbarList3.
/// Aplicações Win32 não empacotadas (sem identidade MSIX) utilizam SetOverlayIcon como API oficial do Shell.
/// </summary>
internal sealed class TaskbarHelper : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    private ITaskbarList3? _taskbarList;
    private bool _initialized;
    private bool _disposed;

    public uint TaskbarButtonCreatedMessage { get; }

    public TaskbarHelper()
    {
        TaskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");
    }

    private void EnsureInitialized()
    {
        if (_initialized || _disposed) return;
        _initialized = true;

        try
        {
            var instance = (ITaskbarList3)new TaskbarListClass();
            instance.HrInit();
            _taskbarList = instance;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Falha ao inicializar ITaskbarList3: {ex.Message}");
        }
    }

    public void UpdateOverlayBadge(IntPtr hwnd, bool showBadge, int unreadCount, string? badgeColorSetting = null)
    {
        if (_disposed || hwnd == IntPtr.Zero) return;

        EnsureInitialized();
        if (_taskbarList == null) return;

        try
        {
            if (!showBadge)
            {
                _taskbarList.SetOverlayIcon(hwnd, IntPtr.Zero, null);
                return;
            }

            using var overlayIcon = CreateTaskbarBadgeIcon(unreadCount, badgeColorSetting);
            string description = unreadCount > 0 ? $"{unreadCount} notificações não lidas" : "Nova notificação";
            _taskbarList.SetOverlayIcon(hwnd, overlayIcon.Handle, description);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Erro ao aplicar overlay na Barra de Tarefas: {ex.Message}");
        }
    }

    private static Icon CreateTaskbarBadgeIcon(int unreadCount, string? badgeColorSetting)
    {
        const int size = 32;
        var (fillColor, textColor) = ResolveBadgeColors(badgeColorSetting);

        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);

            // Cápsula horizontal maximizada sem borda escura (ocupa o limite físico do slot 32x32)
            const float pillWidth = 31.8f;
            const float pillHeight = 29.0f;
            float pillX = (size - pillWidth) / 2.0f;
            float pillY = (size - pillHeight) / 2.0f;

            var pillRect = new RectangleF(pillX, pillY, pillWidth, pillHeight);
            using (var pillPath = CreatePillPath(pillRect))
            using (var fillBrush = new SolidBrush(fillColor))
            {
                g.FillPath(fillBrush, pillPath);
            }

            if (unreadCount > 0)
            {
                string label = unreadCount > 99 ? "99+" : unreadCount.ToString();
                float emSize = label.Length switch
                {
                    >= 3 => 14.0f,
                    2 => 18.5f,
                    _ => 25.0f
                };

                using var family = new FontFamily("Segoe UI");
                using var path = new GraphicsPath();
                path.AddString(
                    label,
                    family,
                    (int)FontStyle.Regular,
                    emSize,
                    PointF.Empty,
                    StringFormat.GenericTypographic);

                CenterGlyphOptically(path, size, label.Length);

                using var textBrush = new SolidBrush(textColor);
                g.FillPath(textBrush, path);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static void CenterGlyphOptically(GraphicsPath path, int canvasSize, int charCount)
    {
        RectangleF bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        float center = canvasSize / 2.0f;
        float dx = center - (bounds.X + bounds.Width / 2.0f);
        float dy = center - (bounds.Y + bounds.Height / 2.0f);

        using (var initialMatrix = new Matrix())
        {
            initialMatrix.Translate(dx, dy);
            path.Transform(initialMatrix);
        }

        // Calcula o centroide óptico real (ponderado pelo alpha dos pixels) para corrigir dígitos assimétricos como "1", "3", "4" e "7"
        using var measureBmp = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);
        using (var mg = Graphics.FromImage(measureBmp))
        {
            mg.SmoothingMode = SmoothingMode.AntiAlias;
            mg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            mg.Clear(Color.Transparent);
            mg.FillPath(Brushes.White, path);
        }

        double sumX = 0;
        double sumY = 0;
        double totalAlpha = 0;

        for (int py = 0; py < canvasSize; py++)
        {
            for (int px = 0; px < canvasSize; px++)
            {
                byte alpha = measureBmp.GetPixel(px, py).A;
                if (alpha > 0)
                {
                    sumX += (px + 0.5) * alpha;
                    sumY += (py + 0.5) * alpha;
                    totalAlpha += alpha;
                }
            }
        }

        if (totalAlpha <= 0) return;

        float centroidX = (float)(sumX / totalAlpha);
        float centroidY = (float)(sumY / totalAlpha);
        float weight = charCount == 1 ? 0.95f : (charCount == 2 ? 0.55f : 0.35f);

        float optDx = (center - centroidX) * weight;
        float optDy = (center - centroidY) * weight;

        using var optMatrix = new Matrix();
        optMatrix.Translate(optDx, optDy);
        path.Transform(optMatrix);
    }

    private static GraphicsPath CreatePillPath(RectangleF rect)
    {
        var path = new GraphicsPath();
        float d = Math.Min(rect.Width, rect.Height);
        path.AddArc(rect.X, rect.Y, d, d, 90, 180);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }

    private static (Color Fill, Color Text) ResolveBadgeColors(string? badgeColorSetting)
    {
        bool isLightTaskbar = IsSystemLightTheme();

        if (string.Equals(badgeColorSetting?.Trim(), "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return GetWindowsAccentBadgeColors(isLightTaskbar);
        }

        Color fill = Color.FromArgb(255, 209, 52, 56); // Padrão #D13438 (vermelho Fluent / Teams suavizado)
        if (!string.IsNullOrWhiteSpace(badgeColorSetting))
        {
            try
            {
                Color parsed = ColorTranslator.FromHtml(badgeColorSetting.Trim());
                fill = Color.FromArgb(255, parsed.R, parsed.G, parsed.B);
            }
            catch
            {
                // Mantém vermelho padrão caso o código hexadecimal informado seja inválido
            }
        }

        double luminance = (0.299 * fill.R) + (0.587 * fill.G) + (0.114 * fill.B);
        Color text = luminance > 155 ? Color.FromArgb(255, 17, 17, 17) : Color.White;
        return (fill, text);
    }

    private static (Color Fill, Color Text) GetWindowsAccentBadgeColors(bool isLightTaskbar)
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var winColor = uiSettings.GetColorValue(isLightTaskbar
                ? Windows.UI.ViewManagement.UIColorType.AccentDark1
                : Windows.UI.ViewManagement.UIColorType.AccentLight2);

            var fill = Color.FromArgb(255, winColor.R, winColor.G, winColor.B);
            var text = isLightTaskbar ? Color.White : Color.FromArgb(255, 17, 17, 17);
            return (fill, text);
        }
        catch
        {
            return isLightTaskbar
                ? (Color.FromArgb(255, 0, 95, 184), Color.White)
                : (Color.FromArgb(255, 96, 205, 255), Color.FromArgb(255, 17, 17, 17));
        }
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("SystemUsesLightTheme");
            return value is int intVal && intVal == 1;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_taskbarList != null)
        {
            try
            {
                Marshal.ReleaseComObject(_taskbarList);
            }
            catch { }
            _taskbarList = null;
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarListClass;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, int tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }
}
