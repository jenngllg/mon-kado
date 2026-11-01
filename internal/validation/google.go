package validation

import (
	"github.com/JennGllg/mon-kado/internal/domain"
	"net/url"
)

// GoogleQuery accepts the single exact frontend return path and an optional boolean.
func GoogleQuery(query url.Values) (bool, error) {
	errors := []domain.ValidationError{}
	add := func(path string) {
		errors = append(errors, domain.ValidationError{PropertyName: path, ErrorMessage: "The Google sign-in parameter is invalid."})
	}
	if values, exists := query["returnPath"]; exists && (len(values) != 1 || values[0] != "/login/google-return") {
		add("returnPath")
	}
	persistent := false
	if values, exists := query["rememberMe"]; exists {
		value := canonicalValue(query.Get("rememberMe"), "true", "false")
		if len(values) != 1 || !oneOf(value, "true", "false") {
			add("rememberMe")
		} else {
			persistent = value == "true"
		}
	}
	if len(errors) > 0 {
		return false, &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The query contains validation errors.", Validation: errors}
	}
	return persistent, nil
}
