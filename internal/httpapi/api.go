// Package httpapi serves the versioned API and centralizes security and error translation.
package httpapi

import (
	"context"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/JennGllg/mon-kado/internal/importer"
	"github.com/JennGllg/mon-kado/internal/logging"
	"io"
	"log/slog"
	"mime"
	"net"
	"net/http"
	"runtime/debug"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/JennGllg/mon-kado/internal/admission"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/JennGllg/mon-kado/internal/telemetry"
	"github.com/JennGllg/mon-kado/internal/validation"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgconn"
)

// API is the HTTP composition root with injected business services and structured logging.
type API struct {
	Service        *application.Service
	Logger         *slog.Logger
	mux            *http.ServeMux
	limiter        rateLimiter
	Google         application.GoogleClient
	Importer       application.WishImportClient
	ImageAdmission *admission.Limiter
	Telemetry      *telemetry.Collector
	operations     map[string]options
}

type request struct {
	*http.Request
	Principal application.Principal
	Now       time.Time
}
type response struct {
	Status        int
	Body          any
	Version       *uint32
	Location      string
	Session       *application.SessionResult
	GuestToken    string
	GuestExpires  time.Time
	Binary        []byte
	MediaType     string
	Stream        io.ReadCloser
	ContentLength int64
	DownloadName  string
	Cookies       []*http.Cookie
}
type endpoint func(*request) (response, error)
type options struct {
	RatePolicy     string
	RateWindow     time.Duration
	RateByShare    bool
	ImageAdmission bool
	Auth           bool
	Admin          bool
	CSRF           bool
	Rate           int
	RateByMember   bool
}

// New registers all endpoints before accepting requests.
func New(service *application.Service, logger *slog.Logger) *API {
	a := &API{Service: service, Logger: logger, mux: http.NewServeMux(), limiter: rateLimiter{windows: map[string]rateWindow{}}, operations: map[string]options{}}
	a.ImageAdmission = admission.New(nil)
	a.Telemetry = telemetry.New("api", func() time.Time { return service.Now() }, time.Now)
	imports := service.Config.Imports()
	client := importer.New()
	client.Timeout, client.MaximumRedirects, client.MaximumHTMLBytes = imports.Timeout, imports.MaximumRedirects, imports.MaximumHTMLBytes
	client.Catalog = &importer.ProductCatalog{Client: client, Enabled: imports.CatalogEnabled, IndexKey: imports.CatalogIndexKey}
	a.Importer = client
	a.routes()
	a.registerOpenAPI()
	a.validateSpecification()
	return a
}

