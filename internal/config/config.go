// Package config loads validated process configuration without storing secrets.
package config

import (
	"encoding/base64"
	"errors"
	"net/mail"
	"net/netip"
	"net/url"
	"os"
	"strconv"
	"strings"
	"time"
)

// Config is the immutable configuration shared by API and worker processes.
type Config struct {
	DatabaseURL              string
	Address                  string
	Production               bool
	SigningKey               []byte
	EncryptionKey            []byte
	Issuer                   string
	Audience                 string
	FrontendOrigin           string
	AllowedOrigins           []string
	AllowedHosts             []string
	ImagePath                string
	ExportPath               string
	WorkerInterval           time.Duration
	HTTPTimeout              time.Duration
	MaximumAttempts          int
	GmailSender              string
	GmailClientID            string
	GmailClientSecret        string
	GmailRefreshToken        string
	EmailProvider            string
	MailboxPath              string
	GoogleClientID           string
	GoogleClientSecret       string
	GoogleEnabled            bool
	PublicAPIOrigin          string
	TrustedProxies           []netip.Prefix
	JobOptions               JobOptions
	ImportOptions            ImportOptions
	PerimeterOptions         PerimeterOptions
	ObservabilityOptions     ObservabilityOptions
	ProviderFailureThreshold int
	ProviderBreakDuration    time.Duration
}

