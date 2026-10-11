// Package persistence implements transactional PostgreSQL access and migrations.
package persistence

import (
	"context"
	"embed"
	"fmt"
	"sort"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

//go:embed migrations/*.sql
var migrations embed.FS

// Store owns the PostgreSQL pool. Each application operation owns its transaction.
type Store struct{ Pool *pgxpool.Pool }

// Open configures a bounded pool and verifies connectivity using the caller context.
func Open(ctx context.Context, connection string) (*Store, error) {
	c, err := pgxpool.ParseConfig(connection)
	if err != nil {
		return nil, fmt.Errorf("DATABASE_URL is invalid")
	}
	c.MaxConns = 10
	c.ConnConfig.ConnectTimeout = 5 * time.Second
	c.MinConns = 0
	c.MaxConnIdleTime = 5 * time.Minute
	c.ConnConfig.RuntimeParams["timezone"] = "UTC"
	p, err := pgxpool.NewWithConfig(ctx, c)
	if err != nil {
		return nil, err
	}
	if err = p.Ping(ctx); err != nil {
		p.Close()
		return nil, fmt.Errorf("PostgreSQL is unavailable")
	}
	return &Store{Pool: p}, nil
}

// Close releases the pool after HTTP and workers have stopped.
func (s *Store) Close() { s.Pool.Close() }

// Transaction atomically applies an application operation with cancellation.
func (s *Store) Transaction(ctx context.Context, fn func(pgx.Tx) error) error {
	tx, err := s.Pool.Begin(ctx)
	if err != nil {
		return err
	}
	defer tx.Rollback(ctx)
	if err = fn(tx); err != nil {
		return err
	}
	return tx.Commit(ctx)
}

// Migrate applies embedded SQL under a database-wide advisory lock. API startup does not call it.
func (s *Store) Migrate(ctx context.Context) error {
	conn, err := s.Pool.Acquire(ctx)
	if err != nil {
		return err
	}
	defer conn.Release()
	if _, err = conn.Exec(ctx, "SELECT pg_advisory_lock(771206481)"); err != nil {
		return err
	}
	defer conn.Exec(context.WithoutCancel(ctx), "SELECT pg_advisory_unlock(771206481)")
	if _, err = conn.Exec(ctx, `CREATE TABLE IF NOT EXISTS mon_kado_migrations (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())`); err != nil {
		return err
	}
	files, err := migrations.ReadDir("migrations")
	if err != nil {
		return err
	}
	sort.Slice(files, func(i, j int) bool { return files[i].Name() < files[j].Name() })
	for _, file := range files {
		var applied bool
		if err = conn.QueryRow(ctx, "SELECT EXISTS(SELECT 1 FROM mon_kado_migrations WHERE name=$1)", file.Name()).Scan(&applied); err != nil {
			return err
		}
		if applied {
			continue
		}
		sql, err := migrations.ReadFile("migrations/" + file.Name())
		if err != nil {
			return err
		}
		if err = applyMigration(ctx, conn, file.Name(), sql); err != nil {
			return err
		}
	}
	return nil
}

func applyMigration(ctx context.Context, conn *pgxpool.Conn, name string, sql []byte) error {
	tx, err := conn.Begin(ctx)
	if err != nil {
		return err
	}
	defer tx.Rollback(ctx)
	if _, err = tx.Exec(ctx, string(sql)); err != nil {
		return fmt.Errorf("migration %s: %w", name, err)
	}
	if _, err = tx.Exec(ctx, "INSERT INTO mon_kado_migrations(name) VALUES($1)", name); err != nil {
		return err
	}
	return tx.Commit(ctx)
}
