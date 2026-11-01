package importer_test

import (
	"context"
	"encoding/pem"
	"errors"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"net/netip"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"sync/atomic"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestDownload_WhenMerchantUsesHTTPS_NegotiatesHTTP2AndSupportsHTTP1(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("The isolated trust store uses the native Linux SSL_CERT_FILE contract.")
	}
	if destination := os.Getenv("MONKADO_IMPORT_TEST_TLS_DESTINATION"); destination != "" {
		// Arrange: only this child process trusts the local merchant fixture's certificate.
		client := importer.New()
		client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
		var calls atomic.Int32
		client.Dialer = dialerFunc(func(ctx context.Context, network, address string) (net.Conn, error) {
			calls.Add(1)
			if network != "tcp" || address != "8.8.8.8:443" {
				return nil, errors.New("HTTPS destination was not pinned to the validated public address")
			}
			return (&net.Dialer{}).DialContext(ctx, network, destination)
		})
		// Act
		body, final, media, err := client.Download(t.Context(), "https://example.com/product?variant=blue", 100)
		// Assert: Host, path and SNI are checked by the server and verified TLS handshake.
		if err != nil || calls.Load() != 1 || string(body) != os.Getenv("MONKADO_IMPORT_TEST_PROTOCOL") || final == nil || final.String() != "https://example.com/product?variant=blue" || media != "text/plain" {
			t.Fatalf("HTTPS protocol or authority contract differs: protocol %q, calls %d, error %v", body, calls.Load(), err)
		}
		return
	}
	for _, test := range []struct {
		name, protocol string
		http2          bool
	}{{"HTTP2", "HTTP/2.0", true}, {"HTTP1 fallback", "HTTP/1.1", false}} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange: a real local TLS peer, with no Internet or process-wide trust changes.
			server := httptest.NewUnstartedServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				if r.Host != "example.com" || r.URL.RequestURI() != "/product?variant=blue" || r.TLS == nil || r.TLS.ServerName != "example.com" {
					http.Error(w, "merchant authority differs", http.StatusBadRequest)
					return
				}
				w.Header().Set("Content-Type", "text/plain")
				_, _ = io.WriteString(w, r.Proto)
			}))
			server.EnableHTTP2 = test.http2
			server.StartTLS()
			t.Cleanup(server.Close)
			trust := filepath.Join(t.TempDir(), "merchant.pem")
			if err := os.WriteFile(trust, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: server.Certificate().Raw}), 0o600); err != nil {
				t.Fatal(err)
			}
			binary, err := os.Executable()
			if err != nil {
				t.Fatal(err)
			}
			child := exec.CommandContext(t.Context(), binary, "-test.run=^TestDownload_WhenMerchantUsesHTTPS_NegotiatesHTTP2AndSupportsHTTP1$")
			child.Env = append(os.Environ(), "SSL_CERT_FILE="+trust, "SSL_CERT_DIR="+t.TempDir(),
				"MONKADO_IMPORT_TEST_TLS_DESTINATION="+server.Listener.Addr().String(),
				"MONKADO_IMPORT_TEST_PROTOCOL="+test.protocol, "HTTPS_PROXY=http://127.0.0.1:1")
			// Act / Assert
			if output, err := child.CombinedOutput(); err != nil {
				t.Fatalf("native TLS merchant fixture failed: %v\n%s", err, strings.TrimSpace(string(output)))
			}
		})
	}
}
