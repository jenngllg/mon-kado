// Verify the isolated manual demonstration with simulated merchant and API data.
const { chromium, expect } = require("../../../../mon-kado-front/node_modules/@playwright/test");
const { resolve } = require("node:path");

const listId = "019c52dd-56c1-7cc6-8a95-243f3a032e04";
const extension = resolve(__dirname, "..");
const merchantURL = "https://www.fr.fnac.be/POP-TV-Wednesday-Rave-n-W/a19427058?oref=9dd7ca02-4f7c-413e-41da-bd307ea09a80";
const imageURL = "https://static.fnac-static.com/multimedia/product.png";
const name = "Figurine Funko Pop TV Wednesday Rave'n W", price = "24,48";

async function run() {
  const context = await chromium.launchPersistentContext("", { executablePath: process.env.DIRECT_IMPORT_CHROMIUM, headless: true,
    ignoreDefaultArgs: ["--disable-extensions"], args: [`--disable-extensions-except=${extension}`, `--load-extension=${extension}`, "--no-proxy-server", "--enable-unsafe-extension-debugging"] });
  try {
    const blank = context.pages()[0] || await context.newPage();
    const png = await blank.evaluate(() => { const c = document.createElement("canvas"); c.width = c.height = 150; const g = c.getContext("2d"); g.fillStyle = "#e61e14"; g.fillRect(0, 0, 150, 150); return c.toDataURL("image/png").split(",")[1]; });
    const unexpected = [];
    await context.route("**/*", (route) => {
      const url = route.request().url();
      if (url.startsWith("chrome-extension://") || url.startsWith("http://localhost:5176/") || url.startsWith("http://127.0.0.1:7006/")) return route.continue();
      if (url === merchantURL) return route.fulfill({ contentType: "text/html; charset=utf-8", body: `<!doctype html><h1>${name}</h1><img src="${imageURL}" style="width:200px;height:200px"><script type="application/ld+json">${JSON.stringify({ "@type": "Product", name, image: imageURL, offers: { price: "24.48", priceCurrency: "EUR" } })}</script>` });
      if (url === imageURL) return route.fulfill({ contentType: "image/png", body: Buffer.from(png, "base64") });
      unexpected.push(new URL(url).origin); return route.abort();
    });
    await blank.goto(merchantURL); await blank.locator("img").evaluate((image) => image.decode());
    const cdp = await context.browser().newBrowserCDPSession();
    const entry = (await cdp.send("Extensions.getExtensions")).extensions.find((item) => item.path === extension);
    const findTab = async (prefix) => (await cdp.send("Target.getTargets", { filter: [{ type: "tab" }] })).targetInfos.find((tab) => tab.url.startsWith(prefix));
    const merchant = await findTab(merchantURL);
    if (!entry || !merchant) throw new Error("Extension or simulated merchant unavailable");
    await cdp.send("Extensions.triggerAction", { id: entry.id, targetId: merchant.targetId });
    const popup = await context.newPage(); await popup.goto(`chrome-extension://${entry.id}/popup.html`);
    await cdp.send("Target.activateTarget", { targetId: merchant.targetId }); await popup.reload();
    await expect(popup.locator("#name")).toHaveValue(name);
    await popup.locator("#save").click(); await popup.locator("#keep-visible-image").click();
    await expect(popup.locator("#status")).toContainText("Image gardée pour cette fiche");
    const app = await context.newPage(); await app.goto(`http://localhost:5176/lists/${listId}`);
    await app.getByRole("link", { name: "Ajouter un souhait", exact: true }).click();
    await app.getByRole("textbox", { name: /Lien du produit/ }).waitFor();
    const appTab = await findTab("http://localhost:5176/");
    if (!appTab) throw new Error("Demo frontend unavailable");
    await cdp.send("Extensions.triggerAction", { id: entry.id, targetId: appTab.targetId });
    await cdp.send("Target.activateTarget", { targetId: appTab.targetId }); await popup.reload();
    await popup.locator("#apply").click();
    await expect(popup.locator("#status")).toContainText("Champs remplis");
    await expect(app.getByRole("textbox", { name: /Lien du produit/ })).toHaveValue(merchantURL);
    await app.getByRole("button", { name: "Continuer", exact: true }).click();
    await expect(app.getByRole("textbox", { name: /Nom du souhait/ })).toHaveValue(name);
    await expect(app.getByRole("textbox", { name: /Prix en euros/ })).toHaveValue(price);
    await popup.locator("#apply").click();
    await expect(popup.locator("#status")).toContainText("Reviens d’abord à l’étape du lien");
    await popup.locator("#use-saved-image").click();
    await expect(popup.locator("#status")).toContainText("Image transmise");
    await expect(app.getByRole("img", { name: "Image du souhait" })).toBeVisible();
    const blockedWrite = await context.request.post(`http://127.0.0.1:7006/api/v1/wishlists/${listId}/wishes`, { data: {} });
    expect(blockedWrite.status()).toBe(405); expect(unexpected).toEqual([]);
    console.log("Local demonstration: direct fields and image transferred; simulated API refuses writes.");
  } finally { await context.close(); }
}
run().catch((error) => { console.error(error.message); process.exitCode = 1; });
