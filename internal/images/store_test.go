package images_test

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
	"testing"

	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/google/uuid"
)

func TestImageStorage_WhenIdentifierIsInvalid_RefusesAllOperationsWithoutFiles(t *testing.T) {
	for _, identifier := range []string{"", "../outside", "not-a-uuid"} {
		t.Run(identifier, func(t *testing.T) {
			// Arrange
			root := t.TempDir()
			// Act / Assert
			if err := images.WritePending(t.Context(), root, identifier, []byte("fixture")); err == nil {
				t.Fatal("invalid write accepted")
			}
			if err := images.Finalize(root, identifier); err == nil {
				t.Fatal("invalid finalize accepted")
			}
			if err := images.Remove(root, identifier); err == nil {
				t.Fatal("invalid removal accepted")
			}
			if _, err := images.Read(t.Context(), root, identifier); !errors.Is(err, os.ErrNotExist) {
				t.Fatalf("read error = %v", err)
			}
			entries, err := os.ReadDir(root)
			if err != nil || len(entries) != 0 {
				t.Fatal("invalid identifiers caused filesystem effects")
			}
		})
	}
}

func TestImageStorage_WhenCallerCancels_DoesNotCreateStorage(t *testing.T) {
	// Arrange
	root := filepath.Join(t.TempDir(), "not-created")
	id := uuid.NewString()
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	// Act / Assert
	if err := images.WritePending(ctx, root, id, []byte("fixture")); !errors.Is(err, context.Canceled) {
		t.Fatalf("write error = %v", err)
	}
	if _, err := images.Read(ctx, root, id); !errors.Is(err, context.Canceled) {
		t.Fatalf("read error = %v", err)
	}
	if _, err := os.Stat(root); !errors.Is(err, os.ErrNotExist) {
		t.Fatal("cancelled operation created storage")
	}
}

func TestImageStorage_WhenFilesAlreadyExist_DoesNotOverwriteOrLosePendingMarkers(t *testing.T) {
	for _, pending := range []bool{false, true} {
		t.Run(map[bool]string{false: "image", true: "pending"}[pending], func(t *testing.T) {
			// Arrange
			root, id := t.TempDir(), uuid.NewString()
			content := []byte("existing normalized fixture")
			if err := images.WritePending(t.Context(), root, id, content); err != nil {
				t.Fatal(err)
			}
			if !pending {
				if err := images.Finalize(root, id); err != nil {
					t.Fatal(err)
				}
			}
			// Act
			if err := images.WritePending(t.Context(), root, id, []byte("replacement")); err == nil {
				t.Fatal("duplicate write accepted")
			}
			// Assert
			actual, err := images.Read(t.Context(), root, id)
			if err != nil || !bytes.Equal(actual, content) {
				t.Fatal("existing image was lost or changed")
			}
			_, err = os.Stat(filepath.Join(root, id+".pending"))
			if pending && err != nil || !pending && !errors.Is(err, os.ErrNotExist) {
				t.Fatal("pending marker state changed")
			}
		})
	}
}

func TestImageStorage_WhenPathIsUnavailable_ReturnsErrorsWithoutRemovingOtherFiles(t *testing.T) {
	// Arrange
	root := t.TempDir()
	id := uuid.NewString()
	blocked := filepath.Join(root, "file")
	if err := os.WriteFile(blocked, []byte("sentinel"), 0600); err != nil {
		t.Fatal(err)
	}
	// Act / Assert
	if err := images.WritePending(t.Context(), blocked, id, nil); err == nil {
		t.Fatal("non-directory storage accepted")
	}
	for _, unavailable := range []string{blocked, filepath.Join(root, "absent")} {
		if _, err := images.Read(t.Context(), unavailable, id); err == nil {
			t.Fatal("unavailable storage accepted")
		}
	}
	if err := images.Finalize(root, id); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("missing marker error = %v", err)
	}
	image := filepath.Join(root, id+".webp")
	if err := os.Mkdir(image, 0700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(image, "preserved"), []byte("sentinel"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := images.Remove(root, id); err == nil {
		t.Fatal("nonempty directory removal accepted")
	}
	if _, err := images.Read(t.Context(), root, id); !errors.Is(err, os.ErrNotExist) {
		t.Fatal("directory exposed as image")
	}
	if content, err := os.ReadFile(blocked); err != nil || string(content) != "sentinel" {
		t.Fatal("unrelated file changed")
	}
}

func TestImageStorage_WhenImageExceedsLimit_RefusesBeforeReadingContent(t *testing.T) {
	// Arrange
	root, id := t.TempDir(), uuid.NewString()
	file, err := os.Create(filepath.Join(root, id+".webp"))
	if err != nil {
		t.Fatal(err)
	}
	if err := file.Truncate(images.MaximumInputBytes + 1); err != nil {
		file.Close()
		t.Fatal(err)
	}
	if err := file.Close(); err != nil {
		t.Fatal(err)
	}
	// Act / Assert
	if _, err := images.Read(t.Context(), root, id); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("oversized image error = %v", err)
	}
}
