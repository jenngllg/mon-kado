//go:build integration

package httpapi_test

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/cookiejar"
	"net/http/httptest"
	"net/url"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/JennGllg/mon-kado/internal/persistence"
	"github.com/JennGllg/mon-kado/internal/security"
)

type fixture struct {
	api          *httpapi.API
	server       *httptest.Server
	store        *persistence.Store
	service      *application.Service
	client       *http.Client
	csrf, access string
}

func newFixture(t *testing.T) *fixture {
	t.Helper()
	connection := os.Getenv("MONKADO_TEST_DATABASE_URL")
	if connection == "" {
		t.Skip("MONKADO_TEST_DATABASE_URL must point to an isolated PostgreSQL test database")
	}
	u, err := url.Parse(connection)
	if err != nil || !strings.HasSuffix(u.Path, "_test") {
		t.Fatal("integration tests require a database name ending in _test")
	}
	store, err := persistence.Open(t.Context(), connection)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(store.Close)
	guard, err := store.Pool.Acquire(t.Context())
	if err != nil {
		t.Fatal(err)
	}
	if _, err = guard.Exec(t.Context(), `SELECT pg_advisory_lock(771206482)`); err != nil {
		guard.Release()
		t.Fatal(err)
	}
	// Separate native test processes must not truncate a fixture another process is using.
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.WithoutCancel(t.Context()), 5*time.Second)
		defer cancel()
		guard.Exec(ctx, `SELECT pg_advisory_unlock(771206482)`)
		guard.Release()
	})
	if err = store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}
	if _, err = store.Pool.Exec(t.Context(), `TRUNCATE users,guest_sessions,google_authentication_flows,administrative_account_erasure_events,account_erasure_email_outbox CASCADE`); err != nil {
		t.Fatal(err)
	}
	cfg := config.Config{SigningKey: []byte(strings.Repeat("s", 32)), EncryptionKey: []byte(strings.Repeat("e", 32)), Issuer: "MonKado.Api", Audience: "MonKado.Frontend", FrontendOrigin: "http://localhost:5173", AllowedOrigins: []string{"http://localhost:5173"}, AllowedHosts: []string{"127.0.0.1"}, ImagePath: t.TempDir(), ExportPath: t.TempDir()}
	service := application.New(store, cfg, time.Now)
	service.Config.MaximumAttempts = 10
	service.Config.WorkerInterval = time.Second
	api := httpapi.New(service, slog.New(slog.NewJSONHandler(io.Discard, nil)))
	server := httptest.NewServer(api)
	t.Cleanup(server.Close)
	jar, _ := cookiejar.New(nil)
	f := &fixture{api: api, store: store, service: service, server: server, client: &http.Client{Jar: jar}}
	f.csrfToken(t)
	return f
}

func (f *fixture) csrfToken(t *testing.T) {
	t.Helper()
	r := f.send(t, "GET", "/security/csrf-token", "", nil)
	defer r.Body.Close()
	var token domain.CsrfTokenResponse
	if err := json.NewDecoder(r.Body).Decode(&token); err != nil {
		t.Fatal(err)
	}
	f.csrf = token.Token
}

func (f *fixture) send(t *testing.T, method, path, body string, headers map[string]string) *http.Response {
	t.Helper()
	req, err := http.NewRequestWithContext(t.Context(), method, f.server.URL+path, bytes.NewBufferString(body))
	if err != nil {
		t.Fatal(err)
	}
	if body != "" {
		req.Header.Set("Content-Type", "application/json")
	}
	if f.csrf != "" {
		req.Header.Set("X-CSRF-TOKEN", f.csrf)
	}
	if f.access != "" {
		req.Header.Set("Authorization", "Bearer "+f.access)
	}
	for key, value := range headers {
		req.Header.Set(key, value)
	}
	r, err := f.client.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	return r
}

