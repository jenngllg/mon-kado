package importer

import (
	"context"
	"crypto/tls"
	"errors"
	"net"
	"net/netip"
	"net/url"
	"slices"
	"strings"
	"sync/atomic"
	"testing"
)

type relayHello struct {
	serverName string
	protocols  []string
	versions   []uint16
}

type relayProtocolDialer struct {
	hello   chan relayHello
	closed  chan struct{}
	context context.Context
	calls   *atomic.Int32
}

func (d relayProtocolDialer) DialContext(ctx context.Context, network, address string) (net.Conn, error) {
	if network != "tcp" || address != "8.8.8.8:443" || ctx.Err() != nil {
		return nil, errors.New("unvalidated relay destination")
	}
	d.calls.Add(1)
	client, server := net.Pipe()
	go func() {
		defer close(d.closed)
		defer server.Close()
		tlsServer := tls.Server(server, &tls.Config{MinVersion: tls.VersionTLS12, GetConfigForClient: func(hello *tls.ClientHelloInfo) (*tls.Config, error) {
			d.hello <- relayHello{hello.ServerName, slices.Clone(hello.SupportedProtos), slices.Clone(hello.SupportedVersions)}
			return nil, errors.New("synthetic handshake inspection complete")
		}})
		_ = tlsServer.HandshakeContext(d.context)
	}()
	return client, nil
}

func TestCloudflare_WhenPermanentRelayConnects_UsesHTTP1WhilePreservingTLSAndPinnedAddress(t *testing.T) {
	for _, relay := range []bool{true, false} {
		t.Run(map[bool]string{true: "relay", false: "ordinary connector"}[relay], func(t *testing.T) {
			// Arrange
			client := New()
			client.Resolver = managedResolver(func(context.Context, string, string) ([]netip.Addr, error) {
				return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, nil
			})
			probe := relayProtocolDialer{make(chan relayHello, 1), make(chan struct{}), t.Context(), &atomic.Int32{}}
			client.Dialer = probe
			target, _ := url.Parse("https://merchant.fr/product")
			// Act
			var err error
			if relay {
				_, err = NewCloudflare(client, "https://monkado-wish-import.account.workers.dev", strings.Repeat("a", 64)).Get(t.Context(), target)
			} else {
				_, err = client.Preview(t.Context(), target.String())
			}
			// Assert
			if err == nil && relay {
				t.Fatal("synthetic TLS refusal was ignored")
			}
			if probe.calls.Load() != 1 {
				t.Fatal("expected exactly one pinned TLS connection")
			}
			<-probe.closed
			hello := <-probe.hello
			wantServer := "merchant.fr"
			if relay {
				wantServer = "monkado-wish-import.account.workers.dev"
			}
			if hello.serverName != wantServer || slices.Contains(hello.protocols, "h2") == relay {
				t.Fatalf("relay protocol or TLS authority changed: %s %v", hello.serverName, hello.protocols)
			}
			for _, version := range hello.versions {
				if version < tls.VersionTLS12 {
					t.Fatal("TLS minimum weakened")
				}
			}
		})
	}
}
