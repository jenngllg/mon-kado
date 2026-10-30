"""Atomically exchange two private installation directories on Linux."""

import ctypes
import os
from pathlib import Path
import stat

from deploy_policy import DeploymentError, ensure

AT_FDCWD = -100
RENAME_EXCHANGE = 2


def exchange(left, right):
    """Exchange two same-parent root-owned directories without an absent interval."""
    left, right = Path(left), Path(right)
    ensure(left.is_absolute() and right.is_absolute() and left.parent == right.parent and left != right,
           "TOOL_SWAP_INVALID")
    for path in (left, right):
        info = path.lstat()
        ensure(path.resolve() == path and stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and info.st_gid == 0 and
               not info.st_mode & 0o022, "TOOL_SWAP_INVALID")
    ensure(left.stat().st_dev == right.stat().st_dev, "TOOL_SWAP_INVALID")
    library = ctypes.CDLL(None, use_errno=True)
    rename = library.renameat2
    rename.argtypes = (ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint)
    rename.restype = ctypes.c_int
    if rename(AT_FDCWD, os.fsencode(left), AT_FDCWD, os.fsencode(right), RENAME_EXCHANGE) != 0:
        raise DeploymentError("TOOL_SWAP_FAILED")
    descriptor = os.open(left.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)
