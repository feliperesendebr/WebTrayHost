namespace WebTrayHost.Configuration;

public class AppSettings
{
    public WindowSettings Window { get; set; } = new();
    public WebSettings Web { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
}

public class WindowSettings
{
    public string Title { get; set; } = "Aplicativo de Mensagens Web";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 850;
    public bool StartMinimized { get; set; } = false;
    public bool StartHiddenInTray { get; set; } = false;
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
}

public class WebSettings
{
    public string Url { get; set; } = "https://messaging.example.com";
    public string UserDataFolder { get; set; } = @"%LOCALAPPDATA%\WebTrayHost\ProfileData";
    public string? UserAgent { get; set; }
}

public class NotificationSettings
{
    /// <summary>
    /// Ativa ou desativa as notificações nativas disparadas pelo executável (Windows Action Center).
    /// </summary>
    public bool EnableCustomToastNotifications { get; set; } = true;

    /// <summary>
    /// Monitora contadores de mensagens não lidas no título da página (ex: "(1) Conversas").
    /// </summary>
    public bool MonitorDocumentTitle { get; set; } = true;

    /// <summary>
    /// Expressão regular para capturar o número de mensagens pendentes no título da aba.
    /// </summary>
    public string TitleNotificationRegex { get; set; } = @"^\((\d+)\)";

    /// <summary>
    /// Executa som padrão do Windows ao emitir o Toast.
    /// </summary>
    public bool PlaySound { get; set; } = true;
}
