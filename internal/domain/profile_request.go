package domain

import (
	"encoding/json"
	"strings"
)

// UnmarshalJSON distinguishes an omitted search preference from an explicitly supplied null.
func (r *UpdateMemberProfileRequest) UnmarshalJSON(data []byte) error {
	type profileRequest UpdateMemberProfileRequest
	var decoded profileRequest
	if err := json.Unmarshal(data, &decoded); err != nil {
		return err
	}
	var properties map[string]json.RawMessage
	if err := json.Unmarshal(data, &properties); err != nil {
		return err
	}
	for name := range properties {
		if strings.EqualFold(name, "isVisibleInMemberSearch") {
			decoded.HasVisibilityPreference = true
		}
	}
	*r = UpdateMemberProfileRequest(decoded)
	return nil
}
