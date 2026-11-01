// Accept only the result of a recent explicit selection in the same active tab.
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || !["monkado-image-selected", "monkado-image-cancelled"].includes(message.action) || sender.id !== chrome.runtime.id || sender.frameId !== 0 || !sender.tab?.id) return;
  (async () => {
    const { pendingImageSelection } = await chrome.storage.session.get("pendingImageSelection");
    if (!pendingImageSelection || pendingImageSelection.tabId !== sender.tab.id || pendingImageSelection.expires < Date.now()) return { ok: false };
    const [tab, active] = await Promise.all([chrome.tabs.get(sender.tab.id), chrome.tabs.query({ active: true, windowId: sender.tab.windowId })]);
    let pageURL;
    try { pageURL = new URL(tab.url); pageURL.hash = ""; } catch { return { ok: false }; }
    if (active[0]?.id !== tab.id || pageURL.href !== pendingImageSelection.url) return { ok: false };
    await chrome.storage.session.remove("pendingImageSelection");
    if (message.action === "monkado-image-cancelled") return { ok: true };
    let image;
    try { image = new URL(message.image); } catch { return { ok: false }; }
    if (!["http:", "https:"].includes(image.protocol) || image.username || image.password || image.href.length > 4096) return { ok: false };
    await chrome.storage.session.set({ manualImage: { tabId: tab.id, url: pageURL.href, image: image.href, expires: pendingImageSelection.expires } });
    return { ok: true };
  })().then(sendResponse, () => sendResponse({ ok: false }));
  return true;
});
