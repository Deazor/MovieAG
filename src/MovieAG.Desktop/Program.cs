namespace MovieAG.Desktop;

internal static class Program
{
    private const string Title = "MovieAG";
    private const string InstanceMutexName = @"Local\MovieAG.Desktop";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("MovieAG уже запущен.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DesktopServer server;
        try
        {
            // До Application.Run контекста синхронизации ещё нет, поэтому синхронное ожидание безопасно.
            server = DesktopServer.StartAsync(DesktopPaths.Create()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось запустить MovieAG.\n\n{ex.Message}", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            Application.Run(new MainForm(server.StartUri, server.Paths.WebViewData));
        }
        finally
        {
            server.StopAsync().GetAwaiter().GetResult();
        }
    }
}
