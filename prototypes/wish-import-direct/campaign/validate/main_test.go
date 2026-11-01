package main

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"image"
	"image/png"
	"io"
	"net"
	"net/http"
	"net/netip"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

type resolver struct{}

func (resolver) LookupNetIP(context.Context, string, string) ([]netip.Addr, error) {
	return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, nil
}

type dialer func(context.Context, string, string) (net.Conn, error)

func (d dialer) DialContext(ctx context.Context, network, address string) (net.Conn, error) {
	return d(ctx, network, address)
}

func TestValidate_WhenPublicImageIsDecodable_ReportsDecoded(t *testing.T) {
	// Arrange
	var data bytes.Buffer
	if err := png.Encode(&data, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	client := importer.New()
	client.Resolver = resolver{}
	calls := 0
	client.Dialer = dialer(func(ctx context.Context, network, address string) (net.Conn, error) {
		calls++
		if ctx.Err() != nil || network != "tcp" || address != "8.8.8.8:80" {
			t.Error("context or validated address changed")
		}
		local, remote := net.Pipe()
		go func() {
			defer remote.Close()
			if _, err := http.ReadRequest(bufio.NewReader(remote)); err != nil {
				return
			}
			response := http.Response{StatusCode: 200, Header: http.Header{"Content-Type": []string{"image/png"}}, Body: io.NopCloser(bytes.NewReader(data.Bytes())), ContentLength: int64(data.Len()), Close: true}
			_ = response.Write(remote)
		}()
		return local, nil
	})
	// Act
	result := validate(t.Context(), client, sample{ID: 1, ImageURL: "http://merchant.example/image"})
	// Assert
	if !result.Decoded || result.Category != "decoded" || calls != 1 {
		t.Fatalf("unexpected image result: %#v, calls %d", result, calls)
	}
}

func TestRun_WhenCandidatesAbsentOrPrivate_RecordsSafeOutcomes(t *testing.T) {
	// Arrange
	dir := t.TempDir()
	input, output := filepath.Join(dir, "input.ndjson"), filepath.Join(dir, "output.ndjson")
	if err := os.WriteFile(input, []byte("{\"id\":1}\n{\"id\":2,\"imageURL\":\"http://127.0.0.1/image\"}\n"), 0600); err != nil {
		t.Fatal(err)
	}
	// Act
	err := run(t.Context(), input, output)
	// Assert
	if err != nil {
		t.Fatal(err)
	}
	data, err := os.ReadFile(output)
	if err != nil {
		t.Fatal(err)
	}
	got := map[int]outcome{}
	for _, line := range strings.Split(strings.TrimSpace(string(data)), "\n") {
		var row outcome
		if err := json.Unmarshal([]byte(line), &row); err != nil {
			t.Fatal(err)
		}
		got[row.ID] = row
	}
	if len(got) != 2 || got[1].Category != "no_candidate" || got[2].Category != "download_unavailable" || got[1].Decoded || got[2].Decoded {
		t.Fatalf("unexpected outcomes: %#v", got)
	}
	if err := run(t.Context(), input, output); err == nil {
		t.Fatal("existing evidence was overwritten")
	}
}
