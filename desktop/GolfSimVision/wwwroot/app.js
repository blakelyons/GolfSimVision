// Typed message bridge to the C# backend -- every message crossing this boundary is a
// plain, fixed-shape object (ADR-0006: typed messages on every interface). Outbound
// messages arrive wrapped as {kind, payload}: kind "status" carries an AppStatusMessage,
// kind "preview" carries {JpegBase64}. Inbound shape: UiRequest {type, mode?}.

const el = (id) => document.getElementById(id);

const previewImg = el("preview");
const cameraErrorEl = el("cameraError");

const connectionStateEl = el("connectionState");
const readyStateEl = el("readyState");
const lastResponseEl = el("lastResponse");
const lastErrorEl = el("lastError");

const captureModeEl = el("captureMode");
const ringBufferStateEl = el("ringBufferState");
const measuredFpsEl = el("measuredFps");
const lastStrokeFrameCountEl = el("lastStrokeFrameCount");
const captureDegradedEl = el("captureDegraded");

function post(type, mode) {
  window.chrome.webview.postMessage({ type, mode });
}

el("sendTestShot").addEventListener("click", () => post("sendTestShot"));
el("setFullShot").addEventListener("click", () => post("setCaptureMode", "fullShot"));
el("setPutting").addEventListener("click", () => post("setCaptureMode", "putting"));
el("triggerStroke").addEventListener("click", () => post("triggerTestStroke"));
el("endStroke").addEventListener("click", () => post("endTestStroke"));

function applyStatus(status) {
  connectionStateEl.textContent = status.ConnectionState ?? "—";
  readyStateEl.textContent = status.IsReadyForNextShot ? "yes" : "no";
  lastResponseEl.textContent =
    status.LastResponseCode != null
      ? `${status.LastResponseCode} ${status.LastResponseMessage ?? ""}`
      : "—";
  lastErrorEl.textContent = status.LastError ?? "";

  captureModeEl.textContent = status.CaptureMode ?? "—";
  ringBufferStateEl.textContent = status.RingBufferState ?? "—";
  measuredFpsEl.textContent = status.MeasuredFps != null ? status.MeasuredFps.toFixed(1) : "—";
  lastStrokeFrameCountEl.textContent = status.LastStrokeFrameCount ?? "—";
  cameraErrorEl.textContent = status.CameraError ? `Camera failed to open: ${status.CameraError}` : "";
  captureDegradedEl.textContent = status.CaptureDegraded ? `Degraded: ${status.CaptureDegradedReason ?? ""}` : "";
}

function applyPreview(preview) {
  previewImg.src = `data:image/jpeg;base64,${preview.JpegBase64}`;
}

window.chrome.webview.addEventListener("message", (event) => {
  const { kind, payload } = event.data;
  if (kind === "status") applyStatus(payload);
  else if (kind === "preview") applyPreview(payload);
});
