package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestRun_WhenProductsDiffer_RecordsPartialFailuresAndOriginalImageEvidence(t *testing.T) {
	// Arrange
	directory := t.TempDir()
	input, output := filepath.Join(directory, "input.json"), filepath.Join(directory, "evidence")
	data := []byte(`[{"merchant":"Complete","url":"https://shop.test/complete"},{"merchant":"Partial","url":"https://shop.test/partial"},{"merchant":"Unavailable","url":"https://shop.test/unavailable"},{"merchant":"Rejected","url":"https://shop.test/rejected"}]`)
	if err := os.WriteFile(input, data, 0600); err != nil {
		t.Fatal(err)
	}
	calls := 0
	preview := func(ctx context.Context, source string) (domain.WishImportPreview, error) {
		if ctx != t.Context() {
			t.Fatal("caller context was replaced")
		}
		calls++
		name, price := "Gift", json.Number("12.30")
		result := domain.WishImportPreview{Url: &source, Quantity: 1, Warnings: []string{}}
		switch calls {
		case 1:
			result.Name, result.Price = &name, &price
			result.Image = &domain.WishImportImage{ContentType: "image/webp", ContentBase64: base64.StdEncoding.EncodeToString([]byte("original normalized image"))}
		case 2:
			result.Name = &name
		case 4:
			return result, errors.New("untrusted merchant error must not enter evidence")
		}
		return result, nil
	}
	// Act
	if err := run(t.Context(), input, output, preview); err != nil {
		t.Fatal(err)
	}
	// Assert
	content, err := os.ReadFile(filepath.Join(output, "results.json"))
	if err != nil {
		t.Fatal(err)
	}
	var report struct {
		Mode    string
		Results []observation
	}
	if err := json.Unmarshal(content, &report); err != nil {
		t.Fatal(err)
	}
	if calls != 4 || report.Mode != "direct-default-providers-disabled" || len(report.Results) != 4 {
		t.Fatal("audit skipped a product")
	}
	for index, want := range []string{"name-price-image", "partial", "unavailable", "rejected"} {
		if report.Results[index].Status != want {
			t.Fatalf("status %d = %s", index, report.Results[index].Status)
		}
	}
	first := report.Results[0]
	image, err := os.ReadFile(filepath.Join(output, first.Image))
	if err != nil || string(image) != "original normalized image" || len(first.Hash) != 64 || first.Preview.Image != nil {
		t.Fatal("image bytes/hash were lost or base64 duplicated")
	}
}

func TestRun_WhenInputInvalidOrOutputExists_DoesNotContactMerchants(t *testing.T) {
	for _, source := range []string{`[]`, `{}`, `[{}]`, `[{"merchant":"Gift"}]`, `[{"merchant":"Gift","url":"https://shop.test/p"}]`} {
		t.Run(source, func(t *testing.T) {
			// Arrange
			directory := t.TempDir()
			input := filepath.Join(directory, "input.json")
			if err := os.WriteFile(input, []byte(source), 0600); err != nil {
				t.Fatal(err)
			}
			// Act
			err := run(t.Context(), input, directory, func(context.Context, string) (domain.WishImportPreview, error) {
				t.Fatal("unexpected network request")
				return domain.WishImportPreview{}, nil
			})
			// Assert
			if err == nil {
				t.Fatal("invalid input or existing output accepted")
			}
		})
	}
}

func TestRun_WhenCanceledBeforeFirstProduct_DoesNotContactMerchants(t *testing.T) {
	// Arrange
	directory := t.TempDir()
	input := filepath.Join(directory, "input.json")
	if err := os.WriteFile(input, []byte(`[{"merchant":"Gift","url":"https://shop.test/p"}]`), 0600); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	// Act
	err := run(ctx, input, filepath.Join(directory, "evidence"), func(context.Context, string) (domain.WishImportPreview, error) {
		t.Fatal("unexpected network request")
		return domain.WishImportPreview{}, nil
	})
	// Assert
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("cancellation lost: %v", err)
	}
}
