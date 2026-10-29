package importer

import (
	"math/big"
	"net/url"
	"regexp"
	"strings"

	"golang.org/x/net/html"
)

var productDescriptionClass = regexp.MustCompile(`(?i)(?:^|[^a-z])product[-_ ]?description(?:$|[^a-z])`)
var volumeDescription = regexp.MustCompile(`(?i)^\s*(?:volume|contenance|capacity)\s*:\s*([0-9]+(?:[.,][0-9]+)?)\s*(ml|cl|l)\s*(?:ou|or|/)\s*([0-9]+(?:[.,][0-9]+)?)\s*(ml|cl|l)\s*$`)

// hasUnselectedDescriptionVolumes only handles an explicit alternative in the
// product's own description, with no URL-selected variant. A claimed default
// price from another provider cannot identify which volume the user wanted.
func hasUnselectedDescriptionVolumes(root *html.Node, base *url.URL, name *string) bool {
	if name == nil || *name == "" {
		return false
	}
	if _, ok := unselectedVariantPage(base); !ok {
		return false
	}
	headings := selectNodes(root, func(n *html.Node) bool {
		return n.Data == "h1" && !hiddenMarkup(n) && !unrelatedDescription(n) && headingNamesProduct(nodeText(n), *name)
	})
	if len(headings) != 1 {
		return false
	}
	productHeading := headings[0]
	for _, description := range selectNodes(root, func(n *html.Node) bool {
		return n.Type == html.ElementNode && (token(attr(n, "itemprop"), "description") || productDescriptionClass.MatchString(attr(n, "class")))
	}) {
		if hiddenMarkup(description) || unrelatedDescription(description) {
			continue
		}
		matchedVolume := false
		for _, element := range selectNodes(description, func(n *html.Node) bool {
			return (n.Data == "p" || n.Data == "li") && !hiddenMarkup(n)
		}) {
			text := normalizeText(nodeText(element))
			if len(text) > 128 {
				continue
			}
			matches := volumeDescription.FindStringSubmatch(text)
			if len(matches) != 5 {
				continue
			}
			first, firstOK := volumeInMillilitres(matches[1], matches[2])
			second, secondOK := volumeInMillilitres(matches[3], matches[4])
			if firstOK && secondOK && first.Cmp(second) != 0 {
				matchedVolume = true
				break
			}
		}
		if !matchedVolume {
			continue
		}
		for scope, depth := description.Parent, 0; scope != nil && depth < 8 && scope.Data != "body" && scope.Data != "html"; scope, depth = scope.Parent, depth+1 {
			if scope.Data == "aside" || scope.Data == "nav" || scope.Data == "footer" || scope.Data == "a" {
				break
			}
			headings := selectNodes(scope, func(n *html.Node) bool { return n.Data == "h1" && !hiddenMarkup(n) })
			if len(headings) == 1 && headings[0] == productHeading {
				return true
			}
			// A recommendation article must not borrow the main product heading
			// from a surrounding container. A product article can contain it.
			if scope.Data == "article" {
				break
			}
		}
	}
	return false
}

func volumeInMillilitres(value, unit string) (*big.Rat, bool) {
	amount, ok := new(big.Rat).SetString(strings.ReplaceAll(value, ",", "."))
	if !ok || amount.Sign() <= 0 {
		return nil, false
	}
	scale := int64(1)
	switch strings.ToLower(unit) {
	case "cl":
		scale = 10
	case "l":
		scale = 1000
	}
	return amount.Mul(amount, big.NewRat(scale, 1)), true
}

func unrelatedDescription(node *html.Node) bool {
	for n := node; n != nil; n = n.Parent {
		if n.Data == "aside" || n.Data == "nav" || n.Data == "footer" || n.Data == "a" || unrelatedPriceClass.MatchString(attr(n, "class")) {
			return true
		}
	}
	return false
}
