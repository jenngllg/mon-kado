package telemetry

import (
	"context"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
)

const maximumSnapshotBytes = 65536

// FileStore publishes durable private snapshots in a dedicated service directory.
type FileStore struct{ Directory string }

func rejectLink(path string) error {
	info, err := os.Lstat(path)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil {
		return err
	}
	if info.Mode()&os.ModeSymlink != 0 {
		return errors.New("telemetry paths must not be symbolic links")
	}
	return nil
}

// Write replaces a snapshot atomically, leaving previous evidence intact on failure.
func (s FileStore) Write(ctx context.Context, snapshot Snapshot) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	content, err := json.Marshal(snapshot)
	if err != nil {
		return err
	}
	if len(content) > maximumSnapshotBytes {
		return errors.New("telemetry snapshot exceeds the size limit")
	}
	if err = os.MkdirAll(s.Directory, 0700); err != nil {
		return err
	}
	destination := filepath.Join(s.Directory, "snapshot.json")
	if err = rejectLink(s.Directory); err != nil {
		return err
	}
	if err = rejectLink(destination); err != nil {
		return err
	}
	if err = os.Chmod(s.Directory, 0700); err != nil {
		return err
	}
	file, err := os.CreateTemp(s.Directory, "snapshot-*.new")
	if err != nil {
		return err
	}
	defer os.Remove(file.Name())
	if err = file.Chmod(0600); err == nil {
		_, err = file.Write(content)
	}
	if err == nil {
		err = file.Sync()
	}
	closeErr := file.Close()
	if err != nil {
		return err
	}
	if closeErr != nil {
		return closeErr
	}
	if err = ctx.Err(); err != nil {
		return err
	}
	return os.Rename(file.Name(), destination)
}
