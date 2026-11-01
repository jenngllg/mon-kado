package validation

import (
	"net/url"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
)

// AdministrativeMemberQuery validates the optional account-name filter.
func AdministrativeMemberQuery(values url.Values) (string, error) {
	entries, exists := values["q"]
	if !exists {
		return "", nil
	}
	if len(entries) != 1 || !Text(entries[0], 100, false) {
		return "", &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The query contains validation errors.", Validation: []domain.ValidationError{{PropertyName: "q", ErrorMessage: "The member filter is invalid."}}}
	}
	return strings.TrimSpace(entries[0]), nil
}
