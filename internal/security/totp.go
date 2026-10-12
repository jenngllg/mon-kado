package security

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha1"
	"crypto/subtle"
	"encoding/base32"
	"encoding/binary"
	"encoding/hex"
	"fmt"
	"strings"
	"time"
)

// AuthenticatorSecret creates a 160-bit RFC 6238 shared secret.
func AuthenticatorSecret() string {
	b := make([]byte, 20)
	rand.Read(b)
	return base32.StdEncoding.WithPadding(base32.NoPadding).EncodeToString(b)
}

// AuthenticatorCode computes the six-digit SHA-1 TOTP at the given counter.
func AuthenticatorCode(secret string, counter int64) (string, error) {
	key, err := base32.StdEncoding.WithPadding(base32.NoPadding).DecodeString(secret)
	if err != nil || len(key) != 20 {
		return "", fmt.Errorf("invalid authenticator secret")
	}
	var data [8]byte
	binary.BigEndian.PutUint64(data[:], uint64(counter))
	hash := hmac.New(sha1.New, key)
	hash.Write(data[:])
	digest := hash.Sum(nil)
	offset := digest[len(digest)-1] & 15
	value := binary.BigEndian.Uint32(digest[offset:offset+4]) & 0x7fffffff
	return fmt.Sprintf("%06d", value%1000000), nil
}

// VerifyAuthenticator accepts a neighboring time step once and rejects replay.
func VerifyAuthenticator(secret, code string, now time.Time, last *int64) (int64, bool) {
	if len(code) != 6 {
		return 0, false
	}
	for _, r := range code {
		if r < '0' || r > '9' {
			return 0, false
		}
	}
	for _, step := range []int64{now.Unix() / 30, now.Unix()/30 - 1, now.Unix()/30 + 1} {
		if last != nil && step <= *last {
			continue
		}
		expected, err := AuthenticatorCode(secret, step)
		if err == nil && subtle.ConstantTimeCompare([]byte(expected), []byte(code)) == 1 {
			return step, true
		}
	}
	return 0, false
}

// RecoveryCode creates a 128-bit single-use fallback proof.
func RecoveryCode() string {
	b := make([]byte, 16)
	rand.Read(b)
	value := strings.ToUpper(hex.EncodeToString(b))
	return value[:8] + "-" + value[8:16] + "-" + value[16:24] + "-" + value[24:]
}

// NormalizeRecoveryCode canonicalizes the display separators for hashing.
func NormalizeRecoveryCode(value string) string {
	return strings.ToUpper(strings.ReplaceAll(value, "-", ""))
}
