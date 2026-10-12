# Native local transport security review

Recorded on 2026-10-12 for the Go migration in PR #127. Both findings are
resolved as contextual false positives in SonarQube Cloud. The normal quality
gate passes with security, reliability and maintainability rated A, new-code
line coverage 80.4%, duplication 0.1% and reviewed hotspots 100%.

| Finding | Reviewed behavior | Public-contract evidence |
| --- | --- | --- |
| `go:S2092`, `internal/httpapi/cookies.go` | Production always sets Secure, HttpOnly, SameSite Strict and `__Host-` names, including the plaintext hop behind the trusted TLS proxy. Non-production HTTP preserves the required development cookie policy. | `TestCSRFCookies_WhenTransportAndEnvironmentDiffer_PreservesProductionSecurityAndLocalHTTP`, full native integration/race suite and real Caddy production-cookie checks |
| `go:S5332`, `src/Tools.GmailOAuthBootstrap/broker.go` | HTTP is confined to the ephemeral IPv4 loopback callback. Exact Host, PKCE S256, state and replay checks bind consent to one exchange. Google authorization and token endpoints use HTTPS. | Complete autonomous Gmail module suite, including real loopback consent and fake token transport |

The OAuth callback follows [RFC 8252 sections 7.3 and 8.3](https://www.rfc-editor.org/rfc/rfc8252.html#section-7.3).
Changing it to an Internet callback or an unsupported TLS redirect would alter
the existing native-client contract.

The owner's [manual qualification run](https://github.com/jenngllg/mon-kado/actions/runs/38217577524)
used the existing Sonar connection only after the complete technical gate passed.
The reviewed code, entrypoints and module dependencies were pinned to their Git
identities; both transport files also had exact SHA-256 checks. Both issue
identities were validated before either transition. Changed source, unexpected
issues or redirects prevented the review. The API recorded the rationale on each
finding. The ordinary [PR quality run](https://github.com/jenngllg/mon-kado/actions/runs/38217583386)
also passes.

The temporary manual review mechanism is removed after these recorded
transitions. No automatic dismissal remains in CI, and analysis rules and
thresholds are unchanged. A future change to the production cookie policy,
callback binding or provider transport requires a new security review.

Historical secret scanning recognizes only the exact public Gmail module Git
identity at the retired review-script path. This is a repository fingerprint,
not a credential; the general credential rules remain active.
