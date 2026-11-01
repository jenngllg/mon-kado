package httpapi

import (
	"encoding/json"
	"strings"
	"testing"
)

func TestActivityPayload_WhenDTOContainsSecrets_MasksFieldsAndURLCredentials(t *testing.T) {
	// Arrange
	input := map[string]any{
		"name": "Élodie <script>", "price": json.Number("19.99"),
		"password": "secret-password", "email": "private@example.test", "recoveryCode": "secret-recovery",
		"newCredential": "secret-future-field", "url": "https://user:secret@shop.example/product?token=secret-query#secret-fragment",
		"groups": []any{map[string]any{"groupId": "technical-id", "isVisible": true, "token": "nested-secret"}},
	}
	// Act
	encoded := activityPayload(input)
	var result map[string]any
	if err := json.Unmarshal(encoded, &result); err != nil {
		t.Fatal(err)
	}
	// Assert
	if result["name"] != input["name"] || result["price"] != 19.99 || result["url"] != "https://shop.example/product" {
		t.Fatalf("business payload was lost: %s", encoded)
	}
	for _, value := range []string{"secret-password", "private@example.test", "secret-recovery", "secret-future-field", "secret-query", "secret-fragment", "nested-secret"} {
		if strings.Contains(string(encoded), value) {
			t.Fatalf("sensitive value retained: %s", value)
		}
	}
	if result["newCredential"] != activityRedacted {
		t.Fatal("new fields must be masked by default")
	}
}

func TestActivityPayload_WhenPayloadCannotBeSafelyStored_OmitsIt(t *testing.T) {
	// Arrange
	cases := []struct {
		name     string
		input    any
		expected string
	}{
		{"oversized", map[string]any{"note": strings.Repeat("x", activityPayloadLimit)}, `{"_omitted":"payloadTooLarge"}`},
		{"unsupported", make(chan int), ""},
		{"unsafeURL", map[string]any{"url": "javascript:secret"}, `{"url":"[REDACTED]"}`},
		{"nullURL", map[string]any{"url": nil}, `{"url":null}`},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			// Act
			result := activityPayload(tc.input)
			// Assert
			if string(result) != tc.expected {
				t.Fatalf("unexpected safe payload: %s", result)
			}
		})
	}
}
