package buildinfo_test

import (
	"bytes"
	"encoding/json"
	"errors"
	"runtime"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/buildinfo"
)

type failingWriter struct{}

func (failingWriter) Write([]byte) (int, error) { return 0, errors.New("write unavailable") }

func TestWrite_WhenBinaryIdentityRequested_EmitsValidatedPublicMetadataOnly(t *testing.T) {
	for _, revision := range []string{"local", strings.Repeat("a", 40), "private-invalid-revision"} {
		t.Run(revision, func(t *testing.T) {
			// Arrange
			previous := buildinfo.Revision
			buildinfo.Revision = revision
			t.Cleanup(func() { buildinfo.Revision = previous })
			var output bytes.Buffer
			// Act
			err := buildinfo.Write(&output)
			// Assert
			var value map[string]any
			if err != nil || json.Unmarshal(output.Bytes(), &value) != nil || len(value) != 4 || value["os"] != runtime.GOOS || value["architecture"] != runtime.GOARCH || value["schemaVersion"] != float64(1) {
				t.Fatal("public binary metadata differs")
			}
			want := revision
			if revision == "private-invalid-revision" {
				want = "local"
			}
			if value["revision"] != want {
				t.Fatal("invalid revision was disclosed")
			}
		})
	}
	if err := buildinfo.Write(failingWriter{}); err == nil {
		t.Fatal("writer failure ignored")
	}
}

func TestVersion_WhenArgumentsAreSupplied_HandlesMetadataWithoutStartingServices(t *testing.T) {
	for _, test := range []struct {
		arguments       []string
		handled, failed bool
	}{
		{nil, false, false}, {[]string{"--version"}, true, false}, {[]string{"--unknown"}, true, true}, {[]string{"--version", "secret-argument"}, true, true},
	} {
		t.Run(strings.Join(test.arguments, "-"), func(t *testing.T) {
			// Arrange
			var output bytes.Buffer
			// Act
			handled, err := buildinfo.Version(test.arguments, &output)
			// Assert
			if handled != test.handled || (err != nil) != test.failed || (output.Len() > 0) != (test.handled && !test.failed) {
				t.Fatal("metadata argument handling differs")
			}
		})
	}
}
