package persistence

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
)

// MigrationSource identifies an immutable SQL file embedded in a release binary.
type MigrationSource struct {
	ID     string `json:"id"`
	SHA256 string `json:"sha256"`
}

// MigrationCatalog describes all native SQL migrations without opening a database.
// ModelHash binds the ordered SQL source sequence, rather than an ORM model snapshot.
type MigrationCatalog struct {
	SchemaVersion int               `json:"schemaVersion"`
	Migrations    []MigrationSource `json:"migrations"`
	ModelHash     string            `json:"modelHash"`
}

// Catalog returns the release's ordered migration identities and normalized source digests.
func Catalog() (MigrationCatalog, error) {
	files, err := migrations.ReadDir("migrations")
	if err != nil {
		return MigrationCatalog{}, err
	}
	result := MigrationCatalog{SchemaVersion: 2, Migrations: []MigrationSource{}}
	model := sha256.New()
	for _, file := range files {
		body, err := migrations.ReadFile("migrations/" + file.Name())
		if err != nil {
			return MigrationCatalog{}, err
		}
		body = bytes.ReplaceAll(body, []byte("\r\n"), []byte("\n"))
		digest := sha256.Sum256(body)
		result.Migrations = append(result.Migrations, MigrationSource{ID: file.Name(), SHA256: hex.EncodeToString(digest[:])})
		model.Write([]byte(file.Name()))
		model.Write([]byte{0})
		model.Write(body)
		model.Write([]byte{0})
	}
	result.ModelHash = hex.EncodeToString(model.Sum(nil))
	return result, nil
}
