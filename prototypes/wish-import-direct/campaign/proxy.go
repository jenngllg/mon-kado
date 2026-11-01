// Command campaign is a local, public-only network guard for manual browser experiments.
package main

import (
	"bufio"
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"os"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

const tunnelBytes = 10 * 1024 * 1024

func dialPublic(ctx context.Context, network, address string) (net.Conn, error) {
	host, port, err := net.SplitHostPort(address)
	if err != nil || (port != "80" && port != "443") || host == "" || strings.Contains(host, "%") {
		return nil, errors.New("invalid destination")
	}
	addresses, err := net.DefaultResolver.LookupNetIP(ctx, "ip", host)
	if err != nil || len(addresses) == 0 {
		return nil, errors.New("unresolved destination")
	}
	for _, ip := range addresses {
		if !importer.PublicAddress(ip.Unmap()) {
			return nil, errors.New("non-public destination")
		}
	}
	var last error
	for _, ip := range addresses {
		var c net.Conn
		c, last = (&net.Dialer{Timeout: 5 * time.Second}).DialContext(ctx, network, net.JoinHostPort(ip.Unmap().String(), port))
		if last == nil {
			return c, nil
		}
		if ctx.Err() != nil {
			return nil, ctx.Err()
		}
	}
	return nil, last
}

func main() {
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		fmt.Fprintln(os.Stderr, "Cannot start the local network guard")
		os.Exit(1)
	}
	fmt.Println(listener.Addr().(*net.TCPAddr).Port)
	transport := &http.Transport{Proxy: nil, DisableKeepAlives: true, DialContext: dialPublic, ResponseHeaderTimeout: 10 * time.Second}
	defer transport.CloseIdleConnections()
	permits := make(chan struct{}, 32)
	server := &http.Server{ReadHeaderTimeout: 5 * time.Second, Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		select {
		case permits <- struct{}{}:
			defer func() { <-permits }()
		case <-r.Context().Done():
			return
		}
		if r.Method == http.MethodConnect {
			host, port, splitErr := net.SplitHostPort(r.Host)
			if splitErr != nil || port != "443" || host == "" {
				http.Error(w, "Invalid destination", http.StatusForbidden)
				return
			}
			upstream, dialErr := dialPublic(r.Context(), "tcp", r.Host)
			if dialErr != nil {
				http.Error(w, "Destination unavailable", http.StatusBadGateway)
				return
			}
			hijacker, ok := w.(http.Hijacker)
			if !ok {
				upstream.Close()
				return
			}
			client, buffered, hijackErr := hijacker.Hijack()
			if hijackErr != nil {
				upstream.Close()
				return
			}
			defer client.Close()
			defer upstream.Close()
			deadline := time.Now().Add(25 * time.Second)
			_ = client.SetDeadline(deadline)
			_ = upstream.SetDeadline(deadline)
			if _, writeErr := client.Write([]byte("HTTP/1.1 200 Connection Established\r\n\r\n")); writeErr != nil {
				return
			}
			done := make(chan struct{}, 1)
			go func(reader *bufio.Reader) {
				_, _ = io.Copy(upstream, io.LimitReader(reader, tunnelBytes))
				_ = upstream.Close()
				done <- struct{}{}
			}(buffered.Reader)
			_, _ = io.Copy(client, io.LimitReader(upstream, tunnelBytes))
			_ = client.Close()
			<-done
			return
		}
		if (r.Method != http.MethodGet && r.Method != http.MethodHead) || r.URL.Scheme != "http" || r.URL.User != nil || r.URL.Hostname() == "" || r.URL.Port() != "" && r.URL.Port() != "80" {
			http.Error(w, "Invalid destination", http.StatusForbidden)
			return
		}
		r.RequestURI = ""
		r.Header.Del("Proxy-Connection")
		response, requestErr := transport.RoundTrip(r)
		if requestErr != nil {
			http.Error(w, "Destination unavailable", http.StatusBadGateway)
			return
		}
		defer response.Body.Close()
		for name, values := range response.Header {
			for _, value := range values {
				w.Header().Add(name, value)
			}
		}
		w.WriteHeader(response.StatusCode)
		_, _ = io.Copy(w, io.LimitReader(response.Body, tunnelBytes))
	})}
	if err = server.Serve(listener); err != nil && !errors.Is(err, http.ErrServerClosed) {
		fmt.Fprintln(os.Stderr, "Local network guard stopped")
	}
}
