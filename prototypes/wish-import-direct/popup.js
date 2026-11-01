const status = document.getElementById("status");
const result = document.getElementById("result");
const save = document.getElementById("save");
const copyVisibleImage = document.getElementById("copy-visible-image");
const chooseImage = document.getElementById("choose-image");
const keepVisibleImage = document.getElementById("keep-visible-image");
const useSavedImage = document.getElementById("use-saved-image");
const maxSavedImageBytes = 4 * 1024 * 1024;
const apply = document.getElementById("apply");
const pasteImage = document.getElementById("paste-image");
const clear = document.getElementById("clear");
let activeTab;

function isMonKado(url) {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "https:" && ["monkado.fr", "www.monkado.fr"].includes(parsed.hostname) && !parsed.port ||
      ["http:", "https:"].includes(parsed.protocol) && parsed.hostname === "localhost" && ["5173", "5176"].includes(parsed.port);
  } catch { return false; }
}

function show(data) {
  for (const field of ["name", "price", "url", "image"]) document.getElementById(field).value = data[field] || "";
  result.hidden = false;
}

function readSuggestion() {
  const data = Object.fromEntries(["name", "price", "url", "image"].map((field) => [field, document.getElementById(field).value.trim()]));
  let parsed;
  try { parsed = new URL(data.url); } catch { return null; }
  if (!["http:", "https:"].includes(parsed.protocol) || parsed.username || parsed.password || data.url.length > 2048 || [...data.name].length > 100 || data.price && !/^\d{1,8}(?:[.,]\d{1,2})?$/.test(data.price)) return null;
  parsed.hash = ""; data.url = parsed.href;
  return data;
}

function imageMatchesSuggestion(capturedImage, data) {
  return !!capturedImage && !!data && capturedImage.url === data.url && !!capturedImage.source &&
    ["name", "price", "image"].every((field) => capturedImage.source[field] === data[field]);
}

async function readActivePage() {
  [activeTab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!activeTab?.id || !/^https?:\/\//i.test(activeTab.url || "")) throw new Error("Ouvre d’abord une fiche produit sur un site marchand.");
  if (isMonKado(activeTab.url)) {
    const { suggestion, capturedImage } = await chrome.storage.session.get(["suggestion", "capturedImage"]);
    if (!suggestion) throw new Error("Ouvre d’abord une fiche produit et garde ses suggestions.");
    show(suggestion); apply.hidden = false; pasteImage.hidden = false; clear.hidden = false;
    useSavedImage.hidden = !imageMatchesSuggestion(capturedImage, suggestion);
    status.textContent = "Ouvre l’ajout d’un souhait, puis remplis les champs vides.";
    return;
  }
  await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, files: ["extractor.js"] });
  const [execution] = await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, func: () => globalThis.monKadoExtractPage() });
  if (!execution?.result) throw new Error("Cette page ne peut pas être lue.");
  const data = execution.result;
  const { manualImage } = await chrome.storage.session.get("manualImage");
  let chosen = false;
  if (manualImage) {
    await chrome.storage.session.remove("manualImage");
    if (!data.blocked && manualImage.tabId === activeTab.id && manualImage.url === data.url && manualImage.expires >= Date.now()) {
      data.image = manualImage.image; chosen = true;
      await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, func: () => globalThis.monKadoStopImageSelection?.() });
    }
  }
  show(data);
  status.textContent = data.verificationRedirect ? "Une vérification du marchand est nécessaire. Termine-la puis rouvre l’extension sur la fiche produit." : data.blocked ? data.nameFromURL ? "Le site a refusé la page. Le nom vient du lien : vérifie-le et complète le souhait." : "Le site a refusé la page. Tu peux conserver le lien et compléter le souhait." : data.nameFromURL ? "La page ne fournit pas de données produit. Le nom vient du lien : vérifie-le et complète le souhait." : data.nameTruncated ? "Le nom a été raccourci à 100 caractères : vérifie les suggestions." : data.name ? "Suggestions prêtes à vérifier." : "Aucun nom fiable trouvé ; tu peux compléter les champs.";
  if (data.displayedVariantPrice) status.textContent += " Le prix correspond à la variante affichée : vérifie-la.";
  if (data.storeOnly) status.textContent += " Le marchand indique une disponibilité en magasin uniquement.";
  if (chosen) status.textContent += " L’image choisie est prête à être gardée : vérifie son aperçu.";
  save.hidden = false;
  chooseImage.hidden = !!data.blocked;
  copyVisibleImage.hidden = !data.image || !!data.blocked;
  keepVisibleImage.hidden = copyVisibleImage.hidden;
}

result.addEventListener("click", async (event) => {
  const field = event.target.dataset.copy;
  if (!field) return;
  const value = document.getElementById(field).value;
  if (!value) { status.textContent = "Ce champ est vide."; return; }
  try { await navigator.clipboard.writeText(value); status.textContent = "Copié dans le presse-papiers."; }
  catch { status.textContent = "La copie a échoué : sélectionne le texte pour le copier."; }
});