func value(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

// Load rejects incomplete configuration before opening listeners or running jobs.
func Load() (Config, error) {
	environment := value("APP_ENV", "local")
	if environment != "local" && environment != "production" {
		return Config{}, errors.New("APP_ENV must be local or production")
	}
	c := Config{
		DatabaseURL: os.Getenv("DATABASE_URL"), Address: value("HTTP_ADDRESS", ":7000"),
		Production: environment == "production",
		Issuer:     value("JWT_ISSUER", "MonKado.Api"), Audience: value("JWT_AUDIENCE", "MonKado.Frontend"),
		FrontendOrigin: value("FRONTEND_ORIGIN", "http://localhost:5173"),
		AllowedOrigins: strings.Split(value("ALLOWED_ORIGINS", "http://localhost:5173"), ","),
		AllowedHosts:   strings.Split(value("ALLOWED_HOSTS", "localhost,127.0.0.1"), ","),
		ImagePath:      value("IMAGE_PATH", ".local/gift-images"), ExportPath: value("EXPORT_PATH", ".local/personal-data-exports"),
		GmailSender: os.Getenv("GMAIL_SENDER_ADDRESS"), GmailClientID: os.Getenv("GMAIL_CLIENT_ID"),
		GmailClientSecret: os.Getenv("GMAIL_CLIENT_SECRET"), GmailRefreshToken: os.Getenv("GMAIL_REFRESH_TOKEN"),
		EmailProvider:  value("EMAIL_PROVIDER", "Disabled"),
		MailboxPath:    value("MAILBOX_PATH", ".local/mailbox"),
		GoogleClientID: os.Getenv("GOOGLE_CLIENT_ID"), GoogleClientSecret: os.Getenv("GOOGLE_CLIENT_SECRET"),
		PublicAPIOrigin: value("PUBLIC_API_ORIGIN", "http://localhost:7000"),
	}
	var err error
	c.ProviderFailureThreshold, err = strconv.Atoi(value("PROVIDER_FAILURE_THRESHOLD", "5"))
	if err != nil || c.ProviderFailureThreshold < 1 || c.ProviderFailureThreshold > 100 {
		return c, errors.New("PROVIDER_FAILURE_THRESHOLD must be between 1 and 100")
	}
	c.ProviderBreakDuration, err = time.ParseDuration(value("PROVIDER_BREAK_DURATION", "30s"))
	if err != nil || c.ProviderBreakDuration <= 0 {
		return c, errors.New("PROVIDER_BREAK_DURATION must be positive")
	}
	c.JobOptions, err = loadJobs()
	if err != nil {
		return c, err
	}
	c.ImportOptions, err = loadImports()
	if err != nil {
		return c, err
	}
	c.PerimeterOptions, err = loadPerimeter()
	if err != nil {
		return c, err
	}
	c.ObservabilityOptions, err = loadObservability()
	if err != nil {
		return c, err
	}
	c.GoogleEnabled, err = strconv.ParseBool(value("GOOGLE_ENABLED", "false"))
	if err != nil {
		return c, errors.New("GOOGLE_ENABLED must be a boolean")
	}
	for _, text := range strings.Split(os.Getenv("TRUSTED_PROXIES"), ",") {
		if strings.TrimSpace(text) == "" {
			continue
		}
		prefix, parseErr := netip.ParsePrefix(strings.TrimSpace(text))
		if parseErr != nil {
			return c, errors.New("TRUSTED_PROXIES must contain explicit CIDR networks")
		}
		c.TrustedProxies = append(c.TrustedProxies, prefix.Masked())
	}
	c.SigningKey, err = base64.StdEncoding.DecodeString(os.Getenv("JWT_SIGNING_KEY"))
	if err != nil || len(c.SigningKey) < 32 {
		return c, errors.New("JWT_SIGNING_KEY must be Base64 with at least 32 bytes")
	}
	c.EncryptionKey, err = base64.StdEncoding.DecodeString(os.Getenv("DATA_ENCRYPTION_KEY"))
	if err != nil || len(c.EncryptionKey) != 32 {
		return c, errors.New("DATA_ENCRYPTION_KEY must be Base64 with exactly 32 bytes")
	}
	if c.DatabaseURL == "" {
		return c, errors.New("DATABASE_URL is required")
	}
	c.WorkerInterval, err = time.ParseDuration(value("WORKER_INTERVAL", "10s"))
	if err != nil || c.WorkerInterval <= 0 {
		return c, errors.New("WORKER_INTERVAL must be positive")
	}
	c.HTTPTimeout, err = time.ParseDuration(value("PROVIDER_HTTP_TIMEOUT", "15s"))
	if err != nil || c.HTTPTimeout <= 0 || c.HTTPTimeout > time.Minute {
		return c, errors.New("PROVIDER_HTTP_TIMEOUT must be positive and at most one minute")
	}
	if c.HTTPTimeout >= c.JobOptions.LeaseDuration {
		return c, errors.New("provider timeout must be shorter than the outbox lease")
	}
	c.MaximumAttempts, err = strconv.Atoi(value("OUTBOX_MAXIMUM_ATTEMPTS", "10"))
	if err != nil || c.MaximumAttempts < 1 {
		return c, errors.New("OUTBOX_MAXIMUM_ATTEMPTS must be positive")
	}
	for _, origin := range append(append([]string{}, c.AllowedOrigins...), c.FrontendOrigin) {
		u, err := url.Parse(origin)
		if err != nil || u.Host == "" || u.User != nil || u.RawQuery != "" || u.Fragment != "" || u.Path != "" || strings.Contains(origin, "*") || (u.Scheme != "https" && (c.Production || u.Scheme != "http")) {
			return c, errors.New("frontend and allowed origins must be exact HTTP origins, using HTTPS in production")
		}
	}
	if c.GoogleEnabled && (c.GoogleClientID == "" || c.GoogleClientSecret == "") {
		return c, errors.New("enabled Google authentication requires client credentials")
	}
	if c.GoogleEnabled {
		origin, err := url.Parse(c.PublicAPIOrigin)
		if err != nil || origin.Scheme != "https" || origin.Host == "" || origin.Path != "" || origin.RawQuery != "" || origin.Fragment != "" || origin.User != nil {
			return c, errors.New("Google authentication requires an exact HTTPS PUBLIC_API_ORIGIN")
		}
	}
	if c.EmailProvider != "Gmail" && c.EmailProvider != "Disabled" && c.EmailProvider != "Mailbox" {
		return c, errors.New("EMAIL_PROVIDER must be Gmail, Mailbox or Disabled")
	}
	if c.EmailProvider == "Mailbox" && c.Production {
		return c, errors.New("Mailbox delivery is only available in local development")
	}
	if c.EmailProvider == "Gmail" && (c.GmailSender == "" || c.GmailClientID == "" || c.GmailClientSecret == "" || c.GmailRefreshToken == "") {
		return c, errors.New("Gmail delivery requires sender address and OAuth credentials")
	}
	if c.EmailProvider == "Gmail" {
		address, err := mail.ParseAddress(c.GmailSender)
		if err != nil || address.Address != c.GmailSender {
			return c, errors.New("GMAIL_SENDER_ADDRESS must be an exact email address")
		}
	}
	for i, host := range c.AllowedHosts {
		host = strings.TrimSpace(host)
		if host == "" || strings.ContainsAny(host, "*/\\@\r\n\t ") {
			return c, errors.New("ALLOWED_HOSTS must contain exact host names or IP addresses")
		}
		c.AllowedHosts[i] = host
	}
	return c, nil
}
