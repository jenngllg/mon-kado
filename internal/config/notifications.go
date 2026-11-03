package config

import (
	"errors"
	"strconv"
	"time"
)

// NotificationOptions bounds live connections, transport deadlines and reminder grouping.
type NotificationOptions struct {
	MaximumStreams                                                                        int
	HeartbeatInterval, WriteTimeout, ReconnectDelay, GroupingWindow, SharingReminderDelay time.Duration
}

// Notifications supplies safe defaults for explicit service composition as well as runtime configuration.
func (c Config) Notifications() NotificationOptions {
	n := c.NotificationOptions
	if n.MaximumStreams == 0 {
		n.MaximumStreams = 256
	}
	if n.HeartbeatInterval == 0 {
		n.HeartbeatInterval = 20 * time.Second
	}
	if n.WriteTimeout == 0 {
		n.WriteTimeout = 5 * time.Second
	}
	if n.ReconnectDelay == 0 {
		n.ReconnectDelay = time.Second
	}
	if n.GroupingWindow == 0 {
		n.GroupingWindow = 5 * time.Minute
	}
	if n.SharingReminderDelay == 0 {
		n.SharingReminderDelay = 48 * time.Hour
	}
	return n
}

func loadNotifications() (NotificationOptions, error) {
	n := Config{}.Notifications()
	var err error
	n.MaximumStreams, err = strconv.Atoi(value("NOTIFICATION_MAXIMUM_STREAMS", strconv.Itoa(n.MaximumStreams)))
	if err != nil || n.MaximumStreams < 1 || n.MaximumStreams > 4096 {
		return n, errors.New("NOTIFICATION_MAXIMUM_STREAMS must be between 1 and 4096")
	}
	for _, setting := range []struct {
		key      string
		target   *time.Duration
		min, max time.Duration
	}{
		{"NOTIFICATION_HEARTBEAT_INTERVAL", &n.HeartbeatInterval, time.Second, time.Minute},
		{"NOTIFICATION_WRITE_TIMEOUT", &n.WriteTimeout, time.Second, 30 * time.Second},
		{"NOTIFICATION_RECONNECT_DELAY", &n.ReconnectDelay, 100 * time.Millisecond, 30 * time.Second},
		{"NOTIFICATION_GROUPING_WINDOW", &n.GroupingWindow, time.Second, time.Hour},
		{"NOTIFICATION_SHARING_REMINDER_DELAY", &n.SharingReminderDelay, time.Minute, 30 * 24 * time.Hour},
	} {
		duration, err := time.ParseDuration(value(setting.key, setting.target.String()))
		if err != nil || duration < setting.min || duration > setting.max {
			return n, errors.New(setting.key + " is outside its allowed duration range")
		}
		*setting.target = duration
	}
	return n, nil
}