chooseImage.addEventListener("click", async () => {
  const data = readSuggestion();
  let pageURL;
  try { pageURL = new URL(activeTab.url); pageURL.hash = ""; } catch { /* The active page must have a valid URL. */ }
  if (!data || !pageURL || data.url !== pageURL.href) { status.textContent = "Le lien doit correspondre à cette fiche produit avant de choisir son image."; return; }
  try {
    await chrome.storage.session.remove("manualImage");
    await chrome.storage.session.set({ pendingImageSelection: { tabId: activeTab.id, url: data.url, expires: Date.now() + 5 * 60 * 1000 } });
    await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, files: ["manual-pick.js"] });
    status.textContent = "Clique sur l’image du produit dans la page, puis rouvre l’extension.";
  } catch {
    await chrome.storage.session.remove("pendingImageSelection");
    status.textContent = "Impossible de choisir une image sur cette page.";
  }
});

save.addEventListener("click", async () => {
  const data = readSuggestion();
  if (!data) { status.textContent = "Vérifie le lien, le nom et le prix avant de continuer."; return; }
  try {
    const { capturedImage } = await chrome.storage.session.get("capturedImage");
    if (capturedImage && !imageMatchesSuggestion(capturedImage, data)) await chrome.storage.session.remove("capturedImage");
    await chrome.storage.session.set({ suggestion: data });
    status.textContent = "Suggestions gardées. Ouvre l’ajout d’un souhait dans MonKado, puis cette extension.";
  }
  catch { status.textContent = "Impossible de garder ces suggestions."; }
});

async function captureVisibleImage() {
  const imageURL = document.getElementById("image").value.trim();
    await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, files: ["capture.js"] });
    const locate = async () => (await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, func: (url) => globalThis.monKadoVisibleImage(url), args: [imageURL] }))[0]?.result;
    const stillActive = async () => {
      const [tab] = await chrome.tabs.query({ active: true, windowId: activeTab.windowId });
      if (tab?.id !== activeTab.id) return false;
      try {
        const current = new URL(tab.url), original = new URL(activeTab.url);
        current.hash = ""; original.hash = "";
        return current.href === original.href;
      } catch { return false; }
    };
    if (!await stillActive()) throw new Error("Reviens sur la fiche produit et réessaie.");
    let before = await locate();
    if (!before) before = (await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, func: (url) => globalThis.monKadoPositionImage(url), args: [imageURL] }))[0]?.result;
    if (!before) throw new Error("Affiche entièrement l’image détectée, sans élément par-dessus. Réduis le zoom si nécessaire.");
    if (!await stillActive()) throw new Error("Reviens sur la fiche produit et réessaie.");
    const screenshot = await chrome.tabs.captureVisibleTab(activeTab.windowId, { format: "png" });
    const after = await locate();
    if (!await stillActive() || !after || ["x", "y", "width", "height", "viewportWidth", "viewportHeight"].some((key) => Math.abs(after[key] - before[key]) > 0.5)) {
      throw new Error("La page a changé pendant la capture. Réessaie.");
    }
    const dimensions = globalThis.monKadoScreenshotDimensions(screenshot);
    if (!dimensions) throw new Error("Screenshot size out of bounds");
    const bitmap = await createImageBitmap(await (await fetch(screenshot)).blob());
    try {
      if (bitmap.width !== dimensions.width || bitmap.height !== dimensions.height) throw new Error("Invalid screenshot dimensions");
      const scaleX = bitmap.width / before.viewportWidth, scaleY = bitmap.height / before.viewportHeight;
      if (scaleX < 0.9 || scaleX > 4 || Math.abs(scaleX - scaleY) > 0.05) throw new Error("Unexpected screenshot dimensions");
      const x = Math.ceil(before.x * scaleX), y = Math.ceil(before.y * scaleY);
      const right = Math.floor((before.x + before.width) * scaleX), bottom = Math.floor((before.y + before.height) * scaleY);
      const width = right - x, height = bottom - y;
      if (width < 1 || height < 1 || width * height > 16_000_000 || right > bitmap.width || bottom > bitmap.height) throw new Error("Image dimensions out of bounds");
      const canvas = document.createElement("canvas"); canvas.width = width; canvas.height = height;
      canvas.getContext("2d").drawImage(bitmap, x, y, width, height, 0, 0, width, height);
      const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/png"));
      if (!blob || !blob.size || blob.size > 10 * 1024 * 1024) throw new Error("Image size out of bounds");
      return blob;
    } finally { bitmap.close(); }
}

