package security

import (
	"crypto/pbkdf2"
	"crypto/sha1"
	"crypto/sha256"
	"crypto/sha512"
	"encoding/base64"
	"encoding/binary"
	"hash"
	"testing"
)

func TestHashPassword_WhenCreatingCredential_UsesStrengthenedSaltedPolicy(t *testing.T) {
	// Arrange
	password := "A long Unicode password 🎁"
	// Act
	first, second := HashPassword(password), HashPassword(password)
	// Assert
	data, err := base64.StdEncoding.DecodeString(first)
	if err != nil || len(data) != 61 || data[0] != 1 || binary.BigEndian.Uint32(data[1:5]) != 2 || binary.BigEndian.Uint32(data[5:9]) != 220000 || binary.BigEndian.Uint32(data[9:13]) != 16 {
		t.Fatal("new credentials do not meet the PBKDF2-HMAC-SHA512 policy")
	}
	if first == second || !VerifyPassword(password, first) || PasswordNeedsRehash(first) {
		t.Fatal("salted credentials must verify without another upgrade")
	}
}

func TestPasswordNeedsRehash_WhenVerifiedParametersDiffer_PreservesCompatibilityAndStrongerHashes(t *testing.T) {
	for _, tc := range []struct {
		name                  string
		format                byte
		prf, iterations, size int
		upgrade               bool
	}{
		{"identityV2", 0, 0, 1000, 32, true},
		{"previousSHA512", 1, 2, 100000, 32, true},
		{"legacySHA1", 1, 0, 1000, 32, true},
		{"legacySHA256", 1, 1, 100000, 32, true},
		{"shortDerivedKey", 1, 2, 220000, 16, true},
		{"currentPolicy", 1, 2, 220000, 32, false},
		{"higherCost", 1, 2, 300000, 32, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			password := "A long Unicode password 🎁"
			encoded := passwordVector(t, password, tc.format, tc.prf, tc.iterations, tc.size)
			// Act
			valid, upgrade := VerifyPassword(password, encoded), PasswordNeedsRehash(encoded)
			// Assert
			if !valid || upgrade != tc.upgrade || VerifyPassword("A different password", encoded) {
				t.Fatal("credential verification or upgrade policy differs")
			}
		})
	}
}

func TestPasswordNeedsRehash_WhenEncodingIsInvalid_DoesNotTreatItAsVerified(t *testing.T) {
	// Arrange
	for _, encoded := range []string{"", "not base64", "AQ==", "AA==", base64.StdEncoding.EncodeToString(make([]byte, 13))} {
		// Act / Assert
		if PasswordNeedsRehash(encoded) {
			t.Fatal("invalid credential was marked for upgrade")
		}
	}
}

func passwordVector(t *testing.T, password string, format byte, prf, iterations, size int) string {
	t.Helper()
	salt := []byte("migration-salt!!")
	algorithms := []func() hash.Hash{sha1.New, sha256.New, sha512.New}
	key, err := pbkdf2.Key(algorithms[prf], password, salt, iterations, size)
	if err != nil {
		t.Fatal(err)
	}
	if format == 0 {
		return base64.StdEncoding.EncodeToString(append(append([]byte{0}, salt...), key...))
	}
	data := make([]byte, 13)
	data[0] = format
	binary.BigEndian.PutUint32(data[1:5], uint32(prf))
	binary.BigEndian.PutUint32(data[5:9], uint32(iterations))
	binary.BigEndian.PutUint32(data[9:13], uint32(len(salt)))
	return base64.StdEncoding.EncodeToString(append(append(data, salt...), key...))
}
