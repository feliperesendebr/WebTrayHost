using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using WebTrayHost.Configuration;

namespace WebTrayHost;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory);

        string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        if (File.Exists(appSettingsPath))
        {
            builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        }
        else
        {
            builder.AddJsonFile("appsettings.sample.json", optional: true, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables(prefix: "WEBTRAY_")
               .AddCommandLine(args);

        var configuration = builder.Build();

        var settings = new AppSettings();
        configuration.Bind(settings);

        // Registra o AUMID do processo para o Windows vincular ao atalho do menu Iniciar
        string aumid = GetAumid(settings.Window.Title);
        ShortcutHelper.SetCurrentProcessExplicitAppUserModelID(aumid);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(settings, aumid));
    }

    public static string GetAumid(string title)
    {
        string clean = Regex.Replace(title ?? "App", @"[^a-zA-Z0-9]", "");
        return $"WebTrayHost.{clean}";
    }
}
