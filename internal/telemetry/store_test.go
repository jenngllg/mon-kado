package telemetry_test

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/telemetry"
)

func TestFileStore_WhenWritingSnapshot_ReplacesPrivateEvidenceWithoutTemporaryFiles(t *testing.T) {
	// Arrange
	store := telemetry.FileStore{Directory: filepath.Join(t.TempDir(), "api")}
	collector := telemetry.New("api", time.Now, time.Now)
	// Act
	if err := store.Write(t.Context(), collector.Capture()); err != nil {
		t.Fatal(err)
	}
	collector.RecordHTTP(200, time.Millisecond, false)
	if err := store.Write(t.Context(), collector.Capture()); err != nil {
		t.Fatal(err)
	}
	// Assert
	content, err := os.ReadFile(filepath.Join(store.Directory, "snapshot.json"))
	if err != nil {
		t.Fatal(err)
	}
	var snapshot telemetry.Snapshot
	if err = json.Unmarshal(content, &snapshot); err != nil || snapshot.HTTP.Requests != 1 {
		t.Fatal("snapshot replacement differs")
	}
	entries, err := os.ReadDir(store.Directory)
	if err != nil || len(entries) != 1 {
		t.Fatal("temporary snapshot leaked")
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	if err = store.Write(ctx, collector.Capture()); !errors.Is(err, context.Canceled) {
		t.Fatal("cancelled write proceeded")
	}
	unchanged, _ := os.ReadFile(filepath.Join(store.Directory, "snapshot.json"))
	if !bytes.Equal(content, unchanged) {
		t.Fatal("cancelled write replaced evidence")
	}
	oversized := collector.Capture()
	oversized.Service = strings.Repeat("x", 65536)
	if err = store.Write(t.Context(), oversized); err == nil {
		t.Fatal("oversized telemetry accepted")
	}
}

func TestFileStore_WhenDestinationIsLinked_RejectsExternalEvidence(t *testing.T) {
	// Arrange
	root := t.TempDir()
	external := filepath.Join(root, "external.json")
	if err := os.WriteFile(external, []byte("private"), 0600); err != nil {
		t.Fatal(err)
	}
	directory := filepath.Join(root, "api")
	if err := os.Mkdir(directory, 0700); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(external, filepath.Join(directory, "snapshot.json")); err != nil {
		t.Skip("symlink creation is unavailable on this host")
	}
	// Act
	err := (telemetry.FileStore{Directory: directory}).Write(t.Context(), telemetry.New("api", time.Now, time.Now).Capture())
	// Assert
	if err == nil {
		t.Fatal("linked snapshot accepted")
	}
	content, _ := os.ReadFile(external)
	if string(content) != "private" {
		t.Fatal("external file overwritten")
	}
}

type recordingStore struct {
	ctx     context.Context
	calls   int
	failure error
	cancel  context.CancelFunc
}

func (s *recordingStore) Write(ctx context.Context, _ telemetry.Snapshot) error {
	s.ctx = ctx
	s.calls++
	if s.cancel != nil {
		s.cancel()
	}
	return s.failure
}

func TestPublisher_WhenSchedulerCloses_PublishesImmediatelyWithoutFabricatingFailures(t *testing.T) {
	// Arrange
	var output bytes.Buffer
	store := &recordingStore{failure: errors.New("contains-private-provider-details")}
	p := telemetry.Publisher{Collector: telemetry.New("api", time.Now, time.Now), Store: store, Logger: slog.New(slog.NewJSONHandler(&output, nil))}
	ticks := make(chan time.Time, 1)
	ticks <- time.Now()
	close(ticks)
	// Act
	p.Run(t.Context(), ticks)
	// Assert
	if store.calls != 2 || store.ctx != t.Context() || strings.Contains(output.String(), "contains-private") {
		t.Fatal("publisher leaked failure contents or replaced caller context")
	}
	ctx, cancel := context.WithCancel(t.Context())
	store.cancel = cancel
	store.calls = 0
	p.Run(ctx, make(chan time.Time))
	if store.calls != 1 {
		t.Fatal("publisher did not stop when cancelled")
	}
	p.Run(ctx, nil)
	if store.calls != 1 {
		t.Fatal("cancelled publisher performed I/O")
	}
}

type cancellationStore struct {
	entered  chan context.Context
	finished chan struct{}
}

func (s cancellationStore) Write(ctx context.Context, _ telemetry.Snapshot) error {
	s.entered <- ctx
	<-ctx.Done()
	close(s.finished)
	return ctx.Err()
}

func TestPublisher_WhenOwnedSchedulerStops_CancelsPendingIOAndWaitsForCompletion(t *testing.T) {
	// Arrange
	store := cancellationStore{entered: make(chan context.Context, 1), finished: make(chan struct{})}
	publisher := telemetry.Publisher{Collector: telemetry.New("api", time.Now, time.Now), Store: store, Logger: slog.New(slog.NewJSONHandler(&bytes.Buffer{}, nil))}
	// Act
	stop := publisher.Start(t.Context(), time.Hour)
	var writeContext context.Context
	select {
	case writeContext = <-store.entered:
	case <-t.Context().Done():
		t.Fatal("immediate publication did not start")
	}
	stop()
	stop()
	// Assert
	if !errors.Is(writeContext.Err(), context.Canceled) {
		t.Fatal("pending snapshot write was not cancelled")
	}
	select {
	case <-store.finished:
	default:
		t.Fatal("stop returned before pending I/O completed")
	}
}
