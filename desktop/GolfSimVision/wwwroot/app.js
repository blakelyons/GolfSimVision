// Typed message bridge to the C# backend -- every message crossing this boundary is a
// plain, fixed-shape object matching GolfSimVision.UiMessages (ADR-0006: typed messages
// on every interface, never free-form data). Outbound shape: AppStatusMessage. Inbound
// shape: UiRequest.

const connectionStateEl = document.getElementById("connectionState");
const readyStateEl = document.getElementById("readyState");
const lastResponseEl = document.getElementById("lastResponse");
const lastErrorEl = document.getElementById("lastError");
const sendTestShotButton = document.getElementById("sendTestShot");

sendTestShotButton.addEventListener("click", () => {
  window.chrome.webview.postMessage({ type: "sendTestShot" });
});

window.chrome.webview.addEventListener("message", (event) => {
  /** @type {{ConnectionState: string, IsReadyForNextShot: boolean, LastResponseCode: number|null, LastResponseMessage: string|null, LastError: string|null}} */
  const status = event.data;

  connectionStateEl.textContent = status.ConnectionState ?? "—";
  readyStateEl.textContent = status.IsReadyForNextShot ? "yes" : "no";
  lastResponseEl.textContent =
    status.LastResponseCode != null
      ? `${status.LastResponseCode} ${status.LastResponseMessage ?? ""}`
      : "—";
  lastErrorEl.textContent = status.LastError ?? "";
});
