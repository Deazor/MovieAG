namespace MovieAG.Desktop;

/// <summary>
/// Где приложение хранит данные. Конфиг лежит в Roaming, база и профиль WebView2 в Local.
/// Переменная MOVIEAG_HOME переносит всё в одну папку: так удобно для тестов и «портативного» запуска.
/// </summary>
internal sealed record DesktopPaths(string ConfigDirectory, string DataDirectory)
{
    private const string AppFolder = "MovieAG";
    private const string HomeVariable = "MOVIEAG_HOME";

    public string SourcesConfig => Path.Combine(ConfigDirectory, "sources.yaml");
    public string Database => Path.Combine(DataDirectory, "movieag.db");
    public string WebViewData => Path.Combine(DataDirectory, "WebView2");

    public static DesktopPaths Create()
    {
        var home = Environment.GetEnvironmentVariable(HomeVariable);
        var paths = string.IsNullOrWhiteSpace(home)
            ? new DesktopPaths(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder))
            : new DesktopPaths(home, home);

        Directory.CreateDirectory(paths.ConfigDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        return paths;
    }
}
