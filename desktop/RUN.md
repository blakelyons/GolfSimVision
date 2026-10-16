# Running GolfSimVision

One-line PowerShell command (copy-paste the whole thing):

```powershell
cd "D:\SharedProjects\WebcamPutting\GolfSimVision\desktop\GolfSimVision"; & "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" run
```

If `dotnet` is already on your PATH in that shell, this shorter version also works:

```powershell
cd "D:\SharedProjects\WebcamPutting\GolfSimVision\desktop\GolfSimVision"; dotnet run
```

## Notes

- A window titled "GolfSimVision" opens after a few seconds. Camera negotiation can take
  up to ~15s on this hardware -- that's expected, not a hang.
- First launch may prompt a Windows Firewall dialog (E6 Connect listening on port 2483).
  Allow it, or the Garmin Golf app won't be able to reach it.
- To stop: close the app window, or Ctrl+C in the terminal.
- Settings file: `%LOCALAPPDATA%\GolfSimVision\settings.json`
- ONNX model (must exist for ball detection to work):
  `desktop\GolfSimVision\Models\golf_ball_v1.onnx`