func requireStatus(t *testing.T, r *http.Response, expected int, target any) {
	t.Helper()
	defer r.Body.Close()
	data, _ := io.ReadAll(r.Body)
	if r.StatusCode != expected {
		t.Fatalf("expected %d, got %d: %s", expected, r.StatusCode, data)
	}
	if target != nil {
		if err := json.Unmarshal(data, target); err != nil {
			t.Fatal(err)
		}
	}
}

func (f *fixture) member(t *testing.T, email string) string {
	t.Helper()
	password := "A long test password 🎁"
	registration, _ := json.Marshal(map[string]string{"email": email, "password": password, "displayName": "Test member"})
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", string(registration), nil), 202, nil)
	var outbox, id, hash string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT o.id::text,u.id::text,u.password_hash FROM authentication_email_outbox o JOIN users u ON u.id=o.user_id WHERE u.email=$1`, email).Scan(&outbox, &id, &hash); err != nil {
		t.Fatal(err)
	}
	if hash == password || !security.VerifyPassword(password, hash) {
		t.Fatal("password persistence invariant violated")
	}
	message, err := f.service.PrepareAuthenticationEmail(t.Context(), outbox)
	if err != nil {
		t.Fatal(err)
	}
	link, err := url.Parse(message.ActionURL)
	if err != nil {
		t.Fatal(err)
	}
	credentials := actionCredentials(t, link)
	if link.Path != "/confirm-email" || credentials.Get("userId") != id {
		t.Fatal("confirmation link does not target the frontend confirmation contract")
	}
	confirmation, _ := json.Marshal(map[string]string{"userId": id, "token": credentials.Get("token")})
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/email-confirmations", string(confirmation), nil), 204, nil)
	login, _ := json.Marshal(map[string]string{"email": email, "password": password})
	var tokens domain.AccessTokenResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", string(login), nil), 200, &tokens)
	f.access = tokens.AccessToken
	f.csrfToken(t)
	return id
}

func TestWishlists_WhenCreatingUpdatingAndDeleting_PreservesHTTPContracts(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner@example.test")
	// Act
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Birthday","occasion":"birthday","message":null}`, nil)
	location, etag := created.Header.Get("Location"), created.Header.Get("ETag")
	var list domain.WishlistResponse
	requireStatus(t, created, 201, &list)
	// Assert
	if location != "/api/v1/wishlists/"+list.Id || len(etag) != 10 || list.UpdatedAt != nil || list.Message != nil {
		t.Fatal("created wishlist contract differs")
	}
	requireStatus(t, f.send(t, "GET", location, "", nil), 200, &list)
	requireStatus(t, f.send(t, "PUT", location, `{"name":"New name","occasion":"christmas"}`, nil), 428, nil)
	updated := f.send(t, "PUT", location, `{"name":"New name","occasion":"christmas"}`, map[string]string{"If-Match": etag})
	newETag := updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &list)
	if list.Name != "New name" || list.UpdatedAt == nil || newETag == etag {
		t.Fatal("updated resource did not expose its new version")
	}
	requireStatus(t, f.send(t, "DELETE", location, "", map[string]string{"If-Match": etag}), 412, nil)
	requireStatus(t, f.send(t, "DELETE", location, "", map[string]string{"If-Match": newETag}), 204, nil)
	requireStatus(t, f.send(t, "GET", location, "", nil), 404, nil)
	requireStatus(t, f.send(t, "DELETE", location, "", map[string]string{"If-Match": newETag}), 404, nil)
}

