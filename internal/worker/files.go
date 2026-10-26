package worker

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/google/uuid"
	"io"
	"os"
	"path/filepath"
	"strings"
)

func (w *Worker) reconcileFiles(ctx context.Context) (resultErr error) {
	w.scanLock.Lock()
	defer w.scanLock.Unlock()
	defer func() {
		if resultErr != nil {
			w.closeFilesLocked()
		}
	}()
	cutoff := w.Service.Now().Add(-w.Service.Config.Jobs().PendingGracePeriod)
	for _, root := range []string{w.Service.Config.ImagePath, w.Service.Config.ExportPath} {
		if w.directories == nil {
			w.directories = map[string]*os.File{}
		}
		directory := w.directories[root]
		var err error
		if directory == nil {
			directory, err = os.Open(root)
		}
		if errors.Is(err, os.ErrNotExist) {
			continue
		}
		if err != nil {
			return err
		}
		w.directories[root] = directory
		info, err := os.Lstat(root)
		if err != nil || !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
			directory.Close()
			return errors.New("storage must be a real directory")
		}
		for scanned := 0; scanned < 1000; scanned += 100 {
			entries, readErr := directory.ReadDir(100)
			if readErr != nil && !errors.Is(readErr, io.EOF) {
				directory.Close()
				return readErr
			}
			for _, entry := range entries {
				if err = ctx.Err(); err != nil {
					directory.Close()
					return err
				}
				name := entry.Name()
				info, err := entry.Info()
				if errors.Is(err, os.ErrNotExist) {
					continue
				}
				if err != nil {
					directory.Close()
					return err
				}
				if !info.Mode().IsRegular() || info.ModTime().After(cutoff) {
					continue
				}
				if strings.HasSuffix(name, ".pending") && root == w.Service.Config.ImagePath {
					id := strings.TrimSuffix(name, ".pending")
					if _, err = uuid.Parse(id); err != nil {
						continue
					}
					var referenced bool
					if err = w.Service.Store.Pool.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM wishes WHERE image_id=$1 UNION ALL SELECT 1 FROM users WHERE profile_image_id=$1 UNION ALL SELECT 1 FROM private_groups WHERE profile_image_id=$1)`, id).Scan(&referenced); err != nil {
						directory.Close()
						return err
					}
					if referenced {
						err = images.Finalize(root, id)
					} else {
						err = images.Remove(root, id)
					}
					if err != nil && !errors.Is(err, os.ErrNotExist) {
						directory.Close()
						return err
					}
				}
				if strings.HasSuffix(name, ".zip") && root == w.Service.Config.ExportPath {
					id := strings.TrimSuffix(name, ".zip")
					if _, err = uuid.Parse(id); err != nil {
						continue
					}
					var referenced bool
					if err = w.Service.Store.Pool.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM member_data_exports WHERE archive_id=$1)`, id).Scan(&referenced); err != nil {
						directory.Close()
						return err
					}
					if !referenced {
						err = os.Remove(filepath.Join(root, name))
					}
					if err != nil && !errors.Is(err, os.ErrNotExist) {
						directory.Close()
						return err
					}
				}
				if strings.HasSuffix(name, ".tmp") {
					if err = os.Remove(filepath.Join(root, name)); err != nil && !errors.Is(err, os.ErrNotExist) {
						directory.Close()
						return err
					}
				}
				if root == w.Service.Config.ExportPath && (strings.HasPrefix(name, "manifest-") || strings.HasSuffix(name, ".pending")) {
					if err = os.Remove(filepath.Join(root, name)); err != nil && !errors.Is(err, os.ErrNotExist) {
						directory.Close()
						delete(w.directories, root)
						return err
					}
				}
			}
			if errors.Is(readErr, io.EOF) {
				directory.Close()
				delete(w.directories, root)
				break
			}
		}
	}
	return nil
}
