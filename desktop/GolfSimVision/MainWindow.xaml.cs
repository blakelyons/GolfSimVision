using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using GolfSimVision.Settings;
using GolfSimVision.Simulator;
using GolfSimVision.UiMessages;
using Microsoft.Web.WebView2.Core;

namespace GolfSimVision;

/// <summary>
/// The app shell: hosts the WebView2 UI, owns the one SimulatorConnector, and is the
/// only place that translates between the UI's typed messages and the backend's typed
/// domain objects. Ticket #2's whole job is proving this path end to end with a manual
/// test-shot button -- no camera, no R10, no detection yet.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions InboundOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private SimulatorConnector? _connector;
    private GsProCodec? _codec;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await Browser.EnsureCoreWebView2Async();

        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "golfsimvision.app", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
        Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        Browser.CoreWebView2.Navigate("https://golfsimvision.app/index.html");

        _connector = new SimulatorConnector(_settings.GsProHost, _settings.GsProPort, _settings.DeviceId, _settings.Units);
        _codec = new GsProCodec(_settings.DeviceId, _connector.NextShotNumber, _settings.Units);
        _connector.OnStatus = _ => Dispatcher.Invoke(() => PostStatus());
        _connector.OnResponse = response => Dispatcher.Invoke(() => PostStatus(response));
        _connector.Connect();
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        UiRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<UiRequest>(e.WebMessageAsJson, InboundOptions);
        }
        catch (JsonException)
        {
            return;
        }
        if (request is null) return;

        switch (request.Type)
        {
            case "sendTestShot":
                SendTestShot();
                break;
        }
    }

    private void SendTestShot()
    {
        if (_connector is null || _codec is null || !_connector.IsConnected)
        {
            PostStatus(lastError: "Not connected to GSPro");
            return;
        }

        // Send() can block the calling thread for up to MinShotInterval (the pacing
        // guard) -- run it off the UI dispatcher thread so a rapid double-click never
        // freezes the window. Captured locally since this runs after OnWebMessageReceived
        // returns, by which point _connector/_codec could theoretically be reassigned.
        var connector = _connector;
        var codec = _codec;
        Task.Run(() =>
        {
            try
            {
                // A manual, developer-facing test shot -- not a real measurement. Values
                // match GSPRO_PUTTING.md's verified working putt payload (exp 8).
                var payload = codec.BuildPutt(speed: 5.0, hla: 0.0, vla: 0.0);
                connector.Send(payload);
                Dispatcher.Invoke(() => PostStatus());
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => PostStatus(lastError: ex.Message));
            }
        });
    }

    private void PostStatus(GsProResponse? response = null, string? lastError = null)
    {
        var message = new AppStatusMessage(
            ConnectionState: _connector?.State.ToString() ?? nameof(Simulator.ConnectionState.Disabled),
            IsReadyForNextShot: _connector?.IsReadyForNextShot ?? false,
            LastResponseCode: response?.Code,
            LastResponseMessage: response?.Message,
            LastError: lastError);
        var json = JsonSerializer.Serialize(message);
        Browser.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _connector?.Disconnect();
        _settingsStore.Save(_settings);
    }
}
