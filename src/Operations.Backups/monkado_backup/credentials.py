"""Local, interactive Bitwarden handoff; no secret is printed or passed as an argument."""

import getpass
import hmac
import os

from .operations import SETTINGS
from .policy import BackupError


def install_password(prompt=getpass.getpass):
    """Store an existing generator-produced recovery password exactly once."""
    password = prompt("Paste the 64-character random password saved in Bitwarden: ")
    confirmation = prompt("Confirm the same password: ")
    if len(password) < 64 or len(password) > 256 or not password.isascii() or not password.isprintable():
        raise BackupError("INVALID_RECOVERY_PASSWORD")
    if not hmac.compare_digest(password, confirmation):
        raise BackupError("RECOVERY_PASSWORD_MISMATCH")
    descriptor = os.open(SETTINGS / "password", os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="ascii") as stream:
        stream.write(password)
        stream.flush()
        os.fsync(stream.fileno())
