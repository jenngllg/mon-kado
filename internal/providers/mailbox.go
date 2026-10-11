package providers

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/google/uuid"
	"os"
	"path/filepath"
)

// Mailbox is a local-only, file-based substitute for network email delivery.
type Mailbox struct{ Path string }

// Send writes one private JSON message per outbox identity for local development.
func (m *Mailbox) Send(ctx context.Context, id string, message application.EmailMessage) (string, error) {
	if err := ctx.Err(); err != nil {
		return "", err
	}
	parsed, err := uuid.Parse(id)
	if err != nil || parsed == uuid.Nil {
		return "", &application.DeliveryError{Category: "invalidMessage", Retryable: false}
	}
	if err = os.MkdirAll(m.Path, 0700); err != nil {
		return "", err
	}
	info, err := os.Lstat(m.Path)
	if err != nil || !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return "", errors.New("local mailbox must be a real directory")
	}
	data, err := json.MarshalIndent(message, "", "  ")
	if err != nil {
		return "", err
	}
	path := filepath.Join(m.Path, parsed.String()+".json")
	_, err = os.Lstat(path)
	if err == nil {
		info, err := os.Lstat(path)
		if err != nil || !info.Mode().IsRegular() || info.Size() > 1<<20 {
			return "", errors.New("local mailbox message is invalid")
		}
		existing, err := os.ReadFile(path)
		if err != nil {
			return "", err
		}
		if !bytes.Equal(existing, data) {
			return "", errors.New("local mailbox message conflicts with its outbox identity")
		}
		return "mailbox-" + parsed.String(), nil
	}
	if !errors.Is(err, os.ErrNotExist) {
		return "", err
	}
	file, err := os.CreateTemp(m.Path, parsed.String()+"-*.pending")
	if err != nil {
		return "", err
	}
	defer os.Remove(file.Name())
	_, writeErr := file.Write(data)
	if writeErr == nil {
		writeErr = file.Sync()
	}
	closeErr := file.Close()
	if writeErr != nil || closeErr != nil {
		return "", errors.Join(writeErr, closeErr)
	}
	if err = os.Link(file.Name(), path); err != nil {
		if errors.Is(err, os.ErrExist) {
			return m.Send(ctx, id, message)
		}
		return "", err
	}
	return "mailbox-" + parsed.String(), ctx.Err()
}
