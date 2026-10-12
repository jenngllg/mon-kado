// Package logging defines stable structured event identities shared by native processes.
package logging

// HTTP and application boundary event identifiers occupy the 0–999 range.
const (
	BusinessOperationSucceeded  = 100
	HTTPRequestCompleted        = 101
	BusinessOperationFailed     = 400
	UnhandledRequestPanic       = 500
	UnexpectedDependencyFailure = 501
	UnhandledRequestFailure     = 502
	APIListenerStarted          = 901
	APIStopped                  = 902
	WorkerStopped               = 903
	MigrationFailed             = 904
	MigrationsApplied           = 905
)

// Account provider event identifiers occupy the 1000–1999 range.
const GoogleCallbackRejected = 1201

// Worker event identifiers occupy the 6000–6999 range.
const (
	WorkerPassFailed                 = 6000
	AuthenticationEmailDelivered     = 6001
	AuthenticationEmailAttemptFailed = 6002
	WorkerPassCompleted              = 6003
	ArchiveDownloadInterrupted       = 6101
	NotificationAttemptFailed        = 6201
	TelemetrySnapshotFailed          = 6500
)
