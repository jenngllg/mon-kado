"""Bind private process configuration to the same native database inspected by operations."""

import base64
import re
from urllib.parse import parse_qsl, urlsplit


def validate_database_environment(raw):
    """Reject alternate databases or hosts without returning or logging credential material."""
    def require(condition):
        if not condition:
            raise ValueError("NATIVE_DATABASE_CONFIGURATION_INVALID")

    require(isinstance(raw, bytes) and len(raw) <= 131072)
    values = []
    for line in raw.decode("utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        key, separator, value = line.partition("=")
        require(separator == "=" and re.fullmatch(r"[A-Z][A-Z0-9_]*", key) is not None)
        if key != "DATABASE_URL":
            continue
        if len(value) > 1 and value[0] in ("'", '"') and value[-1] == value[0]:
            value = value[1:-1]
        values.append(value)
    require(len(values) == 1 and not any(character.isspace() for character in values[0]))
    parsed = urlsplit(values[0])
    require(parsed.scheme in ("postgres", "postgresql") and parsed.hostname == "127.0.0.1"
            and parsed.port in (None, 5432) and parsed.username == "mon_kado" and bool(parsed.password)
            and parsed.path == "/mon_kado" and not parsed.fragment)
    require(parse_qsl(parsed.query, strict_parsing=True) == [("sslmode", "disable")])


def validate_runtime_keys(raw):
    """Require both stable cryptographic keys in a native encrypted capture without returning their values."""
    try:
        values = {}
        for line in raw.decode("utf-8").splitlines():
            name, separator, value = line.strip().partition("=")
            if separator != "=" or name not in ("JWT_SIGNING_KEY", "DATA_ENCRYPTION_KEY"):
                continue
            if name in values:
                raise ValueError()
            if len(value) > 1 and value[0] in ("'", '"') and value[-1] == value[0]:
                value = value[1:-1]
            values[name] = base64.b64decode(value, validate=True)
        if set(values) != {"JWT_SIGNING_KEY", "DATA_ENCRYPTION_KEY"} or any(len(value) != 32 for value in values.values()):
            raise ValueError()
    except (UnicodeError, ValueError, TypeError):
        raise ValueError("NATIVE_RUNTIME_KEYS_INVALID") from None
