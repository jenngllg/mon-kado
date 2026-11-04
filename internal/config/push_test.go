package config

import (
	"crypto/ecdh"
	"crypto/rand"
	"encoding/base64"
	"testing"
)

func TestPushOptions_WhenEnabled_RequiresMatchingKeysAndContact(t *testing.T) {
	// Arrange
	key, err := ecdh.P256().GenerateKey(rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	values := map[string]string{"PUSH_ENABLED": "true", "PUSH_VAPID_PRIVATE_KEY": base64.RawURLEncoding.EncodeToString(key.Bytes()), "PUSH_VAPID_PUBLIC_KEY": base64.RawURLEncoding.EncodeToString(key.PublicKey().Bytes()), "PUSH_VAPID_SUBJECT": "https://monkado.fr"}
	for k, v := range values {
		t.Setenv(k, v)
	}
	// Act / Assert
	if p, err := loadPush(); err != nil || !p.Enabled {
		t.Fatal("valid push config rejected", err)
	}
	for _, item := range []struct{ key, value string }{{"PUSH_ENABLED", "invalid"}, {"PUSH_VAPID_PRIVATE_KEY", "!"}, {"PUSH_VAPID_PRIVATE_KEY", "AA"}, {"PUSH_VAPID_PUBLIC_KEY", "mismatch"}, {"PUSH_VAPID_SUBJECT", "file:///private"}, {"PUSH_VAPID_SUBJECT", "mailto:"}, {"PUSH_VAPID_SUBJECT", "https://"}} {
		t.Run(item.key+item.value, func(t *testing.T) {
			t.Setenv(item.key, item.value)
			if _, err := loadPush(); err == nil {
				t.Fatal("invalid setting accepted")
			}
		})
	}
	t.Setenv("PUSH_ENABLED", "false")
	t.Setenv("PUSH_VAPID_PRIVATE_KEY", "")
	if p, err := loadPush(); err != nil || p.Enabled {
		t.Fatal("disabled configuration requires keys")
	}
}

func TestLoad_WhenPushConfigurationIsInvalid_RejectsBeforeApplicationStartup(t *testing.T) {
	// Arrange
	validEnvironment(t)
	t.Setenv("PUSH_ENABLED", "invalid")
	// Act
	_, err := Load()
	// Assert
	if err == nil || err.Error() != "PUSH_ENABLED must be a boolean" {
		t.Fatal("invalid push setting was not rejected by application configuration")
	}
}
