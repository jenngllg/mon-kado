# Forward-only schema transitions

Use this path when the protected backend publication adds migrations and sets
`rollbackAllowed` to `false`. The existing schema-unchanged paired rollback path
remains separate. Neither path restores PostgreSQL or runs down migrations.

The forward path authenticates the currently installed frontend against its
original immutable manifest and coordinated proof. Its original configuration
and backend bindings remain unchanged. The actual local HTTPS API must have the
exact OpenAPI digest in that original proof before the transition can start.
The current backend binaries, complete SQL history and frontend archive bytes
are also checked. This admits a previously deployed backend-only update without
assuming that different revisions are compatible.

## Reviewed tool upgrade

Publish the exact green tools revision with `publish-coordinated-tools.yml` and
retain its production environment approval. Independently pin the protected
manifest SHA-256, then use the already trusted `coordinated_bootstrap.py` to
extract the immutable archive into a new root-owned mode-0700 directory. Execute
the reviewed staged installer with the staging directory's deployment modules
first in `PYTHONPATH`, followed by the installed Native, Frontend, production
manifest and backup module directories.

```sh
python3 /private/reviewed-tool/src/Operations.Deployment/coordinated_install_cli.py upgrade /private/reviewed-tool
```

Both application publication timers must be disabled and inactive. The upgrade
captures a new coherent snapshot and verifies its encrypted offsite copy and full
integrity before any code replacement. It verifies the old installed inventory,
records intent under the existing boot guard and closes public traffic. Only
authenticated old or already-installed new bytes can be replaced. The two
inventoried deployment configuration modules are the only configuration changes
allowed; the resulting complete configuration hash must equal the protected
tools manifest. The current backend is then rebaselined through its existing
verified rebaseline boundary, with an explicit reader for its historical session
contract. Application authorization and the candidate's smoke use only `isAdmin`.

A separate new verified snapshot follows installation, before traffic reopens.
The application versions and SQL history do not change during the tool upgrade.
The receipt retains both snapshot references and the configuration transition.
An interruption retains `coordinated-install.json`; boot keeps traffic closed.
Resume only the same independently verified source:

```sh
python3 /private/reviewed-tool/src/Operations.Deployment/coordinated_install_cli.py recover-upgrade /private/reviewed-tool
```

Historical tool inventories and encrypted captures remain verifiable with their
original inventories. No old approval or archive is rewritten.

## Paired application migration

Publish the exact green backend with rollback disabled. Stage the exact green
frontend using `Stage approved coordinated frontend`, which verifies the new
backend in an isolated native database before producing its immutable proof.
The staging operation does not switch the site. Preserve both production
environment approvals.

```sh
python3 /opt/monkado/src/Operations.Deployment/coordinated_cli.py forward-deploy
```

The command takes another new verified offsite snapshot before changing the
backend. It holds the normal backup/deployment locks, stages approved artifacts,
and records a version-2 transition before closing public traffic. Only an
append-only migration catalog is admitted. Each SQL file remains transactional
and ledgered by the native migrator. The native deployment engine installs and
verifies the new backend while the old frontend is still inaccessible publicly.

A separate new verified offsite snapshot precedes the frontend switch. Candidate
frontend bytes and the served API digest must match the protected proof. Both
real native smokes and the frontend HTTPS checks pass before the durable commit
and traffic reopening. Frontend publication remains paused for review.

Failure preserves the journal and closed gate. Recovery reconciles completed
effects, including an interrupted backup, without selecting a new publication:

```sh
python3 /opt/monkado/src/Operations.Deployment/coordinated_cli.py forward-recover
```

Generic `recover` dispatches version-2 journals to this same forward engine.
Already ledgered migrations are not replayed. Unexpected SQL history, changed
code, a different candidate or a failed verification keeps traffic closed. After
a committed-but-unacknowledged switch, the exact candidate is reverified before
reopening. Save the receipts and every upgrade and rollout snapshot reference in the
deployment report; never label a failed or interrupted transition successful.
