// Package security implements credentials, opaque tokens and authenticated encryption.
package security

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/hmac"
	"crypto/pbkdf2"
	"crypto/rand"
	"crypto/sha1"
	"crypto/sha256"
	"crypto/sha512"
	"crypto/subtle"
	"encoding/base64"
	"encoding/binary"
	"errors"
	"hash"
	"strings"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"github.com/google/uuid"
)

const passwordIterations = 100000

// NewID creates a time-ordered UUID v7 before persistence.
func NewID() string { return uuid.Must(uuid.NewV7()).String() }

// HashPassword creates a salted PBKDF2 credential compatible with Identity v3.
func HashPassword(password string) string {
	salt := make([]byte, 16)
	rand.Read(salt)
	key, err := pbkdf2.Key(sha512.New, password, salt, passwordIterations, 32)
	if err != nil {
		panic(err)
	}
	data := make([]byte, 13, 61)
	data[0] = 1
	binary.BigEndian.PutUint32(data[1:5], 2)
	binary.BigEndian.PutUint32(data[5:9], passwordIterations)
	binary.BigEndian.PutUint32(data[9:13], uint32(len(salt)))
	data = append(append(data, salt...), key...)
	return base64.StdEncoding.EncodeToString(data)
}

// VerifyPassword verifies Identity v2/v3 credentials in constant time.
func VerifyPassword(password, encoded string) bool {
	data, err := base64.StdEncoding.DecodeString(encoded)
	if err != nil || len(data) < 1 {
		return false
	}
	if data[0] == 0 && len(data) == 49 {
		key, err := pbkdf2.Key(sha1.New, password, data[1:17], 1000, 32)
		return err == nil && subtle.ConstantTimeCompare(key, data[17:]) == 1
	}
	if data[0] != 1 || len(data) < 13 {
		return false
	}
	prf := binary.BigEndian.Uint32(data[1:5])
	iterations := binary.BigEndian.Uint32(data[5:9])
	saltLength := binary.BigEndian.Uint32(data[9:13])
	if iterations < 1 || iterations > 2000000 || saltLength < 16 || uint64(saltLength)+29 > uint64(len(data)) {
		return false
	}
	var algorithm func() hash.Hash
	switch prf {
	case 0:
		algorithm = sha1.New
	case 1:
		algorithm = sha256.New
	case 2:
		algorithm = sha512.New
	default:
		return false
	}
	offset := 13 + int(saltLength)
	key, err := pbkdf2.Key(algorithm, password, data[13:offset], int(iterations), len(data)-offset)
	return err == nil && subtle.ConstantTimeCompare(key, data[offset:]) == 1
}

// RandomToken generates a canonical 256-bit Base64URL bearer secret.
func RandomToken() string {
	b := make([]byte, 32)
	rand.Read(b)
	return base64.RawURLEncoding.EncodeToString(b)
}

// HashToken stores only the SHA-256 digest of a presented token.
func HashToken(value string) []byte { h := sha256.Sum256([]byte(value)); return h[:] }

// VerifyToken compares token material without timing-dependent comparisons.
func VerifyToken(value string, expected []byte) bool {
	return subtle.ConstantTimeCompare(HashToken(value), expected) == 1
}

// SessionToken preserves the session UUID and secret browser token format.
func SessionToken(id string) string { return strings.ReplaceAll(id, "-", "") + "." + RandomToken() }

// SessionID validates a refresh or guest token before extracting its identifier.
func SessionID(token string) (string, error) {
	parts := strings.Split(token, ".")
	if len(parts) != 2 || len(parts[0]) != 32 {
		return "", errors.New("invalid session token")
	}
	id, err := uuid.Parse(parts[0])
	secret, decodeErr := base64.RawURLEncoding.DecodeString(parts[1])
	if err != nil || decodeErr != nil || len(secret) != 32 || base64.RawURLEncoding.EncodeToString(secret) != parts[1] {
		return "", errors.New("invalid session token")
	}
	return id.String(), nil
}

// Cryptography protects secrets with purpose-bound AES-GCM and issues access tokens.
type Cryptography struct {
	SigningKey    []byte
	EncryptionKey []byte
	Issuer        string
	Audience      string
	Now           func() time.Time
}

// Protect authenticates encrypted data together with its purpose and resource identifier.
func (c *Cryptography) Protect(value, purpose string) (string, error) {
	block, err := aes.NewCipher(c.EncryptionKey)
	if err != nil {
		return "", err
	}
	gcm, err := cipher.NewGCMWithRandomNonce(block)
	if err != nil {
		return "", err
	}
	return base64.RawURLEncoding.EncodeToString(gcm.Seal(nil, nil, []byte(value), []byte(purpose))), nil
}

// Unprotect rejects corrupted data and secrets encrypted for a different purpose.
func (c *Cryptography) Unprotect(value, purpose string) (string, error) {
	block, err := aes.NewCipher(c.EncryptionKey)
	if err != nil {
		return "", err
	}
	gcm, err := cipher.NewGCMWithRandomNonce(block)
	if err != nil {
		return "", err
	}
	data, err := base64.RawURLEncoding.DecodeString(value)
	if err != nil || len(data) < gcm.NonceSize()+gcm.Overhead() {
		return "", errors.New("invalid protected data")
	}
	plain, err := gcm.Open(nil, nil, data, []byte(purpose))
	return string(plain), err
}

// AccessToken issues an HS256 token with a short lifetime and no personal claims.
func (c *Cryptography) AccessToken(userID, tokenID string) (string, error) {
	now := c.Now().UTC()
	claims := jwt.RegisteredClaims{Issuer: c.Issuer, Audience: jwt.ClaimStrings{c.Audience}, Subject: userID,
		ID: strings.ReplaceAll(tokenID, "-", ""), IssuedAt: jwt.NewNumericDate(now), ExpiresAt: jwt.NewNumericDate(now.Add(15 * time.Minute))}
	return jwt.NewWithClaims(jwt.SigningMethodHS256, claims).SignedString(c.SigningKey)
}

// VerifyAccessToken validates algorithm, signature, issuer, audience and expiry.
func (c *Cryptography) VerifyAccessToken(value string) (*jwt.RegisteredClaims, error) {
	claims := &jwt.RegisteredClaims{}
	_, err := jwt.ParseWithClaims(value, claims, func(token *jwt.Token) (any, error) { return c.SigningKey, nil },
		jwt.WithValidMethods([]string{"HS256"}), jwt.WithIssuer(c.Issuer), jwt.WithAudience(c.Audience),
		jwt.WithExpirationRequired(), jwt.WithIssuedAt(), jwt.WithTimeFunc(c.Now))
	if err != nil {
		return nil, err
	}
	if _, err = uuid.Parse(claims.Subject); err != nil {
		return nil, err
	}
	if _, err = uuid.Parse(claims.ID); err != nil {
		return nil, err
	}
	return claims, nil
}

// Sign computes a purpose-bound HMAC for browser proofs.
func (c *Cryptography) Sign(value string) string {
	h := hmac.New(sha256.New, c.SigningKey)
	h.Write([]byte(value))
	return base64.RawURLEncoding.EncodeToString(h.Sum(nil))
}
