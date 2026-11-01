package providers_test

import (
	"errors"
	"os"
	"path/filepath"
	"sync"
	"testing"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/providers"
)

func TestMailbox_WhenAttemptsRace_PublishesOneCompleteMessageAndRemovesTemporaryFiles(t *testing.T) {
	// Arrange
	mailbox := providers.Mailbox{Path: t.TempDir()}
	id := "01900000-0000-7000-8000-000000000001"
	message := application.EmailMessage{To: "local@example.test", Subject: "Concurrent delivery", Text: "A complete message"}
	var running sync.WaitGroup
	failures := make(chan error, 8)
	// Act
	for range 8 {
		running.Go(func() { _, err := mailbox.Send(t.Context(), id, message); failures <- err })
	}
	running.Wait()
	close(failures)
	entries, err := os.ReadDir(mailbox.Path)
	// Assert
	for failure := range failures {
		if failure != nil {
			t.Fatal(failure)
		}
	}
	if err != nil || len(entries) != 1 || entries[0].Name() != id+".json" {
		t.Fatal("mailbox publication left partial or duplicate files")
	}
	message.Text = "Conflicting content"
	if _, err = mailbox.Send(t.Context(), id, message); err == nil {
		t.Fatal("outbox identity was silently overwritten")
	}
}

func TestMailbox_WhenStorageOrIdentityInvalid_RejectsWithoutPublication(t *testing.T) {
	for _, mode := range []string{"identity", "nilIdentity", "pathIsFile", "destinationIsDirectory", "oversizedExisting"} {
		t.Run(mode, func(t *testing.T) {
			// Arrange
			root := t.TempDir()
			mailbox := providers.Mailbox{Path: filepath.Join(root, "messages")}
			id := "01900000-0000-7000-8000-000000000001"
			if mode == "identity" {
				id = "../outside"
			}
			if mode == "nilIdentity" {
				id = "00000000-0000-0000-0000-000000000000"
			}
			if mode == "pathIsFile" {
				if err := os.WriteFile(mailbox.Path, []byte("file"), 0600); err != nil {
					t.Fatal(err)
				}
			}
			if mode == "destinationIsDirectory" {
				if err := os.MkdirAll(filepath.Join(mailbox.Path, id+".json"), 0700); err != nil {
					t.Fatal(err)
				}
			}
			if mode == "oversizedExisting" {
				if err := os.MkdirAll(mailbox.Path, 0700); err != nil {
					t.Fatal(err)
				}
				if err := os.WriteFile(filepath.Join(mailbox.Path, id+".json"), make([]byte, (1<<20)+1), 0600); err != nil {
					t.Fatal(err)
				}
			}
			// Act
			_, err := mailbox.Send(t.Context(), id, application.EmailMessage{})
			// Assert
			if err == nil {
				t.Fatal("invalid mailbox destination or identity accepted")
			}
			if mode == "identity" || mode == "nilIdentity" {
				var failure *application.DeliveryError
				if !errors.As(err, &failure) || failure.Retryable {
					t.Fatal("invalid identity should be a terminal delivery failure")
				}
			}
		})
	}
}
