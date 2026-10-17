package config

import (
	"errors"
	"regexp"
	"strconv"
	"time"
)

// MaximumImportHTMLBytes bounds product pages independently of image download limits.
// Modern merchant pages can exceed two MiB before their product metadata ends.
const MaximumImportHTMLBytes = 4 * 1024 * 1024

// ImportOptions bounds merchant previews within the supported security limits.
type ImportOptions struct {
	CatalogEnabled                                  bool
	CatalogIndexKey                                 string
	Timeout                                         time.Duration
	MaximumHTMLBytes, MaximumRedirects, PermitLimit int
}

// Imports returns the startup-validated merchant settings or the composition defaults.
func (c Config) Imports() ImportOptions {
	if c.ImportOptions.Timeout == 0 {
		return ImportOptions{Timeout: 20 * time.Second, MaximumHTMLBytes: MaximumImportHTMLBytes, MaximumRedirects: 3, PermitLimit: 10, CatalogEnabled: c.ImportOptions.CatalogEnabled, CatalogIndexKey: c.ImportOptions.CatalogIndexKey}
	}
	return c.ImportOptions
}

func loadImports() (ImportOptions, error) {
	options := Config{}.Imports()
	var err error
	options.CatalogEnabled, err = strconv.ParseBool(value("WISH_IMPORT_CATALOG_ENABLED", "false"))
	if err != nil {
		return options, errors.New("WISH_IMPORT_CATALOG_ENABLED must be a boolean")
	}
	options.CatalogIndexKey = value("WISH_IMPORT_CATALOG_INDEX_KEY", "")
	if options.CatalogEnabled && !regexp.MustCompile(`^key_[A-Za-z0-9_]{1,96}$`).MatchString(options.CatalogIndexKey) {
		return options, errors.New("WISH_IMPORT_CATALOG_INDEX_KEY must identify the public storefront search index")
	}
	options.Timeout, err = time.ParseDuration(value("WISH_IMPORT_TIMEOUT", "20s"))
	if err != nil || options.Timeout < time.Second || options.Timeout > 20*time.Second {
		return options, errors.New("WISH_IMPORT_TIMEOUT must be between one and twenty seconds")
	}
	for _, setting := range []struct {
		Key              string
		Target           *int
		Minimum, Maximum int
	}{
		{"WISH_IMPORT_MAXIMUM_HTML_BYTES", &options.MaximumHTMLBytes, 1, MaximumImportHTMLBytes},
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
