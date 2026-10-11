package httpapi

import (
	_ "embed"
	"encoding/json"
	"net/http"
	"sort"
	"strings"
)

//go:embed specification.json
var specification []byte

// Patterns returns a sorted copy of the registered method and path contracts.
func (a *API) Patterns() []string {
	patterns := make([]string, 0, len(a.operations)+1)
	for pattern := range a.operations {
		patterns = append(patterns, pattern)
	}
	patterns = append(patterns, "GET /security/csrf-token")
	sort.Strings(patterns)
	return patterns
}

func (a *API) registerOpenAPI() {
	a.mux.HandleFunc("GET /openapi/v1.json", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json; charset=utf-8")
		w.Write(specification)
	})
}

func (a *API) validateSpecification() {
	var document struct {
		Paths map[string]map[string]json.RawMessage `json:"paths"`
	}
	if err := json.Unmarshal(specification, &document); err != nil {
		panic("invalid OpenAPI specification")
	}
	for _, pattern := range a.Patterns() {
		method, path, _ := strings.Cut(pattern, " ")
		method = strings.ToLower(method)
		if len(document.Paths[path][method]) == 0 {
			panic("registered endpoint is missing its OpenAPI contract: " + pattern)
		}
	}
}
