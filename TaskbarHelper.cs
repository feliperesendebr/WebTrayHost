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

    public void UpdateOverlayBadge(IntPtr hwnd, bool showBadge, int unreadCount)
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

            using var overlayIcon = CreateTaskbarBadgeIcon(unreadCount);
            string description = unreadCount > 0 ? $"{unreadCount} notificações não lidas" : "Nova notificação";
            _taskbarList.SetOverlayIcon(hwnd, overlayIcon.Handle, description);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Erro ao aplicar overlay na Barra de Tarefas: {ex.Message}");
        }
    }

    private static Icon CreateTaskbarBadgeIcon(int unreadCount)
    {
        const int size = 32;
        var (fillColor, textColor, borderColor) = GetNativeBadgeColors();

        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);

            // Cápsula horizontal (pill) estilo badge nativo do Windows 11
            const float pillWidth = 31.5f;
            const float pillHeight = 25.0f;
            float pillX = (size - pillWidth) / 2.0f;
            float pillY = (size - pillHeight) / 2.0f;
            const float border = 1.25f;

            var outerRect = new RectangleF(pillX, pillY, pillWidth, pillHeight);
            using (var outerPath = CreatePillPath(outerRect))
            using (var borderBrush = new SolidBrush(borderColor))
            {
                g.FillPath(borderBrush, outerPath);
            }

            var innerRect = new RectangleF(
                pillX + border,
                pillY + border,
                pillWidth - (border * 2),
                pillHeight - (border * 2));
            using (var innerPath = CreatePillPath(innerRect))
            using (var fillBrush = new SolidBrush(fillColor))
            {
                g.FillPath(fillBrush, innerPath);
            }

            if (unreadCount > 0)
            {
                string label = unreadCount > 9 ? "9+" : unreadCount.ToString();
                float emSize = label.Length > 1 ? 16.5f : 20.5f;

                using var family = new FontFamily("Segoe UI");
                using var path = new GraphicsPath();
                path.AddString(
                    label,
                    family,
                    (int)FontStyle.Regular,
                    emSize,
                    PointF.Empty,
                    StringFormat.GenericTypographic);

                // Centraliza exatamente pela caixa delimitadora visual do glifo
                RectangleF bounds = path.GetBounds();
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    float dx = (size / 2.0f) - (bounds.X + bounds.Width / 2.0f);
                    float dy = (size / 2.0f) - (bounds.Y + bounds.Height / 2.0f);
                    using var matrix = new Matrix();
                    matrix.Translate(dx, dy);
                    path.Transform(matrix);

                    using var textBrush = new SolidBrush(textColor);
                    g.FillPath(textBrush, path);
                }
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

    private static GraphicsPath CreatePillPath(RectangleF rect)
    {
        var path = new GraphicsPath();
        float d = Math.Min(rect.Width, rect.Height);
        path.AddArc(rect.X, rect.Y, d, d, 90, 180);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }

    private static (Color Fill, Color Text, Color Border) GetNativeBadgeColors()
    {
        bool isLightTaskbar = IsSystemLightTheme();

        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var winColor = uiSettings.GetColorValue(isLightTaskbar
                ? Windows.UI.ViewManagement.UIColorType.AccentDark1
                : Windows.UI.ViewManagement.UIColorType.AccentLight2);

            var fill = Color.FromArgb(255, winColor.R, winColor.G, winColor.B);
            var text = isLightTaskbar ? Color.White : Color.FromArgb(255, 17, 17, 17);
            var border = isLightTaskbar ? Color.White : Color.FromArgb(230, 24, 24, 24);
            return (fill, text, border);
        }
        catch
        {
            return isLightTaskbar
                ? (Color.FromArgb(255, 0, 95, 184), Color.White, Color.White)
                : (Color.FromArgb(255, 96, 205, 255), Color.FromArgb(255, 17, 17, 17), Color.FromArgb(230, 24, 24, 24));
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
