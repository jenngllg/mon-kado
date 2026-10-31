// Command WishImportAudit explicitly checks public product URLs using the real
// direct import pipeline. It never creates wishes or contacts private services.
package main

import (
	"context"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"os/signal"
	"path/filepath"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/importer"
)

type candidate struct {
	Merchant string `json:"merchant"`
	URL      string `json:"url"`
}

type observation struct {
	Merchant string                   `json:"merchant"`
	URL      string                   `json:"url"`
	Status   string                   `json:"status"`
	At       time.Time                `json:"observedAt"`
	Elapsed  int64                    `json:"elapsedMs"`
	Preview  domain.WishImportPreview `json:"preview"`
	Image    string                   `json:"imageFile,omitempty"`
	Hash     string                   `json:"imageSha256,omitempty"`
}

func main() {
	input := flag.String("input", "", "JSON array of merchant and url objects (required)")
	output := flag.String("output", "", "New evidence directory (required)")
	live := flag.Bool("live", false, "Explicitly permit real public merchant requests")
	flag.Parse()
	if !*live || *input == "" || *output == "" {
		fmt.Fprintln(os.Stderr, "Use -live -input candidates.json -output new-evidence-directory; this contacts real merchants.")
		os.Exit(2)
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()
	if err := run(ctx, *input, *output, importer.New().Preview); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run(ctx context.Context, input, output string, previewProduct func(context.Context, string) (domain.WishImportPreview, error)) error {
	content, err := os.ReadFile(input)
	if err != nil {
		return fmt.Errorf("read candidates: %w", err)
	}
	var candidates []candidate
	if err := json.Unmarshal(content, &candidates); err != nil || len(candidates) == 0 || len(candidates) > 100 {
		return fmt.Errorf("expected between 1 and 100 merchant/url candidates")
	}
	for _, entry := range candidates {
		if entry.Merchant == "" || entry.URL == "" {
			return fmt.Errorf("every candidate requires merchant and url")
		}
	}
	if err := os.Mkdir(output, 0700); err != nil {
		return fmt.Errorf("create new evidence directory: %w", err)
	}
	var results []observation
	for index, entry := range candidates {
		if err := ctx.Err(); err != nil {
			return err
		}
		start := time.Now().UTC()
		preview, err := previewProduct(ctx, entry.URL)
		result := observation{Merchant: entry.Merchant, URL: entry.URL, At: start, Elapsed: time.Since(start).Milliseconds(), Preview: preview, Status: "partial"}
		if err != nil {
			result.Status = "rejected"
		} else if preview.Name == nil && preview.Price == nil && preview.Image == nil {
			result.Status = "unavailable"
		} else if preview.Name != nil && preview.Price != nil && preview.Image != nil {
			result.Status = "name-price-image"
		}
		if preview.Image != nil {
			bytes, err := base64.StdEncoding.DecodeString(preview.Image.ContentBase64)
			if err != nil {
				return fmt.Errorf("invalid normalized image")
			}
			name := fmt.Sprintf("%02d.webp", index+1)
			if err := os.WriteFile(filepath.Join(output, name), bytes, 0600); err != nil {
				return err
			}
			digest := sha256.Sum256(bytes)
			result.Image, result.Hash = name, hex.EncodeToString(digest[:])
			result.Preview.Image = nil // Original normalized bytes are stored separately.
		}
		results = append(results, result)
		content, err := json.MarshalIndent(struct {
			Mode    string        `json:"mode"`
			Results []observation `json:"results"`
		}{"direct-default-providers-disabled", results}, "", "  ")
		if err != nil {
			return err
		}
		if err := os.WriteFile(filepath.Join(output, "results.json"), append(content, '\n'), 0600); err != nil {
			return err
		}
		fmt.Printf("%02d/%02d %s: %s (%d ms)\n", index+1, len(candidates), entry.Merchant, result.Status, result.Elapsed)
	}
	return nil
}
