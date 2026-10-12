package validation

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/google/uuid"
	"net/url"
	"strings"
	"time"
)

// AuditQuery validates UUIDs, explicit UTC dates and all conjunctive filters together.
func AuditQuery(query url.Values) (application.AuditFilter, error) {
	var result application.AuditFilter
	errors := []domain.ValidationError{}
	add := func(path string) {
		errors = append(errors, domain.ValidationError{PropertyName: path, ErrorMessage: "The audit filter is invalid."})
	}
	for _, key := range []string{"action", "administratorId", "memberId", "wishlistId", "exportId", "requestReference", "from", "to"} {
		values, present := query[key]
		if !present {
			continue
		}
		if len(values) != 1 || strings.TrimSpace(values[0]) == "" {
			add(key)
			continue
		}
		value := strings.TrimSpace(values[0])
		switch key {
		case "action":
			value = canonicalValue(value, "wishlistSuspended", "wishlistSuspensionReasonUpdated", "wishlistReactivated", "memberDataExportRequested", "memberDataExportDownloadStarted", "memberErased", "memberSessionsRevoked")
			if !oneOf(value, "wishlistSuspended", "wishlistSuspensionReasonUpdated", "wishlistReactivated", "memberDataExportRequested", "memberDataExportDownloadStarted", "memberErased", "memberSessionsRevoked") {
				add(key)
			} else {
				result.Action = value
			}
		case "requestReference":
			if !Text(value, 128, false) {
				add(key)
			} else {
				result.Reference = &value
			}
		case "from", "to":
			date, err := time.Parse(time.RFC3339Nano, value)
			_, offset := date.Zone()
			if err != nil || offset != 0 {
				add(key)
			} else if key == "from" {
				result.From = &date
			} else {
				result.To = &date
			}
		default:
			id, err := uuid.Parse(value)
			if err != nil || id == uuid.Nil {
				add(key)
				continue
			}
			value = id.String()
			switch key {
			case "administratorId":
				result.Administrator = &value
			case "memberId":
				result.Member = &value
			case "wishlistId":
				result.Wishlist = &value
			case "exportId":
				result.Export = &value
			}
		}
	}
	if result.From != nil && result.To != nil && !result.From.Before(*result.To) {
		add("to")
	}
	if len(errors) > 0 {
		return result, &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The query contains validation errors.", Validation: errors}
	}
	return result, nil
}
