package validation

import (
	"regexp"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
)

var groupJoinCode = regexp.MustCompile(`^[A-Z2-7]{16}$`)

// GroupJoinCode normalizes a manually entered bearer code without reflecting it in errors.
func GroupJoinCode(value string) (string, error) {
	code := strings.ToUpper(strings.ReplaceAll(strings.TrimSpace(value), "-", ""))
	if len(value) > 32 || !groupJoinCode.MatchString(code) {
		return "", &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The group code is invalid.", Validation: []domain.ValidationError{{PropertyName: "X-MonKado-Share-Token", ErrorMessage: "A valid group code is required."}}}
	}
	return code, nil
}
