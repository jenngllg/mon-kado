// Command api serves MonKado using the Go standard HTTP server.
package main

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/logging"
	"net"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/buildinfo"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/JennGllg/mon-kado/internal/persistence"
	"github.com/JennGllg/mon-kado/internal/telemetry"
)

func main() {
	if err := run(); err != nil {
		logging.ProcessLogger(logging.NewJSONLogger(os.Stderr, "api", "local")).Error("API stopped", "EventId", logging.APIStopped, "error", err)
		os.Exit(1)
	}
}

func run() error {
	if handled, err := buildinfo.Version(os.Args[1:], os.Stdout); handled {
		return err
	}
	logger := logging.NewJSONLogger(os.Stdout, "api", "local")
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
	api := httpapi.New(application.New(store, cfg, time.Now), logger)
	logger = logging.NewJSONLogger(os.Stdout, "api", cfg.Observability().Version, api.Patterns()...)
	api.Logger = logger
	if options := cfg.Observability(); options.Enabled {
		publisher := telemetry.Publisher{Collector: api.Telemetry, Store: telemetry.FileStore{Directory: filepath.Join(options.Directory, "api")}, Logger: logger}
		defer publisher.Start(ctx, options.Interval)()
	}
	server := &http.Server{Addr: cfg.Address, Handler: api, ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 30 * time.Second, WriteTimeout: 30 * time.Second, IdleTimeout: 60 * time.Second, MaxHeaderBytes: 16 * 1024}
	listener, err := net.Listen("tcp", cfg.Address)
	if err != nil {
		return err
	}
	defer listener.Close()
	done := make(chan error, 1)
	go func() { done <- server.Serve(listener) }()
	logging.ProcessLogger(logger).Info("API listener started", "EventId", logging.APIListenerStarted)
	select {
	case err = <-done:
		if errors.Is(err, http.ErrServerClosed) {
			return nil
		}
		return err
	case <-ctx.Done():
		shutdown, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		return server.Shutdown(shutdown)
	}
}
