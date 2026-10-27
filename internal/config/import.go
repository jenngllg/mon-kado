package config

import (
	"errors"
	"os"
	"regexp"
	"strconv"
	"time"
)

// MaximumImportHTMLBytes bounds product pages independently of image download limits.
// Modern merchant pages can exceed two MiB before their product metadata ends.
const MaximumImportHTMLBytes = 4 * 1024 * 1024

// MaximumImportDuration leaves transport and browser processing time within ten seconds.
const MaximumImportDuration = 8 * time.Second

// ImportOptions bounds merchant previews within the supported security limits.
type ImportOptions struct {
	LocalCloudflareEnabled                          bool
	LocalCloudflareKey                              string
	LocalFirecrawlEnabled                           bool
	LocalFirecrawlKey                               string
	LocalFirecrawlProductEnabled                    bool
	LocalCloudflareImagesEnabled                    bool
	LocalFirecrawlImagesEnabled                     bool
	Timeout                                         time.Duration
	MaximumHTMLBytes, MaximumRedirects, PermitLimit int
}

// Imports returns the startup-validated merchant settings or the composition defaults.
func (c Config) Imports() ImportOptions {
	if c.ImportOptions.Timeout == 0 {
		options := c.ImportOptions
		options.Timeout, options.MaximumHTMLBytes, options.MaximumRedirects, options.PermitLimit = MaximumImportDuration, MaximumImportHTMLBytes, 3, 10
		return options
	}
	return c.ImportOptions
}

func loadImports() (ImportOptions, error) {
	options := Config{}.Imports()
	var err error
	options.LocalCloudflareEnabled, err = strconv.ParseBool(value("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", "false"))
	if err != nil {
		return options, errors.New("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED must be a boolean")
	}
	if options.LocalCloudflareEnabled {
		if value("APP_ENV", "local") != "local" {
			return options, errors.New("the Cloudflare development relay is restricted to the local environment")
		}
		options.LocalCloudflareKey = os.Getenv("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY")
		if !regexp.MustCompile(`^[a-f0-9]{64}$`).MatchString(options.LocalCloudflareKey) {
			return options, errors.New("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY must be a 32-byte hexadecimal secret")
		}
	}
	options.LocalFirecrawlEnabled, err = strconv.ParseBool(value("WISH_IMPORT_LOCAL_FIRECRAWL_ENABLED", "false"))
	if err != nil {
		return options, errors.New("WISH_IMPORT_LOCAL_FIRECRAWL_ENABLED must be a boolean")
	}
	if options.LocalFirecrawlEnabled {
		if value("APP_ENV", "local") != "local" || !options.LocalCloudflareEnabled {
			return options, errors.New("the Firecrawl development fallback requires local Cloudflare")
		}
		options.LocalFirecrawlKey = os.Getenv("FIRECRAWL_API_KEY")
		if !regexp.MustCompile(`^fc-[A-Za-z0-9_-]{16,256}$`).MatchString(options.LocalFirecrawlKey) {
			return options, errors.New("FIRECRAWL_API_KEY is required for the development fallback")
		}
	}
	for _, setting := range []struct {
		key    string
		target *bool
		parent bool
	}{
		{"WISH_IMPORT_LOCAL_FIRECRAWL_PRODUCT_ENABLED", &options.LocalFirecrawlProductEnabled, options.LocalFirecrawlEnabled},
		{"WISH_IMPORT_LOCAL_CLOUDFLARE_IMAGES_ENABLED", &options.LocalCloudflareImagesEnabled, options.LocalCloudflareEnabled},
		{"WISH_IMPORT_LOCAL_FIRECRAWL_IMAGES_ENABLED", &options.LocalFirecrawlImagesEnabled, options.LocalFirecrawlEnabled},
	} {
		enabled, parseErr := strconv.ParseBool(value(setting.key, "false"))
		if parseErr != nil {
			return options, errors.New(setting.key + " must be a boolean")
		}
		if enabled && (value("APP_ENV", "local") != "local" || !setting.parent) {
			return options, errors.New(setting.key + " requires its local development provider")
		}
		*setting.target = enabled
	}
	options.Timeout, err = time.ParseDuration(value("WISH_IMPORT_TIMEOUT", MaximumImportDuration.String()))
	if err != nil || options.Timeout < time.Second || options.Timeout > MaximumImportDuration {
		return options, errors.New("WISH_IMPORT_TIMEOUT must be between one and eight seconds")
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
