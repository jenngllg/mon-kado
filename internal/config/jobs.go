package config

import (
	"errors"
	"strconv"
	"time"
)

// JobOptions bounds native worker batches, leases, attempts and retention.
type JobOptions struct {
	BatchSize, ExportBatchSize, ExportMaximumAttempts                                                                     int
	LeaseDuration, RenewalInterval, ExportTimeout, ExportLifetime, PendingGracePeriod, FirstRetryDelay, MaximumRetryDelay time.Duration
	ExportMaximumBytes                                                                                                    int64
}

// Jobs returns configured settings with safe defaults for explicit composition in tests.
func (c Config) Jobs() JobOptions {
	j := c.JobOptions
	if j.BatchSize == 0 {
		j.BatchSize = 100
	}
	if j.ExportBatchSize == 0 {
		j.ExportBatchSize = 5
	}
	if j.ExportMaximumAttempts == 0 {
		j.ExportMaximumAttempts = 5
	}
	if j.LeaseDuration == 0 {
		j.LeaseDuration = 2 * time.Minute
	}
	if j.RenewalInterval == 0 {
		j.RenewalInterval = 30 * time.Second
	}
	if j.ExportTimeout == 0 {
		j.ExportTimeout = 10 * time.Minute
	}
	if j.ExportLifetime == 0 {
		j.ExportLifetime = 24 * time.Hour
	}
	if j.PendingGracePeriod == 0 {
		j.PendingGracePeriod = time.Hour
	}
	if j.FirstRetryDelay == 0 {
		j.FirstRetryDelay = time.Minute
	}
	if j.MaximumRetryDelay == 0 {
		j.MaximumRetryDelay = 24 * time.Hour
	}
	if j.ExportMaximumBytes == 0 {
		j.ExportMaximumBytes = 1024 * 1024 * 1024
	}
	return j
}

func loadJobs() (JobOptions, error) {
	j := Config{}.Jobs()
	for _, entry := range []struct {
		Key    string
		Target *time.Duration
	}{
		{"JOB_LEASE_DURATION", &j.LeaseDuration}, {"JOB_RENEWAL_INTERVAL", &j.RenewalInterval}, {"EXPORT_TIMEOUT", &j.ExportTimeout}, {"EXPORT_LIFETIME", &j.ExportLifetime}, {"PENDING_FILE_GRACE_PERIOD", &j.PendingGracePeriod}, {"OUTBOX_FIRST_RETRY_DELAY", &j.FirstRetryDelay}, {"OUTBOX_MAXIMUM_RETRY_DELAY", &j.MaximumRetryDelay},
	} {
		duration, err := time.ParseDuration(value(entry.Key, entry.Target.String()))
		if err != nil || duration <= 0 {
			return j, errors.New(entry.Key + " must be a positive duration")
		}
		*entry.Target = duration
	}
	for _, entry := range []struct {
		Key    string
		Target *int
	}{{"JOB_BATCH_SIZE", &j.BatchSize}, {"EXPORT_BATCH_SIZE", &j.ExportBatchSize}, {"EXPORT_MAXIMUM_ATTEMPTS", &j.ExportMaximumAttempts}} {
		number, err := strconv.Atoi(value(entry.Key, strconv.Itoa(*entry.Target)))
		if err != nil || number < 1 || number > 1000 {
			return j, errors.New(entry.Key + " must be between 1 and 1000")
		}
		*entry.Target = number
	}
	var err error
	j.ExportMaximumBytes, err = strconv.ParseInt(value("EXPORT_MAXIMUM_BYTES", strconv.FormatInt(j.ExportMaximumBytes, 10)), 10, 64)
	if err != nil || j.ExportMaximumBytes < 1024 || j.ExportMaximumBytes > 1024*1024*1024 {
		return j, errors.New("EXPORT_MAXIMUM_BYTES must be between 1024 and 1073741824")
	}
	if j.RenewalInterval >= j.LeaseDuration || j.FirstRetryDelay > j.MaximumRetryDelay || j.PendingGracePeriod <= j.ExportTimeout {
		return j, errors.New("job renewal must precede lease expiry, retry delays must be ordered, and file grace must exceed export timeout")
	}
	return j, nil
}
