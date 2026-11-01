package security

import (
	"crypto/aes"
	"crypto/cipher"
	"encoding/base64"
	"strings"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

func TestVerifyPassword_WhenCredentialIsInvalid_Rejects(t *testing.T) {
	// Arrange
	valid := HashPassword("A long Unicode password 🎁")
	for _, tc := range []struct {
		password, hash string
		expected       bool
	}{
		{"A long Unicode password 🎁", valid, true}, {"wrong", valid, false},
		{"password", "", false}, {"password", "not base64", false}, {"password", "AQ==", false},
	} {
		t.Run(tc.password+tc.hash[:min(len(tc.hash), 5)], func(t *testing.T) {
			// Act
			actual := VerifyPassword(tc.password, tc.hash)
			// Assert
			if actual != tc.expected {
				t.Fatalf("VerifyPassword = %v, expected %v", actual, tc.expected)
			}
		})
	}
}

func TestProtect_WhenUsingNativeNonceManagement_PreservesStoredCiphertextFormat(t *testing.T) {
	// Arrange
	key := []byte(strings.Repeat("k", 32))
	block, err := aes.NewCipher(key)
	if err != nil {
		t.Fatal(err)
	}
	previous, err := cipher.NewGCM(block)
	if err != nil {
		t.Fatal(err)
	}
	nonce := []byte("test-nonce12")
	// The old format is nonce followed by ciphertext and the authentication tag.
	stored := base64.RawURLEncoding.EncodeToString(previous.Seal(nonce, nonce, []byte("stored proof"), []byte("resource|1")))
	cryptography := &Cryptography{EncryptionKey: key}
	// Act
	plain, err := cryptography.Unprotect(stored, "resource|1")
	fresh, freshErr := cryptography.Protect("new proof", "resource|1")
	data, decodeErr := base64.RawURLEncoding.DecodeString(fresh)
	// Assert
	if err != nil || plain != "stored proof" || freshErr != nil || decodeErr != nil || len(data) < previous.NonceSize()+previous.Overhead() {
		t.Fatal("native nonce management changed the encrypted format")
	}
	decoded, err := previous.Open(nil, data[:previous.NonceSize()], data[previous.NonceSize():], []byte("resource|1"))
	if err != nil || string(decoded) != "new proof" {
		t.Fatal("new ciphertext cannot be read in the previous format")
	}
}

func TestProtect_WhenPurposeOrCiphertextChanges_Rejects(t *testing.T) {
	// Arrange
	c := &Cryptography{EncryptionKey: []byte(strings.Repeat("k", 32))}
	protected, err := c.Protect("sensitive token", "share|123")
	if err != nil {
		t.Fatal(err)
	}
	// Act
	plain, err := c.Unprotect(protected, "share|123")
	// Assert
	if err != nil || plain != "sensitive token" {
		t.Fatal("encrypted data did not round-trip")
	}
	for _, tc := range []struct{ value, purpose string }{{protected, "email|123"}, {"!" + protected, "share|123"}, {"", "share|123"}} {
		if _, err = c.Unprotect(tc.value, tc.purpose); err == nil {
			t.Fatal("tampered protected data was accepted")
		}
	}
}

func TestVerifyAccessToken_WhenExpiredOrWrongAudience_Rejects(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 11, 12, 0, 0, 0, time.UTC)
	c := &Cryptography{SigningKey: []byte(strings.Repeat("k", 32)), Issuer: "issuer", Audience: "audience", Now: func() time.Time { return now }}
	value, err := c.AccessToken(NewID(), NewID())
	if err != nil {
		t.Fatal(err)
	}
	// Act
	claims, err := c.VerifyAccessToken(value)
	// Assert
	if err != nil || claims.ExpiresAt.Time.Sub(now) != 15*time.Minute {
		t.Fatal("valid access token was rejected")
	}
	c.Audience = "other"
	if _, err = c.VerifyAccessToken(value); err == nil {
		t.Fatal("wrong audience was accepted")
	}
	c.Audience = "audience"
	now = now.Add(15 * time.Minute)
	if _, err = c.VerifyAccessToken(value); err == nil {
		t.Fatal("expired access token was accepted")
	}
	bad := jwt.NewWithClaims(jwt.SigningMethodNone, jwt.MapClaims{"sub": NewID()})
	unsigned, _ := bad.SignedString(jwt.UnsafeAllowNoneSignatureType)
	if _, err = c.VerifyAccessToken(unsigned); err == nil {
		t.Fatal("unsigned access token was accepted")
	}
}

func TestSessionID_WhenFormatIsInvalid_Rejects(t *testing.T) {
	// Arrange
	id := NewID()
	token := SessionToken(id)
	// Act
	actual, err := SessionID(token)
	// Assert
	if err != nil || actual != id || !VerifyToken(token, HashToken(token)) {
		t.Fatal("valid session token failed")
	}
	for _, value := range []string{"", "no-token", id + ".invalid", token + "=", token + ".extra"} {
		if _, err := SessionID(value); err == nil {
			t.Fatalf("invalid token accepted: %q", value)
		}
	}
}
