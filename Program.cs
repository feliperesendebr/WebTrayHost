using Microsoft.Extensions.Configuration;
using WebTrayHost.Configuration;

namespace WebTrayHost;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory);

        string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        if (File.Exists(appSettingsPath))
        {
            // O arquivo local appsettings.json possui prioridade absoluta sobre o modelo
            builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        }
        else
        {
            // Fallback apenas se o appsettings.json não existir
            builder.AddJsonFile("appsettings.sample.json", optional: true, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables(prefix: "WEBTRAY_")
               .AddCommandLine(args);

        var configuration = builder.Build();

        var settings = new AppSettings();
        configuration.Bind(settings);

        Application.Run(new MainForm(settings));
    }
}