func (a *API) route(pattern string, opt options, handler endpoint) {
	a.operations[pattern] = opt
	a.mux.HandleFunc(pattern, func(w http.ResponseWriter, r *http.Request) {
		var p application.Principal
		if authorization := r.Header.Get("Authorization"); authorization != "" {
			if !strings.HasPrefix(authorization, "Bearer ") {
				a.writeError(w, r, domain.Failure(401, domain.SecurityUnauthorized, "Bearer authentication is required."))
				return
			}
			var err error
			p, err = a.Service.Authenticate(r.Context(), strings.TrimPrefix(authorization, "Bearer "))
			if err != nil {
				a.writeError(w, r, err)
				return
			}
		}
		if opt.Auth && p.MemberID == "" {
			a.writeError(w, r, domain.Failure(401, domain.SecurityUnauthorized, "Authentication is required."))
			return
		}
		if opt.ImageAdmission {
			release, accepted, err := a.ImageAdmission.Acquire(r.Context())
			if err != nil {
				a.writeError(w, r, err)
				return
			}
			if !accepted {
				w.Header().Set("Retry-After", "60")
				a.writeError(w, r, domain.Failure(429, domain.RequestRateLimitExceeded, "The request rate limit has been exceeded."))
				return
			}
			defer release()
		}
		if opt.Admin {
			if err := application.AuthorizeAdministrator(p); err != nil {
				a.writeError(w, r, err)
				return
			}
		}
		for _, key := range []string{"wishlistId", "wishId", "shareLinkId", "memberId", "reportId", "exportId", "id"} {
			if value := r.PathValue(key); value != "" {
				id, err := uuid.Parse(value)
				if err != nil || id == uuid.Nil {
					a.writeError(w, r, domain.Failure(404, domain.RequestNotFound, "The requested resource was not found."))
					return
				}
				r.SetPathValue(key, id.String())
			}
		}
		if opt.Rate > 0 {
			ip := a.clientAddress(r)
			policy := pattern
			if opt.RatePolicy != "" {
				policy = opt.RatePolicy
			}
			if opt.RateByMember {
				ip = p.MemberID
			}
			if opt.RateByShare {
				ip += "|" + r.PathValue("shareLinkId")
			}
			window := opt.RateWindow
			if window == 0 {
				window = time.Minute
			}
			if !a.limiter.allowWindow(policy+"|"+ip, opt.Rate, window, a.Service.Now()) {
				w.Header().Set("Retry-After", strconv.FormatInt(int64(window/time.Second), 10))
				a.writeError(w, r, domain.Failure(429, domain.RequestRateLimitExceeded, "The request rate limit has been exceeded."))
				return
			}
		}
		if opt.CSRF && !a.validCSRF(r, p.MemberID) {
			a.writeError(w, r, domain.Failure(400, domain.RequestBadRequest, "The CSRF token is invalid."))
			return
		}
		result, err := handler(&request{Request: r, Principal: p, Now: a.Service.Now()})
		if err != nil {
			a.writeError(w, r, err)
			return
		}
		if result.Session != nil {
			a.sessionCookie(w, r, result.Session)
		}
		if result.GuestToken != "" {
			a.setCookie(w, r, "Guest", result.GuestToken, result.GuestExpires, 0)
		}
		if result.Version != nil {
			w.Header().Set("ETag", application.EntityTag(*result.Version))
		}
		if result.Location != "" {
			w.Header().Set("Location", result.Location)
		}
		for _, cookie := range result.Cookies {
			// These response cookies carry OAuth correlation/external proofs and always require HTTPS.
			cookie.Secure = true
			http.SetCookie(w, cookie)
		}
		if result.Status == 0 {
			result.Status = http.StatusOK
		}
		if result.Stream != nil {
			defer result.Stream.Close()
			w.Header().Set("Content-Type", result.MediaType)
			w.Header().Set("Content-Disposition", mime.FormatMediaType("attachment", map[string]string{"filename": result.DownloadName}))
			w.Header().Set("Content-Length", strconv.FormatInt(result.ContentLength, 10))
			w.WriteHeader(result.Status)
			if _, err := io.Copy(w, result.Stream); err != nil {
				a.Logger.ErrorContext(r.Context(), "Archive download interrupted", "EventId", logging.ArchiveDownloadInterrupted, "CorrelationId", logging.Correlation(w.Header().Get("X-Correlation-ID")), "TraceId", logging.Trace(w.Header().Get("X-Trace-ID")))
			}
		} else if result.Binary != nil {
			w.Header().Set("Content-Type", result.MediaType)
			if result.ContentLength > 0 {
				w.Header().Set("Content-Length", strconv.FormatInt(result.ContentLength, 10))
			}
			w.WriteHeader(result.Status)
			w.Write(result.Binary)
		} else {
			a.absoluteImageURLs(r, result.Body)
			writeJSON(w, result.Status, result.Body)
		}
		a.Logger.InfoContext(r.Context(), "Business operation succeeded", "EventId", logging.BusinessOperationSucceeded, "Operation", pattern, "StatusCode", result.Status, "UserId", p.MemberID, "CorrelationId", logging.Correlation(w.Header().Get("X-Correlation-ID")), "TraceId", logging.Trace(w.Header().Get("X-Trace-ID")))
	})
}

