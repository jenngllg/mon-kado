package httpapi

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"net/url"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/logging"
)

const activityPayloadLimit = 16 * 1024
const activityRedacted = "[REDACTED]"

// activityPayload accepts decoded request DTOs only. New fields are masked by
// default so adding a credential to a DTO cannot silently expose it in history.
func activityPayload(input any) json.RawMessage {
	encoded, err := json.Marshal(input)
	if err != nil {
		return nil
	}
	var value any
	decoder := json.NewDecoder(bytes.NewReader(encoded))
	decoder.UseNumber()
	if decoder.Decode(&value) != nil {
		return nil
	}
	filtered, err := json.Marshal(filterActivityValue(value, 0))
	if err != nil {
		return nil
	}
	if len(filtered) > activityPayloadLimit {
		return json.RawMessage(`{"_omitted":"payloadTooLarge"}`)
	}
	return filtered
}

func filterActivityValue(value any, depth int) any {
	if depth > 8 {
		return activityRedacted
	}
	switch data := value.(type) {
	case map[string]any:
		for key, field := range data {
			switch key {
			case "name", "note", "message", "details", "reviewNote", "reason", "status", "displayName", "color", "size", "price", "quantity", "occasion", "eventDate", "surpriseMode", "isArchived", "isFavorite", "isSurprise", "acceptsSecondHand", "isVisibleInMemberSearch", "memberId", "confirmedMemberId", "groupId", "wishlistId", "wishId", "exportId", "shareLinkId", "reportId", "invitationId", "subscriptionId", "wishIds", "groups", "isVisible", "requestReference", "page", "sourceWishId", "targetWishlistId", "sourceWishlistId", "position":
				data[key] = filterActivityValue(field, depth+1)
			case "url":
				if field == nil {
					continue
				}
				data[key] = activityRedacted
				if text, ok := field.(string); ok {
					parsed, err := url.Parse(text)
					if err == nil && (parsed.Scheme == "https" || parsed.Scheme == "http") && parsed.Hostname() != "" {
						parsed.User, parsed.RawQuery, parsed.Fragment, parsed.RawFragment = nil, "", "", ""
						parsed.ForceQuery = false
						data[key] = parsed.String()
					}
				}
			default:
				data[key] = activityRedacted
			}
		}
	case []any:
		for i, field := range data {
			data[i] = filterActivityValue(field, depth+1)
		}
	}
	return value
}

func (a *API) recordActionActivity(r *request, pattern, level, trace string, failures ...error) {
	var failure error
	if len(failures) > 0 {
		failure = failures[0]
	}
	if !recordableActionActivity(r, pattern, level, failure) {
		return
	}
	payload := r.activityPayload
	if strings.Contains(pattern, "/suggestions") {
		// Beneficiaries must not discover private group ideas through administrator history.
		payload = json.RawMessage(`{"_omitted":"privateGroupSuggestion"}`)
	}
	if level == "error" && failure != nil {
		payload = activityFailurePayload(payload, failure, r.Header.Get("X-Correlation-ID"))
	}
	if err := a.Service.RecordActivity(r.Context(), r.Principal.MemberID, "action", activityLabel(pattern), level, payload, r.Principal.ActorMemberID); err != nil {
		a.Logger.ErrorContext(r.Context(), "Activity recording failed", "EventId", logging.ActivityRecordingFailed, "CorrelationId", logging.Correlation(r.Header.Get("X-Correlation-ID")), "TraceId", logging.Trace(trace))
	}
}

func recordableActionActivity(r *request, pattern, level string, failure error) bool {
	if r.Principal.MemberID == "" || strings.Contains(pattern, "/auth/") || (strings.Contains(pattern, "/admin/impersonations") || strings.HasSuffix(pattern, "/impersonations")) || strings.HasPrefix(pattern, "POST /api/v1/member-activity/") {
		return false
	}
	if errors.Is(failure, context.Canceled) {
		return false
	}
	if r.Method != "GET" || r.Principal.ActorMemberID != "" {
		return true
	}
	return level == "error" && failure != nil && !optionalActivityAbsence(pattern, failure)
}

func optionalActivityAbsence(pattern string, failure error) bool {
	var business *domain.Error
	if !errors.As(failure, &business) {
		return false
	}
	switch business.Code {
	case domain.GiftReservationNotFound, domain.WishlistParticipantNotFound, domain.WishlistSubscriptionNotFound:
		return true
	case domain.MemberDataExportNotFound:
		return strings.HasSuffix(pattern, "/data-exports/latest")
	case domain.GroupJoinLinkNotFound:
		return strings.HasSuffix(pattern, "/join-link")
	}
	return false
}

func activityFailurePayload(payload json.RawMessage, failure error, correlation string) json.RawMessage {
	status, code := 500, ""
	var business *domain.Error
	if errors.As(failure, &business) {
		status, code = business.Status, business.Code
	} else if isDependencyFailure(failure) {
		status, code = 503, domain.TechnicalDependencyUnavailable
	}
	fields := map[string]any{}
	_ = json.Unmarshal(payload, &fields)
	if fields == nil {
		fields = map[string]any{}
	}
	fields["statusCode"], fields["errorCode"], fields["correlationId"] = status, code, correlation
	encoded, _ := json.Marshal(fields)
	if len(encoded) > activityPayloadLimit {
		encoded, _ = json.Marshal(map[string]any{"_omitted": "payloadTooLarge", "statusCode": status, "errorCode": code, "correlationId": correlation})
	}
	return encoded
}
