package domain

import "time"

// CreatePushSubscriptionRequest explicitly activates a browser device.
type CreatePushSubscriptionRequest struct {
	Endpoint      string   `json:"endpoint"`
	Name          string   `json:"name"`
	DisabledKinds []string `json:"disabledKinds"`
}

// UpdatePushSubscriptionRequest saves a caller-owned device's complete notification choices.
type UpdatePushSubscriptionRequest struct {
	Name          string   `json:"name"`
	DisabledKinds []string `json:"disabledKinds"`
}

// PushSubscriptionResponse identifies a private device without its capability URL.
type PushSubscriptionResponse struct {
	Id            string    `json:"id"`
	Name          string    `json:"name"`
	EndpointHash  string    `json:"endpointHash"`
	DisabledKinds []string  `json:"disabledKinds"`
	CreatedAt     time.Time `json:"createdAt"`
	Version       string    `json:"version"`
}

// PushConfigurationResponse describes voluntary push availability.
type PushConfigurationResponse struct {
	Enabled   bool    `json:"enabled"`
	PublicKey *string `json:"publicKey"`
}
