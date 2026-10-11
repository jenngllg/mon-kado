package validation

import (
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/google/uuid"
	"net/url"
	"strings"
)

// ReportFilters validates optional filters and applies the pending default only when absent.
func ReportFilters(query url.Values) (string, string, *bool, error) {
	reason, status := canonicalValue(query.Get("reason"), "spamOrScam", "inappropriateContent", "privacyViolation", "other"), "pending"
	var suspended *bool
	errors := []domain.ValidationError{}
	add := func(path string) {
		errors = append(errors, domain.ValidationError{PropertyName: path, ErrorMessage: "The filter value is invalid."})
	}
	if reason != "" && !oneOf(reason, "spamOrScam", "inappropriateContent", "privacyViolation", "other") {
		add("reason")
	}
	if values, exists := query["reason"]; exists && (len(values) != 1 || reason == "") {
		add("reason")
	}
	if values, exists := query["status"]; exists {
		status = canonicalValue(query.Get("status"), "pending", "upheld", "dismissed", "all")
		if len(values) != 1 || !oneOf(status, "pending", "upheld", "dismissed", "all") {
			add("status")
		}
	}
	if values, exists := query["isSuspended"]; exists {
		text := canonicalValue(query.Get("isSuspended"), "true", "false")
		if len(values) != 1 || !oneOf(text, "true", "false") {
			add("isSuspended")
		} else {
			value := text == "true"
			suspended = &value
		}
	}
	if len(errors) > 0 {
		return "", "", nil, &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The query contains validation errors.", Validation: errors}
	}
	if status == "all" {
		status = ""
	}
	return reason, status, suspended, nil
}

func canonicalValue(value string, allowed ...string) string {
	for _, candidate := range allowed {
		if strings.EqualFold(value, candidate) {
			return candidate
		}
	}
	return value
}

// QueryBoolean accepts a single textual boolean and defaults to false only when absent.
func QueryBoolean(query url.Values, property string) (bool, error) {
	values, present := query[property]
	if !present {
		return false, nil
	}
	value := canonicalValue(query.Get(property), "true", "false")
	if len(values) != 1 || !oneOf(value, "true", "false") {
		return false, &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The query contains validation errors.", Validation: []domain.ValidationError{{PropertyName: property, ErrorMessage: "The query parameter must be a boolean."}}}
	}
	return value == "true", nil
}

// ReservationStatus validates the optional reservation history filter.
func ReservationStatus(query url.Values) (string, error) {
	values, present := query["status"]
	if !present {
		return "", nil
	}
	value := canonicalValue(query.Get("status"), "active", "cancelled", "unavailable")
	if len(values) != 1 || !oneOf(value, "active", "cancelled", "unavailable") {
		return "", &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The query contains validation errors.", Validation: []domain.ValidationError{{PropertyName: "status", ErrorMessage: "The reservation status is invalid."}}}
	}
	return value, nil
}

// ConfirmedMember validates an explicit destructive target confirmation in the common pipeline.
func ConfirmedMember(value *string, expected string) error {
	actual, err := uuid.Parse(textValue(value))
	target, targetErr := uuid.Parse(expected)
	if err != nil || targetErr != nil || actual != target {
		return &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The member confirmation does not match the target.", Validation: []domain.ValidationError{{PropertyName: "confirmedMemberId", ErrorMessage: "The confirmed member must match the route target."}}}
	}
	return nil
}
func textValue(value *string) string {
	if value == nil {
		return ""
	}
	return *value
}
