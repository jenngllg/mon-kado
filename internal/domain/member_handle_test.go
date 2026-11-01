package domain_test

import (
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestSplitMemberHandle_WhenSearchContainsTag_SeparatesOnlyFourASCIIDigits(t *testing.T) {
	for _, scenario := range []struct{ input, name, tag string }{
		{" Jenn#1234 ", "Jenn", "1234"}, {"J#0001", "J", "0001"}, {"Éloïse#0042", "Éloïse", "0042"},
		{"Jenn", "Jenn", ""}, {"Jenn#123", "Jenn#123", ""}, {"Jenn#12345", "Jenn#12345", ""},
		{"Jenn#１２３４", "Jenn#１２３４", ""}, {"Jenn#12x4", "Jenn#12x4", ""}, {"#1234", "#1234", ""},
		{"Jenn#1234#0042", "Jenn#1234", "0042"},
	} {
		t.Run(scenario.input, func(t *testing.T) {
			// Arrange / Act
			name, tag := domain.SplitMemberHandle(scenario.input)
			// Assert
			if name != scenario.name || tag != scenario.tag {
				t.Fatalf("unexpected parsed handle: %q / %q", name, tag)
			}
		})
	}
}
