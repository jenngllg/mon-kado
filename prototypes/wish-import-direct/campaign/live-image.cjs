// Explicit manual experiment against one public merchant page, with a local public-IP guard.
const { chromium, expect } = require("../../../../mon-kado-front/node_modules/@playwright/test");
const { spawn } = require("node:child_process");
const { createServer } = require("node:net");
const { createHash } = require("node:crypto");
const path = require("node:path");
const readline = require("node:readline");
const { pathToFileURL } = require("node:url");

const repo = path.resolve(__dirname, "../../..");
const extension = path.resolve(__dirname, "..");
const sites = {
  joueclub: { label: "JouéClub", url: "https://www.joueclub.fr/jeux-de-constructions-maquettes/lego-icons-10311-l-orchidee-5702017224336.html", name: /ORCHIDEE/i },
  tediber: { label: "Tediber", url: "https://www.tediber.com/products/oreiller", name: /Oreiller incroyable/i },
  bose: { label: "Bose", url: "https://www.bose.fr/p/headphones/bose-quietcomfort-headphones-2nd-gen/QC2G-HEADPHONEARN.html", name: /casque QuietComfort/i, maxRequests: 200 },
};
const site = sites[process.argv[2] || "joueclub"];
if (!site) throw new Error("Choose joueclub, tediber or bose.");

async function startGuard() {
  const process = spawn(path.join(repo, ".local/direct-campaign-guard.exe"), [], { cwd: repo, stdio: ["ignore", "pipe", "pipe"] });
  const lines = readline.createInterface({ input: process.stdout });
  const port = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => { process.kill(); reject(new Error("Network guard startup timed out")); }, 30000);
    lines.once("line", (line) => { clearTimeout(timer); /^\d+$/.test(line) ? resolve(Number(line)) : reject(new Error("Invalid guard startup")); });
    process.once("exit", () => { clearTimeout(timer); reject(new Error("Network guard stopped")); });
  });
  return { process, port };
}

async function verifyGuard(context) {
  let hits = 0;
  const server = createServer((connection) => { hits++; connection.end("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"); });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const page = await context.newPage();
  try {
    try { await page.goto(`http://127.0.0.1:${server.address().port}/`, { timeout: 5000 }); } catch { /* Rejection is expected. */ }
    if (hits) throw new Error("Browser bypassed the network guard");
  } finally { await page.close(); await new Promise((resolve) => server.close(resolve)); }
}

