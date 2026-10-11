# Administrator two-factor authentication

Administration requires the live Administrator role and a session verified against the member's current TOTP credential. Assign the role through a controlled database operation; never accept a role supplied by a client. The next password or Google sign-in requires enrollment if no active factor exists.

Enrollment returns a secret and ten recovery codes. Store recovery codes privately; server persistence holds hashes only. TOTP has a 30-second step and a bounded clock window. Accepted counters cannot be replayed. Failed verification attempts are rate-limited and eventually lock the factor temporarily.

Using a recovery code requires replacement of the authenticator before a session is issued. Replacement changes the credential identity, rotates recovery codes and revokes existing sessions. Management proofs are purpose-bound, session-bound, short-lived and single-use. Never disclose them in logs.

API routes live under `/api/v1/auth/two-factor` and `/api/v1/members/current/two-factor`; see `/openapi/v1.json` for request and response contracts. Google enrollment defers account association until the factor flow completes.
