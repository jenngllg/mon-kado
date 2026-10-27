package importer

import (
	"encoding/json"
	"net/url"
	"regexp"
	"strings"

	"golang.org/x/net/html"
)

var explicitStateEuro = regexp.MustCompile(`(?i)^([0-9]+(?:[.,][0-9]{1,2})?)\s*(?:€|EUR)$`)

// Passive component state is only additional evidence for an otherwise unpriced
// product. A document-matched SKU and the component's sole product heading bind
// it to the requested item; no application code or merchant selector is run.
func passiveProductStatePrice(root *html.Node, product map[string]any, base, canonical *url.URL) *json.Number {
	identifier := scalar(product, "sku")
	selection, valid := passiveStateSelection(base)
	if identifier == "" || len(identifier) > 128 || !valid || productMatch(product, base, canonical) == 0 {
		return nil
	}
	var prices []price
	states := 0
	for _, owner := range selectNodes(root, func(n *html.Node) bool { return n.Data != "script" && n.Data != "style" }) {
		for _, attribute := range owner.Attr {
			if !strings.HasPrefix(attribute.Key, "data-") || !strings.HasPrefix(strings.TrimSpace(attribute.Val), "{") {
				continue
			}
			states++
			if states > 32 || len(attribute.Val) > 128*1024 {
				return nil
			}
			if !json.Valid([]byte(attribute.Val)) {
				continue
			}
			decoder := json.NewDecoder(strings.NewReader(attribute.Val))
			decoder.UseNumber()
			var state map[string]any
			if decoder.Decode(&state) != nil || stateProductCode(state) != identifier || scalar(state, "name") == "" {
				continue
			}
			headings := selectNodes(owner, func(n *html.Node) bool { return n.Data == "h1" })
			if len(headings) != 1 || selectionText(nodeText(headings[0])) != selectionText(scalar(state, "name")) {
				continue
			}
			values, valid := stateProductPrices(state, selection)
			if !valid {
				return nil
			}
			prices = append(prices, values...)
		}
	}
	selected, _ := choosePrice(prices)
	return selected
}

func stateProductCode(product map[string]any) string {
	if sku := scalar(product, "sku"); sku != "" {
		return sku
	}
	return scalar(product, "code")
}

func passiveStateSelection(base *url.URL) (map[string]string, bool) {
	query, err := url.ParseQuery(base.RawQuery)
	if err != nil {
		return nil, false
	}
	selection := make(map[string]string, len(query))
	for key, values := range query {
		if strings.HasPrefix(key, "variant[") && strings.HasSuffix(key, "]") {
			key = strings.TrimSuffix(strings.TrimPrefix(key, "variant["), "]")
		}
		switch key {
		case "color", "size", "material", "pattern", "sku", "mpn", "gtin", "gtin13":
		default:
			return nil, false
		}
		if len(values) != 1 || values[0] == "" || len(values[0]) > 128 {
			return nil, false
		}
		if previous, exists := selection[key]; exists && previous != values[0] {
			return nil, false
		}
		selection[key] = values[0]
	}
	return selection, true
}

func stateMatchesSelection(node map[string]any, selection map[string]string) bool {
	for key, expected := range selection {
		if scalar(node, key) != expected {
			return false
		}
	}
	return true
}

func stateProductPrices(root map[string]any, selection map[string]string) ([]price, bool) {
	name := selectionText(scalar(root, "name"))
	remaining := maximumProductCandidates
	var values []price
	var visit func(any, int) bool
	visit = func(value any, depth int) bool {
		if depth > 16 {
			return false
		}
		switch node := value.(type) {
		case map[string]any:
			remaining--
			if remaining < 0 {
				return false
			}
			if code := stateProductCode(node); code != "" && scalar(node, "name") != "" {
				// Another named product could be a recommendation or an independently
				// priced variant. Neither can be silently omitted from a price decision.
				if len(code) > 128 || selectionText(scalar(node, "name")) != name {
					return false
				}
				if _, hasPrice := node["price"]; (depth != 0 || hasPrice) && stateMatchesSelection(node, selection) {
					candidate := price{}
					if formatted, ok := node["price"].(string); ok {
						if match := explicitStateEuro.FindStringSubmatch(normalizeText(formatted)); len(match) == 2 {
							candidate = price{strings.Replace(match[1], ",", ".", 1), "EUR"}
						}
					}
					values = append(values, candidate)
				}
			}
			for _, child := range node {
				if !visit(child, depth+1) {
					return false
				}
			}
		case []any:
			remaining--
			if remaining < 0 {
				return false
			}
			for _, child := range node {
				if !visit(child, depth+1) {
					return false
				}
			}
		}
		return true
	}
	valid := visit(root, 0)
	return values, valid
}
