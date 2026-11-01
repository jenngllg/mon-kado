// Check the encoded screenshot and PNG header before decoding the full image.
globalThis.monKadoScreenshotDimensions = function (dataURL) {
  const prefix = "data:image/png;base64,";
  if (typeof dataURL !== "string" || !dataURL.startsWith(prefix) || dataURL.length > 32 * 1024 * 1024) return null;
  try {
    const header = atob(dataURL.slice(prefix.length, prefix.length + 32));
    if (header.length < 24 || [..."\x89PNG\r\n\x1a\n"].some((char, index) => header[index] !== char) || header.slice(12, 16) !== "IHDR") return null;
    const uint32 = (offset) => header.charCodeAt(offset) * 0x1000000 + header.charCodeAt(offset + 1) * 0x10000 + header.charCodeAt(offset + 2) * 0x100 + header.charCodeAt(offset + 3);
    const width = uint32(16), height = uint32(20);
    return width && height && width * height <= 32_000_000 ? { width, height } : null;
  } catch { return null; }
};
