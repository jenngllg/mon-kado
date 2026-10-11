package httpapi

import "github.com/JennGllg/mon-kado/internal/domain"

func (a *API) exportRoutes() {
	for _, prefix := range []string{"/api/v1/members/current/data-exports", "/api/v1/admin/members/{memberId}/data-exports"} {
		opt := options{Auth: true, Admin: prefix != "/api/v1/members/current/data-exports", Rate: 30}
		a.route("POST "+prefix, opt, a.requestExport)
		a.route("GET "+prefix+"/latest", opt, a.readExport)
		a.route("GET "+prefix+"/{exportId}", opt, a.readExport)
		a.route("GET "+prefix+"/{exportId}/archive", opt, a.exportArchive)
	}
}
func exportMember(r *request) string {
	if id := r.PathValue("memberId"); id != "" {
		return id
	}
	return r.Principal.MemberID
}
func (a *API) requestExport(r *request) (response, error) {
	var reference *string
	if r.PathValue("memberId") != "" {
		input, err := body[domain.RequestAdministrativeDataExportRequest](r)
		if err != nil {
			return response{}, err
		}
		reference = input.RequestReference
	}
	result, err := a.Service.RequestExport(r.Context(), exportMember(r), r.Principal, reference)
	status := 202
	if result.Status == "ready" {
		status = 200
	}
	return response{Status: status, Body: result}, err
}
func (a *API) readExport(r *request) (response, error) {
	result, err := a.Service.Export(r.Context(), exportMember(r), r.PathValue("exportId"))
	return response{Body: result}, err
}
func (a *API) exportArchive(r *request) (response, error) {
	file, size, err := a.Service.ExportArchive(r.Context(), exportMember(r), r.PathValue("exportId"), r.Principal, r.PathValue("memberId") != "")
	return response{Stream: file, ContentLength: size, DownloadName: "monkado-personal-data.zip", MediaType: "application/zip"}, err
}
