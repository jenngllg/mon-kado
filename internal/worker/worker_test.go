package worker

import (
	"testing"
	"time"
)

func TestRetryDelay_WhenAttemptsIncrease_UsesBoundedExponentialDelay(t *testing.T) {
	for _, tc := range []struct {
		attempt  int
		expected time.Duration
	}{{1, time.Minute}, {2, 2 * time.Minute}, {3, 4 * time.Minute}, {30, 24 * time.Hour}} {
		// Arrange
		// Act
		actual := retryDelay(tc.attempt, time.Minute, 24*time.Hour)
		// Assert
		if actual != tc.expected {
			t.Fatalf("attempt %d: %s, expected %s", tc.attempt, actual, tc.expected)
		}
	}
}
