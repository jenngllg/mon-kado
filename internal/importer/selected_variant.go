package importer

import (
	"net/url"
	"strings"

	"golang.org/x/net/html"
)

// The selected HTML option can disambiguate structured variants even when their
// URLs are identical. Read passive selection state, never run page JavaScript or
// infer a variant from its position or price. Query-selected URLs retain priority.
func selectedMarkupVariant(root *html.Node, product map[string]any, base, canonical *url.URL) map[string]any {
	variants := groupVariants(product, base, canonical)
	if !schemaKind(product["@type"], "ProductGroup") || len(variants) < 2 || base.RawQuery != "" {
		return product
	}
	options := selectNodes(root, func(n *html.Node) bool {
		return n.Data == "option" && hasAttr(n, "selected") ||
			attr(n, "role") == "option" && attr(n, "aria-selected") == "true" ||
			n.Data == "input" && attr(n, "type") == "radio" && hasAttr(n, "checked") && !hasAttr(n, "disabled")
	})
	if len(options) > 32 {
		return product
	}
	selected := make([]int, len(variants))
	for i := range selected {
		selected[i] = i
	}
	for _, option := range options {
		labels := selectionLabels(root, option)
		matching := map[int]bool{}
		for i, variant := range variants {
			for _, key := range []string{"size", "color", "material", "pattern", "sku", "mpn", "gtin", "gtin13"} {
				if value := selectionText(scalar(variant, key)); value != "" && labels[value] {
					matching[i] = true
					break
				}
			}
		}
		if len(matching) == 0 || len(matching) == len(variants) {
			continue
		}
		var intersection []int
		for _, index := range selected {
			if matching[index] {
				intersection = append(intersection, index)
			}
		}
		selected = intersection
	}
	if len(selected) != 1 {
		return product
	}
	copy := make(map[string]any, len(product))
	for key, value := range product {
		copy[key] = value
	}
	copy["hasVariant"] = []any{variants[selected[0]]}
	return copy
}

func selectionText(value string) string {
	return strings.ToLower(normalizeText(html.UnescapeString(value)))
}

func selectionLabels(root, option *html.Node) map[string]bool {
	labels := map[string]bool{}
	scope := option
	if option.Data == "input" {
		// A wrapping label or list item is the semantic option, not the complete
		// form or arbitrary surrounding product content.
		for p, depth := option.Parent, 0; p != nil && depth < 4; p, depth = p.Parent, depth+1 {
			if p.Data == "label" || p.Data == "li" {
				scope = p
				break
			}
			if p.Data == "form" {
				break
			}
		}
		if id := attr(option, "id"); id != "" {
			for _, label := range selectNodes(root, func(n *html.Node) bool { return n.Data == "label" && attr(n, "for") == id }) {
				labels[selectionText(nodeText(label))] = true
			}
		}
	}
	remaining := 128
	var visit func(*html.Node)
	visit = func(n *html.Node) {
		remaining--
		if remaining < 0 || n.Data == "script" || n.Data == "style" {
			return
		}
		if n.Type == html.TextNode {
			if value := selectionText(n.Data); value != "" && len(value) <= 128 {
				labels[value] = true
			}
		}
		for child := n.FirstChild; child != nil && remaining >= 0; child = child.NextSibling {
			visit(child)
		}
	}
	visit(scope)
	if remaining < 0 {
		return nil
	}
	labels[selectionText(attr(option, "value"))] = true
	labels[selectionText(attr(option, "aria-label"))] = true
	return labels
}
