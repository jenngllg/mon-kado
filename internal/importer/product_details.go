package importer

import (
	"net/url"
	"regexp"
	"strings"
	"unicode/utf8"

	"github.com/JennGllg/mon-kado/internal/validation"
	"golang.org/x/net/html"
	"golang.org/x/text/unicode/norm"
)

// Product details use the URL-selected identity, never a storefront's default
// control selection. Shared variant attributes are safe only when all agree.
func productDetails(root *html.Node, product map[string]any, base, canonical *url.URL) Metadata {
	result := Metadata{}
	variants := groupVariants(product, base, canonical)
	for _, candidate := range []string{scalar(product, "description"), sharedDetail(variants, "description"), readMeta(root, "og:description"), readMeta(root, "twitter:description"), readMeta(root, "description")} {
		if result.Note = cleanProductDetail(candidate, 500, true); result.Note != nil {
			break
		}
	}
	attribute := func(key string, ambiguous *bool) *string {
		value := detailValue(product[key])
		*ambiguous = product[key] != nil && value == ""
		if len(variants) > 0 {
			shared := sharedDetail(variants, key)
			if shared == "" || value != "" && selectionText(value) != selectionText(shared) {
				*ambiguous = true
				return nil
			}
			value = shared
		}
		return cleanProductDetail(value, 100, false)
	}
	result.Color = attribute("color", &result.colorAmbiguous)
	result.Size = attribute("size", &result.sizeAmbiguous)
	if result.Size == nil && product["size"] == nil {
		variantSize := false
		for _, variant := range variants {
			variantSize = variantSize || variant["size"] != nil
		}
		if !variantSize {
			var dimensions []string
			for _, axis := range []struct{ key, label string }{{"width", "L"}, {"height", "H"}, {"depth", "P"}} {
				ambiguous := false
				if value := attribute(axis.key, &ambiguous); value != nil && dimensionValue.MatchString(*value) {
					dimensions = append(dimensions, axis.label+" "+*value)
				}
			}
			if len(dimensions) > 0 {
				result.Size = cleanProductDetail(strings.Join(dimensions, " × "), 100, false)
			}
		}
	}
	return result
}

var dimensionValue = regexp.MustCompile(`(?i)^[0-9]{1,8}(?:[.,][0-9]{1,4})?\s*(?:mm|cm|m|in|inch|inches|ft)$`)

func detailValue(value any) string {
	switch v := value.(type) {
	case string:
		return normalizeText(html.UnescapeString(v))
	case map[string]any:
		// Schema.org size also permits SizeSpecification/QuantitativeValue.
		if name := scalar(v, "name"); name != "" {
			return name
		}
		if schemaKind(v["@type"], "QuantitativeValue") {
			value := scalar(v, "value") + " " + scalar(v, "unitText")
			if dimensionValue.MatchString(value) {
				return value
			}
		}
		return ""
	case []any:
		var values []string
		for _, item := range v {
			value := detailValue(item)
			if value == "" {
				return ""
			}
			values = append(values, value)
		}
		return unique(values)
	}
	return ""
}

func sharedDetail(variants []map[string]any, key string) string {
	var values []string
	for _, variant := range variants {
		value := detailValue(variant[key])
		if value == "" {
			return ""
		}
		values = append(values, value)
	}
	return unique(values)
}

func cleanProductDetail(value string, maximum int, description bool) *string {
	if description {
		root, err := html.Parse(strings.NewReader(html.UnescapeString(value)))
		if err != nil {
			return nil
		}
		var text strings.Builder
		var visit func(*html.Node)
		visit = func(n *html.Node) {
			if n.Data == "script" || n.Data == "style" || n.Data == "noscript" || n.Data == "template" {
				return
			}
			if n.Type == html.TextNode {
				text.WriteString(n.Data)
			}
			for child := n.FirstChild; child != nil; child = child.NextSibling {
				visit(child)
			}
			if n.Type == html.ElementNode && (n.Data == "p" || n.Data == "div" || n.Data == "li" || n.Data == "br") {
				text.WriteByte(' ')
			}
		}
		visit(root)
		value = text.String()
	}
	value = norm.NFC.String(normalizeText(value))
	if value == "" || !validation.Text(value, utf8.RuneCountInString(value), false) {
		return nil
	}
	if runes := []rune(value); len(runes) > maximum {
		if !description {
			return nil
		}
		value = strings.TrimSpace(string(runes[:maximum]))
		if i := strings.LastIndex(value, " "); i >= maximum*4/5 {
			value = value[:i]
		}
	}
	return &value
}
