package images_test

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
	"testing"

	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestRead_WhenImagePathIsUntrusted_RejectsTraversalOversizedAndCancelledReads(t *testing.T) {
	// Arrange
	root, id := t.TempDir(), security.NewID()
	content := []byte("private normalized fixture")
	if err := images.WritePending(t.Context(), root, id, content); err != nil {
		t.Fatal(err)
	}
	// Act
	for _, untrusted := range []string{"../secret", "../../" + id, "", id + "/../../secret"} {
		if _, err := images.Read(t.Context(), root, untrusted); !errors.Is(err, os.ErrNotExist) {
			t.Fatal("untrusted path escaped identifier validation")
		}
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	if _, err := images.Read(ctx, root, id); !errors.Is(err, context.Canceled) {
		t.Fatal("cancelled file read performed I/O")
	}
	file, err := os.OpenFile(filepath.Join(root, id+".webp"), os.O_WRONLY, 0600)
	if err != nil {
		t.Fatal(err)
	}
	if err = file.Truncate(images.MaximumInputBytes + 1); err != nil {
		t.Fatal(err)
	}
	file.Close()
	_, err = images.Read(t.Context(), root, id)
	// Assert
	if !errors.Is(err, os.ErrNotExist) {
		t.Fatal("oversized private file was read")
	}
}

func TestRead_WhenImageIsLinkedOutsidePrivateRoot_RejectsExternalFile(t *testing.T) {
	// Arrange
	root, id := t.TempDir(), security.NewID()
	external := filepath.Join(t.TempDir(), "private-secret")
	secret := []byte("must remain outside image storage")
	if err := os.WriteFile(external, secret, 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(external, filepath.Join(root, id+".webp")); err != nil {
		t.Skip("symlink creation is unavailable on this host")
	}
	// Act
	_, err := images.Read(t.Context(), root, id)
	// Assert
	if err == nil {
		t.Fatal("external symlink was read")
	}
	unchanged, err := os.ReadFile(external)
	if err != nil || !bytes.Equal(unchanged, secret) {
		t.Fatal("external file was modified")
	}
}
