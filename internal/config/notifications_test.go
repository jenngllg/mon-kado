package config

import (
	"testing"
	"time"
)

func TestNotificationOptions_WhenConfigured_UsesValidatedBounds(t *testing.T) {
	// Arrange
	defaults := Config{}.Notifications()
	if defaults.MaximumStreams != 256 || defaults.GroupingWindow != 5*time.Minute || defaults.SharingReminderDelay != 48*time.Hour {
		t.Fatal("incorrect notification defaults")
	}
	for _, test := range []struct{ key, valid, invalid string }{
		{"NOTIFICATION_MAXIMUM_STREAMS", "12", "0"},
		{"NOTIFICATION_HEARTBEAT_INTERVAL", "10s", "0s"},
		{"NOTIFICATION_WRITE_TIMEOUT", "3s", "invalid"},
		{"NOTIFICATION_RECONNECT_DELAY", "500ms", "1h"},
		{"NOTIFICATION_GROUPING_WINDOW", "2m", "0s"},
		{"NOTIFICATION_SHARING_REMINDER_DELAY", "24h", "1000h"},
	} {
		t.Run(test.key, func(t *testing.T) {
			validEnvironment(t)
			t.Setenv(test.key, test.valid)
			// Act / Assert
			if _, err := Load(); err != nil {
				t.Fatal("valid notification setting rejected", err)
			}
			t.Setenv(test.key, test.invalid)
			if _, err := Load(); err == nil {
				t.Fatal("invalid notification setting accepted")
			}
		})
	}
}