func TestWishes_WhenCreatedAndReordered_PreservesDecimalAndParentContracts(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Gifts","occasion":"other"}`, nil), 201, &list)
	path := "/api/v1/wishlists/" + list.Id + "/wishes"
	// Act
	var first, second domain.WishResponse
	requireStatus(t, f.send(t, "POST", path, `{"name":"First","price":1234.56,"quantity":2}`, nil), 201, &first)
	requireStatus(t, f.send(t, "POST", path, `{"name":"Second"}`, nil), 201, &second)
	collection := f.send(t, "GET", path, "", nil)
	etag := collection.Header.Get("ETag")
	var wishes domain.WishCollectionResponse
	requireStatus(t, collection, 200, &wishes)
	// Assert
	if first.Price == nil || first.Price.String() != "1234.56" || second.Quantity != 1 || first.Position >= second.Position || len(wishes.Wishes) != 2 {
		t.Fatal("wish contracts differ")
	}
	order := fmt.Sprintf(`{"wishIds":[%q,%q]}`, second.Id, first.Id)
	var reordered domain.WishOrderResponse
	requireStatus(t, f.send(t, "PATCH", path, order, map[string]string{"If-Match": etag}), 200, &reordered)
	if reordered.Wishes[0].Id != second.Id {
		t.Fatal("wishes were not reordered")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+security.NewID()+"/wishes/"+first.Id, "", nil), 404, nil)
}

func TestAPI_WhenInputIsInvalid_AggregatesErrorsAndRejectsCSRF(t *testing.T) {
	// Arrange
	f := newFixture(t)
	// Act
	var result domain.ErrorResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", `{}`, nil), 400, &result)
	// Assert
	if len(result.ValidationErrors) != 3 || result.ErrorCode == nil || *result.ErrorCode != domain.RequestValidationError {
		t.Fatal("validation errors were not aggregated")
	}
	f.csrf = "invalid"
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", `{}`, nil), 400, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 401, nil)
}

func (f *fixture) visitor(t *testing.T) *fixture {
	t.Helper()
	jar, _ := cookiejar.New(nil)
	visitor := &fixture{server: f.server, store: f.store, service: f.service, client: &http.Client{Jar: jar}}
	visitor.csrfToken(t)
	return visitor
}

func TestReservations_WhenVisitorsCompeteForLastUnit_OnlyOneSucceeds(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Gifts","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"One gift","quantity":1}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	created := f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &link)
	parsed, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": parsed.Fragment}
	first, second := f.visitor(t), f.visitor(t)
	path := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, first.send(t, "POST", path+"/participants", `{"displayName":"First guest"}`, headers), 201, nil)
	requireStatus(t, second.send(t, "POST", path+"/participants", `{"displayName":"Second guest"}`, headers), 201, nil)
	reservationPath := path + "/wishes/" + wish.Id + "/reservations/current"
	// Act
	results := make(chan *http.Response, 2)
	failures := make(chan error, 2)
	for _, actor := range []*fixture{first, second} {
		go func(actor *fixture) {
			req, err := http.NewRequestWithContext(t.Context(), "PUT", f.server.URL+reservationPath, strings.NewReader(`{"quantity":1}`))
			if err != nil {
				failures <- err
				return
			}
			req.Header.Set("Content-Type", "application/json")
			req.Header.Set("X-CSRF-TOKEN", actor.csrf)
			req.Header.Set("X-MonKado-Share-Token", parsed.Fragment)
			r, err := actor.client.Do(req)
			if err != nil {
				failures <- err
				return
			}
			results <- r
		}(actor)
	}
	// Assert
	statuses := map[int]int{}
	for i := 0; i < 2; i++ {
		select {
		case err := <-failures:
			t.Fatal(err)
		case r := <-results:
			statuses[r.StatusCode]++
			r.Body.Close()
		case <-t.Context().Done():
			t.Fatal(t.Context().Err())
		}
	}
	if statuses[201] != 1 || statuses[409] != 1 {
		t.Fatalf("unexpected concurrent reservation results: %v", statuses)
	}
	var shared domain.SharedWishlistResponse
	requireStatus(t, first.send(t, "GET", path, "", headers), 200, &shared)
	if len(shared.Wishes) != 1 || shared.Wishes[0].ReservedQuantity == nil || *shared.Wishes[0].ReservedQuantity != 1 || shared.Wishes[0].AvailableQuantity == nil || *shared.Wishes[0].AvailableQuantity != 0 {
		t.Fatal("reservation quantity invariant violated")
	}
	rotated := f.send(t, "PUT", "/api/v1/wishlists/"+list.Id+"/share-link", "", map[string]string{"If-Match": etag})
	requireStatus(t, rotated, 200, &link)
	requireStatus(t, first.send(t, "GET", path, "", headers), 404, nil)
}

