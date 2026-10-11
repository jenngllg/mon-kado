// Command migrate applies the reviewed embedded PostgreSQL migrations separately from API startup.
package main

import (
	"context"
	"github.com/JennGllg/mon-kado/internal/logging"
	"log/slog"
	"os"
	"os/signal"
	"syscall"

	"github.com/JennGllg/mon-kado/internal/persistence"
)

func main() {
	logger := logging.ProcessLogger(slog.New(slog.NewJSONHandler(os.Stdout, nil)))
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
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
