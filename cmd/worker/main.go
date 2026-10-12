// Command worker executes MonKado background processing without a container runtime.
package main

import (
	"context"
	"github.com/JennGllg/mon-kado/internal/logging"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/buildinfo"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/persistence"
	"github.com/JennGllg/mon-kado/internal/providers"
	"github.com/JennGllg/mon-kado/internal/telemetry"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func main() {
	if err := run(); err != nil {
		logging.ProcessLogger(logging.NewJSONLogger(os.Stderr, "worker", "local")).Error("Worker stopped", "EventId", logging.WorkerStopped, "error", err)
		os.Exit(1)
	}
}
func run() error {
	if handled, err := buildinfo.Version(os.Args[1:], os.Stdout); handled {
		return err
	}
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
	logger := logging.NewJSONLogger(os.Stdout, "worker", cfg.Observability().Version)
	collector := telemetry.New("worker", time.Now, time.Now)
	w := &worker.Worker{Service: application.New(store, cfg, time.Now), Sender: sender, Logger: logger, Telemetry: collector}
	if options := cfg.Observability(); options.Enabled {
		publisher := telemetry.Publisher{Collector: collector, Store: telemetry.FileStore{Directory: filepath.Join(options.Directory, "worker")}, Logger: logger}
		defer publisher.Start(ctx, options.Interval)()
	}
	return w.Run(ctx)
}
