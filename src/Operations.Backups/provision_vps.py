"""Install the explicitly transferred Drive grant and prompt for the recovery secret."""

import configparser
import os
from pathlib import Path
import sys

from monkado_backup.credentials import install_password
from monkado_backup.operations import SETTINGS, private_file
from monkado_backup.policy import BackupError


def main():
    """Provision files without logging secrets or enabling any backup service."""
    try:
        if os.geteuid() != 0:
            raise ValueError()
        os.umask(0o077)
        source = Path("/home/admin/monkado-backup-813/credentials/rclone.conf")
        if source.is_symlink() or not source.is_file():
            raise ValueError()
        contents = source.read_text(encoding="utf-8")
        config = configparser.RawConfigParser()
        config.read_string(contents)
        if config.sections() != ["monkado"] or config["monkado"]["type"] != "drive" or config["monkado"]["scope"] != "drive.file":
            raise ValueError()
        target = SETTINGS / "rclone.conf"
        if not target.exists():
            descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
                stream.write(contents)
                stream.flush()
                os.fsync(stream.fileno())
        private_file(target)
        repository = SETTINGS / "repository"
        if not repository.exists():
            descriptor = os.open(repository, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w", encoding="ascii") as stream:
                stream.write("rclone:monkado:MonKado-backups/production-v1\n")
        private_file(repository)
        if not (SETTINGS / "password").exists():
            install_password()
        private_file(SETTINGS / "password")
        source.unlink()
        print("Private credentials installed. Temporary Drive copy removed. No backup or timer started.")
        return 0
    except Exception as error:
        code = "CREDENTIAL_SETUP_FAILED"
        if isinstance(error, BackupError):
            code = error.code
        elif isinstance(error, PermissionError):
            code = "CREDENTIAL_FILE_PERMISSION_DENIED"
        elif isinstance(error, FileExistsError):
            code = "CREDENTIAL_FILE_ALREADY_EXISTS"
        elif isinstance(error, FileNotFoundError):
            code = "CREDENTIAL_FILE_MISSING"
        elif isinstance(error, EOFError):
            code = "CREDENTIAL_INPUT_INTERRUPTED"
        print("Credential setup stopped: " + code + ". No secrets are displayed; existing credentials have not been replaced.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
