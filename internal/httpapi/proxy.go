package httpapi

import (
	"net/http"
	"net/netip"
	"strings"
)

func (a *API) trustedProxy(address netip.Addr) bool {
	for _, prefix := range a.Service.Config.TrustedProxies {
		if prefix.Contains(address.Unmap()) {
			return true
		}
	}
	return false
}

func (a *API) requestScheme(r *http.Request) string {
	if r.TLS != nil || a.Service.Config.Production {
		return "https"
	}
	// TLS may terminate at the proxy, but clients cannot assert their own scheme.
	peer, err := netip.ParseAddrPort(r.RemoteAddr)
	if err == nil && a.trustedProxy(peer.Addr()) && r.Header.Get("X-Forwarded-Proto") == "https" {
		return "https"
	}
	return "http"
}

func (a *API) clientAddress(r *http.Request) string {
	peer, err := netip.ParseAddrPort(r.RemoteAddr)
	if err != nil {
		return r.RemoteAddr
	}
	address := peer.Addr().Unmap()
	if !a.trustedProxy(address) {
		return address.String()
	}
	chain := strings.Split(r.Header.Get("X-Forwarded-For"), ",")
	if len(chain) > 16 {
		return address.String()
	}
	for i := len(chain) - 1; i >= 0; i-- {
		candidate, parseErr := netip.ParseAddr(strings.TrimSpace(chain[i]))
		if parseErr != nil {
			return address.String()
		}
		address = candidate.Unmap()
		if !a.trustedProxy(address) {
			break
		}
	}
	return address.String()
}
