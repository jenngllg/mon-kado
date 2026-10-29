package importer

import (
	"encoding/json"
	"net/url"
	"regexp"
	"strings"

	"golang.org/x/net/html"
)

var foreignStartingPrice = regexp.MustCompile(`(?i)(?:[$£¥]\s*[0-9][0-9.,\s]*|[0-9][0-9.,\s]*\s*(?:[$£¥]|USD\b|GBP\b|CHF\b|CAD\b|AUD\b|JPY\b))`)

// An explicitly displayed starting price is not the exact price of an
// unconfigured product, even if a metadata offer describes the minimum.
func hasDisplayedStartingPrice(root *html.Node, base *url.URL, name *string, current *json.Number) bool {
	if _, ok := unselectedVariantPage(base); !ok || name == nil || current == nil {
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
		for priceScope, depth := label, 0; priceScope != nil && depth < 4 && priceScope.Data != "body"; priceScope, depth = priceScope.Parent, depth+1 {
			if priceScope.Data == "a" && attr(priceScope, "href") != "" || priceScope.Data == "article" || priceScope.Data == "aside" || priceScope.Data == "nav" || priceScope.Data == "footer" {
				break
			}
			foreign := compactForeignStartingPrice(priceScope)
			if !foreign && !strings.EqualFold(attr(priceScope, "role"), "status") && !token(attr(priceScope, "itemprop"), "price") && !compactStartingPrice(priceScope, current) {
				continue
			}
			if len(nodeText(priceScope)) > 1024 || !foreign && !visibleAmount(priceScope, current.String()) {
				continue
			}
			for scope, distance := priceScope.Parent, 0; scope != nil && distance < 6 && scope.Data != "body"; scope, distance = scope.Parent, distance+1 {
				headings := selectNodes(scope, func(n *html.Node) bool { return n.Data == "h1" })
				if len(headings) == 1 && (strings.Contains(selectionText(nodeText(headings[0])), selectionText(*name)) || headingNamesProduct(nodeText(headings[0]), *name)) {
					return true
				}
				if scope.Data == "article" || scope.Data == "aside" || scope.Data == "nav" || scope.Data == "footer" || scope.Data == "a" && attr(scope, "href") != "" {
					break
				}
			}
		}
	}
	return false
}

// A displayed minimum in another currency is evidence that the offer and the
// page describe different prices or markets. Neither is a safe exact EUR price.
func compactForeignStartingPrice(scope *html.Node) bool {
	var visible strings.Builder
	var collect func(*html.Node)
	collect = func(node *html.Node) {
		if visible.Len() > 128 || node.Data == "script" || node.Data == "style" || node.Data == "noscript" || hiddenMarkup(node) || struckThrough(node) {
			return
		}
		if node.Type == html.TextNode {
			visible.WriteString(node.Data)
		}
		for child := node.FirstChild; child != nil; child = child.NextSibling {
			collect(child)
		}
	}
	collect(scope)
	text := strings.TrimSpace(visible.String())
	if len(text) > 128 || len(visibleEuroPrice.FindAllString(text, 1)) != 0 {
		return false
	}
	label := fromPriceLabel.FindStringIndex(text)
	if label == nil {
		return false
	}
	return len(foreignStartingPrice.FindAllString(text[label[1]:], 2)) == 1
}

// Some pages put the label and amount in adjacent spans without a price role.
// Accept only a compact block with one matching amount after the label.
func compactStartingPrice(scope *html.Node, expected *json.Number) bool {
	text := strings.TrimSpace(nodeText(scope))
	if len(text) > 128 || hiddenMarkup(scope) {
		return false
	}
	label := fromPriceLabel.FindStringIndex(text)
	if label == nil {
		return false
	}
	prices := visibleEuroPrice.FindAllStringSubmatch(text[label[1]:], 2)
	if len(prices) != 1 || prices[0][2] != "" {
		return false
	}
	amount := strings.TrimSpace(prices[0][1])
	if commaAmount.MatchString(amount) {
		amount = strings.Replace(amount, ",", ".", 1)
	}
	parsed := parseAmount(amount)
	return parsed != nil && parsed.String() == expected.String()
}
