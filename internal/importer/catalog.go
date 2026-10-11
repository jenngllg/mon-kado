package importer

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"net/url"
	"regexp"
	"strconv"
	"strings"
)

// Catalog provides exact-reference product metadata within the shared import budget.
type Catalog interface {
	CanHandle(*url.URL) bool
	Get(context.Context, *url.URL) (Metadata, error)
}

// Downloader bounds and validates every external catalog or image connection.
type Downloader interface {
	Download(context.Context, string, int64) ([]byte, *url.URL, string, error)
}

// ProductCatalog reads only exact Sephora France variants from its public storefront index.
type ProductCatalog struct {
	Client   Downloader
	Enabled  bool
	IndexKey string
}

var productPath = regexp.MustCompile(`^/p/(?:[^/]*-)?(P[0-9]{1,12}|[0-9]{1,12})\.html$`)

func storefront(u *url.URL) bool {
	return u != nil && u.Scheme == "https" && u.User == nil && (u.Port() == "" || u.Port() == "443") && (u.Hostname() == "www.sephora.fr" || u.Hostname() == "sephora.fr")
}

// CanHandle recognizes canonical first-party product paths only when explicitly enabled.
func (c *ProductCatalog) CanHandle(u *url.URL) bool {
	return c.Enabled && storefront(u) && productPath.MatchString(u.Path)
}

// Get fetches a single bounded catalog response and refuses fuzzy or conflicting variants.
func (c *ProductCatalog) Get(ctx context.Context, u *url.URL) (Metadata, error) {
	if !c.CanHandle(u) {
		return Metadata{}, nil
	}
	id := productPath.FindStringSubmatch(u.Path)[1]
	endpoint := "https://ac.cnstrc.com/search/" + url.PathEscape(id) + "?key=" + url.QueryEscape(c.IndexKey) + "&num_results_per_page=3"
	content, _, media, err := c.Client.Download(ctx, endpoint, 256*1024)
	if err != nil {
		return Metadata{}, err
	}
	if media != "application/json" {
		return Metadata{}, errors.New("the public product catalog response is not JSON")
	}
	var document map[string]any
	decoder := json.NewDecoder(bytes.NewReader(content))
	decoder.UseNumber()
	if err = decoder.Decode(&document); err != nil || !json.Valid(content) {
		return Metadata{}, errors.New("the public product catalog response is invalid")
	}
	response, _ := document["response"].(map[string]any)
	results, _ := response["results"].([]any)
	var candidates []map[string]any
	for _, raw := range results {
		result, _ := raw.(map[string]any)
		if candidate := catalogVariant(result, id); candidate != nil {
			candidates = append(candidates, candidate)
		}
	}
	if len(candidates) != 1 {
		return Metadata{}, nil
	}
	data := candidates[0]
	image := ""
	if candidate, err := url.Parse(catalogString(data, "image_url")); err == nil && candidate.Hostname() == "media.sephora.eu" && candidate.User == nil && (candidate.Scheme == "http" && (candidate.Port() == "" || candidate.Port() == "80") || candidate.Scheme == "https" && (candidate.Port() == "" || candidate.Port() == "443")) {
		candidate.Scheme = "https"
		candidate.Host = "media.sephora.eu"
		candidate.RawQuery = "scaleWidth=750&scaleHeight=750&scaleMode=fit"
		image = candidate.String()
	}
	price, _ := data["price"].(map[string]any)
	product := map[string]any{"@type": "Product", "name": catalogString(data, "name"), "url": u.String(), "image": image, "offers": map[string]any{"@type": "Offer", "price": price["salesPrice"], "priceCurrency": catalogString(price, "currency")}}
	encoded, err := json.Marshal(product)
	if err != nil {
		return Metadata{}, err
	}
	return Extract([]byte(`<script type="application/ld+json">`+string(encoded)+`</script>`), u), nil
}

func catalogString(object map[string]any, key string) string {
	value, _ := object[key].(string)
	return value
}

func exactVariant(data map[string]any, id string) bool {
	u, err := url.Parse(catalogString(data, "url"))
	if err != nil || !storefront(u) || catalogString(data, "variation_id") != id {
		return false
	}
	match := productPath.FindStringSubmatch(u.Path)
	return len(match) == 2 && match[1] == id
}

func catalogVariant(result map[string]any, id string) map[string]any {
	data, _ := result["data"].(map[string]any)
	selected := id
	if strings.HasPrefix(id, "P") {
		if catalogString(data, "id") != id {
			return nil
		}
		number, ok := data["defaultVariantId"].(json.Number)
		if !ok {
			return nil
		}
		value, err := number.Int64()
		if err != nil {
			return nil
		}
		selected = strconv.FormatInt(value, 10)
	}
	variants := []map[string]any{data}
	variations, _ := result["variations"].([]any)
	for _, raw := range variations {
		variation, _ := raw.(map[string]any)
		variant, _ := variation["data"].(map[string]any)
		variants = append(variants, variant)
	}
	distinct := map[string]map[string]any{}
	for _, variant := range variants {
		if !exactVariant(variant, selected) {
			continue
		}
		identity, _ := json.Marshal([]any{catalogString(variant, "name"), variant["price"], catalogString(variant, "image_url")})
		distinct[string(identity)] = variant
	}
	if len(distinct) != 1 {
		return nil
	}
	for _, variant := range distinct {
		return variant
	}
	return nil
}
