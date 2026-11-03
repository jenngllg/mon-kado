package httpapi

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"
	"sync"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/logging"
	"github.com/JennGllg/mon-kado/internal/security"
)

type notificationHub struct {
	mu      sync.Mutex
	clients map[chan struct{}]struct{}
}

func (h *notificationHub) subscribe(maximum int) (chan struct{}, func(), bool) {
	h.mu.Lock()
	defer h.mu.Unlock()
	if len(h.clients) >= maximum {
		return nil, func() {}, false
	}
	if h.clients == nil {
		h.clients = map[chan struct{}]struct{}{}
	}
	c := make(chan struct{}, 1)
	h.clients[c] = struct{}{}
	return c, func() { h.mu.Lock(); delete(h.clients, c); h.mu.Unlock() }, true
}
func (h *notificationHub) publish() {
	h.mu.Lock()
	defer h.mu.Unlock()
	for c := range h.clients {
		select {
		case c <- struct{}{}:
		default:
		}
	}
}

// RunNotificationEvents shares one bounded PostgreSQL LISTEN connection across browser streams.
// Reconnection always invalidates the badge so committed changes missed during downtime are recovered.
func (a *API) RunNotificationEvents(ctx context.Context) {
	for ctx.Err() == nil {
		err := a.listenNotifications(ctx)
		if ctx.Err() != nil {
			return
		}
		if err != nil {
			id := security.NewID()
			a.Logger.ErrorContext(ctx, "Notification listener interrupted", "EventId", logging.NotificationListenerInterrupted, "ExceptionType", fmt.Sprintf("%T", err), "CorrelationId", id, "TraceId", strings.ReplaceAll(id, "-", ""))
		}
		a.notifications.publish()
		timer := time.NewTimer(a.Service.Config.Notifications().ReconnectDelay)
		select {
		case <-ctx.Done():
			timer.Stop()
			return
		case <-timer.C:
		}
	}
}
func (a *API) listenNotifications(ctx context.Context) error {
	connection, err := a.Service.Store.Pool.Acquire(ctx)
	if err != nil {
		return err
	}
	defer connection.Release()
	if _, err = connection.Exec(ctx, `LISTEN monkado_notifications`); err != nil {
		return err
	}
	// The connection is closed rather than returned with a lingering LISTEN registration.
	defer connection.Conn().Close(context.WithoutCancel(ctx))
	a.notifications.publish()
	for ctx.Err() == nil {
		if _, err = connection.Conn().WaitForNotification(ctx); err != nil {
			return err
		}
		a.notifications.publish()
	}
	return ctx.Err()
}

func (a *API) notificationRoutes() {
	a.route("GET /api/v1/notifications", options{Auth: true}, func(r *request) (response, error) {
		page, size, err := pagination(r)
		if err != nil {
			return response{}, err
		}
		value, err := a.Service.Notifications(r.Context(), r.Principal.MemberID, page, size)
		return response{Body: value}, err
	})
	a.route("GET /api/v1/notifications/status", options{Auth: true}, func(r *request) (response, error) {
		value, err := a.Service.NotificationStatus(r.Context(), r.Principal.MemberID)
		return response{Body: value}, err
	})
	a.route("POST /api/v1/notifications/read", options{Auth: true, CSRF: true}, func(r *request) (response, error) {
		return response{Status: 204}, a.Service.ReadNotifications(r.Context(), r.Principal.MemberID, "")
	})
	a.route("POST /api/v1/notifications/{notificationId}/read", options{Auth: true, CSRF: true}, func(r *request) (response, error) {
		return response{Status: 204}, a.Service.ReadNotifications(r.Context(), r.Principal.MemberID, r.PathValue("notificationId"))
	})
	a.route("POST /api/v1/notifications/{notificationId}/dismiss", options{Auth: true, CSRF: true}, func(r *request) (response, error) {
		return response{Status: 204}, a.Service.DismissSharingReminder(r.Context(), r.Principal.MemberID, r.PathValue("notificationId"))
	})
	a.route("GET /api/v1/notifications/{notificationId}/destination", options{Auth: true}, func(r *request) (response, error) {
		href, err := a.Service.NotificationDestination(r.Context(), r.Principal.MemberID, r.PathValue("notificationId"))
		return response{Body: struct {
			Href string `json:"href"`
		}{href}}, err
	})
	a.route("GET /api/v1/notifications/events", options{Auth: true}, a.notificationEvents)
}

func (a *API) notificationEvents(r *request) (response, error) {
	settings := a.Service.Config.Notifications()
	events, unsubscribe, available := a.notifications.subscribe(settings.MaximumStreams)
	if !available {
		return response{}, domain.Failure(503, domain.TechnicalServiceUnavailable, "Notification connection capacity has been reached.")
	}
	return response{Events: func(w http.ResponseWriter) {
		defer unsubscribe()
		controller := http.NewResponseController(w)
		controller.SetWriteDeadline(time.Time{})
		w.Header().Set("Content-Type", "text/event-stream")
		w.Header().Set("X-Accel-Buffering", "no")
		ticker := time.NewTicker(settings.HeartbeatInterval)
		defer ticker.Stop()
		// Bound each connection so a routed API can drain during an online release.
		// The browser reconnects to the selected API and reads the current badge.
		lifetime := time.NewTimer(90 * time.Second)
		defer lifetime.Stop()
		var previous *bool
		send := func(force bool) bool {
			// Revalidate the real session and any active simulation before each disclosure.
			principal, err := a.Service.Authenticate(r.Context(), strings.TrimPrefix(r.Header.Get("Authorization"), "Bearer "))
			if err != nil {
				return false
			}
			current := &request{Request: r.Request, Principal: principal, Now: a.Service.Now()}
			if a.applyImpersonation(current, r.Pattern) != nil || current.Principal.MemberID != r.Principal.MemberID {
				return false
			}
			status, err := a.Service.NotificationStatus(r.Context(), r.Principal.MemberID)
			if err != nil {
				return false
			}
			controller.SetWriteDeadline(time.Now().Add(settings.WriteTimeout))
			if !force && previous != nil && *previous == status.HasUnread {
				if _, err = fmt.Fprint(w, ": keepalive\n\n"); err != nil {
					return false
				}
				return controller.Flush() == nil
			}
			previous = &status.HasUnread
			value, err := json.Marshal(status)
			if err != nil {
				return false
			}
			if _, err = fmt.Fprintf(w, "event: notifications\ndata: %s\n\n", value); err != nil {
				return false
			}
			return controller.Flush() == nil
		}
		if !send(true) {
			return
		}
		for {
			select {
			case <-r.Context().Done():
				return
			case <-lifetime.C:
				return
			case <-events:
				if !send(true) {
					return
				}
			case <-ticker.C:
				if !send(false) {
					return
				}
			}
		}
	}}, nil
}
