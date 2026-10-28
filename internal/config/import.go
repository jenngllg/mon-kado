package config

import (
	"errors"
	"net/url"
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
	ManagedProvidersEnabled                                           bool
	CloudflareOrigin, CloudflareKey                                   string
	FirecrawlEnabled, FirecrawlProductEnabled, FirecrawlImagesEnabled bool
	FirecrawlKey                                                      string
	LocalCloudflareEnabled                                            bool
	LocalCloudflareKey                                                string
	LocalFirecrawlEnabled                                             bool
	LocalFirecrawlKey                                                 string
	LocalFirecrawlProductEnabled                                      bool
	LocalCloudflareImagesEnabled                                      bool
	LocalFirecrawlImagesEnabled                                       bool
	LocalCloudflareRenderEnabled                                      bool
	LocalCloudflareRenderOrigin, LocalCloudflareRenderKey             string
	Timeout                                                           time.Duration
	MaximumHTMLBytes, MaximumRedirects, PermitLimit                   int
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
	options.ManagedProvidersEnabled, err = strconv.ParseBool(value("WISH_IMPORT_MANAGED_ENABLED", "false"))
	if err != nil {
		return options, errors.New("WISH_IMPORT_MANAGED_ENABLED must be a boolean")
	}
	if options.ManagedProvidersEnabled {
		options.CloudflareOrigin, err = NormalizeCloudflareRelayOrigin(os.Getenv("WISH_IMPORT_CLOUDFLARE_ORIGIN"))
		if err != nil {
			return options, err
		}
		options.CloudflareKey = os.Getenv("WISH_IMPORT_CLOUDFLARE_KEY")
		if !regexp.MustCompile(`^[a-f0-9]{64}$`).MatchString(options.CloudflareKey) {
			return options, errors.New("WISH_IMPORT_CLOUDFLARE_KEY must be a 32-byte hexadecimal secret")
		}
	}
	for _, setting := range []struct {
		key    string
		target *bool
	}{
		{"WISH_IMPORT_FIRECRAWL_ENABLED", &options.FirecrawlEnabled},
		{"WISH_IMPORT_FIRECRAWL_PRODUCT_ENABLED", &options.FirecrawlProductEnabled},
		{"WISH_IMPORT_FIRECRAWL_IMAGES_ENABLED", &options.FirecrawlImagesEnabled},
	} {
		enabled, parseErr := strconv.ParseBool(value(setting.key, "false"))
		if parseErr != nil {
			return options, errors.New(setting.key + " must be a boolean")
		}
		*setting.target = enabled
	}
	if options.FirecrawlEnabled && !options.ManagedProvidersEnabled || (options.FirecrawlProductEnabled || options.FirecrawlImagesEnabled) && !options.FirecrawlEnabled {
		return options, errors.New("managed Firecrawl settings require their parent provider")
	}
	if options.FirecrawlEnabled {
		options.FirecrawlKey = os.Getenv("FIRECRAWL_API_KEY")
		if !regexp.MustCompile(`^fc-[A-Za-z0-9_-]{16,256}$`).MatchString(options.FirecrawlKey) {
			return options, errors.New("FIRECRAWL_API_KEY is required for the managed fallback")
		}
	}
	options.LocalCloudflareEnabled, err = strconv.ParseBool(value("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", "false"))
	if err != nil {
		return options, errors.New("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED must be a boolean")
	}
	if options.LocalCloudflareEnabled {
		if options.ManagedProvidersEnabled {
			return options, errors.New("local and permanent Cloudflare providers are mutually exclusive")
		}
		if value("APP_ENV", "local") != "local" {
			return options, errors.New("the Cloudflare development relay is restricted to the local environment")
		}
		options.LocalCloudflareKey = os.Getenv("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY")
		if !regexp.MustCompile(`^[a-f0-9]{64}$`).MatchString(options.LocalCloudflareKey) {
			return options, errors.New("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY must be a 32-byte hexadecimal secret")
		}
	}
	options.LocalCloudflareRenderEnabled, err = strconv.ParseBool(value("WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ENABLED", "false"))
	if err != nil {
		return options, errors.New("WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ENABLED must be a boolean")
	}
	if options.LocalCloudflareRenderEnabled {
		if value("APP_ENV", "local") != "local" || !options.ManagedProvidersEnabled {
			return options, errors.New("Cloudflare browser rendering requires local managed imports")
		}
		options.LocalCloudflareRenderOrigin, err = NormalizeCloudflareRenderOrigin(os.Getenv("WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ORIGIN"))
		if err != nil {
			return options, err
		}
		options.LocalCloudflareRenderKey = os.Getenv("WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_KEY")
		if !regexp.MustCompile(`^[a-f0-9]{64}$`).MatchString(options.LocalCloudflareRenderKey) {
			return options, errors.New("Cloudflare browser rendering requires a 32-byte hexadecimal secret")
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

// NormalizeCloudflareRenderOrigin permits only the dedicated local browser Worker.
func NormalizeCloudflareRenderOrigin(value string) (string, error) {
	u, err := url.Parse(value)
	if err != nil || u.Scheme != "https" || u.User != nil || u.Port() != "" || u.ForceQuery || u.RawQuery != "" || u.Fragment != "" || u.RawFragment != "" || u.Path != "" && u.Path != "/" || !regexp.MustCompile(`^monkado-wish-render-local\.[a-z0-9-]+\.workers\.dev$`).MatchString(u.Host) {
		return "", errors.New("Cloudflare browser origin must be the dedicated local HTTPS Worker")
	}
	return "https://" + u.Host, nil
}

// NormalizeCloudflareRelayOrigin restricts the configured provider to its dedicated HTTPS Worker authority.
// Merchant destinations are separate inputs and never receive this authority's secret.
func NormalizeCloudflareRelayOrigin(value string) (string, error) {
	u, err := url.Parse(value)
	if err != nil || u.Scheme != "https" || u.User != nil || u.Port() != "" || u.ForceQuery || u.RawQuery != "" || u.Fragment != "" || u.RawFragment != "" || u.Path != "" && u.Path != "/" || !regexp.MustCompile(`^monkado-wish-import\.[a-z0-9-]+\.workers\.dev$`).MatchString(u.Host) {
		return "", errors.New("WISH_IMPORT_CLOUDFLARE_ORIGIN must be the dedicated HTTPS Worker origin")
	}
	return "https://" + u.Host, nil
}
