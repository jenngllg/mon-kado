package main

import (
	"bytes"
	"encoding/json"
	"testing"

	"github.com/JennGllg/mon-kado/internal/persistence"
)

func TestMetadata_WhenCatalogRequested_InspectsEmbeddedSourcesWithoutPrivateConfiguration(t *testing.T) {
	// Arrange
	t.Setenv("DATABASE_URL", "")
	var output bytes.Buffer
	// Act
	handled, err := metadata([]string{"--catalog"}, &output)
	// Assert
	var catalog persistence.MigrationCatalog
	if !handled || err != nil || json.Unmarshal(output.Bytes(), &catalog) != nil || catalog.SchemaVersion != 2 || len(catalog.Migrations) != 19 {
		t.Fatal("public catalog inspection required private configuration")
	}
	output.Reset()
	if handled, err := metadata(nil, &output); handled || err != nil || output.Len() != 0 {
		t.Fatal("normal migration startup was intercepted")
	}
	if handled, err := metadata([]string{"--untrusted"}, &output); !handled || err == nil {
		t.Fatal("unknown migration argument accepted")
	}
}