async function verifyFrontendTransfer(context, page, popup, cdp, extensionID, suggestion) {
  const { capturedImage } = await popup.evaluate(() => chrome.storage.session.get("capturedImage"));
  const expectedDigest = createHash("sha256").update(Buffer.from(capturedImage.encoded, "base64")).digest("hex");
  // No merchant page remains open once the controlled frontend and fake API are installed.
  await page.close();
  const front = path.resolve(repo, "../mon-kado-front");
  const { controlledApi, listId, frontendOrigin } = await import(pathToFileURL(path.join(front, "e2e/controlledApi.js")).href);
  const api = await controlledApi(context); api.state.authenticated = true;
  await context.route("chrome-extension://**", (route) => route.continue());
  await context.route("http://localhost:5173/**", async (route) => {
    if (!["GET", "HEAD"].includes(route.request().method())) return route.abort();
    const url = new URL(route.request().url()); url.host = "127.0.0.1:5174";
    const response = await fetch(url.href);
    await route.fulfill({ status: response.status, headers: { "Content-Type": response.headers.get("content-type") || "application/octet-stream" }, body: Buffer.from(await response.arrayBuffer()) });
  });
  let previewCalls = 0;
  await context.route(`**/api/v1/wishlists/${listId}/wish-import-previews`, (route) => {
    previewCalls++;
    return route.fulfill({ status: 200, headers: { "Access-Control-Allow-Origin": frontendOrigin, "Access-Control-Allow-Credentials": "true", "Content-Type": "application/json" }, body: "{}" });
  });
  const app = await context.newPage();
  await app.goto(`http://localhost:5173/lists/${listId}`);
  await app.getByRole("link", { name: "Ajouter un souhait", exact: true }).click();
  await app.getByRole("textbox", { name: /Lien du produit/ }).waitFor();
  const target = (await cdp.send("Target.getTargets", { filter: [{ type: "tab" }] })).targetInfos.find((tab) => tab.url.startsWith("http://localhost:5173/"));
  if (!target) throw new Error("Frontend tab unavailable");
  await cdp.send("Extensions.triggerAction", { id: extensionID, targetId: target.targetId });
  await cdp.send("Target.activateTarget", { targetId: target.targetId });
  await popup.reload();
  await app.clock.install();
  await popup.locator("#apply").click();
  await expect(popup.locator("#status")).toContainText("Champs remplis");
  await expect(app.getByRole("textbox", { name: /Lien du produit/ })).toHaveValue(suggestion.url);
  await app.clock.fastForward(1000);
  await app.getByRole("button", { name: "Continuer", exact: true }).click();
  await expect(app.getByRole("textbox", { name: /Nom du souhait/ })).toHaveValue(suggestion.name);
  await expect(app.getByRole("textbox", { name: /Prix en euros/ })).toHaveValue(suggestion.price);
  await popup.locator("#use-saved-image").click();
  await expect(popup.locator("#status")).toContainText("Image transmise");
  await expect(app.getByRole("img", { name: "Image du souhait" })).toBeVisible();
  const digest = await app.getByRole("img", { name: "Image du souhait" }).evaluate(async (image) => {
    const response = await fetch(image.src);
    const data = await response.arrayBuffer();
    return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", data)), (byte) => byte.toString(16).padStart(2, "0")).join("");
  });
  expect(digest).toBe(expectedDigest);
  expect(previewCalls).toBe(0); expect(api.state.wishWrites).toBe(0); expect(api.unexpected).toEqual([]);
}

