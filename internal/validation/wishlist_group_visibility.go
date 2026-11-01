package validation

import (
	"fmt"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/google/uuid"
)

const maximumWishlistGroupSelections = 50

func validateWishlistGroupSelections(choices []domain.WishlistGroupVisibilitySelection, add func(string, string)) {
	if len(choices) > maximumWishlistGroupSelections {
		add("groups", "Select at most 50 groups.")
	}
	seen := map[string]bool{}
	for index, choice := range choices {
		path := fmt.Sprintf("groups[%d]", index)
		if choice.GroupId == nil || strings.TrimSpace(*choice.GroupId) == "" {
			add(path+".groupId", fmt.Sprintf(mandatory, path+".groupId"))
		} else {
			id, err := uuid.Parse(*choice.GroupId)
			if err != nil || id == uuid.Nil || len(*choice.GroupId) != 36 {
				add(path+".groupId", "The identifier is invalid.")
			} else if seen[id.String()] {
				add(path+".groupId", "The group must not be repeated.")
			} else {
				seen[id.String()] = true
			}
		}
		if choice.IsVisible == nil {
			add(path+".isVisible", fmt.Sprintf(mandatory, path+".isVisible"))
		}
	}
}
