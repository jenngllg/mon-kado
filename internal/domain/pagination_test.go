package domain

import "testing"

func TestPage_WhenCollectionIsEmptyOrPageIsBeyondEnd_PreservesMetadata(t *testing.T) {
	for _, tc := range []struct {
		page, size, count, pages int
		previous, next           bool
	}{
		{1, 20, 0, 0, false, false}, {4, 20, 21, 2, true, false}, {1, 20, 21, 2, false, true},
	} {
		// Arrange
		var items []string
		// Act
		result := Page(items, tc.page, tc.size, tc.count)
		// Assert
		if result.Items == nil || result.CurrentPage != tc.page || result.TotalPages != tc.pages || result.HasPreviousPage != tc.previous || result.HasNextPage != tc.next {
			t.Fatalf("unexpected metadata: %+v", result)
		}
	}
}
