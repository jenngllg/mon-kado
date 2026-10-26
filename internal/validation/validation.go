// Package validation aggregates input errors before application services execute.
package validation

import (
	"encoding/json"
	"errors"
	"fmt"
	"net/mail"
	"net/url"
	"reflect"
	"regexp"
	"strings"
	"time"
	"unicode"
	"unicode/utf8"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/google/uuid"
)

const mandatory = "The property %s is mandatory."
const wishVariantMaximumLength = 100
const wishVariantInvalid = "The value must contain valid single-line text of at most 100 characters."

var money = regexp.MustCompile(`^(?:[0-9]{1,8})(?:\.[0-9]{1,2})?$`)

// Text checks Unicode scalar length and rejects unwanted controls.
func Text(value string, max int, multiline bool) bool {
	if !utf8.ValidString(value) || utf8.RuneCountInString(strings.TrimSpace(value)) > max {
		return false
	}
	for _, r := range value {
		if unicode.IsControl(r) && !(multiline && (r == '\n' || r == '\r' || r == '\t')) || !multiline && (r == '\u2028' || r == '\u2029') {
			return false
		}
	}
	return true
}

// HTTPURL validates an absolute web URL without credentials.
func HTTPURL(value string) bool {
	value = strings.TrimSpace(value)
	u, err := url.Parse(value)
	return err == nil && (u.Scheme == "http" || u.Scheme == "https") && u.Hostname() != "" && u.User == nil && !strings.ContainsAny(value, " \t\r\n") && utf8.ValidString(value) && utf8.RuneCountInString(value) <= 2048
}

// Validate returns all invalid input paths. Presence is independent of JSON decoding.
func Validate(input any) error {
	return ValidateAt(input, time.Now())
}

