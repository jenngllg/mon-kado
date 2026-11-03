package config

import (
	"errors"
	"strconv"
	"time"
)

// PerimeterOptions bounds aggregate API traffic before authentication or database access.
type PerimeterOptions struct {
	PermitLimit          int
	ImageReadPermitLimit int
	Window               time.Duration
}

// Perimeter returns the validated quota or the consolidated API defaults.
func (c Config) Perimeter() PerimeterOptions {
	options := c.PerimeterOptions
	if options.PermitLimit == 0 {
		options.PermitLimit, options.Window = 300, time.Minute
	}
	if options.ImageReadPermitLimit == 0 {
		options.ImageReadPermitLimit = 1200
	}
	return options
}

func loadPerimeter() (PerimeterOptions, error) {
	options := Config{}.Perimeter()
	var err error
	options.PermitLimit, err = strconv.Atoi(value("GENERAL_RATE_LIMIT_PERMIT_LIMIT", "300"))
	if err != nil || options.PermitLimit < 1 {
		return options, errors.New("GENERAL_RATE_LIMIT_PERMIT_LIMIT must be positive")
	}
	options.ImageReadPermitLimit, err = strconv.Atoi(value("IMAGE_READ_RATE_LIMIT_PERMIT_LIMIT", "1200"))
	if err != nil || options.ImageReadPermitLimit < 1 {
		return options, errors.New("IMAGE_READ_RATE_LIMIT_PERMIT_LIMIT must be positive")
	}
	seconds, err := strconv.Atoi(value("GENERAL_RATE_LIMIT_WINDOW_SECONDS", "60"))
	if err != nil || seconds < 1 || seconds > 86400 {
		return options, errors.New("GENERAL_RATE_LIMIT_WINDOW_SECONDS must be between 1 and 86400")
	}
	options.Window = time.Duration(seconds) * time.Second
	return options, nil
}
