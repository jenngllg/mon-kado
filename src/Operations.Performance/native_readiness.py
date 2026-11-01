"""Bounded local readiness before starting any load generator."""
import http.client
import socket
import sys
import time
from pathlib import Path


def main():
    deadline = time.monotonic() + 45
    ca = Path(sys.argv[1]) / 'caddy/caddy/pki/authorities/local/root.crt'
    while time.monotonic() < deadline:
        connection = http.client.HTTPConnection('127.0.0.1',7000,timeout=2)
        try:
            connection.request('GET','/readiness',headers={'Host':'mk816.test'})
            response = connection.getresponse()
            if response.status == 200 and ca.is_file():
                return 0
        except (OSError, http.client.HTTPException):
            pass
        finally:
            connection.close()
        # This is a bounded operator readiness wait, never a retry of a business operation.
        time.sleep(.1)
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
