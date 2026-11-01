// Manual experiment: the person, not the test, completes any merchant verification.
const { chromium } = require("../../../../mon-kado-front/node_modules/@playwright/test");
const { spawn } = require("node:child_process");
const { createServer } = require("node:net");
const path = require("node:path");
const readline = require("node:readline");

const repo = path.resolve(__dirname, "../../..");
const extension = path.resolve(__dirname, "..");
const sites = {
  belgium: { host: "www.fr.fnac.be", url: "https://www.fr.fnac.be/POP-TV-Wednesday-Rave-n-W/a19427058?oref=9dd7ca02-4f7c-413e-41da-bd307ea09a80" },
  france: { host: "www.fnac.com", url: "https://www.fnac.com/Figurine-Funko-Pop-TV-Wednesday-Addams/a21262654/w-4" },
};
const site = sites[process.argv[2]];
if (!site) throw new Error("Choose belgium or france.");
const fixture = process.env.DIRECT_FNAC_FIXTURE === "1";

async function startGuard() {
  const child = spawn(path.join(repo, ".local/direct-campaign-guard.exe"), [], { cwd: repo, stdio: ["ignore", "pipe", "ignore"] });
  const lines = readline.createInterface({ input: child.stdout });
  const port = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => { child.kill(); reject(new Error("Guard startup timed out")); }, 30000);
    lines.once("line", (line) => { clearTimeout(timer); /^\d+$/.test(line) ? resolve(Number(line)) : reject(new Error("Invalid guard startup")); });
    child.once("exit", () => { clearTimeout(timer); reject(new Error("Guard stopped")); });
  });
  return { child, port };
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

async function run() {
  const guard = await startGuard(); let context;
  try {
    context = await chromium.launchPersistentContext("", { executablePath: path.join(repo, ".local/direct-poc-chromium/chrome.exe"), headless: fixture,
      locale: "fr-FR", serviceWorkers: "block", ignoreDefaultArgs: ["--disable-extensions"],
      args: [`--disable-extensions-except=${extension}`, `--load-extension=${extension}`, "--enable-unsafe-extension-debugging",
        `--proxy-server=http://127.0.0.1:${guard.port}`, "--proxy-bypass-list=<-loopback>", "--disable-quic", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp"] });
    await verifyGuard(context);
    let admitted = 0;
    await context.route("**/*", (route) => {
      const request = route.request();
      if (request.url().startsWith("chrome-extension://")) return route.continue();
      let url; try { url = new URL(request.url()); } catch { return route.abort(); }
      const challengePost = request.method() === "POST" && (url.hostname === "captcha-delivery.com" || url.hostname.endsWith(".captcha-delivery.com"));
      if ((!(["GET", "HEAD"].includes(request.method())) && !challengePost) || ["media", "font"].includes(request.resourceType()) || ++admitted > 200) return route.abort();
      return route.continue();
    });
    await context.routeWebSocket("**/*", (socket) => socket.close());
    if (fixture) await context.route("**/*", (route) => {
      const url = route.request().url();
      if (url.startsWith("chrome-extension://")) return route.continue();
      if (url !== site.url) return route.abort();
      const name = process.argv[2] === "belgium" ? "Figurine Funko Pop TV Wednesday Rave'n W" : "Figurine Funko Pop TV Wednesday Addams";
      const price = process.argv[2] === "belgium" ? "24.48" : "12.34";
      const product = { "@type": "Product", name, url: site.url, image: "https://static.fnac-static.com/product.jpg", offers: { priceCurrency: "EUR", price } };
      return route.fulfill({ contentType: "text/html; charset=utf-8", body: `<!doctype html><h1>${name}</h1><script type="application/ld+json">${JSON.stringify(product)}</script>` });
    });
    const page = context.pages()[0] || await context.newPage();
    const response = await page.goto(site.url, { waitUntil: "domcontentloaded", timeout: 20000 });
    await page.waitForLoadState("load", { timeout: 5000 }).catch(() => {});
    const productPresent = async () => {
      const current = new URL(page.url());
      return current.hostname === site.host && current.pathname === new URL(site.url).pathname && await page.locator("h1").first().textContent({ timeout: 2500 }).then((title) => /Wednesday/i.test(title || "")).catch(() => false);
    };
    if (!await productPresent()) {
      const current = new URL(page.url());
      const title = await page.title();
      const verification = /captcha|vérifi|verification|robot|just a moment/i.test(title) || /(?:^|\.)captcha-delivery\.com$/i.test(current.hostname) || /DDUser[-_]Challenge/i.test(current.pathname);
      if (!verification) throw new Error(`Merchant did not provide a product or verification page (HTTP ${response?.status() || 0})`);
      console.log(JSON.stringify({ state: "awaiting_human_verification", merchant: process.argv[2] }));
      await new Promise((resolve, reject) => {
        const input = readline.createInterface({ input: process.stdin });
        const timer = setTimeout(() => { input.close(); reject(new Error("Manual verification timed out")); }, 300000);
        input.once("line", (line) => { clearTimeout(timer); input.close(); line.trim() === "ready" ? resolve() : reject(new Error("Manual verification cancelled")); });
      });
      if (!await productPresent()) throw new Error("Product page is not visible after manual verification");
    }
    const cdp = await context.browser().newBrowserCDPSession();
    const entry = (await cdp.send("Extensions.getExtensions")).extensions.find((item) => item.path === extension);
    const merchant = (await cdp.send("Target.getTargets", { filter: [{ type: "tab" }] })).targetInfos.find((item) => { try { return new URL(item.url).hostname === site.host; } catch { return false; } });
    if (!entry || !merchant) throw new Error("Extension or merchant tab unavailable");
    await cdp.send("Target.activateTarget", { targetId: merchant.targetId });
    await cdp.send("Extensions.triggerAction", { id: entry.id, targetId: merchant.targetId });
    const popup = await context.newPage(); await popup.goto(`chrome-extension://${entry.id}/popup.html`);
    await cdp.send("Target.activateTarget", { targetId: merchant.targetId }); await popup.reload();
    await popup.locator("#save").waitFor({ state: "visible", timeout: 10000 });
    const name = await popup.locator("#name").inputValue(), price = await popup.locator("#price").inputValue(), imageAvailable = !!await popup.locator("#image").inputValue();
    const result = { state: name && price && imageAvailable ? "extracted" : "partial", source: fixture ? "simulated" : "merchant", merchant: process.argv[2], name, price, imageAvailable, admittedRequests: admitted };
    console.log(JSON.stringify(result));
  } finally { if (context) await context.close(); guard.child.kill(); }
}
run().catch((error) => {
  const safe = /^(Merchant did not provide a product or verification page \(HTTP \d+\)|Manual verification timed out|Manual verification cancelled|Product page is not visible after manual verification|Browser bypassed the network guard|Guard startup timed out|Guard stopped|Invalid guard startup|Extension or merchant tab unavailable)$/;
  console.error(safe.test(error.message) ? error.message : "Browser or merchant navigation failed"); process.exitCode = 1;
});