// ValidateAt aggregates input errors against the injected current UTC calendar date.
func ValidateAt(input any, now time.Time) error {
	v := reflect.ValueOf(input)
	if v.Kind() == reflect.Pointer {
		v = v.Elem()
	}
	name := v.Type().Name()
	required := map[string][]string{
		"SaveGroupRequest": {"Name"}, "InviteGroupMemberRequest": {"MemberId"},
		"RegisterAccountRequest": {"Email", "Password", "DisplayName"}, "LoginRequest": {"Email", "Password"},
		"JoinSharedWishlistRequest": {"DisplayName"},
		"CreateWishlistRequest":     {"Name", "Occasion"}, "UpdateWishlistRequest": {"Name", "Occasion"},
		"CreateWishRequest": {"Name"}, "UpdateWishRequest": {"Name"},
		"CopyOwnedWishRequest": {"DestinationWishlistId"}, "CopyWishRequest": {"SourceShareLinkId", "SourceWishId"},
		"SetWishlistArchivedRequest": {"IsArchived"}, "SetWishFavoriteRequest": {"IsFavorite"},
		"UpsertGiftReservationRequest": {"Quantity"}, "UpdateMemberProfileRequest": {"DisplayName"},
		"UpdateMemberPasswordRequest": {"CurrentPassword", "NewPassword"}, "UpdateMemberEmailRequest": {"Email", "CurrentPassword"},
		"ConfirmEmailRequest": {"UserId", "Token"}, "ConfirmMemberEmailChangeRequest": {"RequestId", "Token"},
		"RequestEmailConfirmationRequest": {"Email"}, "RequestPasswordResetRequest": {"Email"},
		"ResetPasswordRequest": {"UserId", "Token", "NewPassword"}, "ConfirmMemberAccountDeletionRequest": {"Token"},
		"CreateWishImportPreviewRequest": {"Url"}, "ReportSharedWishlistRequest": {"Reason"},
		"ReorderWishesRequest": {"WishIds"}, "UpdateWishlistModerationRequest": {"IsSuspended"},
		"UpdateWishlistReportReviewRequest": {"Status"}, "RequestAdministrativeDataExportRequest": {"RequestReference"},
		"ExecuteAdministrativeAccountErasureRequest": {"RequestReference", "ConfirmedMemberId"},
		"RevokeMemberSessionsRequest":                {"RequestReference", "ConfirmedMemberId"},
		"TwoFactorCompletionRequest":                 {"Flow"}, "TwoFactorFlowRequest": {"Flow"},
		"TwoFactorSetupConfirmationRequest": {"Flow", "Code"}, "TwoFactorReauthenticationRequest": {"Purpose"},
		"CompleteGoogleSessionRequest": {"Flow"}, "LinkGoogleAccountRequest": {"Flow", "CurrentPassword"},
	}
	errors := []domain.ValidationError{}
	add := func(path, message string) {
		errors = append(errors, domain.ValidationError{PropertyName: path, ErrorMessage: message})
	}
	if profile, ok := v.Interface().(domain.UpdateMemberProfileRequest); ok && profile.HasVisibilityPreference && profile.IsVisibleInMemberSearch == nil {
		add("isVisibleInMemberSearch", fmt.Sprintf(mandatory, "isVisibleInMemberSearch"))
	}
	for i := 0; i < v.NumField(); i++ {
		field := v.Type().Field(i)
		path := field.Tag.Get("json")
		value := v.Field(i)
		absent := value.Kind() == reflect.Pointer && value.IsNil() || value.Kind() == reflect.Slice && value.IsNil()
		if value.Kind() == reflect.Pointer && !absent {
			value = value.Elem()
		}
		if value.Kind() == reflect.String && strings.TrimSpace(value.String()) == "" {
			absent = true
		}
		isRequired := false
		for _, r := range required[name] {
			if r == field.Name {
				isRequired = true
			}
		}
		if absent {
			if isRequired {
				add(path, fmt.Sprintf(mandatory, path))
			}
			continue
		}
		if value.Kind() == reflect.String {
			s := value.String()
			switch field.Name {
			case "Color", "Size":
				if !Text(s, wishVariantMaximumLength, false) {
					add(path, wishVariantInvalid)
				}
			case "Email":
				address, err := mail.ParseAddress(s)
				if err != nil || address.Address != s || len(s) > 254 || !strings.Contains(s, "@") {
					add(path, "The email address is invalid.")
				}
			case "Name":
				if !Text(s, 100, false) {
					add(path, "The name must contain valid text of at most 100 characters.")
				}
			case "DisplayName":
				if !Text(s, 80, false) {
					add(path, "The display name must contain valid text of at most 80 characters.")
				}
			case "Note", "Message":
				if !Text(s, 500, true) {
					add(path, "The text must contain at most 500 characters.")
				}
			case "Details", "ReviewNote":
				if !Text(s, 1000, true) {
					add(path, "The text must contain at most 1000 characters.")
				}
			case "RequestReference":
				if !Text(s, 128, false) {
					add(path, "The reference must contain at most 128 characters and no control characters.")
				}
			case "Url":
				if !HTTPURL(s) {
					add(path, "The URL must be an absolute HTTP or HTTPS URL without credentials.")
				}
			case "EventDate":
				if date, err := time.Parse("2006-01-02", s); err != nil {
					add(path, "The date must use yyyy-MM-dd.")
				} else if date.Format("2006-01-02") < now.UTC().Format("2006-01-02") {
					add(path, "The event date must be today or later.")
				}
			case "Occasion":
				if !oneOf(s, "birthday", "christmas", "wedding", "birth", "other") {
					add(path, "The occasion is invalid.")
				}
			case "Status":
				if !oneOf(s, "pending", "upheld", "dismissed") {
					add(path, "The report status is invalid.")
				}
			case "Reason":
				if name == "ReportSharedWishlistRequest" && !oneOf(s, "spamOrScam", "inappropriateContent", "privacyViolation", "other") {
					add(path, "The report reason is invalid.")
				}
			case "Price":
				if !money.MatchString(s) || s == "0" || s == "0.0" || s == "0.00" {
					add(path, "The price must be positive with at most two fractional digits.")
				}
			case "Flow":
				if len(s) != 43 || !regexp.MustCompile(`^[A-Za-z0-9_-]+$`).MatchString(s) {
					add(path, "The flow must be a canonical Base64URL proof.")
				}
			case "Code":
				if !regexp.MustCompile(`^[0-9]{6}$`).MatchString(s) {
					add(path, "The authenticator code must contain six digits.")
				}
			case "RecoveryCode":
				if !regexp.MustCompile(`(?i)^(?:[0-9a-f]{32}|[0-9a-f]{8}(?:-[0-9a-f]{8}){3})$`).MatchString(s) {
					add(path, "The recovery code is invalid.")
				}
			case "Purpose":
				if !oneOf(s, "replaceAuthenticator", "regenerateRecoveryCodes") {
					add(path, "The management purpose is invalid.")
				}
			case "Password", "NewPassword", "CurrentPassword":
				n := utf8.RuneCountInString(s)
				if n > 128 {
					add(path, "The password must not exceed 128 characters.")
				}
				if (field.Name == "NewPassword" || name == "RegisterAccountRequest") && n < 12 {
					add(path, "The password must contain at least 12 characters.")
				}
			case "MemberId", "UserId", "RequestId", "ConfirmedMemberId", "DestinationWishlistId", "SourceShareLinkId", "SourceWishId":
				if id, err := uuid.Parse(s); err != nil || id == uuid.Nil || len(s) != 36 {
					add(path, "The identifier is invalid.")
				}
			}
		}
		if field.Name == "Quantity" && (value.Int() < 1 || value.Int() > 100) {
			add(path, "The quantity must be between 1 and 100.")
		}
		if field.Name == "WishIds" {
			if value.Len() > 1000 {
				add(path, "The collection must contain at most 1000 items.")
			}
			seen := map[string]bool{}
			for j := 0; j < value.Len(); j++ {
				s := value.Index(j).String()
				id, err := uuid.Parse(s)
				if err != nil || id == uuid.Nil || len(s) != 36 || seen[id.String()] {
					add(fmt.Sprintf("%s[%d]", path, j), "The wish identifier must be valid and unique.")
				}
				seen[id.String()] = true
			}
		}
	}
	if name == "TwoFactorCompletionRequest" || name == "TwoFactorReauthenticationRequest" {
		code, recovery := v.FieldByName("Code"), v.FieldByName("RecoveryCode")
		if !code.IsNil() && !recovery.IsNil() || name == "TwoFactorReauthenticationRequest" && code.IsNil() && recovery.IsNil() {
			add("code", "Supply exactly one verification proof.")
		}
	}
	if name == "UpdateWishlistModerationRequest" {
		input := input.(domain.UpdateWishlistModerationRequest)
		if input.IsSuspended != nil {
			if *input.IsSuspended && (input.Reason == nil || strings.TrimSpace(*input.Reason) == "" || !Text(*input.Reason, 1000, true)) {
				add("reason", "A suspension requires a valid reason of at most 1000 characters.")
			}
			if !*input.IsSuspended && input.Reason != nil {
				add("reason", "The reason must be null when reactivating a wishlist.")
			}
		}
	}
	if name == "ReportSharedWishlistRequest" {
		input := input.(domain.ReportSharedWishlistRequest)
		if input.Reason != nil && *input.Reason == "other" && (input.Details == nil || strings.TrimSpace(*input.Details) == "") {
			add("details", "Details are mandatory for the other reason.")
		}
	}
	if len(errors) == 0 {
		return nil
	}
	return &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The request contains validation errors.", Validation: errors}
}

