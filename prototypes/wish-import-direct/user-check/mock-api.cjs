// Isolated, in-memory API for the manual local frontend demonstration; it never persists data.
const http = require("node:http");

const allowedOrigin = "http://localhost:5176";
const listId = "019c52dd-56c1-7cc6-8a95-243f3a032e04";
const wishlist = { id: listId, name: "Liste de démonstration", occasion: "birthday", eventDate: "2027-12-20", message: "", isSuspended: false, isArchived: false };
const headers = { "Access-Control-Allow-Origin": allowedOrigin, "Access-Control-Allow-Credentials": "true", "Access-Control-Allow-Methods": "GET,POST,OPTIONS", "Access-Control-Allow-Headers": "Content-Type,Authorization,X-CSRF-TOKEN,X-Correlation-ID,If-Match", "Access-Control-Expose-Headers": "ETag,X-Correlation-ID", "Content-Type": "application/json", "Cache-Control": "no-store" };

const server = http.createServer((request, response) => {
  if (request.headers.origin && request.headers.origin !== allowedOrigin) { response.writeHead(403).end(); return; }
  if (request.method === "OPTIONS") { response.writeHead(204, headers).end(); return; }
  const url = new URL(request.url, "http://127.0.0.1:7006");
  const send = (status, body, etag) => { response.writeHead(status, { ...headers, ...(etag ? { ETag: etag } : {}) }); response.end(JSON.stringify(body)); };
  if (request.method === "GET" && url.pathname === "/security/csrf-token") return send(200, { token: "demo-only" });
  if (request.method === "POST" && url.pathname === "/api/v1/auth/sessions/refresh") return send(200, { accessToken: "demo-only", expiresIn: 900, tokenType: "Bearer" });
  if (request.method === "GET" && url.pathname === "/api/v1/auth/sessions/current") return send(200, { id: "019c52dd-56c1-7cc6-8a95-243f3a032e05", displayName: "Compte de démonstration", email: "demo@example.test", roles: ["member"], isGoogleLinked: false, isVisibleInMemberSearch: false }, '"demo-user"');
  if (request.method === "GET" && url.pathname === "/api/v1/wishlists") return send(200, [wishlist]);
  if (request.method === "GET" && url.pathname === `/api/v1/wishlists/${listId}`) return send(200, wishlist, '"demo-list"');
  if (request.method === "GET" && url.pathname === `/api/v1/wishlists/${listId}/wishes`) return send(200, { wishes: [] }, '"demo-wishes"');
  if (request.method === "GET" && url.pathname === `/api/v1/wishlists/${listId}/share-link`) return send(404, { statusCode: 404, title: null, message: null, errorCode: "WISHLIST_SHARE_LINK_NOT_FOUND", validationErrors: null });
  if (!["GET", "HEAD"].includes(request.method)) return send(405, { statusCode: 405, title: "Demo only", message: "This demonstration cannot save changes.", errorCode: null, validationErrors: null });
  return send(404, { statusCode: 404, title: "Not found", message: "This demonstration has no such resource.", errorCode: null, validationErrors: null });
});
server.listen(7006, "127.0.0.1", () => console.log("Isolated demo API ready on 127.0.0.1:7006; writes disabled."));
