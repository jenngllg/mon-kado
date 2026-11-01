package persistence_test

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"testing"

	"github.com/JennGllg/mon-kado/internal/persistence"
)

func TestCatalog_WhenReleaseInspectedWithoutDatabase_MatchesEveryEmbeddedSQLSource(t *testing.T) {
	// Arrange
	files, err := os.ReadDir("migrations")
	if err != nil {
		t.Fatal(err)
	}
	model := sha256.New()
	want := []persistence.MigrationSource{}
	for _, file := range files {
		body, err := os.ReadFile(filepath.Join("migrations", file.Name()))
		if err != nil {
			t.Fatal(err)
		}
		body = bytes.ReplaceAll(body, []byte("\r\n"), []byte("\n"))
		digest := sha256.Sum256(body)
		want = append(want, persistence.MigrationSource{ID: file.Name(), SHA256: hex.EncodeToString(digest[:])})
		model.Write([]byte(file.Name() + "\x00"))
		model.Write(body)
		model.Write([]byte{0})
	}
	// Act
	catalog, err := persistence.Catalog()
	// Assert
	if err != nil || catalog.SchemaVersion != 2 || !reflect.DeepEqual(catalog.Migrations, want) || catalog.ModelHash != hex.EncodeToString(model.Sum(nil)) {
		t.Fatal("binary migration metadata differs from reviewed SQL sources")
	}
	catalog.Migrations[0].ID = "modified-by-caller"
	independent, _ := persistence.Catalog()
	if !reflect.DeepEqual(independent.Migrations, want) {
		t.Fatal("catalog caller mutated embedded metadata")
	}
	content, err := json.Marshal(independent)
	if err != nil || bytes.Contains(content, []byte("CREATE TABLE")) {
		t.Fatal("catalog disclosed SQL contents")
	}
}
