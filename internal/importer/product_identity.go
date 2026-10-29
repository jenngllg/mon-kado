package importer

import (
	"encoding/json"
	"strings"
)

// A product may publish its GTIN on an Offer instead of the Product. Only infer
// identity from offers when every offer identifies the same trade item, so an
// unselected size or model cannot be silently merged with another variant.
func productGTIN(product map[string]any) (string, bool) {
	identifier, conflict := declaredGTIN(product)
	if conflict {
		return "", true
	}
	offers := offerNodes(product)
	if len(offers) > maximumProductCandidates {
		return "", true
	}
	inferred, unknown := "", false
	for _, offer := range offers {
		candidate, conflict := declaredGTIN(offer)
		if conflict || candidate != "" && (identifier != "" && candidate != identifier || inferred != "" && candidate != inferred) {
			return "", true
		}
		if candidate == "" {
			unknown = true
		} else {
			inferred = candidate
		}
	}
	if identifier != "" {
		return identifier, false
	}
	if unknown {
		return "", false
	}
	return inferred, false
}

func declaredGTIN(entity map[string]any) (string, bool) {
	identifier := ""
	for _, field := range []struct {
		name   string
		digits int
	}{{"gtin", 0}, {"gtin8", 8}, {"gtin12", 12}, {"gtin13", 13}, {"gtin14", 14}} {
		candidate := normalizedGTIN(entity[field.name], field.digits)
		if candidate == "" {
			continue
		}
		if identifier != "" && candidate != identifier {
			return "", true
		}
		identifier = candidate
	}
	return identifier, false
}

func normalizedGTIN(value any, expectedDigits int) string {
	var digits string
	switch value := value.(type) {
	case string:
		digits = strings.TrimSpace(value)
	case json.Number:
		// JSON-LD sometimes uses a number. Keep its exact decimal spelling;
		// floating-point conversion could change identifiers or accept exponents.
		digits = value.String()
	}
	length := len(digits)
	if length != 8 && length != 12 && length != 13 && length != 14 || expectedDigits != 0 && length != expectedDigits {
		return ""
	}
	sum, weight, nonzero := 0, 1, false
	for i := length - 1; i >= 0; i-- {
		if digits[i] < '0' || digits[i] > '9' {
			return ""
		}
		number := int(digits[i] - '0')
		nonzero = nonzero || number != 0
		sum += number * weight
		weight = 4 - weight
	}
	if !nonzero || sum%10 != 0 {
		return ""
	}
	return strings.Repeat("0", 14-length) + digits
}