func TestReservationHistory_WhenMemberCancels_RetainsLifecycle(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Gifts","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Gift"}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	parsed, _ := url.Parse(link.ShareUrl)
	headers := map[string]string{"X-MonKado-Share-Token": parsed.Fragment}
	member := f.visitor(t)
	member.member(t, "participant@example.test")
	path := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, member.send(t, "POST", path+"/participants", "", headers), 201, nil)
	reservationPath := path + "/wishes/" + wish.Id + "/reservations/current"
	// Act
	created := member.send(t, "PUT", reservationPath, `{"quantity":1}`, headers)
	headers["If-Match"] = created.Header.Get("ETag")
	requireStatus(t, created, 201, nil)
	requireStatus(t, member.send(t, "DELETE", reservationPath, "", headers), 204, nil)
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, member.send(t, "GET", "/api/v1/members/current/reservations?status=cancelled", "", nil), 200, &history)
	// Assert
	if len(history.Items) != 1 || history.Items[0].Status != "cancelled" || history.Items[0].EndedAt == nil {
		t.Fatal("cancelled member lifecycle was not retained")
	}
	requireStatus(t, member.send(t, "GET", "/api/v1/members/current/reservations?page=100", "", nil), 200, &history)
	if len(history.Items) != 0 || history.CurrentPage != 100 || history.TotalCount != 1 {
		t.Fatal("out-of-range pagination metadata differs")
	}
}

func TestEmailChange_WhenConfirmed_ChangesLoginAndRevokesExistingSession(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "original@example.test")
	current := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	etag := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	// Act
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/email", `{"email":"changed@example.test","currentPassword":"A long test password 🎁"}`, map[string]string{"If-Match": etag}), 202, nil)
	var outbox string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT id::text FROM authentication_email_outbox WHERE kind='EMAIL_CHANGE_CONFIRMATION'`).Scan(&outbox); err != nil {
		t.Fatal(err)
	}
	message, err := f.service.PrepareAuthenticationEmail(t.Context(), outbox)
	if err != nil {
		t.Fatal(err)
	}
	link, _ := url.Parse(message.ActionURL)
	credentials := actionCredentials(t, link)
	confirmation, _ := json.Marshal(map[string]string{"requestId": credentials.Get("requestId"), "token": credentials.Get("token")})
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/email-change-confirmations", string(confirmation), nil), 204, nil)
	// Assert
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 401, nil)
	f.access = ""
	f.csrfToken(t)
	var tokens domain.AccessTokenResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", `{"email":"changed@example.test","password":"A long test password 🎁"}`, nil), 200, &tokens)
	if tokens.AccessToken == "" {
		t.Fatal("new login identity was not persisted")
	}
}

func TestAccountDeletion_WhenConfirmed_RemovesIdentityAndPublicResources(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "deleted@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Private","occasion":"other"}`, nil), 201, &list)
	// Act
	requireStatus(t, f.send(t, "POST", "/api/v1/members/current/deletion-requests", "", nil), 202, nil)
	var outbox string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT id::text FROM authentication_email_outbox WHERE kind='ACCOUNT_DELETION_CONFIRMATION'`).Scan(&outbox); err != nil {
		t.Fatal(err)
	}
	message, err := f.service.PrepareAuthenticationEmail(t.Context(), outbox)
	if err != nil {
		t.Fatal(err)
	}
	link, _ := url.Parse(message.ActionURL)
	confirmation, _ := json.Marshal(map[string]string{"token": actionCredentials(t, link).Get("token")})
	requireStatus(t, f.send(t, "POST", "/api/v1/members/current/deletion-requests/confirm", string(confirmation), nil), 204, nil)
	// Assert
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 401, nil)
	f.access = ""
	f.csrfToken(t)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", `{"email":"deleted@example.test","password":"A long test password 🎁"}`, nil), 401, nil)
}