func oneOf(value string, values ...string) bool {
	for _, v := range values {
		if value == v {
			return true
		}
	}
	return false
}

// Decode rejects multiple documents and preserves decimal precision.
func Decode(decoder *json.Decoder, target any) error {
	decoder.UseNumber()
	if err := decoder.Decode(target); err != nil {
		return err
	}
	value := reflect.ValueOf(target).Elem()
	if value.Kind() != reflect.Struct {
		return nil
	}
	for i := 0; i < value.NumField(); i++ {
		field := value.Field(i)
		if field.Kind() != reflect.Pointer || field.IsNil() || field.Elem().Kind() != reflect.String {
			continue
		}
		var values []string
		switch value.Type().Field(i).Name {
		case "Occasion":
			values = []string{"birthday", "christmas", "wedding", "birth", "other"}
		case "Status":
			values = []string{"pending", "upheld", "dismissed"}
		case "Purpose":
			values = []string{"replaceAuthenticator", "regenerateRecoveryCodes"}
		case "Reason":
			if value.Type().Name() == "ReportSharedWishlistRequest" {
				values = []string{"spamOrScam", "inappropriateContent", "privacyViolation", "other"}
			}
		}
		if len(values) == 0 {
			continue
		}
		matched := false
		for _, canonical := range values {
			if strings.EqualFold(field.Elem().String(), canonical) {
				field.Elem().SetString(canonical)
				matched = true
				break
			}
		}
		if !matched {
			return errors.New("unknown JSON enum value")
		}
	}
	return nil
}
