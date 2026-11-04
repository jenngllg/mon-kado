//go:build integration

package httpapi_test

import (
	"fmt"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"testing"
)

func TestPush_WhenRequestsAreInvalid_RejectsWithoutChangingTheDevice(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "push-contract@example.test")
	const path = "/api/v1/members/current/push-subscriptions"
	var configuration domain.PushConfigurationResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/push-configuration", "", nil), 200, &configuration)
	if configuration.Enabled || configuration.PublicKey != nil {
		t.Fatal("disabled push configuration exposed a key")
	}
	// Act / Assert: native permission cannot make an unavailable server accept registrations.
	requireStatus(t, f.send(t, "POST", path, `{"name":"Phone","endpoint":"https://fcm.googleapis.com/contract"}`, nil), 503, nil)
	device := activatePush(t, f, "https://fcm.googleapis.com/contract")
	f.service.Config.PushOptions.PublicKey = "BGsX0fLhLEJH-Lzm5WOkQPJ3A32BLeszoPShOUXYmMKWT-NC4v4af5uO5-tKfA-eFivOM1drMV7Oy7ZAaDe_UfU"
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/push-configuration", "", nil), 200, &configuration)
	if !configuration.Enabled || configuration.PublicKey == nil || *configuration.PublicKey != "BGsX0fLhLEJH-Lzm5WOkQPJ3A32BLeszoPShOUXYmMKWT-NC4v4af5uO5-tKfA-eFivOM1drMV7Oy7ZAaDe_UfU" {
		t.Fatal("enabled configuration omitted its public key")
	}
	requireStatus(t, f.send(t, "POST", path, `{}`, nil), 400, nil)
	requireStatus(t, f.send(t, "PUT", path+"/"+device.Id, `{}`, map[string]string{"If-Match": device.Version}), 400, nil)
	requireStatus(t, f.send(t, "PUT", path+"/"+device.Id, `{"name":"Phone","disabledKinds":[]}`, map[string]string{"If-Match": "weak"}), 400, nil)
	missing := path + "/" + security.NewID()
	requireStatus(t, f.send(t, "GET", missing, "", nil), 404, nil)
	requireStatus(t, f.send(t, "PUT", missing, `{"name":"Phone","disabledKinds":[]}`, map[string]string{"If-Match": device.Version}), 404, nil)
	requireStatus(t, f.send(t, "PUT", pushPreferencePath, `{}`, map[string]string{"If-Match": `"00000000"`}), 400, nil)
	foreign := f.visitor(t)
	foreign.member(t, "push-contract-foreign@example.test")
	requireStatus(t, foreign.send(t, "GET", path+"/"+device.Id, "", nil), 404, nil)
	var persisted domain.PushSubscriptionResponse
	requireStatus(t, f.send(t, "GET", path+"/"+device.Id, "", nil), 200, &persisted)
	if persisted.Name != device.Name || persisted.Version != device.Version || len(persisted.DisabledKinds) != 0 {
		t.Fatal("rejected requests changed the device")
	}
}

func TestPush_WhenDeviceLimitIsReached_RejectsNewDevicesAndStillAllowsReactivation(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "push-limit@example.test")
	const path = "/api/v1/members/current/push-subscriptions"
	for index := range 10 {
		activatePush(t, f, fmt.Sprintf("https://fcm.googleapis.com/limited-%d", index))
	}
	// Act / Assert: an existing endpoint is not counted as another device.
	requireStatus(t, f.send(t, "POST", path, `{"name":"Phone","endpoint":"https://fcm.googleapis.com/overflow"}`, nil), 409, nil)
	requireStatus(t, f.send(t, "POST", path, `{"name":"Phone","endpoint":"https://fcm.googleapis.com/limited-0"}`, nil), 201, nil)
	var devices []domain.PushSubscriptionResponse
	requireStatus(t, f.send(t, "GET", path, "", nil), 200, &devices)
	if len(devices) != 10 {
		t.Fatal("device limit was exceeded or a registered device was lost")
	}
}
