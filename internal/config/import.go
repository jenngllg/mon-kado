package config

import (
	"errors"
	"strconv"
	"time"
)

// ImportOptions bounds merchant previews within the supported security limits.
type ImportOptions struct {
	Timeout                                         time.Duration
	MaximumHTMLBytes, MaximumRedirects, PermitLimit int
}

// Imports returns the startup-validated merchant settings or the composition defaults.
func (c Config) Imports() ImportOptions {
	if c.ImportOptions.Timeout == 0 {
		return ImportOptions{Timeout: 20 * time.Second, MaximumHTMLBytes: 2 * 1024 * 1024, MaximumRedirects: 3, PermitLimit: 10}
	}
	return c.ImportOptions
}

func loadImports() (ImportOptions, error) {
	options := Config{}.Imports()
	var err error
	options.Timeout, err = time.ParseDuration(value("WISH_IMPORT_TIMEOUT", "20s"))
	if err != nil || options.Timeout < time.Second || options.Timeout > 20*time.Second {
		return options, errors.New("WISH_IMPORT_TIMEOUT must be between one and twenty seconds")
	}
	for _, setting := range []struct {
		Key              string
		Target           *int
		Minimum, Maximum int
	}{
		{"WISH_IMPORT_MAXIMUM_HTML_BYTES", &options.MaximumHTMLBytes, 1, 2 * 1024 * 1024},
		{"WISH_IMPORT_MAXIMUM_REDIRECTS", &options.MaximumRedirects, 0, 3},
		{"WISH_IMPORT_PERMIT_LIMIT", &options.PermitLimit, 1, 10},
	} {
		number, err := strconv.Atoi(value(setting.Key, strconv.Itoa(*setting.Target)))
		if err != nil || number < setting.Minimum || number > setting.Maximum {
			return options, errors.New(setting.Key + " exceeds the supported import limits")
		}
		*setting.Target = number
	}
	return options, nil
}
