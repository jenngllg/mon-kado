package logging_test

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/logging"
	"github.com/JennGllg/mon-kado/internal/security"
)

func decodeLog(t *testing.T, output *bytes.Buffer) map[string]any {
	t.Helper()
	var record map[string]any
	if err := json.Unmarshal(output.Bytes(), &record); err != nil {
		t.Fatal(err)
	}
	return record
}

func TestJSONLogger_WhenPrivateValuesReachBoundary_RecordsOnlyCompiledTechnicalFields(t *testing.T) {
	// Arrange
	var output bytes.Buffer
	operation := "GET /api/v1/wishlists/{wishlistId}"
	id := security.NewID()
	logger := logging.NewJSONLogger(&output, "api", strings.Repeat("a", 40), operation)
	// Act
	logger.With("CorrelationId", id).InfoContext(t.Context(), "private-email@example.test", "EventId", logging.BusinessOperationSucceeded,
		"Operation", operation, "UserId", id, "StatusCode", 200, "Method", "GET", "TraceId", strings.ReplaceAll(id, "-", ""),
		"email", "private-email@example.test", "error", errors.New("provider-secret"), "Password", "secret-password", "Authorization", "secret-token",
		"Body", map[string]string{"wish": "private-wish-content"}, "Provider", slog.GroupValue(slog.String("UserId", id)))
	// Assert
	record := decodeLog(t, &output)
	properties := record["properties"].(map[string]any)
	if len(record) != 10 || len(properties) != 4 || record["level"] != "Information" || record["eventName"] != "BusinessOperationSucceeded" || record["correlationId"] != id || record["traceId"] != strings.ReplaceAll(id, "-", "") || properties["Operation"] != operation || properties["UserId"] != id {
		t.Fatal("safe event contract differs")
	}
	for _, secret := range []string{"private-email", "provider-secret", "secret-password", "secret-token", "private-wish"} {
		if strings.Contains(output.String(), secret) {
			t.Fatal("private log value escaped allowlist")
		}
	}
}

func TestJSONLogger_WhenPropertiesHaveUntrustedTypesOrValues_RejectsThem(t *testing.T) {
	for _, test := range []struct {
		key   string
		value any
	}{
		{"UserId", "private-email@example.test"}, {"UserId", security.NewID() + "-secret"}, {"UserId", errors.New("secret")},
		{"StatusCode", -1}, {"StatusCode", "200"}, {"StatusCode", 200.5}, {"ErrorCode", "ACCOUNT_UNTRUSTED_SECRET"},
		{"Operation", "GET /api/v1/wishlists/" + security.NewID()}, {"Operation", "GET /api/v1/wishlists?secret=value"},
		{"Method", "SECRET"}, {"Job", "private-title"}, {"Category", "provider-response"}, {"FailureCategory", "private-recipient"},
		{"Abandoned", "true"}, {"IsReferenced", 1}, {"Arbitrary", true},
	} {
		t.Run(test.key+"/"+slog.AnyValue(test.value).Kind().String(), func(t *testing.T) {
			// Arrange
			var output bytes.Buffer
			logger := logging.NewJSONLogger(&output, "api", "local")
			// Act
			logger.Error("untrusted message", "EventId", logging.BusinessOperationFailed, test.key, test.value)
			// Assert
			if len(decodeLog(t, &output)["properties"].(map[string]any)) != 0 {
				t.Fatal("untrusted property accepted")
			}
		})
	}
}

func TestJSONLogger_WhenTechnicalClassificationsAreKnown_PreservesThem(t *testing.T) {
	for _, test := range []struct {
		key   string
		value any
	}{
		{"ErrorCode", domain.RequestValidationError}, {"Count", int64(0)}, {"AttemptCount", 3}, {"Abandoned", true}, {"IsReferenced", false},
		{"Method", "OPTIONS"}, {"Job", "erasureExpiry"}, {"Category", "network"}, {"FailureCategory", "authorization"},
	} {
		t.Run(test.key, func(t *testing.T) {
			// Arrange
			var output bytes.Buffer
			logger := logging.NewJSONLogger(&output, "worker", "local")
			// Act
			logger.Warn("ignored", "EventId", logging.WorkerPassCompleted, test.key, test.value)
			// Assert
			record := decodeLog(t, &output)
			if record["level"] != "Warning" || len(record["properties"].(map[string]any)) != 1 {
				t.Fatal("technical classification missing")
			}
		})
	}
}

