# Reviewed native edge build

`build/` is the upstream Caddy standard composition, pinned to Caddy 2.11.7,
Go 1.27.3 and corrected HTTP/2 dependencies. Its module lock is independent of
the backend and Gmail modules. It contains no application business logic.
CI builds it with `scripts/Initialize-CICaddy.sh`, verifies module checksums,
scans source and executable, and uses those exact bytes in native HTTPS tests.
Go 1.27.3 includes the certificate distrust update required by GO-2026-6809;
the backend, Gmail tool and edge build all require this patched toolchain.
The official 2.11.7 release executable was built with Go 1.26.8 and still predates
the October HTTP/2 fixes, so production uses the qualified pinned build.

The binary scanner expands the package wildcards in GO-2026-5932 for the
unmaintained OpenPGP packages even when those packages are not imported. The
source scanner reports no imported or called vulnerability. The narrow check
in `scripts/check_caddy_scan.py` permits only this exact advisory, wildcard
symbols, and an actual build graph containing no OpenPGP package. A concrete
symbol, imported OpenPGP package, different package or different advisory fails
the build. No executable code is excluded from scanning.

Production replacement requires a new coherent database/file backup, a verified
encrypted offsite copy, the exact candidate SHA-256 and a retained rollback
executable. Validate the installed configuration and public HTTP/TLS protections
before and after replacement. Keep the production environment files private;
never expand them into logs. Reboot maintenance also requires these safeguards.