// ServeHTTP applies origin and host allowlists, tracing and centralized exception recovery.
func (a *API) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	started := time.Now()
	recorded := &statusWriter{ResponseWriter: w, Status: 200}
	w = recorded
	correlation := r.Header.Get("X-Correlation-ID")
	if id, err := uuid.Parse(correlation); err != nil || id == uuid.Nil {
		correlation = security.NewID()
	}
	logCorrelation := logging.Correlation(correlation)
	trace := strings.ReplaceAll(security.NewID(), "-", "")
	if candidate := incomingTrace(r.Header.Get("traceparent")); candidate != "" {
		trace = candidate
	}
	w.Header().Set("X-Correlation-ID", correlation)
	w.Header().Set("X-Trace-ID", trace)
	r.Header.Set("X-Correlation-ID", correlation)
	r.Header.Set("X-Trace-ID", trace)
	defer func() {
		a.Telemetry.RecordHTTP(recorded.Status, time.Since(started), r.Context().Err() != nil)
		a.Logger.InfoContext(r.Context(), "HTTP request completed", "EventId", logging.HTTPRequestCompleted, "Method", logging.HTTPMethod(r.Method), "Operation", r.Pattern, "StatusCode", recorded.Status, "CorrelationId", logCorrelation, "TraceId", trace)
	}()
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("X-Content-Type-Options", "nosniff")
	w.Header().Set("Referrer-Policy", "no-referrer")
	w.Header().Set("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'")
	defer func() {
		if recovered := recover(); recovered != nil {
			a.Logger.ErrorContext(r.Context(), "Unhandled request panic", "EventId", logging.UnhandledRequestPanic, "StackTrace", string(debug.Stack()), "CorrelationId", logCorrelation, "TraceId", trace)
			writeJSON(w, 500, domain.ErrorResponse{StatusCode: 500, Title: ptr("Internal Server Error"), Message: ptr("An unexpected error occurred.")})
		}
	}()
	host := r.Host
	if name, _, err := net.SplitHostPort(host); err == nil {
		host = name
	}
	allowedHost := false
	for _, value := range a.Service.Config.AllowedHosts {
		if strings.EqualFold(host, value) {
			allowedHost = true
		}
	}
	if !allowedHost {
		a.writeError(w, r, domain.Failure(400, domain.RequestBadRequest, "The request host is invalid."))
		return
	}
	origin := r.Header.Get("Origin")
	googleCallback := a.Service.Config.GoogleEnabled && r.Method == http.MethodPost && r.URL.Path == "/api/v1/auth/google/callback" && origin == "https://accounts.google.com"
	if origin != "" && !googleCallback {
		allowed := false
		for _, value := range a.Service.Config.AllowedOrigins {
			if value == origin {
				allowed = true
			}
		}
		if !allowed {
			a.writeError(w, r, domain.Failure(403, domain.SecurityForbidden, "The request origin is not allowed."))
			return
		}
		w.Header().Set("Access-Control-Allow-Origin", origin)
		w.Header().Set("Access-Control-Allow-Credentials", "true")
		w.Header().Add("Vary", "Origin")
		w.Header().Set("Access-Control-Expose-Headers", "ETag, Location, X-Correlation-ID")
	}
	if r.Method == http.MethodOptions {
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS")
		w.Header().Set("Access-Control-Allow-Headers", "Authorization, Content-Type, X-CSRF-TOKEN, X-Correlation-ID, X-MonKado-Share-Token, If-Match")
		w.WriteHeader(http.StatusNoContent)
		return
	}
	if r.URL.Path == "/api/v1" || strings.HasPrefix(r.URL.Path, "/api/v1/") || r.URL.Path == "/security/csrf-token" || strings.HasPrefix(r.URL.Path, "/security/csrf-token/") {
		quota := a.Service.Config.Perimeter()
		if !a.limiter.allowWindow("perimeter|"+a.clientAddress(r), quota.PermitLimit, quota.Window, a.Service.Now()) {
			w.Header().Set("Retry-After", strconv.FormatInt(int64(quota.Window/time.Second), 10))
			a.writeError(w, r, domain.Failure(429, domain.RequestRateLimitExceeded, "The request rate limit has been exceeded."))
			return
		}
	}
	if strings.HasPrefix(r.URL.Path, "/api/v1/shared-wishlists/") && !a.limiter.allow("shared|"+a.clientAddress(r), 60, a.Service.Now()) {
		w.Header().Set("Retry-After", "60")
		a.writeError(w, r, domain.Failure(429, domain.RequestRateLimitExceeded, "The request rate limit has been exceeded."))
		return
	}
	_, pattern := a.mux.Handler(r)
	if pattern == "" {
		a.writeError(w, r, domain.Failure(404, domain.RequestNotFound, "The requested resource was not found."))
		return
	}
	a.mux.ServeHTTP(w, r)
}

