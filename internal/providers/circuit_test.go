package providers

import (
	"errors"
	"github.com/JennGllg/mon-kado/internal/application"
	"testing"
	"time"
)

func TestCircuit_WhenRepeatedTransientFailures_OpensWithoutReplayingPOST(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 11, 12, 0, 0, 0, time.UTC)
	c := &circuit{}
	// Act
	c.record(now, errors.New("technical"), 2, time.Minute)
	c.record(now, &application.DeliveryError{Category: "authorization", Retryable: false}, 2, time.Minute)
	c.record(now, &application.DeliveryError{Category: "network", Retryable: true}, 2, time.Minute)
	first := c.allow(now)
	c.record(now, &application.DeliveryError{Category: "network", Retryable: true}, 2, time.Minute)
	blocked := c.allow(now)
	c.record(now, &application.DeliveryError{Category: "circuitOpen", Retryable: true}, 2, time.Minute)
	resumed := c.allow(now.Add(time.Minute))
	c.record(now, nil, 2, time.Minute)
	// Assert
	if first != nil || blocked == nil || resumed != nil || c.failures != 0 || !c.until.IsZero() {
		t.Fatal("circuit state transition differs")
	}
}