function captureFailure(error) {
  const safe = ["Reviens sur la fiche produit et réessaie.", "Affiche entièrement l’image détectée, sans élément par-dessus. Réduis le zoom si nécessaire.", "La page a changé pendant la capture. Réessaie."];
  return safe.includes(error?.message) ? error.message : "Impossible de capturer l’image visible. Tu peux utiliser le menu du navigateur.";
}

copyVisibleImage.addEventListener("click", async () => {
  try {
    const blob = await captureVisibleImage();
    await navigator.clipboard.write([new ClipboardItem({ "image/png": blob })]);
    status.textContent = "Image visible copiée. Vérifie son aperçu dans MonKado.";
  } catch (error) { status.textContent = captureFailure(error); }
});

keepVisibleImage.addEventListener("click", async () => {
  const data = readSuggestion();
  let pageURL;
  try { pageURL = new URL(activeTab.url); pageURL.hash = ""; } catch { /* The active page must have a valid URL. */ }
  if (!data || !pageURL || data.url !== pageURL.href) { status.textContent = "Le lien doit correspondre à cette fiche produit avant de garder son image."; return; }
  try {
    const blob = await captureVisibleImage();
    if (blob.size > maxSavedImageBytes) { status.textContent = "L’image visible dépasse 4 Mio : essaie de la copier à la place."; return; }
    const dataURL = await new Promise((resolve, reject) => {
      const reader = new FileReader(); reader.onload = () => resolve(reader.result); reader.onerror = () => reject(reader.error); reader.readAsDataURL(blob);
    });
    await chrome.storage.session.set({ capturedImage: { url: data.url, source: { name: data.name, price: data.price, image: data.image }, type: "image/png", encoded: String(dataURL).split(",", 2)[1] } });
    status.textContent = "Image gardée pour cette fiche. Vérifie son aperçu dans MonKado.";
  } catch (error) { status.textContent = captureFailure(error); }
});

apply.addEventListener("click", async () => {
  const data = readSuggestion();
  if (!data) { status.textContent = "Vérifie le lien, le nom et le prix avant de continuer."; return; }
  try {
    await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, files: ["apply.js"] });
    const [execution] = await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, func: (values) => globalThis.monKadoApplySuggestion(values), args: [data] });
    status.textContent = execution?.result?.message || "Impossible de remplir ce formulaire.";
  } catch { status.textContent = "Impossible de remplir ce formulaire."; }
});

useSavedImage.addEventListener("click", async () => {
  const data = readSuggestion();
  try {
    const { capturedImage } = await chrome.storage.session.get("capturedImage");
    if (!imageMatchesSuggestion(capturedImage, data) || capturedImage.type !== "image/png" || typeof capturedImage.encoded !== "string" || !capturedImage.encoded || capturedImage.encoded.length > 4 * Math.ceil(maxSavedImageBytes / 3)) {
      status.textContent = "Aucune image gardée ne correspond aux informations affichées."; return;
    }
    await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, files: ["apply.js"] });
    const image = { type: capturedImage.type, encoded: capturedImage.encoded, expectedURL: capturedImage.url };
    const [execution] = await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, func: (value) => globalThis.monKadoApplyImage(value), args: [image] });
    status.textContent = execution?.result?.message || "Impossible de transférer l’image.";
  } catch { status.textContent = "Impossible de transférer l’image gardée."; }
});

pasteImage.addEventListener("click", async () => {
  try {
    const entries = await navigator.clipboard.read();
    const supported = ["image/png", "image/jpeg", "image/webp"];
    const entry = entries.find((item) => supported.some((type) => item.types.includes(type)));
    if (!entry) { status.textContent = "Copie d’abord une image JPEG, PNG ou WebP depuis le site marchand."; return; }
    const type = supported.find((value) => entry.types.includes(value));
    const blob = await entry.getType(type);
    if (!blob.size || blob.size > 10 * 1024 * 1024) { status.textContent = "L’image copiée doit mesurer au maximum 10 Mio."; return; }
    const dataURL = await new Promise((resolve, reject) => {
      const reader = new FileReader(); reader.onload = () => resolve(reader.result); reader.onerror = () => reject(reader.error); reader.readAsDataURL(blob);
    });
    const encoded = String(dataURL).split(",", 2)[1];
    await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, files: ["apply.js"] });
    const [execution] = await chrome.scripting.executeScript({ target: { tabId: activeTab.id }, func: (image) => globalThis.monKadoApplyImage(image), args: [{ type, encoded }] });
    status.textContent = execution?.result?.message || "Impossible de transférer l’image.";
  } catch { status.textContent = "Impossible de lire l’image copiée. Tu peux l’ajouter depuis un fichier."; }
});

clear.addEventListener("click", async () => {
  await chrome.storage.session.remove(["suggestion", "capturedImage", "manualImage", "pendingImageSelection"]);
  result.hidden = true; status.textContent = "Suggestions effacées.";
});

readActivePage().catch((error) => { status.textContent = error.message || "Impossible de lire cette page."; });
