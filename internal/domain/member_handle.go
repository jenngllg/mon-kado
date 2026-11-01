package domain

import "strings"

// MemberDisplayNameTagLength is the number of decimal digits in an account tag.
const MemberDisplayNameTagLength = 4

// SplitMemberHandle separates a trailing four-digit tag from a display name search.
// Untagged input remains a display-name search, including names containing hashes.
func SplitMemberHandle(value string) (name, tag string) {
	name = strings.TrimSpace(value)
	separator := strings.LastIndexByte(name, '#')
	if separator <= 0 || len(name)-separator-1 != MemberDisplayNameTagLength {
		return name, ""
	}
	tag = name[separator+1:]
	for _, digit := range tag {
		if digit < '0' || digit > '9' {
			return name, ""
		}
	}
	return name[:separator], tag
}
