package importer

// Schema.org offers also describe creative works, services and events. Their
// commerce relationship identifies merchandise without a merchant or type list.
func offeredEntity(object map[string]any) bool {
	if scalar(object, "name") == "" || object["@type"] == nil {
		return false
	}
	// Containers and offers themselves do not identify the item being sold.
	for _, kind := range []string{"Offer", "AggregateOffer", "OfferCatalog", "ItemList", "WebPage", "WebSite", "Organization"} {
		if schemaKind(object["@type"], kind) {
			return false
		}
	}
	for _, offer := range offerNodes(object) {
		if schemaKind(offer["@type"], "Offer") || schemaKind(offer["@type"], "AggregateOffer") || scalar(offer, "@id") != "" {
			return true
		}
	}
	return false
}
