package importer

import (
	"encoding/json"
	"net/url"
	"regexp"
	"strings"

	"golang.org/x/net/html"
)

var amazonDomains = []string{"amazon.fr", "amazon.com", "amazon.co.uk", "amazon.de", "amazon.it", "amazon.es", "amazon.nl", "amazon.com.be", "amazon.ie", "amazon.pl", "amazon.se", "amazon.com.tr", "amazon.ca", "amazon.com.mx", "amazon.com.br", "amazon.in", "amazon.co.jp", "amazon.com.au", "amazon.sg", "amazon.ae", "amazon.sa", "amazon.eg", "amazon.co.za"}
var amazonContainers = map[string]bool{"corePriceDisplay_desktop_feature_div": true, "corePriceDisplay_mobile_feature_div": true, "corePrice_desktop": true, "corePrice_feature_div": true, "corePrice_mobile_feature_div": true, "apex_desktop": true, "apex_mobile": true, "buybox": true, "mobile_buybox": true}

func amazonHost(host string) bool {
	host = strings.ToLower(host)
	for _, domain := range amazonDomains {
		if host == domain || strings.HasSuffix(host, "."+domain) {
			return true
		}
	}
	return false
}
func ids(names ...string) func(*html.Node) bool {
	return func(n *html.Node) bool {
		for _, name := range names {
			if attr(n, "id") == name {
				return true
			}
		}
		return false
	}
}
func class(n *html.Node, name string) bool { return token(attr(n, "class"), name) }
func texts(nodes []*html.Node) []string {
	var values []string
	for _, n := range nodes {
		value := normalizeText(nodeText(n))
		if value != "" {
			values = append(values, value)
		}
	}
	return values
}
func under(n *html.Node, predicate func(*html.Node) bool) bool {
	for n = n.Parent; n != nil; n = n.Parent {
		if predicate(n) {
			return true
		}
	}
	return false
}

func extractAmazon(root *html.Node, base *url.URL) Metadata {
	name := unique(texts(selectNodes(root, ids("productTitle", "ebooksProductTitle"))))
	if name == "" || len(selectNodes(root, func(n *html.Node) bool {
		return attr(n, "id") == "captchacharacters" || n.Data == "form" && strings.Contains(attr(n, "action"), "validateCaptcha")
	})) > 0 {
		return Metadata{}
	}
	result := Metadata{Name: cleanName(name)}
	images := selectNodes(root, ids("landingImage", "imgBlkFront", "ebooksImgBlkFront"))
	if len(images) > 0 {
		for _, attribute := range []string{"data-old-hires", "data-a-hires", "src"} {
			if result.Image = resolveImage(root, base, attr(images[0], attribute)); result.Image != nil {
				break
			}
		}
	}
	primary := func(n *html.Node) bool { return amazonContainers[attr(n, "id")] }
	var values []string
	for _, node := range selectNodes(root, func(n *html.Node) bool {
		return (class(n, "priceToPay") || class(n, "apex-pricetopay-value")) && under(n, primary)
	}) {
		if value := amazonPriceText(node); value != "" {
			values = append(values, value)
		}
	}
	if len(values) == 0 {
		values = texts(selectNodes(root, func(n *html.Node) bool {
			return attr(n, "id") == "apex-pricetopay-accessibility-label" && under(n, primary)
		}))
	}
	if len(values) == 0 {
		values = texts(selectNodes(root, ids("priceblock_ourprice", "priceblock_dealprice", "priceblock_saleprice")))
	}
	result.Price, result.UnsupportedCurrency = localizedPrices(values)
	return result
}
func amazonPriceText(node *html.Node) string {
	read := func(name string) string {
		return normalizeText(firstText(node, func(n *html.Node) bool { return class(n, name) }))
	}
	if accessible := read("a-offscreen"); accessible != "" {
		return accessible
	}
	whole, fraction, symbol := read("a-price-whole"), read("a-price-fraction"), read("a-price-symbol")
	if whole != "" && fraction != "" && symbol != "" {
		return strings.ReplaceAll(strings.TrimRight(whole, ".,"), ",", ".") + "," + fraction + " " + symbol
	}
	return normalizeText(nodeText(node))
}
func hasEuro(text string) bool {
	return strings.Contains(text, "€") || strings.Contains(strings.ToUpper(text), "EUR")
}

var europeanAmount = regexp.MustCompile(`^(?:[0-9]+|[0-9]{1,3}(?:[ .][0-9]{3})+)(?:,[0-9]{1,2})?$`)
var internationalAmount = regexp.MustCompile(`^(?:[0-9]+|[0-9]{1,3}(?:[ ,][0-9]{3})+)\.[0-9]{1,2}$`)
var euroMarker = regexp.MustCompile(`(?i)EUR`)

func parseLocalizedPrice(text string) *json.Number {
	if !hasEuro(text) {
		return nil
	}
	amount := strings.TrimSpace(euroMarker.ReplaceAllString(strings.ReplaceAll(text, "€", ""), ""))
	if europeanAmount.MatchString(amount) {
		return parseAmount(strings.ReplaceAll(strings.ReplaceAll(strings.ReplaceAll(amount, " ", ""), ".", ""), ",", "."))
	}
	if internationalAmount.MatchString(amount) {
		return parseAmount(strings.ReplaceAll(strings.ReplaceAll(amount, " ", ""), ",", ""))
	}
	return nil
}
func localizedPrices(values []string) (*json.Number, bool) {
	var prices []price
	for _, value := range values {
		currency := ""
		if hasEuro(value) {
			currency = "EUR"
		}
		prices = append(prices, price{value, currency})
	}
	return choosePrice(prices, true)
}

func firstElementChild(node *html.Node) *html.Node {
	if node == nil {
		return nil
	}
	for n := node.FirstChild; n != nil; n = n.NextSibling {
		if n.Type == html.ElementNode {
			return n
		}
	}
	return nil
}
func extractFnac(root *html.Node, base *url.URL) Metadata {
	name := unique(texts(selectNodes(root, func(n *html.Node) bool {
		return n.Data == "h1" && attr(n, "data-automation-id") == "pdp-productInformation-title"
	})))
	if name == "" {
		return Metadata{}
	}
	result := Metadata{Name: cleanName(name)}
	var images []string
	for _, n := range selectNodes(root, func(n *html.Node) bool {
		return n.Data == "img" && hasAttr(n, "src") && normalizeText(attr(n, "alt")) == name && under(n, func(parent *html.Node) bool {
			return parent.Data == "button" && attr(parent, "aria-label") == "Open image 1 in fullscreen" && under(parent, func(section *html.Node) bool { return attr(section, "aria-label") == "Product images" })
		})
	}) {
		images = append(images, attr(n, "src"))
	}
	result.Image = resolveImage(root, base, unique(images))
	var values []string
	for _, box := range selectNodes(root, func(n *html.Node) bool {
		id := attr(n, "data-automation-id")
		return id == "pdp-buyBox-desktop" || id == "pdp-buyBox-mobile"
	}) {
		first := firstElementChild(box)
		if first == nil || first.Data != "div" {
			continue
		}
		container := firstElementChild(first)
		if container == nil || !strings.Contains(attr(container, "class"), "PricingUI-") || !strings.Contains(attr(container, "class"), "__container") {
			continue
		}
		values = append(values, texts(selectNodes(container, func(n *html.Node) bool { return strings.Contains(attr(n, "class"), "__pricingLabelMain") }))...)
	}
	result.Price, result.UnsupportedCurrency = localizedPrices(values)
	return result
}
