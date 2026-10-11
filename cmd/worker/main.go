// Command worker executes MonKado background processing without a container runtime.
package main

import (
	"context"
	"github.com/JennGllg/mon-kado/internal/logging"
	"log/slog"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/persistence"
	"github.com/JennGllg/mon-kado/internal/providers"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func main() {
	if err := run(); err != nil {
		logging.ProcessLogger(slog.New(slog.NewJSONHandler(os.Stderr, nil))).Error("Worker stopped", "EventId", logging.WorkerStopped, "error", err)
		os.Exit(1)
	}
}
func run() error {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	cfg, err := config.Load()
	if err != nil {
		return err
	}
	store, err := persistence.Open(ctx, cfg.DatabaseURL)
	if err != nil {
		return err
	}
	defer store.Close()
	var sender application.EmailSender
	if cfg.EmailProvider == "Mailbox" {
		sender = &providers.Mailbox{Path: cfg.MailboxPath}
	}
	if cfg.EmailProvider == "Gmail" {
		sender, err = providers.NewGmail(cfg, time.Now)
		if err != nil {
			return err
		}
	}
	logger := slog.New(slog.NewJSONHandler(os.Stdout, nil))
	w := &worker.Worker{Service: application.New(store, cfg, time.Now), Sender: sender, Logger: logger}
	return w.Run(ctx)
}
