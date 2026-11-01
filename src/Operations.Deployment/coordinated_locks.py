"""Hold the native deployment locks across both changes and the intervening backup."""

from contextlib import contextmanager
import errno
import fcntl
import os
from pathlib import Path
import stat

from deploy_policy import DeploymentError, ensure


@contextmanager
def locks(directory):
    """Acquire coordination then deployment locks and expose them as fds 8/9."""
    directory = Path(directory)
    descriptors = []
    try:
        # A dedicated root process owns these descriptor numbers. Never replace
        # an unrelated open file that a caller might already be using.
        for target in (8, 9):
            try:
                os.fstat(target)
            except OSError as error:
                ensure(error.errno == errno.EBADF, "LOCK_DESCRIPTOR_UNAVAILABLE")
            else:
                raise DeploymentError("LOCK_DESCRIPTOR_UNAVAILABLE")
        for target, name in ((8, "backup-coordination.lock"), (9, "deploy.lock")):
            raw = os.open(directory / name, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
            try:
                info = os.fstat(raw)
                ensure(stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_gid == 0 and
                       stat.S_IMODE(info.st_mode) == 0o600, "UNSAFE_DEPLOYMENT_LOCK")
                # Preserve the open file description while ensuring neither
                # allocation can collide with the fixed inherited descriptors.
                high = fcntl.fcntl(raw, fcntl.F_DUPFD_CLOEXEC, 10)
            finally:
                os.close(raw)
            try:
                fcntl.flock(high, fcntl.LOCK_EX | fcntl.LOCK_NB)
                os.dup2(high, target, inheritable=True)
                descriptors.append(target)
            finally:
                os.close(high)
        yield
    except BlockingIOError:
        raise DeploymentError("COORDINATION_BUSY") from None
    finally:
        for descriptor in reversed(descriptors):
            os.close(descriptor)
