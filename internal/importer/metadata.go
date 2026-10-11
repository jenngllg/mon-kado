package importer

import (
	"bytes"
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/validation"
	"golang.org/x/net/html"
	"net/url"
	"regexp"
	"strings"
)

// Metadata contains untrusted suggestions that have passed text and monetary validation.
type Metadata struct {
	Name, Image         *string
	Price               *json.Number
	UnsupportedCurrency bool
}
type price struct{ Amount, Currency string }

// Extract prefers JSON-LD product metadata, then OpenGraph, and rejects ambiguous euro prices.
func Extract(content []byte, base *url.URL) Metadata {
	root, err := html.Parse(bytes.NewReader(content))
	if err != nil {
		return Metadata{}
	}
	meta := map[string]string{}
	title := ""
	var productNames, productImages []string
	var prices []price
	var nodes func(*html.Node)
	nodes = func(node *html.Node) {
		attributes := map[string]string{}
		for _, attribute := range node.Attr {
			attributes[strings.ToLower(attribute.Key)] = attribute.Val
		}
		if node.Type == html.ElementNode {
			if node.Data == "meta" {
				key := attributes["property"]
				if key == "" {
					key = attributes["name"]
				}
				if _, exists := meta[key]; !exists {
					meta[key] = attributes["content"]
				}
			}
			if node.Data == "title" && node.FirstChild != nil {
				title = node.FirstChild.Data
			}
			if node.Data == "script" && strings.EqualFold(attributes["type"], "application/ld+json") && node.FirstChild != nil {
				var value any
				decoder := json.NewDecoder(strings.NewReader(node.FirstChild.Data))
				decoder.UseNumber()
				if decoder.Decode(&value) == nil {
					var visit func(any, int)
					visit = func(value any, depth int) {
						if depth > 32 {
							return
						}
						switch object := value.(type) {
						case []any:
							for _, item := range object {
								visit(item, depth+1)
							}
						case map[string]any:
							product := false
							switch kind := object["@type"].(type) {
							case string:
								product = strings.EqualFold(kind, "Product")
							case []any:
								for _, v := range kind {
									if s, ok := v.(string); ok && strings.EqualFold(s, "Product") {
										product = true
									}
								}
							}
							if product {
								if name, ok := object["name"].(string); ok {
									productNames = append(productNames, name)
								}
								switch image := object["image"].(type) {
								case string:
									productImages = append(productImages, image)
								case []any:
									if len(image) > 0 {
										if s, ok := image[0].(string); ok {
											productImages = append(productImages, s)
										}
									}
								case map[string]any:
									if s, ok := image["url"].(string); ok {
										productImages = append(productImages, s)
									}
								}
								var offers func(any)
								offers = func(value any) {
									switch offer := value.(type) {
									case []any:
										for _, item := range offer {
											offers(item)
										}
									case map[string]any:
										amount := ""
										switch p := offer["price"].(type) {
										case string:
											amount = p
										case json.Number:
											amount = p.String()
										}
										currency, _ := offer["priceCurrency"].(string)
										if amount != "" {
											prices = append(prices, price{amount, currency})
										}
									}
								}
								offers(object["offers"])
							}
							for _, child := range object {
								visit(child, depth+1)
							}
						}
					}
					visit(value, 0)
				}
			}
		}
		for child := node.FirstChild; child != nil; child = child.NextSibling {
			nodes(child)
		}
	}
	nodes(root)
	result := Metadata{}
	for _, name := range append(productNames, meta["og:title"], title) {
		name = strings.Join(strings.Fields(name), " ")
		if name != "" && validation.Text(name, 100, false) {
			result.Name = &name
			break
		}
	}
	for _, image := range append(productImages, meta["og:image"]) {
		relative, err := url.Parse(strings.TrimSpace(image))
		if err == nil && image != "" {
			resolved := base.ResolveReference(relative).String()
			if validation.HTTPURL(resolved) {
				result.Image = &resolved
				break
			}
		}
	}
	if len(prices) == 0 && meta["product:price:amount"] != "" {
		prices = append(prices, price{meta["product:price:amount"], meta["product:price:currency"]})
	}
	distinct := map[string]bool{}
	pattern := regexp.MustCompile(`^[0-9]{1,8}(?:\.[0-9]{1,2})?$`)
	for _, price := range prices {
		if !strings.EqualFold(strings.TrimSpace(price.Currency), "EUR") {
			result.UnsupportedCurrency = true
			continue
		}
		amount := strings.TrimSpace(price.Amount)
		if pattern.MatchString(amount) && strings.Trim(amount, "0.") != "" {
			whole, fraction, _ := strings.Cut(amount, ".")
			whole = strings.TrimLeft(whole, "0")
			if whole == "" {
				whole = "0"
			}
			fraction = strings.TrimRight(fraction, "0")
			canonical := whole
			if fraction != "" {
				canonical += "." + fraction
			}
			distinct[canonical] = true
		}
	}
	if len(distinct) == 1 && !result.UnsupportedCurrency {
		for value := range distinct {
			number := json.Number(value)
			result.Price = &number
		}
	}
	return result
}
