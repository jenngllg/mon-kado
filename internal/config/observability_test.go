package config

import (
	"strings"
	"testing"
	"time"
)

func TestLoad_WhenObservabilityIsInvalid_RejectsUnboundedOrNonPrivateSettings(t *testing.T) {
	for _, test := range []struct{ key, value string }{{"OBSERVABILITY_ENABLED", "invalid"}, {"OBSERVABILITY_INTERVAL", "invalid"}, {"OBSERVABILITY_INTERVAL", "4s"}, {"OBSERVABILITY_INTERVAL", "61s"}, {"APP_VERSION", "untrusted-version"}, {"OBSERVABILITY_DIRECTORY", "relative-directory"}} {
		t.Run(test.key+test.value, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("OBSERVABILITY_DIRECTORY", t.TempDir())
			t.Setenv("OBSERVABILITY_ENABLED", "true")
			t.Setenv(test.key, test.value)
			// Act
			_, err := Load()
			// Assert
			if err == nil {
				t.Fatal("invalid observability configuration accepted")
			}
		})
	}
}

func TestLoad_WhenObservabilityIsEnabled_PreservesPrivateDirectoryAndBoundedSchedule(t *testing.T) {
	// Arrange
	validEnvironment(t)
	directory := t.TempDir()
	t.Setenv("OBSERVABILITY_DIRECTORY", directory)
	t.Setenv("OBSERVABILITY_ENABLED", "true")
	t.Setenv("OBSERVABILITY_INTERVAL", "5s")
	t.Setenv("APP_VERSION", strings.Repeat("a", 40))
	// Act
	cfg, err := Load()
	options := cfg.Observability()
	// Assert
	if err != nil || !options.Enabled || options.Directory != directory || options.Interval != 5*time.Second || options.Version != strings.Repeat("a", 40) {
		t.Fatal("observability settings lost")
	}
}
