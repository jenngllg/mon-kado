package config

import (
	"errors"
	"path/filepath"
	"regexp"
	"strconv"
	"time"
)

// ObservabilityOptions configures bounded private process snapshots and the published revision.
type ObservabilityOptions struct {
	Enabled            bool
	Directory, Version string
	Interval           time.Duration
}

// Observability returns validated settings or the disabled local composition defaults.
func (c Config) Observability() ObservabilityOptions {
	options := c.ObservabilityOptions
	if options.Interval == 0 {
		options.Interval = 30 * time.Second
	}
	if options.Directory == "" {
		options.Directory = "/var/lib/monkado-observability"
	}
	if options.Version == "" {
		options.Version = "local"
	}
	return options
}

func loadObservability() (ObservabilityOptions, error) {
	options := Config{}.Observability()
	var err error
	options.Enabled, err = strconv.ParseBool(value("OBSERVABILITY_ENABLED", "false"))
	if err != nil {
		return options, errors.New("OBSERVABILITY_ENABLED must be a boolean")
	}
	options.Interval, err = time.ParseDuration(value("OBSERVABILITY_INTERVAL", "30s"))
	if err != nil || options.Interval < 5*time.Second || options.Interval > 60*time.Second {
		return options, errors.New("OBSERVABILITY_INTERVAL must be between five and sixty seconds")
	}
	options.Directory = value("OBSERVABILITY_DIRECTORY", options.Directory)
	if options.Enabled && !filepath.IsAbs(options.Directory) {
		return options, errors.New("OBSERVABILITY_DIRECTORY must be absolute when enabled")
	}
	options.Version = value("APP_VERSION", "local")
	if options.Version != "local" && !regexp.MustCompile(`^[A-Fa-f0-9]{40}$`).MatchString(options.Version) {
		return options, errors.New("APP_VERSION must be local or a published source revision")
	}
	return options, nil
}
