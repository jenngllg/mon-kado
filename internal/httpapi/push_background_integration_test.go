//go:build integration

package httpapi_test

import (
	"context"
	"log/slog"
	"sync"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/worker"
)

type backgroundPushContextKey struct{}

type backgroundPushSender struct {
	lock            sync.Mutex
	calls           int
	expectedContext any
	invalidContext  bool
}

func (s *backgroundPushSender) Send(ctx context.Context, endpoint string) (int, error) {
	s.lock.Lock()
	defer s.lock.Unlock()
	s.calls++
	s.invalidContext = s.invalidContext || ctx.Value(backgroundPushContextKey{}) != s.expectedContext
	if err := ctx.Err(); err != nil {
		return 0, err
	}
	return 201, nil
}

func runBackgroundPushPass(t *testing.T, service *application.Service, sender application.PushSender) {
	t.Helper()
	// The first pass is synchronized by the production worker's completion event, never a delay.
	service.Config.WorkerInterval = time.Hour
	signal := jobSignal{Job: "mobilePush", Done: make(chan struct{}), once: &sync.Once{}}
	ctx, cancel := context.WithCancel(context.WithValue(t.Context(), backgroundPushContextKey{}, "closed-app-pass"))
	finished := make(chan error, 1)
	w := worker.Worker{Service: service, PushSender: sender, Logger: slog.New(signal)}
	go func() { finished <- w.Run(ctx) }()
	defer func() {
		cancel()
		if err := <-finished; err != nil {
			t.Errorf("worker failed: %v", err)
		}
	}()
	select {
	case <-signal.Done:
	case <-t.Context().Done():
		t.Fatal("mobile push pass did not complete")
	}
}

func TestPushBackground_WhenAPIIsStopped_WorkerDeliversPersistedEventsWithoutLiveClients(t *testing.T) {
	for _, enabled := range []bool{true, false} {
		name := "enabled"
		if !enabled {
			name = "muted"
		}
		t.Run(name, func(t *testing.T) {
			// Arrange: create the event through public HTTP contracts, without any notification stream.
			scenario := newSubscriptionScenario(t)
			requireStatus(t, scenario.member.send(t, "POST", scenario.shared+"/subscriptions", "", scenario.headers), 201, nil)
			activatePush(t, scenario.member, "https://fcm.googleapis.com/closed-app-device")
			requireStatus(t, scenario.owner.send(t, "POST", scenario.base+"/wishes", `{"name":"Closed application event"}`, nil), 201, nil)
			choices := pushChoices()
			choices["wishesAdded"] = enabled
			savePushChoices(t, scenario.member, choices)
			scenario.owner.server.Close()
			// A new service instance shares only durable storage/configuration; the API is no longer serving.
			service := application.New(scenario.member.store, scenario.member.service.Config, scenario.member.service.Now)
			sender := &backgroundPushSender{expectedContext: "closed-app-pass"}

			// Act: the background worker must deliver without an open API or any browser connection.
			runBackgroundPushPass(t, service, sender)
			// Restarting the worker must not replay the acknowledged durable outbox entry.
			runBackgroundPushPass(t, application.New(service.Store, service.Config, service.Now), sender)

			// Assert: current preferences are enforced and successful delivery happens exactly once.
			sender.lock.Lock()
			defer sender.lock.Unlock()
			expected := 0
			if enabled {
				expected = 1
			}
			if sender.calls != expected || sender.invalidContext {
				t.Fatalf("background delivery calls=%d expected=%d invalidContext=%t", sender.calls, expected, sender.invalidContext)
			}
		})
	}
}
