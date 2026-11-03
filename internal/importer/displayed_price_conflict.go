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
		return strings.EqualFold(attr(n, "data-element"), "price") || token(attr(n, "itemprop"), "price") || priceClass.MatchString(attr(n, "class"))
	}) {
		explicit := strings.EqualFold(attr(element, "data-element"), "price") || token(attr(element, "itemprop"), "price")
		if element.Data == "meta" || element.Data == "link" || struckThrough(element) || explicit && hiddenMarkup(element) || !explicit && visuallyHiddenPrice(element) {
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
		// A generic CSS price must contain the amount alone. Set values,
		// instalments and descriptive nearby amounts are not selling prices.
		if !explicit && strings.TrimSpace(matches[0][0]) != text {
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
			if !explicit && hiddenPriceMarkup(element, scope) {
				// aria-hidden on the amount itself can merely avoid announcing
				// it twice. Its ancestors and semantic boundaries still matter.
				if !strings.EqualFold(attr(element, "aria-hidden"), "true") || hiddenPriceMarkup(element.Parent, scope) || unrelatedPriceClass.MatchString(attr(element, "class")) {
					break
				}
			}
			headings := selectNodes(scope, func(n *html.Node) bool { return n.Data == "h1" })
			if len(headings) == 1 && (strings.Contains(selectionText(nodeText(headings[0])), selectionText(scalar(product, "name"))) || headingNamesProduct(nodeText(headings[0]), scalar(product, "name"))) {
				return true
			}
			if scope.Data == "article" || scope.Data == "aside" || scope.Data == "nav" || scope.Data == "footer" || scope.Data == "a" && attr(scope, "href") != "" {
				break
			}
		}
	}
	return false
}

func visuallyHiddenPrice(element *html.Node) bool {
	style := strings.ToLower(strings.Join(strings.Fields(attr(element, "style")), ""))
	return hasAttr(element, "hidden") || hiddenClass.MatchString(attr(element, "class")) || strings.Contains(style, "display:none") || strings.Contains(style, "visibility:hidden") || hiddenMarkup(element.Parent)
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
