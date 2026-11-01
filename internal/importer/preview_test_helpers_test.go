package importer

import (
	"context"
	"encoding/json"
	"errors"
	"net"
	"net/netip"
	"net/url"
	"testing"
)

type previewTestResolver struct{ answers [][]netip.Addr }

func (r *previewTestResolver) LookupNetIP(ctx context.Context, _, _ string) ([]netip.Addr, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	answer := r.answers[0]
	if len(r.answers) > 1 {
		r.answers = r.answers[1:]
	}
	return answer, nil
}

type previewTestPage struct {
	metadata Metadata
	err      error
	calls    int
}

func (p *previewTestPage) CanHandle(*url.URL) bool { return true }
func (p *previewTestPage) Get(context.Context, *url.URL) (Metadata, error) {
	p.calls++
	return p.metadata, p.err
}

type previewTestUnavailableDialer struct{}

func (previewTestUnavailableDialer) DialContext(context.Context, string, string) (net.Conn, error) {
	return nil, errors.New("merchant image unavailable")
}

type previewTestStalledFirstDialer struct{ calls int }

func (d *previewTestStalledFirstDialer) DialContext(ctx context.Context, _, _ string) (net.Conn, error) {
	d.calls++
	if d.calls == 1 {
		<-ctx.Done()
		return nil, ctx.Err()
	}
	return nil, errors.New("merchant image unavailable")
}

type previewTestAlwaysStalledDialer struct{ calls int }

func (d *previewTestAlwaysStalledDialer) DialContext(ctx context.Context, _, _ string) (net.Conn, error) {
	d.calls++
	<-ctx.Done()
	return nil, ctx.Err()
}

type previewTestCancelFallback struct {
	cancel context.CancelFunc
	calls  int
}

func (*previewTestCancelFallback) CanHandle(*url.URL) bool { return true }
func (p *previewTestCancelFallback) Get(context.Context, *url.URL) (Metadata, error) {
	p.calls++
	p.cancel()
	return Metadata{}, context.Canceled
}

func TestPreview_WhenImageRetryStalls_ReservesTimeForPageFallback(t *testing.T) {
	name, price, imageURL := "Gift", json.Number("12.50"), "https://merchant.test/photo"
	primary := &previewTestPage{metadata: Metadata{Name: &name, Price: &price, Images: []string{imageURL}}}
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	fallback := &previewTestCancelFallback{cancel: cancel}
	client := New()
	client.Resolver = &previewTestResolver{answers: [][]netip.Addr{{netip.MustParseAddr("1.1.1.1")}}}
	dialer := &previewTestAlwaysStalledDialer{}
	client.Dialer = dialer
	client.PageSource, client.FallbackSource = primary, fallback
	_, err := client.Preview(ctx, "https://merchant.test/product")
	if !errors.Is(err, context.Canceled) || primary.calls != 1 || fallback.calls != 1 || dialer.calls != 2 {
		t.Fatalf("page fallback did not get a turn: err=%v page calls=%d/%d image attempts=%d", err, primary.calls, fallback.calls, dialer.calls)
	}
}
