package images

import (
	"context"
	"errors"
	"os"
	"path/filepath"

	"github.com/google/uuid"
)

// WritePending stages an exclusive image and marker before the database references it.
func WritePending(ctx context.Context, root, id string, content []byte) error {
	if _, err := uuid.Parse(id); err != nil {
		return errors.New("invalid image identifier")
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	if err := os.MkdirAll(root, 0700); err != nil {
		return err
	}
	info, err := os.Lstat(root)
	if err != nil || !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return errors.New("image storage must be a real directory")
	}
	marker := filepath.Join(root, id+".pending")
	file, err := os.OpenFile(marker, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return err
	}
	if err = file.Close(); err != nil {
		return err
	}
	file, err = os.OpenFile(filepath.Join(root, id+".webp"), os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		os.Remove(marker)
		return err
	}
	_, writeErr := file.Write(content)
	if writeErr == nil {
		writeErr = file.Sync()
	}
	closeErr := file.Close()
	if writeErr != nil || closeErr != nil {
		Remove(root, id)
		if writeErr != nil {
			return writeErr
		}
		return closeErr
	}
	return ctx.Err()
}

// Finalize removes a staging marker after the transaction commits.
func Finalize(root, id string) error {
	if _, err := uuid.Parse(id); err != nil {
		return errors.New("invalid image identifier")
	}
	return os.Remove(filepath.Join(root, id+".pending"))
}

// Remove deletes both image material and its staging marker.
func Remove(root, id string) error {
	if _, err := uuid.Parse(id); err != nil {
		return errors.New("invalid image identifier")
	}
	for _, extension := range []string{".webp", ".pending"} {
		if err := os.Remove(filepath.Join(root, id+extension)); err != nil && !errors.Is(err, os.ErrNotExist) {
			return err
		}
	}
	return nil
}

// Read returns only a regular normalized image file and respects caller cancellation.
func Read(ctx context.Context, root, id string) ([]byte, error) {
	if _, err := uuid.Parse(id); err != nil {
		return nil, os.ErrNotExist
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	path := filepath.Join(root, id+".webp")
	info, err := os.Lstat(path)
	if err != nil {
		return nil, err
	}
	if !info.Mode().IsRegular() || info.Size() > MaximumInputBytes {
		return nil, os.ErrNotExist
	}
	return os.ReadFile(path)
}
