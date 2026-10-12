package validation_test

import (
	"errors"
	"net/url"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func TestReportFilters_WhenQueryValuesVary_PreservesTypedFilterContract(t *testing.T) {
	for _, test := range []struct {
		query, reason, status string
		suspended             *bool
		invalid               bool
	}{
		{query: "", status: "pending"},
		{query: "reason=SPAMORSCAM&status=ALL&isSuspended=TRUE", reason: "spamOrScam", suspended: boolPointer(true)},
		{query: "reason=PrivacyViolation&status=UPHELD&isSuspended=False", reason: "privacyViolation", status: "upheld", suspended: boolPointer(false)},
		{query: "reason=inappropriateContent&status=dismissed", reason: "inappropriateContent", status: "dismissed"},
		{query: "reason=other", reason: "other", status: "pending"},
		{query: "reason=&status=&isSuspended=1", invalid: true},
		{query: "reason=unknown&status=unknown&isSuspended=unknown", invalid: true},
		{query: "reason=other&reason=other&status=all&status=all&isSuspended=true&isSuspended=true", invalid: true},
	} {
		t.Run(test.query, func(t *testing.T) {
			// Arrange
			query, err := url.ParseQuery(test.query)
			if err != nil {
				t.Fatal(err)
			}
			// Act
			reason, status, suspended, err := validation.ReportFilters(query)
			// Assert
			if test.invalid {
				var failure *domain.Error
				if !errors.As(err, &failure) || failure.Status != 400 || len(failure.Validation) != 3 {
					t.Fatalf("expected all three filter errors, got %v", err)
				}
				return
			}
			if err != nil || reason != test.reason || status != test.status || (suspended == nil) != (test.suspended == nil) {
				t.Fatalf("unexpected filters: %q %q %v %v", reason, status, suspended, err)
			}
			if suspended != nil && *suspended != *test.suspended {
				t.Fatal("incorrect boolean filter")
			}
		})
	}
}

func boolPointer(value bool) *bool { return &value }

func TestQueryBoolean_WhenValuesVary_AcceptsOnlyOneTextualBoolean(t *testing.T) {
	for _, test := range []struct {
		query             string
		expected, invalid bool
	}{
		{}, {query: "availableOnly=TRUE", expected: true}, {query: "availableOnly=false"}, {query: "availableOnly=1", invalid: true}, {query: "availableOnly=", invalid: true}, {query: "availableOnly=true&availableOnly=true", invalid: true},
	} {
		t.Run(test.query, func(t *testing.T) {
			// Arrange
			query, err := url.ParseQuery(test.query)
			if err != nil {
				t.Fatal(err)
			}
			// Act
			result, err := validation.QueryBoolean(query, "availableOnly")
			// Assert
			if result != test.expected || (err != nil) != test.invalid {
				t.Fatalf("unexpected boolean: %v %v", result, err)
			}
		})
	}
}

func TestGoogleQuery_WhenBooleanVaries_RejectsDuplicatesAndNonBooleanForms(t *testing.T) {
	for _, test := range []struct {
		query               string
		persistent, invalid bool
	}{
		{query: ""},
		{query: "returnPath=/login/google-return&rememberMe=TRUE", persistent: true},
		{query: "rememberMe=FALSE"},
		{query: "rememberMe=1", invalid: true},
		{query: "rememberMe=", invalid: true},
		{query: "rememberMe=true&rememberMe=false", invalid: true},
		{query: "returnPath=https://attacker.example/", invalid: true},
		{query: "returnPath=/login/google-return&returnPath=/login/google-return", invalid: true},
	} {
		t.Run(test.query, func(t *testing.T) {
			// Arrange
			query, err := url.ParseQuery(test.query)
			if err != nil {
				t.Fatal(err)
			}
			// Act
			persistent, err := validation.GoogleQuery(query)
			// Assert
			if (err != nil) != test.invalid || persistent != test.persistent {
				t.Fatalf("unexpected Google parameter result: %v %v", persistent, err)
			}
		})
	}
}

func TestReservationStatus_WhenQueryVaries_ReturnsCanonicalStatus(t *testing.T) {
	for _, test := range []struct {
		query, expected string
		invalid         bool
	}{
		{}, {query: "status=ACTIVE", expected: "active"}, {query: "status=Cancelled", expected: "cancelled"}, {query: "status=unavailable", expected: "unavailable"},
		{query: "status=", invalid: true}, {query: "status=unknown", invalid: true}, {query: "status=active&status=cancelled", invalid: true},
	} {
		t.Run(test.query, func(t *testing.T) {
			// Arrange
			query, err := url.ParseQuery(test.query)
			if err != nil {
				t.Fatal(err)
			}
			// Act
			value, err := validation.ReservationStatus(query)
			// Assert
			if (err != nil) != test.invalid || value != test.expected {
				t.Fatalf("unexpected status %q %v", value, err)
			}
		})
	}
}

func TestAuditQuery_WhenFiltersAreInvalid_AggregatesErrors(t *testing.T) {
	// Arrange
	query, err := url.ParseQuery("action=unknown&administratorId=invalid&memberId=00000000-0000-0000-0000-000000000000&wishlistId=invalid&exportId=invalid&requestReference=&from=2026-10-11T10:00:00%2B02:00&to=invalid")
	if err != nil {
		t.Fatal(err)
	}
	// Act
	_, err = validation.AuditQuery(query)
	// Assert
	var failure *domain.Error
	if !errors.As(err, &failure) || len(failure.Validation) != 8 {
		t.Fatalf("expected eight filter errors, got %v", err)
	}
}

func TestAuditQuery_WhenDatesAndIdentifiersAreValid_PreservesUTCAndExactReference(t *testing.T) {
	// Arrange
	id := "019a099b-9444-7000-8000-000000000001"
	query, err := url.ParseQuery("action=MEMBERSESSIONSREVOKED&administratorId=" + id + "&memberId=" + id + "&wishlistId=" + id + "&exportId=" + id + "&requestReference=SUPPORT-42&from=2026-10-11T10:00:00Z&to=2026-10-11T10:00:00.0000001Z")
	if err != nil {
		t.Fatal(err)
	}
	// Act
	filter, err := validation.AuditQuery(query)
	// Assert
	if err != nil || filter.Action != "memberSessionsRevoked" || filter.Reference == nil || *filter.Reference != "SUPPORT-42" || filter.From == nil || filter.To == nil || filter.To.Sub(*filter.From) != 100*time.Nanosecond {
		t.Fatalf("unexpected audit filters: %#v %v", filter, err)
	}
	for _, actual := range []*string{filter.Administrator, filter.Member, filter.Wishlist, filter.Export} {
		if actual == nil || *actual != id {
			t.Fatal("identifier filter missing")
		}
	}
	for _, values := range []url.Values{{"to": {"2026-10-11T10:00:00Z"}, "from": {"2026-10-11T10:00:00Z"}}, {"action": {"memberErased", "memberErased"}}, {"from": {""}}} {
		if _, err := validation.AuditQuery(values); err == nil {
			t.Fatal("invalid boundary or duplicate accepted")
		}
	}
}
