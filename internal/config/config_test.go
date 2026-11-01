package config

import (
	"encoding/base64"
	"testing"
	"time"
)

func validEnvironment(t *testing.T) {
	t.Helper()
	for _, key := range []string{"WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ENABLED", "WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ORIGIN", "WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_KEY", "WISH_IMPORT_LOCAL_FIRECRAWL_KEYLESS_ENABLED", "WISH_IMPORT_LOCAL_FIRECRAWL_KEYLESS_IMAGES_ENABLED", "WISH_IMPORT_LOCAL_FIRECRAWL_ACCOUNT_IMAGE_FALLBACK_ENABLED", "WISH_IMPORT_LOCAL_FIRECRAWL_ROLLING_ENABLED"} {
		t.Setenv(key, "")
	}
	for _, key := range []string{"WISH_IMPORT_MANAGED_ENABLED", "WISH_IMPORT_CLOUDFLARE_ORIGIN", "WISH_IMPORT_CLOUDFLARE_KEY", "WISH_IMPORT_FIRECRAWL_ENABLED", "WISH_IMPORT_FIRECRAWL_PRODUCT_ENABLED", "WISH_IMPORT_FIRECRAWL_IMAGES_ENABLED", "WISH_IMPORT_LOCAL_FIRECRAWL_PRODUCT_ENABLED", "WISH_IMPORT_LOCAL_CLOUDFLARE_IMAGES_ENABLED", "WISH_IMPORT_LOCAL_FIRECRAWL_IMAGES_ENABLED"} {
		t.Setenv(key, "")
	}
	for _, key := range []string{"JWT_SIGNING_KEY", "DATA_ENCRYPTION_KEY", "DATABASE_URL", "APP_ENV", "HTTP_ADDRESS", "FRONTEND_ORIGIN", "ALLOWED_ORIGINS", "ALLOWED_HOSTS", "TRUSTED_PROXIES", "EMAIL_PROVIDER", "GOOGLE_ENABLED", "GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_SECRET", "PUBLIC_API_ORIGIN", "WORKER_INTERVAL", "PROVIDER_HTTP_TIMEOUT", "OUTBOX_MAXIMUM_ATTEMPTS", "GMAIL_CLIENT_ID", "GMAIL_CLIENT_SECRET", "GMAIL_SENDER_ADDRESS", "GMAIL_REFRESH_TOKEN", "JOB_LEASE_DURATION", "JOB_RENEWAL_INTERVAL", "EXPORT_TIMEOUT", "EXPORT_LIFETIME", "PENDING_FILE_GRACE_PERIOD", "OUTBOX_FIRST_RETRY_DELAY", "OUTBOX_MAXIMUM_RETRY_DELAY", "JOB_BATCH_SIZE", "EXPORT_BATCH_SIZE", "EXPORT_MAXIMUM_ATTEMPTS", "EXPORT_MAXIMUM_BYTES"} {
		t.Setenv(key, "")
	}
	t.Setenv("JWT_SIGNING_KEY", base64.StdEncoding.EncodeToString(make([]byte, 32)))
	t.Setenv("DATA_ENCRYPTION_KEY", base64.StdEncoding.EncodeToString(make([]byte, 32)))
	t.Setenv("DATABASE_URL", "postgres://test@localhost/mon_kado_test")
	t.Setenv("PROVIDER_FAILURE_THRESHOLD", "")
	t.Setenv("PROVIDER_BREAK_DURATION", "")
	for _, key := range []string{"WISH_IMPORT_TIMEOUT", "WISH_IMPORT_MAXIMUM_HTML_BYTES", "WISH_IMPORT_MAXIMUM_REDIRECTS", "WISH_IMPORT_PERMIT_LIMIT", "WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", "WISH_IMPORT_LOCAL_CLOUDFLARE_KEY", "WISH_IMPORT_LOCAL_FIRECRAWL_ENABLED", "FIRECRAWL_API_KEY"} {
		t.Setenv(key, "")
	}
}

func TestLoad_WhenConfigurationInvalid_RejectsBeforeStarting(t *testing.T) {
	for _, test := range []struct{ Key, Value string }{
		{"JWT_SIGNING_KEY", "bad"}, {"JWT_SIGNING_KEY", "AQ=="}, {"DATA_ENCRYPTION_KEY", "bad"}, {"DATA_ENCRYPTION_KEY", "AQ=="}, {"DATABASE_URL", ""}, {"GOOGLE_ENABLED", "yes"}, {"TRUSTED_PROXIES", "*"}, {"WORKER_INTERVAL", "invalid"}, {"WORKER_INTERVAL", "0s"}, {"PROVIDER_HTTP_TIMEOUT", "0s"}, {"PROVIDER_HTTP_TIMEOUT", "61s"}, {"OUTBOX_MAXIMUM_ATTEMPTS", "0"}, {"ALLOWED_ORIGINS", "https://*.example.com"}, {"FRONTEND_ORIGIN", "https://example.com/"}, {"APP_ENV", "production"}, {"GOOGLE_ENABLED", "true"}, {"EMAIL_PROVIDER", "Unknown"}, {"EMAIL_PROVIDER", "Gmail"}, {"ALLOWED_HOSTS", "*"}, {"JOB_LEASE_DURATION", "bad"}, {"JOB_RENEWAL_INTERVAL", "3m"}, {"JOB_BATCH_SIZE", "0"}, {"EXPORT_BATCH_SIZE", "1001"}, {"EXPORT_MAXIMUM_ATTEMPTS", "bad"}, {"EXPORT_MAXIMUM_BYTES", "1"}, {"EXPORT_MAXIMUM_BYTES", "1073741825"}, {"PENDING_FILE_GRACE_PERIOD", "1m"}, {"OUTBOX_FIRST_RETRY_DELAY", "25h"}, {"JOB_LEASE_DURATION", "10s"},
	} {
		t.Run(test.Key+"="+test.Value, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv(test.Key, test.Value)
			// Act
			_, err := Load()
			// Assert
			if err == nil {
				t.Fatal("invalid configuration accepted")
			}
		})
	}
}

