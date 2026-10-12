// Package buildinfo exposes public release identity without reading private configuration.
package buildinfo

import (
	"encoding/json"
	"errors"
	"io"
	"regexp"
	"runtime"
)

// Revision is set to the reviewed source commit by the native release build's linker flags.
var Revision = "local"

// Version handles the sole metadata argument accepted by API and worker processes.
// An empty argument list leaves normal process startup to the composition root.
func Version(arguments []string, output io.Writer) (bool, error) {
	if len(arguments) == 0 {
		return false, nil
	}
	if len(arguments) != 1 || arguments[0] != "--version" {
		return true, errors.New("unsupported process arguments")
	}
	return true, Write(output)
}

// Write emits bounded public binary identity for deployment preflight checks.
func Write(output io.Writer) error {
	revision := Revision
	if revision != "local" && !regexp.MustCompile(`^[a-f0-9]{40}$`).MatchString(revision) {
		revision = "local"
	}
	return json.NewEncoder(output).Encode(struct {
		SchemaVersion int    `json:"schemaVersion"`
		Revision      string `json:"revision"`
		OS            string `json:"os"`
		Architecture  string `json:"architecture"`
	}{1, revision, runtime.GOOS, runtime.GOARCH})
}
