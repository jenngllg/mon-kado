// Package telemetry records bounded private operational measurements without resource or user labels.
package telemetry

import (
	"strings"
	"sync"
	"time"

	"github.com/JennGllg/mon-kado/internal/security"
)

// Operation is a compiled background operation recognized by the private monitoring contract.
type Operation string

// Background operation names preserve the consolidated monitoring contract.
const (
	AuthenticationEmailDelivery            Operation = "AuthenticationEmailDelivery"
	WishlistModerationEmailDelivery        Operation = "WishlistModerationEmailDelivery"
	AccountErasureProcessing               Operation = "AccountErasureProcessing"
	PersonalDataExport                     Operation = "PersonalDataExport"
	UnconfirmedAccountCleanup              Operation = "UnconfirmedAccountCleanup"
	ExpiredAuthenticationSessionCleanup    Operation = "ExpiredAuthenticationSessionCleanup"
	ExpiredMemberEmailChangeRequestCleanup Operation = "ExpiredMemberEmailChangeRequestCleanup"
	ProcessedAuthenticationEmailCleanup    Operation = "ProcessedAuthenticationEmailCleanup"
	ExpiredGuestSessionCleanup             Operation = "ExpiredGuestSessionCleanup"
	GiftImageCleanup                       Operation = "GiftImageCleanup"
)

var operations = []Operation{AuthenticationEmailDelivery, WishlistModerationEmailDelivery, AccountErasureProcessing, PersonalDataExport, UnconfirmedAccountCleanup, ExpiredAuthenticationSessionCleanup, ExpiredMemberEmailChangeRequestCleanup, ProcessedAuthenticationEmailCleanup, ExpiredGuestSessionCleanup, GiftImageCleanup}

// Snapshot is the bounded, versioned private filesystem contract.
type Snapshot struct {
	SchemaVersion int                    `json:"schemaVersion"`
	Service       string                 `json:"service"`
	BootID        string                 `json:"bootId"`
	CreatedAt     time.Time              `json:"createdAt"`
	HTTP          HTTPMeasurements       `json:"http"`
	Jobs          map[Operation]Progress `json:"jobs"`
}

// HTTPMeasurements contains cumulative request counters and disjoint latency buckets.
type HTTPMeasurements struct {
	Requests        int64   `json:"requests"`
	ServerErrors    int64   `json:"serverErrors"`
	Throttled       int64   `json:"throttled"`
	Abandoned       int64   `json:"abandoned"`
	DurationBuckets []int64 `json:"durationBuckets"`
}

// Progress records actual processing and the next scheduled cycle, without work item identities.
type Progress struct {
	State                    string     `json:"state"`
	ConsecutiveFailures      int64      `json:"consecutiveFailures"`
	StartedAt                *time.Time `json:"startedAt"`
	NextExpectedAt           *time.Time `json:"nextExpectedAt"`
	LastCompletedAt          *time.Time `json:"lastCompletedAt"`
	Successes                int64      `json:"successes"`
	Failures                 int64      `json:"failures"`
	TerminalFailures         int64      `json:"terminalFailures"`
	LastDurationMilliseconds float64    `json:"lastDurationMilliseconds"`
}

type trackedJob struct {
	Progress
	started time.Time
}

// Collector owns synchronized cumulative counters for one process lifetime.
type Collector struct {
	mu             sync.Mutex
	service, boot  string
	now, monotonic func() time.Time
	http           HTTPMeasurements
	jobs           map[Operation]*trackedJob
}

// New composes UTC and monotonic clocks without performing I/O.
func New(service string, now, monotonic func() time.Time) *Collector {
	c := &Collector{service: service, boot: strings.ReplaceAll(security.NewID(), "-", ""), now: now, monotonic: monotonic, http: HTTPMeasurements{DurationBuckets: make([]int64, 9)}, jobs: map[Operation]*trackedJob{}}
	for _, operation := range operations {
		next := now().UTC()
		c.jobs[operation] = &trackedJob{Progress: Progress{State: "waiting", NextExpectedAt: &next}}
	}
	return c
}

// RecordHTTP records one completion without retaining its path, body or caller identity.
func (c *Collector) RecordHTTP(status int, elapsed time.Duration, abandoned bool) {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.http.Requests++
	if status >= 500 && !abandoned {
		c.http.ServerErrors++
	}
	if status == 429 {
		c.http.Throttled++
	}
	if abandoned {
		c.http.Abandoned++
	}
	milliseconds := max(0, float64(elapsed)/float64(time.Millisecond))
	index := 8
	for candidate, bound := range []float64{50, 100, 250, 500, 1000, 2000, 5000, 10000} {
		if milliseconds <= bound {
			index = candidate
			break
		}
	}
	c.http.DurationBuckets[index]++
}

// BeginCycle records the actual start and suspends the next-cycle deadline.
func (c *Collector) BeginCycle(operation Operation) {
	c.mu.Lock()
	defer c.mu.Unlock()
	job, ok := c.jobs[operation]
	if !ok {
		return
	}
	now := c.now().UTC()
	job.State = "running"
	job.StartedAt = &now
	job.NextExpectedAt = nil
	job.started = c.monotonic()
}

// CompleteCycle records elapsed processing and schedules the next actual cycle.
func (c *Collector) CompleteCycle(operation Operation, nextDelay time.Duration, successful bool) {
	c.mu.Lock()
	defer c.mu.Unlock()
	job, ok := c.jobs[operation]
	if !ok {
		return
	}
	now := c.now().UTC()
	next := now.Add(nextDelay)
	job.State = "waiting"
	job.LastCompletedAt = &now
	job.NextExpectedAt = &next
	job.LastDurationMilliseconds = max(0, float64(c.monotonic().Sub(job.started))/float64(time.Millisecond))
	if successful {
		job.Successes++
		job.ConsecutiveFailures = 0
	} else {
		job.Failures++
		job.ConsecutiveFailures++
	}
}

// Disable marks an intentionally unavailable operation without fabricating successful progress.
func (c *Collector) Disable(operation Operation) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if job, ok := c.jobs[operation]; ok {
		job.State = "disabled"
		job.NextExpectedAt = nil
	}
}

// RecordTerminalFailure counts one work item whose persisted outcome can no longer be retried.
func (c *Collector) RecordTerminalFailure(operation Operation) {
	c.RecordTerminalFailures(operation, 1)
}

// RecordTerminalFailures counts confirmed persisted terminal transitions from a bounded batch.
func (c *Collector) RecordTerminalFailures(operation Operation, count int64) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if job, ok := c.jobs[operation]; ok {
		job.TerminalFailures += max(0, count)
	}
}

func copyTime(value *time.Time) *time.Time {
	if value == nil {
		return nil
	}
	copy := *value
	return &copy
}

// Capture returns independent immutable counter values using the injected UTC clock.
func (c *Collector) Capture() Snapshot {
	c.mu.Lock()
	defer c.mu.Unlock()
	http := c.http
	http.DurationBuckets = append([]int64{}, http.DurationBuckets...)
	result := Snapshot{SchemaVersion: 1, Service: c.service, BootID: c.boot, CreatedAt: c.now().UTC(), HTTP: http, Jobs: map[Operation]Progress{}}
	if c.service == "worker" {
		for operation, job := range c.jobs {
			progress := job.Progress
			progress.StartedAt = copyTime(progress.StartedAt)
			progress.NextExpectedAt = copyTime(progress.NextExpectedAt)
			progress.LastCompletedAt = copyTime(progress.LastCompletedAt)
			result.Jobs[operation] = progress
		}
	}
	return result
}
