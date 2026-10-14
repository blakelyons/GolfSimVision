// Typed message bridge to the C# backend -- every message crossing this boundary is a
// plain, fixed-shape object (ADR-0006: typed messages on every interface). Outbound
// messages arrive wrapped as {kind, payload}: kind "status" carries an AppStatusMessage,
// kind "preview" carries {JpegBase64}. Inbound shape: UiRequest {type, mode?, zone?}.

const el = (id) => document.getElementById(id);

const canvas = el("preview");
const ctx = canvas.getContext("2d");
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

function post(type, mode, zone) {
  window.chrome.webview.postMessage({ type, mode, zone });
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

  // Kept as local state (zone) so an in-progress drag doesn't fight the backend's
  // periodically-pushed value -- same caution as V1's CameraView.tsx.
  if (status.Zone && !dragging) {
    zone = status.Zone;
  }
}

// --- Live preview + zone/gateway overlay ------------------------------------------
// Drawn together in one draw() pass on one canvas, never a server-rendered frame plus
// a separate overlay layer (ticket #4's own acceptance criterion).

let latestFrameImage = null;
let zone = {
  StartZone: { X: 0.05, Y: 0.1, W: 0.25, H: 0.8 },
  GatewayX: 0.55,
};
let editMode = false;
let dragging = null; // 'zone' | 'gateway' | null
let dragStart = null; // {x, y} in normalized coords

function applyPreview(preview) {
  const img = new Image();
  img.onload = () => {
    latestFrameImage = img;
    draw();
  };
  img.src = `data:image/jpeg;base64,${preview.JpegBase64}`;
}

function draw() {
  const { width: cw, height: ch } = canvas;
  ctx.clearRect(0, 0, cw, ch);

  if (latestFrameImage) {
    ctx.drawImage(latestFrameImage, 0, 0, cw, ch);
  } else {
    ctx.fillStyle = "#000";
    ctx.fillRect(0, 0, cw, ch);
  }

  const z = zone;
  const sx = z.StartZone.X * cw;
  const sy = z.StartZone.Y * ch;
  const sw = z.StartZone.W * cw;
  const sh = z.StartZone.H * ch;

  ctx.save();
  ctx.strokeStyle = "#22c55e";
  ctx.lineWidth = 2;
  ctx.setLineDash(editMode ? [6, 3] : []);
  ctx.shadowColor = "#22c55e";
  ctx.shadowBlur = editMode ? 8 : 4;
  ctx.strokeRect(sx, sy, sw, sh);
  ctx.fillStyle = "rgba(34, 197, 94, 0.06)";
  ctx.fillRect(sx, sy, sw, sh);
  ctx.restore();

  ctx.save();
  ctx.font = '600 11px "Segoe UI", sans-serif';
  ctx.fillStyle = "rgba(34, 197, 94, 0.9)";
  ctx.fillText("START", sx + 4, sy + 14);
  ctx.restore();

  const gx = z.GatewayX * cw;
  ctx.save();
  ctx.strokeStyle = "#f59e0b";
  ctx.lineWidth = 2;
  ctx.setLineDash(editMode ? [6, 3] : [4, 4]);
  ctx.shadowColor = "#f59e0b";
  ctx.shadowBlur = editMode ? 10 : 4;
  ctx.beginPath();
  ctx.moveTo(gx, 0);
  ctx.lineTo(gx, ch);
  ctx.stroke();
  ctx.restore();

  ctx.save();
  ctx.font = '600 11px "Segoe UI", sans-serif';
  ctx.fillStyle = "rgba(245, 158, 11, 0.9)";
  ctx.fillText("GATE", gx + 4, 16);
  ctx.restore();
}

function getNormPos(e) {
  const rect = canvas.getBoundingClientRect();
  return {
    x: (e.clientX - rect.left) / rect.width,
    y: (e.clientY - rect.top) / rect.height,
  };
}

canvas.addEventListener("mousedown", (e) => {
  if (!editMode) return;
  const pos = getNormPos(e);
  if (Math.abs(pos.x - zone.GatewayX) < 0.02) {
    dragging = "gateway";
  } else {
    dragging = "zone";
    dragStart = pos;
  }
});

canvas.addEventListener("mousemove", (e) => {
  if (!editMode || !dragging) return;
  const pos = getNormPos(e);
  if (dragging === "gateway") {
    zone = { ...zone, GatewayX: Math.max(0.1, Math.min(0.9, pos.x)) };
  } else if (dragging === "zone" && dragStart) {
    const x = Math.min(dragStart.x, pos.x);
    const y = Math.min(dragStart.y, pos.y);
    const w = Math.abs(pos.x - dragStart.x);
    const h = Math.abs(pos.y - dragStart.y);
    zone = { ...zone, StartZone: { X: x, Y: y, W: Math.max(0.05, w), H: Math.max(0.05, h) } };
  }
  draw();
});

canvas.addEventListener("mouseup", () => {
  if (!dragging) return;
  // When a zone drag finishes, auto-position the gateway 10% past the zone's right
  // edge so it's always valid without the user having to manually adjust -- same as
  // V1's CameraView.tsx handleMouseUp.
  if (dragging === "zone") {
    zone = {
      ...zone,
      GatewayX: Math.min(0.9, zone.StartZone.X + zone.StartZone.W + 0.1),
    };
  }
  dragging = null;
  dragStart = null;
  post("updateZoneConfig", undefined, zone);
  draw();
});

el("toggleEdit").addEventListener("click", () => {
  editMode = !editMode;
  el("toggleEdit").textContent = editMode ? "Done Editing" : "Edit Zones";
  draw();
});

window.chrome.webview.addEventListener("message", (event) => {
  const { kind, payload } = event.data;
  if (kind === "status") {
    applyStatus(payload);
    draw();
  } else if (kind === "preview") {
    applyPreview(payload);
  }
});

draw();