func (a *API) writeError(w http.ResponseWriter, r *http.Request, err error) {
	var business *domain.Error
	if !errors.As(err, &business) {
		if errors.Is(err, context.Canceled) {
			return
		}
		var connection *pgconn.ConnectError
		var postgres *pgconn.PgError
		var network net.Error
		if errors.As(err, &connection) || errors.As(err, &network) || errors.As(err, &postgres) && (strings.HasPrefix(postgres.Code, "08") || strings.HasPrefix(postgres.Code, "53") || strings.HasPrefix(postgres.Code, "57P")) {
			business = domain.Failure(503, domain.TechnicalDependencyUnavailable, "A required dependency is unavailable.")
		} else {
			a.Logger.ErrorContext(r.Context(), "Unhandled request failure", "EventId", logging.UnhandledRequestFailure, "ExceptionType", fmt.Sprintf("%T", err), "StackTrace", string(debug.Stack()), "CorrelationId", logging.Correlation(w.Header().Get("X-Correlation-ID")), "TraceId", logging.Trace(w.Header().Get("X-Trace-ID")))
			writeJSON(w, 500, domain.ErrorResponse{StatusCode: 500, Title: ptr("Internal Server Error"), Message: ptr("An unexpected error occurred.")})
			return
		}
		a.Logger.ErrorContext(r.Context(), "Unexpected dependency failure", "EventId", logging.UnexpectedDependencyFailure, "ExceptionType", fmt.Sprintf("%T", err), "StackTrace", string(debug.Stack()), "CorrelationId", logging.Correlation(w.Header().Get("X-Correlation-ID")), "TraceId", logging.Trace(w.Header().Get("X-Trace-ID")))
	} else {
		a.Logger.ErrorContext(r.Context(), "Business operation failed", "EventId", logging.BusinessOperationFailed, "StatusCode", business.Status, "ErrorCode", business.Code, "Operation", r.Pattern, "CorrelationId", logging.Correlation(w.Header().Get("X-Correlation-ID")), "TraceId", logging.Trace(w.Header().Get("X-Trace-ID")))
	}
	writeJSON(w, business.Status, domain.ErrorResponse{StatusCode: business.Status, Title: ptr(http.StatusText(business.Status)), Message: ptr(business.Message), ErrorCode: ptr(business.Code), ValidationErrors: business.Validation})
}

type statusWriter struct {
	http.ResponseWriter
	Status  int
	written bool
}

func (w *statusWriter) WriteHeader(status int) {
	if !w.written {
		w.Status = status
		w.written = true
		w.ResponseWriter.WriteHeader(status)
	}
}
func (w *statusWriter) Write(data []byte) (int, error) {
	if !w.written {
		w.WriteHeader(200)
	}
	return w.ResponseWriter.Write(data)
}
func (w *statusWriter) Unwrap() http.ResponseWriter { return w.ResponseWriter }

func writeJSON(w http.ResponseWriter, status int, body any) {
	if body == nil || status == 204 {
		w.WriteHeader(status)
		return
	}
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(body)
}

func ptr[T any](value T) *T { return &value }

func body[T any](r *request) (T, error) {
	var value T
	contentType, _, err := mime.ParseMediaType(r.Header.Get("Content-Type"))
	if err != nil || contentType != "application/json" {
		return value, domain.Failure(415, domain.RequestUnsupportedMediaType, "The request must use application/json.")
	}
	limit := 4 * 1024
	if _, reorder := any(value).(domain.ReorderWishesRequest); reorder {
		limit = 64 * 1024
	}
	if _, review := any(value).(domain.UpdateWishlistReportReviewRequest); review {
		limit = 16 * 1024
	}
	bytes, err := io.ReadAll(io.LimitReader(r.Body, int64(limit)+1))
	if err != nil {
		return value, domain.Failure(400, domain.RequestBadRequest, "The JSON body is invalid.")
	}
	if len(bytes) > limit {
		return value, domain.Failure(413, domain.RequestPayloadTooLarge, "The request body is too large.")
	}
	if !validation.JSONUnicode(bytes) {
		return value, domain.Failure(400, domain.RequestBadRequest, "The JSON body must contain valid Unicode text.")
	}
	decoder := json.NewDecoder(strings.NewReader(string(bytes)))
	if err = validation.Decode(decoder, &value); err != nil {
		return value, domain.Failure(400, domain.RequestBadRequest, "The JSON body is invalid.")
	}
	if decoder.Decode(new(any)) != io.EOF {
		return value, domain.Failure(400, domain.RequestBadRequest, "The request must contain exactly one JSON object.")
	}
	return value, validateInput(r, value)
}

