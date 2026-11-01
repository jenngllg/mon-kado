// Manual research only: all merchant traffic is direct and constrained by a local public-IP guard.
const { chromium } = require("../../../../mon-kado-front/node_modules/@playwright/test");
const { spawn } = require("node:child_process");
const { createServer } = require("node:net");
const fs = require("node:fs");
const path = require("node:path");
const readline = require("node:readline");

const repo = path.resolve(__dirname, "../../..");
const inventory = path.resolve(repo, ".local/group-release-20261027/merchant-audit-100-v2/inventory-before-generic.json");
const out = process.argv[2];
if (!out) throw new Error("Provide a new output .ndjson file under .local.");
const output = path.resolve(out);
if (!output.startsWith(path.resolve(repo, ".local") + path.sep) || fs.existsSync(output)) throw new Error("Output must be a new file under .local.");
const inventoryRows = JSON.parse(fs.readFileSync(inventory, "utf8")).rows;
if (inventoryRows.length !== 100 || new Set(inventoryRows.map((row) => row.id)).size !== 100 || inventoryRows.some((row) => !row.url)) throw new Error("The original 100-link inventory is incomplete.");
const ids = process.env.DIRECT_CAMPAIGN_IDS ? new Set(process.env.DIRECT_CAMPAIGN_IDS.split(",").map(Number)) : null;
const controls = [
  { id: 101, name: "Fnac Belgique", url: "https://www.fr.fnac.be/POP-TV-Wednesday-Rave-n-W/a19427058?oref=9dd7ca02-4f7c-413e-41da-bd307ea09a80" },
  { id: 102, name: "Fnac France", url: "https://www.fnac.com/Figurine-Funko-Pop-TV-Wednesday-Addams/a21262654/w-4" },
];
const requestedControls = [...controls,
  { id: 103, name: "Cultura Buffy", url: "https://www.cultura.com/p-buffy-contre-les-vampires-figurine-pop-buffy-w-scythe-9-cm-12661219.html" },
  { id: 104, name: "Missiron", url: "https://missiron.com/fr-fr/products/warrior-s-wing-book-nook" },
  { id: 105, name: "La Redoute", url: "https://www.laredoute.fr/ppdp/prod-602388338.aspx?aecid=6ae1f7cb6e208cfe860dfc30#searchkeyword=claris&shoppingtool=search" },
];
const candidates = process.env.DIRECT_CAMPAIGN_USER_LINKS === "1" ? requestedControls : process.env.DIRECT_CAMPAIGN_FNAC === "1" ? controls : inventoryRows;
const rows = ids ? candidates.filter((row) => ids.has(row.id)) : candidates;
if (!rows.length || ids && rows.length !== ids.size) throw new Error("Invalid selected inventory IDs.");
const source = fs.readFileSync(path.resolve(__dirname, "../extractor.js"), "utf8");
const captureSource = fs.readFileSync(path.resolve(__dirname, "../capture.js"), "utf8");

async function startGuard() {
  const binary = path.resolve(repo, ".local/direct-campaign-guard.exe");
  const process = spawn(binary, [], { cwd: repo, stdio: ["ignore", "pipe", "pipe"] });
  const lines = readline.createInterface({ input: process.stdout });
  const port = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => { process.kill(); reject(new Error("Network guard startup timed out")); }, 30000);
    lines.once("line", (line) => { clearTimeout(timer); /^\d+$/.test(line) ? resolve(Number(line)) : reject(new Error("Invalid guard startup")); });
    process.once("exit", () => { clearTimeout(timer); reject(new Error("Network guard stopped during startup")); });
  });
  return { process, port };
}

async function verifyGuard(browser) {
  let hits = 0;
  const server = createServer((connection) => { hits++; connection.end("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"); });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const page = await browser.newPage();
  try {
    try { await page.goto(`http://127.0.0.1:${server.address().port}/`, { timeout: 5000 }); } catch { /* A rejected connection is expected. */ }
    if (hits) throw new Error("The browser bypassed the public-only network guard.");
  } finally { await page.close(); await new Promise((resolve) => server.close(resolve)); }
}

