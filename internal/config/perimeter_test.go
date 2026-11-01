package config

import "testing"

func TestLoad_WhenPerimeterQuotaIsInvalid_RejectsBeforeStartup(t *testing.T) {
	for _, test := range []struct{ key, value string }{{"GENERAL_RATE_LIMIT_PERMIT_LIMIT", "0"}, {"GENERAL_RATE_LIMIT_PERMIT_LIMIT", "bad"}, {"GENERAL_RATE_LIMIT_WINDOW_SECONDS", "0"}, {"GENERAL_RATE_LIMIT_WINDOW_SECONDS", "bad"}, {"GENERAL_RATE_LIMIT_WINDOW_SECONDS", "86401"}} {
		t.Run(test.key+test.value, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv(test.key, test.value)
			// Act
			_, err := Load()
			// Assert
			if err == nil {
				t.Fatal("invalid perimeter quota accepted")
			}
		})
	}
}