func optionalBody[T any](r *request) (T, error) {
	if r.ContentLength == 0 {
		var value T
		return value, validateInput(r, value)
	}
	return body[T](r)
}

func validateInput[T any](r *request, value T) error {
	// Identified participants use the live profile name, so anonymous details
	// must not affect their enrollment or fail guest-only validation.
	if _, join := any(value).(domain.JoinSharedWishlistRequest); join && r.Principal.MemberID != "" {
		return nil
	}
	return validation.ValidateAt(value, r.Now)
}

func incomingTrace(value string) string {
	parts := strings.Split(value, "-")
	if len(parts) != 4 || len(parts[0]) != 2 || parts[0] == "ff" || len(parts[1]) != 32 || len(parts[2]) != 16 || len(parts[3]) != 2 || parts[1] == strings.Repeat("0", 32) || parts[2] == strings.Repeat("0", 16) || strings.ToLower(value) != value {
		return ""
	}
	for _, part := range parts {
		if _, err := hex.DecodeString(part); err != nil {
			return ""
		}
	}
	decoded, _ := hex.DecodeString(parts[1])
	return hex.EncodeToString(decoded)
}

func entityTag(r *request, required bool) (*uint32, error) {
	value := r.Header.Get("If-Match")
	if value == "" {
		if required {
			return nil, domain.Failure(428, domain.RequestPreconditionRequired, "The If-Match header is required.")
		}
		return nil, nil
	}
	if len(value) == 10 && value[0] == '"' && value[9] == '"' {
		if v, err := strconv.ParseUint(value[1:9], 16, 32); err == nil && value == application.EntityTag(uint32(v)) {
			return ptr(uint32(v)), nil
		}
	}
	return nil, &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The request contains validation errors.", Validation: []domain.ValidationError{{PropertyName: "ifMatch", ErrorMessage: "The If-Match header must contain one valid strong entity tag."}}}
}

func pagination(r *request) (int, int, error) {
	page, size := 1, 20
	errors := []domain.ValidationError{}
	for _, key := range []string{"page", "pageSize"} {
		if values, present := r.URL.Query()[key]; present {
			value, err := strconv.Atoi(values[0])
			if err != nil || len(values) != 1 || value < 1 || key == "pageSize" && value > 100 {
				errors = append(errors, domain.ValidationError{PropertyName: key, ErrorMessage: "The pagination value is outside the allowed range."})
				continue
			}
			if key == "page" {
				page = value
			} else {
				size = value
			}
		}
	}
	if len(errors) > 0 {
		return 0, 0, &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The request contains validation errors.", Validation: errors}
	}
	return page, size, nil
}

type rateWindow struct {
	duration time.Duration
	start    time.Time
	count    int
}
type rateLimiter struct {
	mu      sync.Mutex
	windows map[string]rateWindow
}

func (l *rateLimiter) allow(key string, limit int, now time.Time) bool {
	return l.allowWindow(key, limit, time.Minute, now)
}

func (l *rateLimiter) allowWindow(key string, limit int, duration time.Duration, now time.Time) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	if len(l.windows) > 10000 {
		for k, w := range l.windows {
			if now.Sub(w.start) >= w.duration {
				delete(l.windows, k)
			}
		}
		if len(l.windows) > 10000 {
			return false
		}
	}
	w := l.windows[key]
	if now.Sub(w.start) >= duration {
		w = rateWindow{start: now, duration: duration}
	}
	w.count++
	l.windows[key] = w
	return w.count <= limit
}
