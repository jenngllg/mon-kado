//go:build integration

package httpapi_test

import (
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestNotifications_WhenBirthdayRecurs_ProducesFreshAnnualRemindersWithoutDuplicates(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	loaded := s.owner.send(t, "GET", s.base, "", nil)
	version := loaded.Header.Get("ETag")
	requireStatus(t, loaded, 200, nil)
	requireStatus(t, s.owner.send(t, "PUT", s.base, `{"name":"New year birthday","occasion":"birthday","eventDate":"2027-01-01"}`, map[string]string{"If-Match": version}), 200, nil)
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	var member domain.CurrentSessionResponse
	requireStatus(t, s.member.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &member)
	previous := s.owner.service.Now
	defer func() { s.owner.service.Now = previous }()
	var previousID string
	for _, year := range []int{2027, 2028, 2029} {
		for _, days := range []int{7, 1} {
			s.owner.service.Now = func() time.Time { return time.Date(year, 1, 1, 12, 0, 0, 0, time.UTC).AddDate(0, 0, -days) }
			// Act: repeat the scheduler to exercise its durable deduplication.
			for range 2 {
				if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
					t.Fatal(err)
				}
			}
			page, err := s.owner.service.Notifications(t.Context(), member.Id, 1, 20)
			if err != nil {
				t.Fatal(err)
			}
			// Assert: previous years expire, while this year's two thresholds remain distinct.
			expected := 1
			if days == 1 {
				expected = 2
			}
			if len(page.Items) != expected || page.Items[0].DaysRemaining == nil || *page.Items[0].DaysRemaining != days || page.Items[0].Id == previousID {
				t.Fatalf("incorrect annual reminder for year %d, %d days remaining", year, days)
			}
			previousID = page.Items[0].Id
		}
	}
}

func TestNotifications_WhenAOneOffOccasionIsPast_DoesNotRepeatItsReminder(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	loaded := s.owner.send(t, "GET", s.base, "", nil)
	version := loaded.Header.Get("ETag")
	requireStatus(t, loaded, 200, nil)
	requireStatus(t, s.owner.send(t, "PUT", s.base, `{"name":"One-off wedding","occasion":"wedding","eventDate":"2027-01-18"}`, map[string]string{"If-Match": version}), 200, nil)
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	previous := s.owner.service.Now
	defer func() { s.owner.service.Now = previous }()
	s.owner.service.Now = func() time.Time { return time.Date(2028, 1, 11, 12, 0, 0, 0, time.UTC) }
	// Act
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = previous
	// Assert
	requireNotificationAbsent(t, s.member, "occasionReminder")
}

func TestNotifications_WhenBirthdayIsFebruary29_UsesFebruary28OnlyInNonLeapYears(t *testing.T) {
	for _, date := range []time.Time{time.Date(2027, 2, 21, 12, 0, 0, 0, time.UTC), time.Date(2028, 2, 22, 12, 0, 0, 0, time.UTC)} {
		t.Run(date.Format("2006-01-02"), func(t *testing.T) {
			// Arrange
			s := newSubscriptionScenario(t)
			loaded := s.owner.send(t, "GET", s.base, "", nil)
			version := loaded.Header.Get("ETag")
			requireStatus(t, loaded, 200, nil)
			requireStatus(t, s.owner.send(t, "PUT", s.base, `{"name":"Leap birthday","occasion":"birthday","eventDate":"2028-02-29"}`, map[string]string{"If-Match": version}), 200, nil)
			requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
			var member domain.CurrentSessionResponse
			requireStatus(t, s.member.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &member)
			previous := s.owner.service.Now
			defer func() { s.owner.service.Now = previous }()
			s.owner.service.Now = func() time.Time { return date }
			// Act
			if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
				t.Fatal(err)
			}
			page, err := s.owner.service.Notifications(t.Context(), member.Id, 1, 20)
			// Assert
			if err != nil || len(page.Items) != 1 || page.Items[0].DaysRemaining == nil || *page.Items[0].DaysRemaining != 7 {
				t.Fatalf("missing leap birthday reminder: %v", err)
			}
		})
	}
}
