package main

import (
	"bytes"
	"context"
	"errors"
	"io"
	"strings"
	"testing"
)

type brokerFake struct {
	Calls   int
	Err     error
	Context context.Context
}

func (b *brokerFake) Authorize(ctx context.Context, id, secret string, out io.Writer) (string, error) {
	b.Calls++
	b.Context = ctx
	return "test-refresh-token", b.Err
}

func TestRun_WhenCredentialsOrAuthorizationDiffer_ReturnsExpectedExitCode(t *testing.T) {
	for _, test := range []struct {
		Name        string
		Credentials bool
		Err         error
		Code, Calls int
	}{{"missing", false, nil, 2, 0}, {"failure", true, errors.New("private provider error"), 1, 1}, {"success", true, nil, 0, 1}} {
		t.Run(test.Name, func(t *testing.T) {
			// Arrange
			broker := &brokerFake{Err: test.Err}
			var output bytes.Buffer
			getenv := func(string) string {
				if test.Credentials {
					return "test-credential"
				}
				return ""
			}
			// Act
			code := run(t.Context(), getenv, &output, broker)
			// Assert
			if code != test.Code || broker.Calls != test.Calls || strings.Contains(output.String(), "private provider error") {
				t.Fatal("bootstrap exit or secret handling differs")
			}
			if test.Calls == 1 && broker.Context != t.Context() {
				t.Fatal("cancellation context substituted")
			}
			if test.Code == 0 && strings.Count(output.String(), "test-refresh-token") != 1 {
				t.Fatal("refresh token not displayed exactly once")
			}
		})
	}
}