func TestJSONLogger_WhenExceptionHasSensitiveFrames_OmitsPathsArgumentsAndMessage(t *testing.T) {
	// Arrange
	var output bytes.Buffer
	logger := logging.NewJSONLogger(&output, "api", "local")
	stack := "goroutine 1 [running]:\nruntime/debug.Stack()\n\tC:/private/workspace/secret.go:12 +0x23\ngithub.com/JennGllg/mon-kado/internal/httpapi.(*API).ServeHTTP(0x123, secret-password)\nprivate secret-frame()\n"
	// Act
	logger.Error("secret-message", "EventId", logging.UnhandledRequestPanic, "ExceptionType", "*pgconn.PgError", "StackTrace", stack, "error", errors.New("secret-provider-response"))
	// Assert
	record := decodeLog(t, &output)
	if record["exceptionType"] != "*pgconn.PgError" || record["stackTrace"] != "runtime/debug.Stack()\ngithub.com/JennGllg/mon-kado/internal/httpapi.(*API).ServeHTTP()" {
		t.Fatal("technical exception chain differs")
	}
	if strings.Contains(output.String(), "secret") || strings.Contains(output.String(), "0x123") {
		t.Fatal("exception content leaked")
	}
	output.Reset()
	logger.Error("ignored", "EventId", logging.BusinessOperationFailed, "ExceptionType", "PrivateError", "StackTrace", stack)
	if _, found := decodeLog(t, &output)["stackTrace"]; found {
		t.Fatal("business error acquired stack trace")
	}
}

func TestJSONLogger_WhenEventOrScopeIsUnknown_UsesSafeFallbackWithoutInvokingArbitraryValues(t *testing.T) {
	// Arrange
	var output bytes.Buffer
	logger := logging.NewJSONLogger(&output, "private-service", strings.Repeat("Z", 40))
	// Act
	logger.With("UserId", security.NewID(), "CorrelationId", "invalid", "TraceId", strings.Repeat("0", 32)).Info("secret", "EventId", 999999, "Method", "GET")
	// Assert
	record := decodeLog(t, &output)
	if record["eventId"] != float64(0) || record["eventName"] != "ExternalEvent" || record["service"] != "external" || record["version"] != "local" || len(record["properties"].(map[string]any)) != 0 || record["correlationId"] == "invalid" || record["traceId"] == strings.Repeat("0", 32) {
		t.Fatal("untrusted scope was rendered")
	}
	output.Reset()
	logger.With("EventId", logging.BusinessOperationSucceeded).WithGroup("private").With("UserId", security.NewID()).WithGroup("").Info("private-message", "StatusCode", 200)
	if len(decodeLog(t, &output)["properties"].(map[string]any)) != 0 {
		t.Fatal("untrusted group flattened into allowlist")
	}
	output.Reset()
	logger.Debug("ignored", "EventId", logging.HTTPRequestCompleted)
	if output.Len() != 0 {
		t.Fatal("debug event unexpectedly rendered")
	}
}

func TestJSONLogger_WhenRecordsAreConcurrent_ProducesCompleteIndependentJSONLines(t *testing.T) {
	// Arrange
	var output bytes.Buffer
	logger := logging.NewJSONLogger(&output, "worker", "local")
	var group sync.WaitGroup
	// Act
	for index := 0; index < 20; index++ {
		group.Go(func() { logger.With("Count", 1).Info("ignored", "EventId", logging.WorkerPassCompleted) })
	}
	group.Wait()
	// Assert
	decoder := json.NewDecoder(&output)
	count := 0
	for {
		var record map[string]any
		if err := decoder.Decode(&record); err == io.EOF {
			break
		} else if err != nil {
			t.Fatal(err)
		}
		count++
		if _, err := time.Parse(time.RFC3339Nano, record["timestamp"].(string)); err != nil {
			t.Fatal(err)
		}
	}
	if count != 20 {
		t.Fatal("concurrent records were lost")
	}
}
