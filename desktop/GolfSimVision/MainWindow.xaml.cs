using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using GolfSimVision.Calibration;
using GolfSimVision.Capture;
using GolfSimVision.Settings;
using GolfSimVision.Simulator;
using GolfSimVision.UiMessages;
using GolfSimVision.Zones;
using Microsoft.Web.WebView2.Core;

namespace GolfSimVision;

/// <summary>
/// The app shell: hosts the WebView2 UI, owns the one SimulatorConnector and the one
/// UsbWebcam/BufferedCaptureSession, and is the only place that translates between the
/// UI's typed messages and the backend's typed domain objects.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions InboundOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SettingsStore _settingsStore = new();
    private readonly CalibrationService _calibrationService = new(new CalibrationStore());
    private string? _calibrationError;

    // Not readonly: zone/gateway updates (and, later, calibration) mutate this via
    // `with` and persist immediately, not only at clean app-close -- a dragged zone
    // should survive even if the app is later closed uncleanly.
    private AppSettings _settings;

    private SimulatorConnector? _connector;
    private GsProCodec? _codec;

    private IFrameSource? _webcam;
    private BufferedCaptureSession? _captureSession;
    private CaptureHealth? _lastHealth;
    private string? _cameraError;
    private int? _lastStrokeFrameCount;

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

        await OpenCameraAsync();
        PostStatus();
    }

    private async Task OpenCameraAsync()
    {
        _captureSession = new BufferedCaptureSession(_settings.RingBufferCapacity);
        _captureSession.OnRingBufferStateChanged = _ => Dispatcher.Invoke(() => PostStatus());
        _captureSession.OnWindowShipped = window => Dispatcher.Invoke(() =>
        {
            _lastStrokeFrameCount = window.Count;
            PostStatus();
        });

        var webcam = new UsbWebcam();
        webcam.OnLog = msg => Console.WriteLine($"[capture] {msg}");
        webcam.OnFrame = frame => _captureSession?.Ingest(frame);
        webcam.OnPreviewFrame = jpeg => Dispatcher.Invoke(() => PostPreviewFrame(jpeg));
        webcam.OnHealth = health => Dispatcher.Invoke(() =>
        {
            _lastHealth = health;
            PostStatus();
        });
        _webcam = webcam;

        var request = new CameraOpenRequest(
            _settings.CameraIndex, _settings.CameraWidth, _settings.CameraHeight, _settings.CameraFps,
            _settings.CameraExposure, _settings.CameraGain);

        // Each open-time property set can take up to ~15s on known hardware -- run off
        // the UI thread so the window never appears frozen while the camera negotiates.
        var result = await Task.Run(() => webcam.Open(request));

        if (!result.Success)
        {
            _cameraError = result.FailureReason;
            return;
        }

        _cameraError = null;
        // Default to Putting mode so the manual stroke-trigger demo works without the
        // user first having to flip modes -- GolfSimVision's primary scenario.
        _captureSession.SetMode(CaptureMode.Putting);
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
            case "setCaptureMode":
                SetCaptureMode(request.Mode);
                break;
            case "triggerTestStroke":
                _captureSession?.TriggerStroke();
                PostStatus();
                break;
            case "endTestStroke":
                _captureSession?.EndStroke();
                break;
            case "updateZoneConfig":
                UpdateZoneConfig(request.Zone);
                break;
            case "calibrate":
                Calibrate(request.Corners, request.RealWidthMm, request.RealHeightMm);
                break;
        }
    }

    private void Calibrate(PixelPoint[]? corners, double? realWidthMm, double? realHeightMm)
    {
        if (corners is not { Length: 4 } || realWidthMm is not { } w || realHeightMm is not { } h)
        {
            _calibrationError = "Calibration needs exactly 4 corners plus a real width and height";
            PostStatus();
            return;
        }

        try
        {
            var correspondences = RectangleCalibration.BuildCorrespondences(
                corners[0], corners[1], corners[2], corners[3], w, h);
            _calibrationService.Calibrate(correspondences);
            _calibrationError = null;
        }
        catch (Exception ex)
        {
            // Degenerate input (collinear/duplicate corners) throws from HomographySolver
            // -- surfaced to the UI rather than crashing the app.
            _calibrationError = ex.Message;
        }
        PostStatus();
    }

    private void UpdateZoneConfig(ZoneConfig? zone)
    {
        if (zone is null) return;
        _settings = _settings with { Zone = ZoneConfigClamping.Clamp(zone) };
        // Persisted immediately, not deferred to app-close -- see the _settings field's
        // own comment on why.
        _settingsStore.Save(_settings);
        PostStatus();
    }

    private void SetCaptureMode(string? mode)
    {
        if (_captureSession is null) return;
        switch (mode)
        {
            case "fullShot":
                _captureSession.SetMode(CaptureMode.FullShot);
                break;
            case "putting":
                _captureSession.SetMode(CaptureMode.Putting);
                break;
            default:
                return;
        }
        PostStatus();
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
            LastError: lastError,
            CaptureMode: _captureSession?.Mode.ToString(),
            RingBufferState: _captureSession?.State.ToString(),
            MeasuredFps: _lastHealth?.MeasuredFps,
            CaptureDegraded: _lastHealth?.IsDegraded ?? false,
            CaptureDegradedReason: _lastHealth?.DegradedReason,
            CameraError: _cameraError,
            LastStrokeFrameCount: _lastStrokeFrameCount,
            Zone: _settings.Zone,
            CalibrationMillimetersPerPixel: _calibrationService.Current?.MillimetersPerPixel,
            CalibrationVersion: _calibrationService.Current?.Version,
            CalibrationError: _calibrationError);
        PostToUi("status", message);
    }

    private void PostPreviewFrame(byte[] jpeg)
    {
        PostToUi("preview", new { JpegBase64 = Convert.ToBase64String(jpeg) });
    }

    private void PostToUi(string kind, object payload)
    {
        var envelope = new { kind, payload };
        var json = JsonSerializer.Serialize(envelope);
        Browser.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _connector?.Disconnect();
        _webcam?.Close();
        _webcam?.Dispose();
        _settingsStore.Save(_settings);
    }
}
