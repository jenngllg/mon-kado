//go:build integration

package httpapi_test

import (
	"context"
	"fmt"
	"github.com/JennGllg/mon-kado/internal/domain"
	"testing"
)

func TestNotifications_WhenFuturePushOnlyRowsExist_RollbackBinaryHidesThem(t *testing.T) {
	// Arrange: this preparation binary also works before the push column exists.
	s := newGroupScenario(t)
	requireStatus(t, s.owner.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, s.memberID), nil), 201, nil)
	n := notificationOfKind(t, s.member, "groupInvitation")
	var present bool
	if err := s.member.store.Pool.QueryRow(t.Context(), `SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='member_notifications' AND column_name='in_app')`).Scan(&present); err != nil {
		t.Fatal(err)
	}
	if !present {
		if _, err := s.member.store.Pool.Exec(t.Context(), `ALTER TABLE member_notifications ADD COLUMN in_app boolean NOT NULL DEFAULT true`); err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() {
			if _, err := s.member.store.Pool.Exec(context.WithoutCancel(t.Context()), `ALTER TABLE member_notifications DROP COLUMN in_app`); err != nil {
				t.Error(err)
			}
		})
	}
	// Act: emulate durable rows written by the later push binary, then read through public contracts.
	if _, err := s.member.store.Pool.Exec(t.Context(), `UPDATE member_notifications SET in_app=false WHERE id=$1`, n.Id); err != nil {
		t.Fatal(err)
	}
	// Assert: no private content or unread badge is resurrected after a binary rollback.
	requireNotificationAbsent(t, s.member, "groupInvitation")
	var status domain.NotificationStatusResponse
	requireStatus(t, s.member.send(t, "GET", "/api/v1/notifications/status", "", nil), 200, &status)
	if status.HasUnread {
		t.Fatal("push-only notification resurfaced in rollback badge")
	}
	requireStatus(t, s.member.send(t, "GET", "/api/v1/notifications/"+n.Id+"/destination", "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/"+n.Id+"/read", "", nil), 404, nil)
}
