"""Wire pair checks, native deployment, frontend publication and fresh backups."""

from datetime import datetime, timezone
from pathlib import Path

from coordinated_backup import capture
from coordinated_policy import validate_backup, validate_intent, validate_pairs
from deploy_policy import DeploymentError, ensure


class CoordinatedRuntime:
    """Effects for a coordinator holding the native locks on descriptors 8 and 9."""

    def __init__(self, backend, frontend, gate, configuration, tool_digest, backup_state, backup_started,
                 clock=lambda: datetime.now(timezone.utc), backup_capture=capture,
                 maintenance=Path("/var/lib/monkado-backup/maintenance.json")):
        self.backend = backend
        self.frontend = frontend
        self.gate = gate
        self.configuration = configuration
        self.tool_digest = tool_digest
        self.backup_state = backup_state
        self.backup_started = backup_started
        self.clock = clock
        self.backup_capture = backup_capture
        self.maintenance = maintenance
        self.previous = None
        self.candidate = None

    def preflight(self, previous, candidate, backup):
        """Check both approved pairs and the first snapshot before recording intent."""
        configuration = self.configuration()
        validate_pairs(previous, candidate, configuration, self.backend.runtime.history())
        validate_backup(self.backup_state(), backup, self.clock(), self.backup_started)
        ensure(not self.maintenance.exists() and not self.maintenance.is_symlink(), "BACKUP_RECOVERY_REQUIRED")
        self.backend.prepare(previous, candidate)
        self.frontend.prepare(previous, candidate)

    def validate_intent(self, intent):
        """Reject tampered state and rebind only the exact pair in the journal."""
        configuration = self.configuration()
        validate_intent(intent, configuration, self.backend.runtime.history(), self.tool_digest())
        self.previous = intent["previous"]
        self.candidate = intent["candidate"]

    def close_gate(self):
        """Close external traffic; stop Caddy if the rule cannot be verified."""
        try:
            self.gate.close()
        except Exception:
            try:
                self.backend.runtime.run(["systemctl", "stop", "caddy.service"], timeout=120)
            except Exception:
                raise DeploymentError("TRAFFIC_GATE_CLOSURE_UNCERTAIN") from None
            raise DeploymentError("TRAFFIC_GATE_UNAVAILABLE") from None

    def open_gate(self):
        """Open only the previously verified dedicated firewall table."""
        self.gate.open()

    def install_backend(self, candidate):
        ensure(candidate == self.candidate and self.previous is not None, "PAIR_INVALID")
        self.backend.install(self.previous, candidate)

    def capture_frontend_backup(self, candidate):
        ensure(candidate == self.candidate, "PAIR_INVALID")
        return self.backup_capture(clock=self.clock, gate=self.gate, inherited=True)

    def install_frontend(self, candidate):
        ensure(candidate == self.candidate and self.previous is not None, "PAIR_INVALID")
        self.backend.verify_contract(candidate)
        self.frontend.activate(self.previous, candidate)

    def restore_backend(self, previous):
        ensure(previous == self.previous and self.candidate is not None, "PAIR_INVALID")
        ensure(not self.maintenance.exists() and not self.maintenance.is_symlink(), "BACKUP_RECOVERY_REQUIRED")
        self.backend.restore(previous, self.candidate)

    def restore_frontend(self, previous):
        ensure(previous == self.previous and self.candidate is not None, "PAIR_INVALID")
        self.frontend.restore(previous, self.candidate)

    def verify_pair(self, pair):
        """Require real native smokes and HTTPS archive checks while the gate is closed."""
        ensure(pair == self.previous or pair == self.candidate, "PAIR_INVALID")
        self.gate.verify()
        self.frontend.verify(pair)
        self.backend.verify(pair)
        if pair == self.candidate:
            self.backend.verify_contract(pair)
        self.gate.verify()
