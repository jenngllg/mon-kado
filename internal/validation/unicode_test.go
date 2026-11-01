package validation_test

import (
	"testing"

	"github.com/JennGllg/mon-kado/internal/validation"
)

func TestJSONUnicode_WhenEscapesContainSurrogates_RequiresWellFormedPairs(t *testing.T) {
	for _, tc := range []struct {
		json  string
		valid bool
	}{
		{`{"name":"🎁"}`, true}, {`{"name":"\ud83c\udf81"}`, true}, {`{"name":"\u00e9"}`, true}, {`{"name":"\ud800"}`, false}, {`{"name":"\udfff"}`, false}, {`{"name":"\ud800\u0041"}`, false}, {`{"name":"\ud800\uzzzz"}`, false}, {`{"name":"\\ud800"}`, true}, {`{"name":"\"quoted\""}`, true}, {`"\u`, false}, {`"\uzzzz"`, false}, {`"\`, false}, {"\xff", false},
	} {
		t.Run(tc.json, func(t *testing.T) {
			// Arrange
			data := []byte(tc.json)
			// Act
			valid := validation.JSONUnicode(data)
			// Assert
			if valid != tc.valid {
				t.Fatalf("Unicode validity = %v, expected %v", valid, tc.valid)
			}
		})
	}
}
