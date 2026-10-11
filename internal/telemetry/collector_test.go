package telemetry_test

import (
	"sync"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/telemetry"
)

func TestCapture_WhenRequestsHaveDifferentOutcomes_RecordsDisjointCumulativeBuckets(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 11, 12, 0, 0, 0, time.FixedZone("local", 3600))
	c := telemetry.New("api", func() time.Time { return now }, func() time.Time { return now })
	// Act
	for index, elapsed := range []time.Duration{-1, 51, 101, 251, 501, 1001, 2001, 5001, 10001} {
		c.RecordHTTP(200, elapsed*time.Millisecond, index == 0)
	}
	c.RecordHTTP(500, 10*time.Millisecond, false)
	c.RecordHTTP(500, time.Millisecond, true)
	c.RecordHTTP(429, time.Millisecond, false)
	snapshot := c.Capture()
	// Assert
	if snapshot.SchemaVersion != 1 || snapshot.Service != "api" || len(snapshot.BootID) != 32 || snapshot.CreatedAt.Location() != time.UTC || len(snapshot.Jobs) != 0 || snapshot.HTTP.Requests != 12 || snapshot.HTTP.ServerErrors != 1 || snapshot.HTTP.Throttled != 1 || snapshot.HTTP.Abandoned != 2 {
		t.Fatal("HTTP telemetry contract differs")
	}
	for index, count := range snapshot.HTTP.DurationBuckets {
		want := int64(1)
		if index == 0 {
			want = 4
		}
		if count != want {
			t.Fatal("duration buckets overlap or are missing")
		}
	}
	snapshot.HTTP.DurationBuckets[0] = 999
	if c.Capture().HTTP.DurationBuckets[0] != 4 {
		t.Fatal("snapshot exposes mutable counters")
	}
}

func TestCapture_WhenWorkerCyclesComplete_PreservesProgressAndTerminalFailures(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 11, 12, 0, 0, 0, time.UTC)
	monotonic := now
	c := telemetry.New("worker", func() time.Time { return now }, func() time.Time { return monotonic })
	initial := c.Capture()
	if len(initial.Jobs) != 10 || initial.Jobs[telemetry.PersonalDataExport].State != "waiting" {
		t.Fatal("bounded operation set differs")
	}
	// Act
	c.BeginCycle(telemetry.PersonalDataExport)
	if running := c.Capture().Jobs[telemetry.PersonalDataExport]; running.State != "running" || running.StartedAt == nil || running.NextExpectedAt != nil {
		t.Fatal("actual start not recorded")
	}
	now = now.Add(time.Second)
	monotonic = monotonic.Add(250 * time.Millisecond)
	c.CompleteCycle(telemetry.PersonalDataExport, 10*time.Second, false)
	c.RecordTerminalFailure(telemetry.PersonalDataExport)
	// Assert
	failed := c.Capture().Jobs[telemetry.PersonalDataExport]
	if failed.State != "waiting" || failed.Failures != 1 || failed.ConsecutiveFailures != 1 || failed.TerminalFailures != 1 || failed.LastDurationMilliseconds != 250 || failed.LastCompletedAt == nil || !failed.NextExpectedAt.Equal(now.Add(10*time.Second)) {
		t.Fatal("worker failure progress differs")
	}
	*failed.NextExpectedAt = time.Time{}
	if c.Capture().Jobs[telemetry.PersonalDataExport].NextExpectedAt.IsZero() {
		t.Fatal("snapshot exposes mutable timestamps")
	}
	c.BeginCycle(telemetry.PersonalDataExport)
	c.CompleteCycle(telemetry.PersonalDataExport, time.Second, true)
	recovered := c.Capture().Jobs[telemetry.PersonalDataExport]
	if recovered.Successes != 1 || recovered.Failures != 1 || recovered.ConsecutiveFailures != 0 {
		t.Fatal("successful cycle erased history or retained failure streak")
	}
	c.Disable(telemetry.AuthenticationEmailDelivery)
	if disabled := c.Capture().Jobs[telemetry.AuthenticationEmailDelivery]; disabled.State != "disabled" || disabled.NextExpectedAt != nil {
		t.Fatal("disabled operation appears overdue")
	}
	c.BeginCycle("unknown")
	c.CompleteCycle("unknown", time.Second, true)
	c.Disable("unknown")
	c.RecordTerminalFailure("unknown")
	c.RecordTerminalFailures(telemetry.PersonalDataExport, -1)
	if c.Capture().Jobs[telemetry.PersonalDataExport].TerminalFailures != 1 {
		t.Fatal("negative terminal count altered evidence")
	}
	if len(c.Capture().Jobs) != 10 {
		t.Fatal("unbounded operation label accepted")
	}
}

func TestRecordHTTP_WhenRequestsRunConcurrently_PreservesEveryCounter(t *testing.T) {
	// Arrange
	c := telemetry.New("api", time.Now, time.Now)
	var group sync.WaitGroup
	// Act
	for index := 0; index < 10; index++ {
		group.Go(func() {
			for count := 0; count < 100; count++ {
				c.RecordHTTP(200, time.Millisecond, false)
				c.Capture()
			}
		})
	}
	group.Wait()
	// Assert
	if snapshot := c.Capture(); snapshot.HTTP.Requests != 1000 || snapshot.HTTP.DurationBuckets[0] != 1000 {
		t.Fatal("concurrent request counters were lost")
	}
}
