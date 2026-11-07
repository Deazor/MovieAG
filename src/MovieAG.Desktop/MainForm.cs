using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MovieAG.Desktop;

internal sealed class MainForm : Form
{
    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>Сколько ждать после ухода со страницы, чтобы плеер успел сохранить позицию просмотра.</summary>
    private static readonly TimeSpan SaveOnCloseDelay = TimeSpan.FromMilliseconds(800);

    private static readonly Color Background = Color.FromArgb(0x13, 0x12, 0x11);

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    private readonly Uri _startUri;
    private readonly string _userDataFolder;
    private bool _closing;
    private FormBorderStyle _borderBeforeFullScreen;
    private FormWindowState _stateBeforeFullScreen;

    public MainForm(Uri startUri, string userDataFolder)
    {
        _startUri = startUri;
        _userDataFolder = userDataFolder;

        Text = "MovieAG";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        BackColor = Background;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_webView);

        Load += async (_, _) => await InitializeWebViewAsync();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_closing || _webView.CoreWebView2 is null) return;

        // Уход со страницы вызывает pagehide: плеер отправляет последнюю позицию, пока сервер ещё работает.
        e.Cancel = true;
        _closing = true;
        _webView.CoreWebView2.Navigate("about:blank");
        await Task.Delay(SaveOnCloseDelay);
        Close();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            // Профиль браузера храним в данных пользователя: папка рядом с exe может быть недоступна для записи.
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                $"Для работы MovieAG нужен Microsoft Edge WebView2 Runtime.\nСкачайте его: {RuntimeDownloadUrl}",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            _closing = true;
            Close();
            return;
        }

        var core = _webView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.ContainsFullScreenElementChanged += (_, _) => SetFullScreen(core.ContainsFullScreenElement);
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.Navigate(_startUri.ToString());
    }

    /// <summary>Полноэкранное видео растягивает только WebView2, поэтому окно разворачиваем вручную.</summary>
    private void SetFullScreen(bool fullScreen)
    {
        if (fullScreen)
        {
            _borderBeforeFullScreen = FormBorderStyle;
            _stateBeforeFullScreen = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = _borderBeforeFullScreen;
            WindowState = _stateBeforeFullScreen;
        }
    }

    /// <summary>Новые окна не открываем внутри приложения: ссылку, по которой нажал пользователь, отдаём системному браузеру.</summary>
    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.IsUserInitiated) OpenExternal(e.Uri);
    }

    /// <summary>Само окно показывает только интерфейс MovieAG, чужие страницы уходят в браузер.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target)) return;
        if (target.Scheme == "about" || IsSameOrigin(target)) return;

        e.Cancel = true;
        if (e.IsUserInitiated) OpenExternal(e.Uri);
    }

    private bool IsSameOrigin(Uri target) =>
        string.Equals(target.GetLeftPart(UriPartial.Authority), _startUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    private static void OpenExternal(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
    }
}