async function run() {
  const guard = await startGuard(); let context;
  try {
    context = await chromium.launchPersistentContext("", { executablePath: path.join(repo, ".local/direct-poc-chromium/chrome.exe"), headless: true,
      locale: "fr-FR", serviceWorkers: "block", ignoreDefaultArgs: ["--disable-extensions"],
      args: [`--disable-extensions-except=${extension}`, `--load-extension=${extension}`, "--enable-unsafe-extension-debugging",
        `--proxy-server=http://127.0.0.1:${guard.port}`, "--proxy-bypass-list=<-loopback>", "--disable-quic", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp"] });
    await verifyGuard(context);
    let eligible = 0, admitted = 0;
    await context.route("**/*", (route) => {
      const req = route.request(), url = req.url();
      if (url.startsWith("chrome-extension://")) return route.continue();
      if (!["GET", "HEAD"].includes(req.method()) || ["media", "font"].includes(req.resourceType()) || ++eligible > (site.maxRequests || 80)) return route.abort();
      admitted++;
      return route.continue();
    });
    await context.routeWebSocket("**/*", (socket) => socket.close());
    const page = context.pages()[0] || await context.newPage();
    const response = await page.goto(site.url, { waitUntil: "domcontentloaded", timeout: 15000 });
    await page.waitForLoadState("load", { timeout: 3000 }).catch(() => {});
    if ((response?.status() || 0) >= 400) throw new Error(`Merchant refused page (${response.status()})`);
    const cdp = await context.browser().newBrowserCDPSession();
    const entry = (await cdp.send("Extensions.getExtensions")).extensions.find((item) => item.path === extension);
    if (!entry) throw new Error("Prototype extension not loaded");
    const tabs = (await cdp.send("Target.getTargets", { filter: [{ type: "tab" }] })).targetInfos;
    const merchant = tabs.find((tab) => tab.url.startsWith(new URL(site.url).origin + "/"));
    if (!merchant) throw new Error("Merchant tab not available");
    await cdp.send("Target.activateTarget", { targetId: merchant.targetId });
    await cdp.send("Extensions.triggerAction", { id: entry.id, targetId: merchant.targetId });
    const popup = await context.newPage(); await popup.goto(`chrome-extension://${entry.id}/popup.html`);
    await cdp.send("Target.activateTarget", { targetId: merchant.targetId }); await popup.reload();
    await expect(popup.locator("#name")).toHaveValue(site.name);
    if (process.env.DIRECT_IMAGE_PICK === "1") {
      const imageURL = await popup.locator("#image").inputValue();
      await popup.locator("#choose-image").click();
      await expect(popup.locator("#status")).toContainText("Clique sur l’image du produit");
      const rect = await popup.evaluate(async (url) => {
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["capture.js"] });
        return (await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: (value) => globalThis.monKadoPositionImage(value), args: [url] }))[0]?.result;
      }, imageURL);
      if (!rect) throw new Error("Merchant image is not visible for selection");
      await cdp.send("Target.activateTarget", { targetId: merchant.targetId });
      await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height / 2);
      await expect(page.getByText("Image choisie. Rouvre l’extension pour la garder.", { exact: true })).toBeVisible();
      await cdp.send("Target.activateTarget", { targetId: merchant.targetId }); await popup.reload();
      await expect(popup.locator("#status")).toContainText("L’image choisie");
    }
    await popup.evaluate(() => { Object.defineProperty(navigator, "clipboard", { configurable: true, value: { write: async (items) => {
      const blob = await items[0].getType("image/png"); const bitmap = await createImageBitmap(blob);
      globalThis.copyEvidence = { type: blob.type, bytes: blob.size, width: bitmap.width, height: bitmap.height }; bitmap.close();
    } } }); });
    await popup.locator("#copy-visible-image").click();
    await expect(popup.locator("#status")).toContainText("Image visible copiée");
    const evidence = await popup.evaluate(() => globalThis.copyEvidence);
    if (!evidence || evidence.type !== "image/png" || evidence.width < 100 || evidence.height < 100 || evidence.bytes > 10 * 1024 * 1024) throw new Error("Invalid copied image");
    const currentURL = new URL(page.url()); currentURL.hash = "";
    await popup.locator("#url").fill(currentURL.href);
    await popup.locator("#save").click();
    await expect(popup.locator("#status")).toContainText("Suggestions gardées");
    const suggestion = { name: await popup.locator("#name").inputValue(), price: await popup.locator("#price").inputValue(), url: currentURL.href };
    if (!suggestion.price) throw new Error("Merchant price was not extracted");
    await popup.locator("#keep-visible-image").click();
    await expect(popup.locator("#status")).toContainText("Image gardée pour cette fiche");
    const retained = await popup.evaluate(async () => {
      const { capturedImage } = await chrome.storage.session.get("capturedImage");
      const bytes = Uint8Array.from(atob(capturedImage.encoded), (character) => character.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes], { type: capturedImage.type }));
      const result = { url: capturedImage.url, type: capturedImage.type, bytes: bytes.length, width: bitmap.width, height: bitmap.height };
      bitmap.close(); return result;
    });
    if (retained.url !== currentURL.href || retained.type !== "image/png" || retained.width < 100 || retained.height < 100 || retained.bytes > 4 * 1024 * 1024) throw new Error("Invalid retained image");
    if (process.env.DIRECT_IMAGE_TEST_FRONT === "1") await verifyFrontendTransfer(context, page, popup, cdp, entry.id, suggestion);
    console.log(JSON.stringify({ result: process.env.DIRECT_IMAGE_TEST_FRONT === "1" ? "transferred to local form" : "copied and retained", merchant: site.label, manuallySelected: process.env.DIRECT_IMAGE_PICK === "1", type: retained.type, bytes: retained.bytes, width: retained.width, height: retained.height, admittedRequests: admitted }));
  } finally { if (context) await context.close(); guard.process.kill(); }
}
run().catch((error) => { console.error(error.message); process.exitCode = 1; });