async function run() {
  const guard = await startGuard(); let browser;
  try {
    browser = await chromium.launch({ executablePath: path.resolve(repo, ".local/direct-poc-chromium/chrome.exe"), headless: process.env.DIRECT_CAMPAIGN_HEADLESS === "1",
      args: [`--proxy-server=http://127.0.0.1:${guard.port}`, "--proxy-bypass-list=<-loopback>", "--disable-quic", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp"] });
    await verifyGuard(browser);
    fs.writeFileSync(output, "", { flag: "wx" });
    const capture = process.env.DIRECT_CAMPAIGN_CAPTURE === "1" ? `${output}.html` : "";
    if (capture) fs.mkdirSync(capture);
    let next = 0, completed = 0;
    const results = [];
    async function worker() {
      while (next < rows.length) {
        const row = rows[next++]; const started = Date.now();
        const context = await browser.newContext({ locale: "fr-FR", serviceWorkers: "block" });
        let requests = 0; let admittedRequests = 0; let blockedRequests = 0;
        await context.route("**/*", (route) => {
          const request = route.request(); const type = request.resourceType();
          requests++;
          if (!["GET", "HEAD"].includes(request.method()) || ["media", "font"].includes(type) || type === "image" && process.env.DIRECT_CAMPAIGN_ALLOW_IMAGES !== "1" || ++admittedRequests > 80) { blockedRequests++; return route.abort(); }
          return route.continue();
        });
        await context.routeWebSocket("**/*", (socket) => socket.close());
        const page = await context.newPage(); let item;
        const deadline = setTimeout(() => { void page.close(); }, 20000);
        try {
          const response = await page.goto(row.url, { waitUntil: "domcontentloaded", timeout: 15000 });
          await page.waitForLoadState("load", { timeout: 3000 }).catch(() => {});
          if (!/^https?:\/\//i.test(page.url())) { const error = new Error("Browser navigation did not reach a web page"); error.name = "browser_navigation_error"; throw error; }
          if (process.env.DIRECT_CAMPAIGN_APPLE_CHOICE && row.id === 77) {
            const choice = process.env.DIRECT_CAMPAIGN_APPLE_CHOICE;
            if (!["usbc", "wirelesscharging"].includes(choice)) throw new Error("Invalid Apple option");
            const option = page.locator(`input[type="radio"][name="dimensionCharging"][value="${choice}"]`);
            await option.evaluate((input) => { if (!input.labels?.[0]) throw new Error("Option label unavailable"); input.labels[0].click(); });
            if (!await option.isChecked()) throw new Error("Apple option was not selected");
          }
          if (capture) fs.writeFileSync(path.join(capture, `${row.id}.html`), await page.content());
          const evidence = await page.evaluate((script) => {
            (0, eval)(script);
            const title = document.title || "", heading = document.querySelector("h1")?.textContent || "";
            return { data: globalThis.monKadoExtractPage(), challenge: /access denied|accès refusé|verify you are human|vérifi(?:ez|cation).*humain|captcha|just a moment|maintenance|robot check/i.test(`${title} ${heading}`) };
          }, source);
          const result = evidence.data; const blocked = !!result.blocked || evidence.challenge || (response?.status() ?? 0) >= 400;
          const finalURL = new URL(page.url());
          item = { id: row.id, merchant: row.name, url: row.url, finalHost: finalURL.hostname, finalChallengePath: /(?:^|\/)DDUser[-_]Challenge(?:\/|$)/i.test(finalURL.pathname), httpStatus: response?.status() ?? null, durationMs: Date.now() - started,
            blocked, name: blocked && !result.nameFromURL ? "" : result.name || "", nameFromURL: !!result.nameFromURL,
            price: blocked ? "" : result.price || "", displayedVariantPrice: !blocked && !!result.displayedVariantPrice, storeOnly: !blocked && !!result.storeOnly, imageCandidate: !blocked && !!result.image, imageURL: blocked ? "" : result.image || "", imageValidated: false,
            requests, blockedRequests, error: null };
          if (process.env.DIRECT_CAMPAIGN_ALLOW_IMAGES === "1" && result.image) {
            Object.assign(item, await page.evaluate(({ script, url }) => {
              (0, eval)(script);
              const imageLoaded = globalThis.monKadoMatchingImages(url).length > 0;
              const imageVisible = !!globalThis.monKadoVisibleImage(url);
              const imageVisibleAfterScroll = imageLoaded && !imageVisible && !!globalThis.monKadoPositionImage(url);
              return { imageLoaded, imageVisible, imageVisibleAfterScroll };
            }, { script: captureSource, url: result.image }));
            if (process.env.DIRECT_CAMPAIGN_DIAG === "1") item.imageDetails = await page.evaluate((url) => {
              return globalThis.monKadoMatchingImages(url).slice(0, 4).map(({ img }) => {
                const rect = img.getBoundingClientRect(); const style = getComputedStyle(img);
                return { width: Math.round(rect.width), height: Math.round(rect.height), x: Math.round(rect.left), y: Math.round(rect.top), viewportWidth: innerWidth, viewportHeight: innerHeight, visibility: style.visibility,
                  center: document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2) === img,
                  overlay: (() => { const top = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2); if (!top || top === img) return null; const css = getComputedStyle(top); return { tag: top.tagName, classes: top.getAttribute("class"), background: css.backgroundColor, backgroundImage: css.backgroundImage, border: css.borderWidth, shadow: css.boxShadow, filter: css.filter, backdrop: css.backdropFilter, blend: css.mixBlendMode, directText: [...top.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim()), parentContainsImage: !!top.parentElement?.contains(img), stackContainsImage: document.elementsFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2).includes(img), pseudo: ["::before", "::after"].map((pseudo) => { const style = getComputedStyle(top, pseudo); return { content: style.content, background: style.backgroundColor, backgroundImage: style.backgroundImage, border: style.borderWidth, shadow: style.boxShadow }; }), children: [...top.querySelectorAll("*")].map((child) => { const bounds = child.getBoundingClientRect(), style = getComputedStyle(child); return { tag: child.tagName, width: bounds.width, height: bounds.height, display: style.display, visibility: style.visibility, shadow: style.boxShadow, border: style.borderWidth, filter: style.filter, backdrop: style.backdropFilter, pseudo: [getComputedStyle(child, "::before").content, getComputedStyle(child, "::after").content] }; }) }; })(),
                  points: [[0.1, 0.1], [0.9, 0.1], [0.5, 0.5], [0.1, 0.9], [0.9, 0.9]].map(([x, y]) => { const top = document.elementFromPoint(rect.left + rect.width * x, rect.top + rect.height * y); return top === img ? "image" : `${top?.tagName || "none"}.${top?.getAttribute("class") || ""}`.slice(0, 120); }) };
              });
            }, result.image);
          }
          if (process.env.DIRECT_CAMPAIGN_DIAG === "1") item.diagnostic = await page.evaluate(() => {
            const ancestors = []; let node = document.querySelector("h1");
            for (let i = 0; node && i < 8; i++, node = node.parentElement) {
              const prices = [...node.querySelectorAll('[class*="price" i]')].map((element) => { const parent = element.closest('[hidden], [aria-hidden="true"], del, s, [class*="shipping" i], [class*="delivery" i], [class*="livraison" i], [class*="unit-price" i], [class*="old-price" i], [class*="price--old" i]'); return { text: (element.innerText || "").trim().replace(/\s+/g, " ").slice(0, 60), visible: !!element.getClientRects().length, excluded: !!parent, excludedBy: parent ? `${parent.tagName} ${parent.getAttribute("class") || ""} aria-hidden=${parent.getAttribute("aria-hidden")}`.slice(0, 150) : "" }; }).filter((price) => /€/.test(price.text)).slice(0, 6);
              if (prices.length) ancestors.push({ level: i, prices });
            }
            return ancestors;
          });
        } catch (error) {
          item = { id: row.id, merchant: row.name, url: row.url, httpStatus: null, durationMs: Date.now() - started, blocked: false,
            name: "", nameFromURL: false, price: "", imageCandidate: false, imageValidated: false, requests, blockedRequests,
            error: error.message?.match(/net::ERR_[A-Z_]+/)?.[0] || error.name };
        } finally { clearTimeout(deadline); await context.close(); }
        results.push(item); fs.appendFileSync(output, JSON.stringify(item) + "\n");
        completed++; if (completed % 10 === 0 || completed === rows.length) console.log(JSON.stringify({ completed, total: rows.length }));
      }
    }
    await Promise.all(Array.from({ length: 3 }, () => worker()));
    const summary = { total: results.length, completed: results.filter((x) => !x.error).length, blocked: results.filter((x) => x.blocked).length,
      productName: results.filter((x) => x.name && !x.nameFromURL && !x.blocked).length, urlName: results.filter((x) => x.nameFromURL).length,
      price: results.filter((x) => x.price).length, imageCandidate: results.filter((x) => x.imageCandidate).length,
      completeCandidates: results.filter((x) => x.name && !x.nameFromURL && x.price && x.imageCandidate && !x.blocked).length,
      imageValidated: 0, errors: results.filter((x) => x.error).length };
    console.log(JSON.stringify({ summary, output }));
  } finally { if (browser) await browser.close(); guard.process.kill(); }
}
run().catch((error) => { console.error(error.message); process.exitCode = 1; });
