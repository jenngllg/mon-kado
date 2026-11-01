package importer

import (
	"encoding/json"
	"strings"

	"golang.org/x/net/html"
)

// A clearly marked displayed price attached to the product heading is useful
// contrary evidence when structured metadata points at a different variant.
// Do not guess which one is current or replace it with another provider's guess.
func hasDisplayedPriceConflict(root *html.Node, product map[string]any, current *json.Number) bool {
	if current == nil || scalar(product, "name") == "" {
		return false
	}
	for _, element := range selectNodes(root, func(n *html.Node) bool {
		return strings.EqualFold(attr(n, "data-element"), "price") || token(attr(n, "itemprop"), "price")
	}) {
		if hiddenMarkup(element) || element.Data == "meta" || element.Data == "link" || struckThrough(element) {
			continue
		}
		text := strings.TrimSpace(nodeText(element))
		if len(text) > 64 {
			continue
		}
		matches := visibleEuroPrice.FindAllStringSubmatch(text, 2)
		if len(matches) != 1 || matches[0][2] != "" {
			continue
		}
		amount := strings.TrimSpace(matches[0][1])
		if commaAmount.MatchString(amount) {
			amount = strings.Replace(amount, ",", ".", 1)
		}
		displayed := parseAmount(amount)
		if displayed == nil || displayed.String() == current.String() {
			continue
		}
		for scope, depth := element.Parent, 0; scope != nil && depth < 7 && scope.Data != "body"; scope, depth = scope.Parent, depth+1 {
			headings := selectNodes(scope, func(n *html.Node) bool { return n.Data == "h1" })
			if len(headings) == 1 && strings.Contains(selectionText(nodeText(headings[0])), selectionText(scalar(product, "name"))) {
				return true
			}
			if scope.Data == "article" || scope.Data == "aside" || scope.Data == "nav" || scope.Data == "footer" || scope.Data == "a" && attr(scope, "href") != "" {
				break
			}
		}
	}
	return false
}

func struckThrough(node *html.Node) bool {
	for ancestor := node; ancestor != nil; ancestor = ancestor.Parent {
		style := strings.ToLower(strings.Join(strings.Fields(attr(ancestor, "style")), ""))
		if ancestor.Data == "del" || ancestor.Data == "s" || strings.Contains(style, "text-decoration:line-through") {
			return true
		}
	}
	return false
}
