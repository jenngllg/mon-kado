package providers

import (
	"context"
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/application"
	"os"
	"path/filepath"
	"testing"
)

func TestMailbox_WhenAttemptRepeated_WritesOnePrivateMessageWithoutNetwork(t *testing.T) {
	// Arrange
	m := Mailbox{Path: t.TempDir()}
	id := "01900000-0000-7000-8000-000000000001"
	message := application.EmailMessage{To: "local@example.test", Subject: "Confirmation", Text: "Bonjour", ActionURL: "http://localhost/confirm?token=test-proof"}
	// Act
	first, err := m.Send(t.Context(), id, message)
	second, repeatedErr := m.Send(t.Context(), id, message)
	data, readErr := os.ReadFile(filepath.Join(m.Path, id+".json"))
	var restored application.EmailMessage
	decodeErr := json.Unmarshal(data, &restored)
	// Assert
	if err != nil || repeatedErr != nil || readErr != nil || decodeErr != nil || first != second || restored != message {
		t.Fatal("local mailbox delivery contract differs")
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	if _, err = m.Send(ctx, id, message); err != context.Canceled {
		t.Fatal("canceled mailbox operation proceeded")
	}
}
