using System;
using System.Windows;
using System.Windows.Threading;

namespace GolfSimVision;

/// <summary>
/// One process, one window, one WebView2-hosted UI (ADR-0006). No other entry points.
///
/// Global exception logging: by default, an unhandled exception on any thread --
/// including a background thread, like the camera capture loop or a detection callback
/// -- terminates the whole process immediately with no visible cause. These handlers
/// don't change that outcome (a startup-time or UI-thread crash still exits), but they
/// print the real exception to the console before the process goes down, instead of the
/// app just vanishing with nothing to diagnose from.
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Console.Error.WriteLine($"[FATAL] Unhandled UI-thread exception: {e.Exception}");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Console.Error.WriteLine($"[FATAL] Unhandled background-thread exception (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
    }
}
