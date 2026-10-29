package importer

import (
	"net/url"
	"regexp"
	"strings"

	"golang.org/x/net/html"
)

var fromPriceLabel = regexp.MustCompile(`(?i)(?:^|\s)(?:à|a)\s+partir\s+de\s*`)
var sizeSelectorName = regexp.MustCompile(`(?i)(?:^|[-_ ])(?:size|taille|format|volume|capacity|contenance)(?:$|[-_ ])`)

// An explicit starting price and an unselected size are a decision to leave
// the price empty, rather than a reason to ask another provider to guess it.
func hasUnselectedStartingPrice(root *html.Node, product map[string]any, base, canonical *url.URL) bool {
	if _, ok := unselectedVariantPage(base); !ok || scalar(product, "name") == "" {
		return false
	}
	group, ok := product["isVariantOf"].(map[string]any)
	if !schemaKind(product["@type"], "ProductGroup") && (!ok || !schemaKind(group["@type"], "ProductGroup")) {
		return false
	}
	offers := productOffers(product, base, canonical)
	if len(offers) != 1 || !schemaKind(offers[0]["@type"], "AggregateOffer") || len(offerNodes(offers[0])) != 0 || !strings.EqualFold(scalar(offers[0], "priceCurrency"), "EUR") || scalar(offers[0], "price") != "" || scalar(offers[0], "offerCount") == "1" {
		return false
	}
	low := parseAmount(scalar(offers[0], "lowPrice"))
	high := parseAmount(scalar(offers[0], "highPrice"))
	if low == nil || high != nil && minorAmount(low) >= minorAmount(high) {
		return false
	}
	for _, label := range selectNodes(root, func(n *html.Node) bool {
		if n.Data == "script" || n.Data == "style" {
			return false
		}
		for child := n.FirstChild; child != nil; child = child.NextSibling {
			if child.Type == html.TextNode && len(child.Data) <= 128 && fromPriceLabel.MatchString(child.Data) {
				return !hiddenMarkup(n)
			}
		}
		return false
	}) {
		var directText strings.Builder
		for child := label.FirstChild; child != nil; child = child.NextSibling {
			if child.Type == html.TextNode {
				directText.WriteString(child.Data)
				if directText.Len() > 128 {
					break
				}
			}
		}
		text := strings.TrimSpace(directText.String())
		match := fromPriceLabel.FindStringIndex(text)
		if len(text) > 128 || match == nil {
			continue
		}
		amounts := visibleEuroPrice.FindAllStringSubmatch(text[match[1]:], 2)
		if len(amounts) != 1 || amounts[0][2] != "" {
			continue
		}
		value := strings.TrimSpace(amounts[0][1])
		if commaAmount.MatchString(value) {
			value = strings.Replace(value, ",", ".", 1)
		}
		amount := parseAmount(value)
		if amount == nil || amount.String() != low.String() {
			continue
		}
		for scope, depth := label, 0; scope != nil && depth < 8 && scope.Data != "body"; scope, depth = scope.Parent, depth+1 {
			headings := selectNodes(scope, func(n *html.Node) bool { return n.Data == "h1" })
			if len(headings) != 1 || !strings.Contains(selectionText(nodeText(headings[0])), selectionText(scalar(product, "name"))) {
				continue
			}
			for _, selectNode := range selectNodes(scope, func(n *html.Node) bool { return n.Data == "select" }) {
				if unselectedSize(selectNode) {
					return true
				}
			}
		}
	}
	return false
}

func unselectedSize(selectNode *html.Node) bool {
	if !sizeSelectorName.MatchString(attr(selectNode, "id")) && !sizeSelectorName.MatchString(attr(selectNode, "name")) && !sizeSelectorName.MatchString(attr(selectNode, "aria-label")) {
		return false
	}
	options := selectNodes(selectNode, func(n *html.Node) bool { return n.Data == "option" })
	if len(options) > maximumProductCandidates {
		return false
	}
	placeholder, selectedValue := false, false
	values := map[string]bool{}
	for _, option := range options {
		value := strings.TrimSpace(attr(option, "value"))
		if hasAttr(option, "selected") {
			placeholder = placeholder || value == ""
			selectedValue = selectedValue || value != ""
		}
		if value != "" {
			values[value] = true
		}
	}
	return placeholder && !selectedValue && len(values) >= 2
}
