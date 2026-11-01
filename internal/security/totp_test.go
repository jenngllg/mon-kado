package security_test

import (
	"encoding/base32"
	"github.com/JennGllg/mon-kado/internal/security"
	"testing"
	"time"
)

func TestAuthenticatorCode_WhenUsingRFC6238Vectors_MatchesSixDigitValues(t *testing.T) {
	// Arrange
	secret := base32.StdEncoding.WithPadding(base32.NoPadding).EncodeToString([]byte("12345678901234567890"))
	for _, test := range []struct {
		seconds int64
		code    string
	}{{59, "287082"}, {1111111109, "081804"}, {1111111111, "050471"}, {1234567890, "005924"}, {2000000000, "279037"}, {20000000000, "353130"}} {
		t.Run(test.code, func(t *testing.T) {
			// Act
			actual, err := security.AuthenticatorCode(secret, test.seconds/30)
			// Assert
			if err != nil || actual != test.code {
				t.Fatalf("got %s, %v", actual, err)
			}
		})
	}
}

func TestVerifyAuthenticator_WhenCodeWasAccepted_RejectsReplay(t *testing.T) {
	// Arrange
	secret, now := security.AuthenticatorSecret(), time.Unix(1800000000, 0)
	code, err := security.AuthenticatorCode(secret, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	// Act
	step, valid := security.VerifyAuthenticator(secret, code, now, nil)
	_, replay := security.VerifyAuthenticator(secret, code, now, &step)
	// Assert
	if !valid || replay {
		t.Fatal("single-use counter invariant violated")
	}
}

func TestVerifyAuthenticator_WhenInputIsMalformed_RejectsIt(t *testing.T) {
	// Arrange
	for _, code := range []string{"", "12345", "1234567", "１２３４５６", "abcdef"} {
		// Act
		_, valid := security.VerifyAuthenticator(security.AuthenticatorSecret(), code, time.Unix(1800000000, 0), nil)
		// Assert
		if valid {
			t.Fatal("malformed code accepted")
		}
	}
}
