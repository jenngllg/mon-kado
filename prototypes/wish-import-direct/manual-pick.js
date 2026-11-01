// Installed only after the user asks to select an already displayed image.
globalThis.monKadoStopImageSelection?.();
(() => {
  const notice = document.createElement("div");
  notice.setAttribute("role", "status");
  Object.assign(notice.style, { position: "fixed", zIndex: "2147483647", top: "12px", left: "50%", transform: "translateX(-50%)", padding: "12px 16px", background: "#fff", color: "#111", border: "2px solid #111", borderRadius: "8px", font: "16px sans-serif", boxShadow: "0 2px 8px #0006" });
  notice.textContent = "Clique sur l’image du produit. Échap pour annuler.";
  document.documentElement.appendChild(notice);
  let finished = false;
  const cleanup = () => { clearTimeout(timeout); document.removeEventListener("click", select, true); document.removeEventListener("keydown", cancel, true); notice.remove(); };
  const complete = async (message) => {
    if (finished) return;
    finished = true;
    clearTimeout(timeout);
    document.removeEventListener("click", select, true); document.removeEventListener("keydown", cancel, true);
    try {
      const result = await chrome.runtime.sendMessage(message);
      notice.textContent = result?.ok && message.action === "monkado-image-selected" ? "Image choisie. Rouvre l’extension pour la garder." : "Sélection terminée. Tu peux fermer ce message.";
    } catch { notice.textContent = "Sélection impossible. Tu peux fermer ce message."; }
    notice.style.cursor = "pointer"; notice.addEventListener("click", cleanup, { once: true });
  };
  const cancel = (event) => { if (event.isTrusted && event.key === "Escape") void complete({ action: "monkado-image-cancelled" }); };
  const select = (event) => {
    if (!event.isTrusted || notice.contains(event.target)) return;
    const image = event.composedPath().find((node) => node instanceof HTMLImageElement);
    if (!image || image.naturalWidth < 100 || image.naturalHeight < 100) return;
    const url = image.currentSrc || image.src;
    let parsed; try { parsed = new URL(url, document.baseURI); } catch { return; }
    if (!["http:", "https:"].includes(parsed.protocol) || parsed.username || parsed.password || parsed.href.length > 4096) return;
    event.preventDefault(); event.stopImmediatePropagation();
    void complete({ action: "monkado-image-selected", image: parsed.href });
  };
  globalThis.monKadoStopImageSelection = cleanup;
  const timeout = setTimeout(() => { void complete({ action: "monkado-image-cancelled" }); }, 5 * 60 * 1000);
  document.addEventListener("click", select, true); document.addEventListener("keydown", cancel, true);
})();
