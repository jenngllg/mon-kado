// Command validate checks suggested images using the application's public-only connector.
package main

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"sync"
	"time"

	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/JennGllg/mon-kado/internal/importer"
)

type sample struct {
	ID       int    `json:"id"`
	ImageURL string `json:"imageURL"`
}
type outcome struct {
	ID       int    `json:"id"`
	Decoded  bool   `json:"decoded"`
	Category string `json:"category"`
}

func validate(ctx context.Context, client *importer.Client, item sample) outcome {
	result := outcome{ID: item.ID}
	if item.ImageURL == "" {
		result.Category = "no_candidate"
		return result
	}
	budget, cancel := context.WithTimeout(ctx, 8*time.Second)
	defer cancel()
	content, _, _, err := client.Download(budget, item.ImageURL, images.MaximumInputBytes)
	if err != nil {
		result.Category = "download_unavailable"
		return result
	}
	_, err = images.Process(budget, content, 1600, false)
	if err != nil {
		result.Category = "decode_or_budget"
		return result
	}
	result.Decoded = true
	result.Category = "decoded"
	return result
}

func run(ctx context.Context, input, output string) error {
	source, err := os.Open(input)
	if err != nil {
		return err
	}
	defer source.Close()
	destination, err := os.OpenFile(output, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		return err
	}
	defer destination.Close()
	var entries []sample
	scanner := bufio.NewScanner(source)
	scanner.Buffer(make([]byte, 4096), 1024*1024)
	for scanner.Scan() {
		var item sample
		if err = json.Unmarshal(scanner.Bytes(), &item); err != nil {
			return errors.New("invalid campaign evidence")
		}
		entries = append(entries, item)
	}
	if err = scanner.Err(); err != nil {
		return err
	}
	jobs, results := make(chan sample), make(chan outcome)
	var workers sync.WaitGroup
	for range 2 {
		workers.Add(1)
		go func() {
			defer workers.Done()
			client := importer.New()
			client.Timeout = 8 * time.Second
			for item := range jobs {
				results <- validate(ctx, client, item)
			}
		}()
	}
	go func() {
		for _, item := range entries {
			jobs <- item
		}
		close(jobs)
		workers.Wait()
		close(results)
	}()
	encoder := json.NewEncoder(destination)
	var writeErr error
	for result := range results {
		if writeErr == nil {
			writeErr = encoder.Encode(result)
		}
	}
	return writeErr
}

func main() {
	if len(os.Args) != 3 {
		fmt.Fprintln(os.Stderr, "Provide input and a new output file.")
		os.Exit(2)
	}
	if err := run(context.Background(), os.Args[1], os.Args[2]); err != nil {
		fmt.Fprintln(os.Stderr, "Image validation failed.")
		os.Exit(1)
	}
}
