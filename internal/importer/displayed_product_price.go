package importer

import (
	"encoding/json"
	"regexp"
	"strings"

	"golang.org/x/net/html"
)

var priceClass = regexp.MustCompile(`(?i)(?:^|[\s_-])price(?:$|[\s_-])`)
var choiceLabel = regexp.MustCompile(`(?i)(?:^|[\s_-])(size|taille|color|colour|couleur|variant|variante|capacity|contenance|format)(?:$|[\s_-])`)
var hiddenClass = regexp.MustCompile(`(?i)(?:^|[\s_-])hidden(?:$|[\s_-])`)
var unrelatedPriceClass = regexp.MustCompile(`(?i)(?:^|[\s_-])(?:old|previous|original|was|related|recommendations?|suggestions?|upsell|crosssell|card|tile)(?:$|[\s_-])`)
var startingPrice = regexp.MustCompile(`(?i)(?:dès|des|from|starting\s+at|(?:à|a)\s+partir\s+de)\s*[0-9][0-9.,\s\x{00a0}\x{202f}]*(?:€|EUR\b)`)

// displayedProductPrice only considers a unique, plainly marked euro price
// beside a matching product heading. Competing amounts and unselected choices
// remain for a provider with stronger product data to resolve.
func displayedProductPrice(root *html.Node, name *string) *json.Number {
	if name == nil {
		return nil
	}
	headings := selectNodes(root, func(n *html.Node) bool {
		return n.Data == "h1" && !hiddenMarkup(n) && headingNamesProduct(nodeText(n), *name)
	})
	if len(headings) != 1 {
		return nil
	}
	for scope, depth := headings[0].Parent, 0; scope != nil && depth < 9 && scope.Data != "body" && scope.Data != "html"; scope, depth = scope.Parent, depth+1 {
		if scope.Data == "nav" || scope.Data == "footer" || scope.Data == "aside" || scope.Data == "a" {
			return nil
		}
		amounts := map[string]bool{}
		for _, n := range selectNodes(scope, func(n *html.Node) bool {
			return priceClass.MatchString(attr(n, "class"))
		}) {
			if hiddenPriceMarkup(n, scope) || struckThrough(n) {
				continue
			}
			text := strings.TrimSpace(nodeText(n))
			if len(text) > 128 || fromPriceLabel.MatchString(text) {
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
			if parsed := parseAmount(amount); parsed != nil {
				amounts[parsed.String()] = true
			}
		}
		if len(amounts) == 0 {
			continue
		}
		if len(amounts) != 1 || hasPriceChoice(scope) || startingPrice.MatchString(nodeText(scope)) {
			return nil
		}
		for amount := range amounts {
			value := json.Number(amount)
			return &value
		}
	}
	return nil
}

func hiddenPriceMarkup(n, scope *html.Node) bool {
	for ancestor := n; ancestor != nil && ancestor != scope; ancestor = ancestor.Parent {
		if ancestor.Data == "a" && attr(ancestor, "href") != "" || ancestor.Data == "article" || ancestor.Data == "aside" || ancestor.Data == "nav" || ancestor.Data == "footer" || hiddenClass.MatchString(attr(ancestor, "class")) || unrelatedPriceClass.MatchString(attr(ancestor, "class")) || hiddenMarkup(ancestor) {
			return true
		}
	}
	return false
}

func hasPriceChoice(scope *html.Node) bool {
	controls := selectNodes(scope, func(n *html.Node) bool {
		role := strings.ToLower(attr(n, "role"))
		return n.Data == "select" || n.Data == "button" || n.Data == "input" && strings.EqualFold(attr(n, "type"), "radio") || role == "radiogroup" || role == "listbox" || role == "group"
	})
	if len(controls) > maximumProductCandidates {
		return true
	}
	labelFor, labelledBy := map[string]bool{}, map[string]bool{}
	for _, control := range controls {
		label := strings.Join([]string{attr(control, "name"), attr(control, "id"), attr(control, "aria-label")}, " ")
		if hiddenMarkup(control) {
			continue
		}
		if control.Data == "button" && len(nodeText(control)) <= 128 {
			label += " " + normalizeText(nodeText(control))
		}
		if choiceLabel.MatchString(label) || ancestorPriceChoice(scope, control) {
			return true
		}
		if id := attr(control, "id"); id != "" {
			labelFor[id] = true
		}
		for _, id := range strings.Fields(attr(control, "aria-labelledby")) {
			labelledBy[id] = true
		}
	}
	for _, label := range selectNodes(scope, func(n *html.Node) bool {
		return n.Data == "label" && labelFor[attr(n, "for")] || labelledBy[attr(n, "id")]
	}) {
		text := normalizeText(nodeText(label))
		if len(text) <= 128 && choiceLabel.MatchString(text) {
			return true
		}
	}
	return false
}

func ancestorPriceChoice(scope, control *html.Node) bool {
	for ancestor := control.Parent; ancestor != nil && ancestor != scope; ancestor = ancestor.Parent {
		if ancestor.Data == "label" && len(nodeText(ancestor)) <= 128 && choiceLabel.MatchString(normalizeText(nodeText(ancestor))) {
			return true
		}
		if ancestor.Data != "fieldset" {
			continue
		}
		for child := ancestor.FirstChild; child != nil; child = child.NextSibling {
			if child.Data == "legend" && len(nodeText(child)) <= 128 && choiceLabel.MatchString(normalizeText(nodeText(child))) {
				return true
			}
		}
		break
	}
	return false
}
