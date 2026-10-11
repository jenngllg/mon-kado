// Package domain defines the public business contracts and predictable failures.
package domain

import "fmt"

// ValidationError describes one invalid JSON property, including its full path.
type ValidationError struct {
	PropertyName string `json:"propertyName"`
	ErrorMessage string `json:"errorMessage"`
}

// ErrorResponse is the stable HTTP error envelope. Null properties remain present.
type ErrorResponse struct {
	StatusCode       int               `json:"statusCode"`
	Title            *string           `json:"title"`
	Message          *string           `json:"message"`
	ErrorCode        *string           `json:"errorCode"`
	ValidationErrors []ValidationError `json:"validationErrors"`
}

// Error carries a predictable business failure without sensitive request values.
type Error struct {
	Status     int
	Code       string
	Message    string
	Validation []ValidationError
}

// Error returns the stable functional code and safe public failure description.
func (e *Error) Error() string { return fmt.Sprintf("%s: %s", e.Code, e.Message) }

// Failure constructs a predictable failure for translation at the HTTP boundary.
func Failure(status int, code, message string) *Error {
	return &Error{Status: status, Code: code, Message: message}
}