func TestLoad_WhenNativeLocalConfigurationValid_UsesBoundedDefaults(t *testing.T) {
	// Arrange
	validEnvironment(t)
	t.Setenv("TRUSTED_PROXIES", "127.0.0.1/32, ::1/128")
	// Act
	c, err := Load()
	// Assert
	if err != nil || c.EmailProvider != "Disabled" || len(c.TrustedProxies) != 2 || c.Jobs().ExportMaximumBytes != 1<<30 || c.WorkerInterval != 10*time.Second {
		t.Fatalf("unexpected configuration: %v", err)
	}
}

func TestLoad_WhenEnvironmentMisspelled_RejectsInsteadOfDisablingProductionProtection(t *testing.T) {
	for _, environment := range []string{"Production", "prod", "development", "production ", "unknown"} {
		t.Run(environment, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("APP_ENV", environment)
			// Act
			_, err := Load()
			// Assert
			if err == nil || err.Error() != "APP_ENV must be local or production" {
				t.Fatal("unknown environment silently disabled production protection")
			}
		})
	}
}

func TestLoad_WhenGmailSenderIsNotAnExactAddress_RejectsStartup(t *testing.T) {
	for _, address := range []string{"invalid", "Sender <sender@example.test>", "sender@example.test,other@example.test", "sender@example.test\r\nBcc:other@example.test"} {
		t.Run(address, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("EMAIL_PROVIDER", "Gmail")
			for _, key := range []string{"GMAIL_CLIENT_ID", "GMAIL_CLIENT_SECRET", "GMAIL_REFRESH_TOKEN"} {
				t.Setenv(key, "test-setting")
			}
			t.Setenv("GMAIL_SENDER_ADDRESS", address)
			// Act
			_, err := Load()
			// Assert
			if err == nil {
				t.Fatal("invalid sender address accepted")
			}
		})
	}
}

func TestLoad_WhenGoogleEnabled_RequiresHTTPSCallbackAndCredentials(t *testing.T) {
	// Arrange
	validEnvironment(t)
	t.Setenv("GOOGLE_ENABLED", "true")
	t.Setenv("GOOGLE_CLIENT_ID", "client")
	t.Setenv("GOOGLE_CLIENT_SECRET", "secret")
	// Act
	_, invalid := Load()
	t.Setenv("PUBLIC_API_ORIGIN", "https://api.example.com")
	c, err := Load()
	// Assert
	if invalid == nil || err != nil || !c.GoogleEnabled {
		t.Fatal("Google callback validation differs")
	}
}

func TestLoad_WhenProvidersConfigured_ValidatesMailboxAndCircuitSettings(t *testing.T) {
	for _, provider := range []string{"Gmail", "Mailbox"} {
		t.Run(provider, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("EMAIL_PROVIDER", provider)
			for _, key := range []string{"GMAIL_CLIENT_ID", "GMAIL_CLIENT_SECRET", "GMAIL_REFRESH_TOKEN", "GMAIL_SENDER_ADDRESS"} {
				t.Setenv(key, "test-setting")
			}
			t.Setenv("GMAIL_SENDER_ADDRESS", "sender@example.test")
			// Act
			c, err := Load()
			// Assert
			if err != nil || c.EmailProvider != provider || c.ProviderFailureThreshold != 5 || c.ProviderBreakDuration != 30*time.Second {
				t.Fatal("valid provider settings rejected")
			}
			if provider == "Mailbox" {
				t.Setenv("APP_ENV", "production")
				t.Setenv("FRONTEND_ORIGIN", "https://example.test")
				t.Setenv("ALLOWED_ORIGINS", "https://example.test")
				if _, err = Load(); err == nil {
					t.Fatal("local mailbox enabled in production")
				}
			}
		})
	}
	for _, test := range []struct{ Key, Value string }{{"PROVIDER_FAILURE_THRESHOLD", "0"}, {"PROVIDER_FAILURE_THRESHOLD", "101"}, {"PROVIDER_FAILURE_THRESHOLD", "invalid"}, {"PROVIDER_BREAK_DURATION", "invalid"}, {"PROVIDER_BREAK_DURATION", "0s"}} {
		t.Run(test.Key+test.Value, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv(test.Key, test.Value)
			// Act
			_, err := Load()
			// Assert
			if err == nil {
				t.Fatal("invalid circuit settings accepted")
			}
		})
	}
}
