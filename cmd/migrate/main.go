// Command migrate applies the reviewed embedded PostgreSQL migrations separately from API startup.
package main

import (
	"context"
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/logging"
	"io"
	"os"
	"os/signal"
	"syscall"

	"github.com/JennGllg/mon-kado/internal/buildinfo"
	"github.com/JennGllg/mon-kado/internal/persistence"
)

func main() {
	logger := logging.ProcessLogger(logging.NewJSONLogger(os.Stdout, "migrate", os.Getenv("APP_VERSION")))
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if handled, err := metadata(os.Args[1:], os.Stdout); handled {
		if err != nil {
			logger.Error("Migration metadata failed", "EventId", logging.MigrationFailed)
			os.Exit(1)
		}
		return
	}
	store, err := persistence.Open(ctx, os.Getenv("DATABASE_URL"))
	if err == nil {
		defer store.Close()
		err = store.Migrate(ctx)
	}
	if err != nil {
		logger.Error("Migration failed", "EventId", logging.MigrationFailed, "error", err)
		os.Exit(1)
	}
	logger.Info("Migrations applied", "EventId", logging.MigrationsApplied)
}

func metadata(arguments []string, output io.Writer) (bool, error) {
	if len(arguments) == 1 && arguments[0] == "--catalog" {
		catalog, err := persistence.Catalog()
		if err != nil {
			return true, err
		}
		return true, json.NewEncoder(output).Encode(catalog)
	}
	return buildinfo.Version(arguments, output)
}
