package importer

import (
	"net/url"
	"strings"

	"golang.org/x/net/html"
	"golang.org/x/net/publicsuffix"
)

// A storefront or unloaded application shell often exposes only its site brand.
// Do not let that placeholder prevent a rendered fallback's actual product name
// from being used. Explicit structured product names retain their authority.
func siteBrandName(name string, root *html.Node, base *url.URL) bool {
	normalize := func(value string) string {
		return strings.NewReplacer(" ", "", "-", "").Replace(selectionText(value))
	}
	name = normalize(name)
	if site := normalize(readMeta(root, "og:site_name")); site != "" && name == site {
		return true
	}
	domain, err := publicsuffix.EffectiveTLDPlusOne(strings.ToLower(base.Hostname()))
	if err != nil {
		return false
	}
	suffix, _ := publicsuffix.PublicSuffix(domain)
	return name == normalize(strings.TrimSuffix(domain, "."+suffix)) ||
		name == normalize(domain) || name == normalize(base.Hostname())
}
