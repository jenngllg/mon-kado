package config

import (
	"crypto/ecdh"
	"encoding/base64"
	"errors"
	"net/url"
	"os"
	"strconv"
)

// PushOptions holds VAPID credentials shared by API and worker.
type PushOptions struct {
	Enabled                        bool
	PublicKey, PrivateKey, Subject string
}

func loadPush() (PushOptions, error) {
	p := PushOptions{PublicKey: os.Getenv("PUSH_VAPID_PUBLIC_KEY"), PrivateKey: os.Getenv("PUSH_VAPID_PRIVATE_KEY"), Subject: os.Getenv("PUSH_VAPID_SUBJECT")}
	var err error
	p.Enabled, err = strconv.ParseBool(value("PUSH_ENABLED", "false"))
	if err != nil {
		return p, errors.New("PUSH_ENABLED must be a boolean")
	}
	if !p.Enabled {
		return p, nil
	}
	raw, err := base64.RawURLEncoding.DecodeString(p.PrivateKey)
	if err != nil {
		return p, errors.New("PUSH_VAPID_PRIVATE_KEY must be a base64url P-256 private key")
	}
	key, err := ecdh.P256().NewPrivateKey(raw)
	if err != nil || base64.RawURLEncoding.EncodeToString(key.PublicKey().Bytes()) != p.PublicKey {
		return p, errors.New("PUSH VAPID keys must be a matching P-256 pair")
	}
	u, err := url.Parse(p.Subject)
	if err != nil || (u.Scheme != "mailto" && u.Scheme != "https") || (u.Scheme == "mailto" && u.Opaque == "") || (u.Scheme == "https" && (u.Hostname() == "" || u.User != nil)) {
		return p, errors.New("PUSH_VAPID_SUBJECT must be a contact mailto or HTTPS URL")
	}
	return p, nil
}
