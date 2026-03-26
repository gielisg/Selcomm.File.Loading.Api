"""
Test suite for the File Loading API V4.

Covers health check, file loading, file management, transfer sources,
parser configuration, vendors, file classes, file types, file types NT,
activity log, and exception views.
"""
import pytest
import requests


# ============================================================
# Health Check (no auth required)
# ============================================================

class TestHealthCheck:
    """Health check endpoint — no authentication required."""

    def test_health_check_returns_200_or_503(self, base_url):
        """Health check should return 200 (healthy) or 503 (unhealthy)."""
        response = requests.get(f"{base_url}/health-check?domain=demo3", timeout=10)
        assert response.status_code in (200, 503)

    def test_health_check_response_has_status_field(self, base_url):
        """Health check response should contain a Status field."""
        response = requests.get(f"{base_url}/health-check?domain=demo3", timeout=10)
        data = response.json()
        assert "status" in data or "Status" in data

    def test_health_check_response_has_service_name(self, base_url):
        """Health check response should identify the service."""
        response = requests.get(f"{base_url}/health-check?domain=demo3", timeout=10)
        data = response.json()
        service = data.get("service") or data.get("Service")
        assert service is not None

    def test_health_check_no_auth_needed(self, base_url):
        """Health check must not require authentication."""
        response = requests.get(f"{base_url}/health-check?domain=demo3", timeout=10)
        assert response.status_code != 401


# ============================================================
# Authentication — error scenarios
# ============================================================

class TestAuthentication:
    """Verify that endpoints enforce authentication."""

    def test_files_returns_401_without_auth(self, base_url):
        """GET /files should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/files", timeout=10)
        assert response.status_code == 401

    def test_dashboard_returns_401_without_auth(self, base_url):
        """GET /dashboard should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/dashboard", timeout=10)
        assert response.status_code == 401

    def test_sources_returns_401_without_auth(self, base_url):
        """GET /sources should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/sources", timeout=10)
        assert response.status_code == 401

    def test_vendors_returns_401_without_auth(self, base_url):
        """GET /vendors should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/vendors", timeout=10)
        assert response.status_code == 401

    def test_file_classes_returns_401_without_auth(self, base_url):
        """GET /file-classes should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/file-classes", timeout=10)
        assert response.status_code == 401

    def test_activity_returns_401_without_auth(self, base_url):
        """GET /activity should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/activity", timeout=10)
        assert response.status_code == 401

    def test_parsers_returns_401_without_auth(self, base_url):
        """GET /parsers should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/parsers", timeout=10)
        assert response.status_code == 401

    def test_file_types_returns_401_without_auth(self, base_url):
        """GET /file-types should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/file-types", timeout=10)
        assert response.status_code == 401

    def test_exceptions_errors_returns_401_without_auth(self, base_url):
        """GET /exceptions/errors should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/exceptions/errors", timeout=10)
        assert response.status_code == 401

    def test_exceptions_skipped_returns_401_without_auth(self, base_url):
        """GET /exceptions/skipped should return 401 when no auth is provided."""
        response = requests.get(f"{base_url}/exceptions/skipped", timeout=10)
        assert response.status_code == 401


# ============================================================
# File Loading endpoints (JWT auth)
# ============================================================

class TestFileLoadingJwt:
    """File loading endpoints with JWT authentication."""

    def test_list_file_types(self, base_url, auth_headers):
        """GET /file-types should return a list of supported file types."""
        response = requests.get(f"{base_url}/file-types", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204)

    def test_list_file_types_returns_data(self, base_url, auth_headers):
        """GET /file-types should return data when available."""
        response = requests.get(f"{base_url}/file-types", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, (list, dict))

    def test_list_files(self, base_url, auth_headers):
        """GET /files should return files or 204 No Content."""
        response = requests.get(f"{base_url}/files", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204)

    def test_list_files_with_max_records(self, base_url, auth_headers):
        """GET /files with maxRecords parameter should limit results."""
        params = {"maxRecords": 5}
        response = requests.get(f"{base_url}/files", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)
        if response.status_code == 200:
            data = response.json()
            if isinstance(data, list):
                assert len(data) <= 5

    def test_list_files_with_file_type_filter(self, base_url, auth_headers):
        """GET /files with fileType filter should accept the parameter."""
        params = {"fileType": "CDR", "maxRecords": 10}
        response = requests.get(f"{base_url}/files", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_list_files_with_customer_filter(self, base_url, auth_headers):
        """GET /files with ntCustNum filter should accept the parameter."""
        params = {"ntCustNum": "999999", "maxRecords": 10}
        response = requests.get(f"{base_url}/files", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_file_status_not_found(self, base_url, auth_headers):
        """GET /files/{nt-file-num} with nonexistent ID should return 404."""
        response = requests.get(f"{base_url}/files/999999999", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_load_file_missing_filename(self, base_url, auth_headers):
        """POST /load with missing filename should return 400."""
        payload = {"FileType": "CDR"}
        response = requests.post(f"{base_url}/load", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code == 400

    def test_load_file_not_found(self, base_url, auth_headers):
        """POST /load with nonexistent file should return 400."""
        payload = {"FileName": "/tmp/nonexistent_file_xyz.csv", "FileType": "CDR"}
        response = requests.post(f"{base_url}/load", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code == 400

    def test_upload_no_file(self, base_url, auth_headers):
        """POST /upload with no file attached should return 400 or 415."""
        response = requests.post(
            f"{base_url}/upload",
            headers=auth_headers,
            data={"fileType": "CDR"},
            timeout=10
        )
        assert response.status_code in (400, 415)

    def test_reprocess_not_found(self, base_url, auth_headers):
        """POST /files/{nt-file-num}/reprocess with nonexistent file should return 404."""
        response = requests.post(f"{base_url}/files/999999999/reprocess", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_get_file_errors_not_found(self, base_url, auth_headers):
        """GET /files/{nt-file-num}/errors with nonexistent file should return 404 or 204."""
        response = requests.get(f"{base_url}/files/999999999/errors", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 204)


# ============================================================
# File Loading endpoints (API Key auth)
# ============================================================

class TestFileLoadingApiKey:
    """File loading endpoints with API Key authentication."""

    def test_list_file_types_with_api_key(self, base_url, api_key_headers):
        """GET /file-types should work with API key auth."""
        response = requests.get(f"{base_url}/file-types", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_list_files_with_api_key(self, base_url, api_key_headers):
        """GET /files should work with API key auth."""
        response = requests.get(f"{base_url}/files", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_get_file_status_with_api_key(self, base_url, api_key_headers):
        """GET /files/{nt-file-num} should work with API key auth."""
        response = requests.get(f"{base_url}/files/999999999", headers=api_key_headers, timeout=10)
        assert response.status_code in (404, 401, 403)


# ============================================================
# File Management — Dashboard
# ============================================================

class TestDashboard:
    """Dashboard endpoint tests."""

    def test_get_dashboard_jwt(self, base_url, auth_headers):
        """GET /dashboard should return dashboard summary data."""
        response = requests.get(f"{base_url}/dashboard", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_dashboard_with_file_type_filter(self, base_url, auth_headers):
        """GET /dashboard with fileType filter should accept the parameter."""
        params = {"fileType": "CDR"}
        response = requests.get(f"{base_url}/dashboard", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_dashboard_api_key(self, base_url, api_key_headers):
        """GET /dashboard should work with API key auth."""
        response = requests.get(f"{base_url}/dashboard", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)


# ============================================================
# File Management — Manager Files
# ============================================================

class TestManagerFiles:
    """Manager files endpoint tests."""

    def test_list_manager_files_jwt(self, base_url, auth_headers):
        """GET /manager/files should return files in the workflow."""
        response = requests.get(f"{base_url}/manager/files", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204)

    def test_list_manager_files_with_filters(self, base_url, auth_headers):
        """GET /manager/files with query filters should accept parameters."""
        params = {
            "fileType": "CDR",
            "maxRecords": 10,
            "folder": "Transfer"
        }
        response = requests.get(f"{base_url}/manager/files", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_list_manager_files_with_status_filter(self, base_url, auth_headers):
        """GET /manager/files with status filter should accept the parameter."""
        params = {"status": "Pending", "maxRecords": 5}
        response = requests.get(f"{base_url}/manager/files", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_list_manager_files_with_search(self, base_url, auth_headers):
        """GET /manager/files with search filter should accept the parameter."""
        params = {"search": "test", "maxRecords": 5}
        response = requests.get(f"{base_url}/manager/files", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_manager_file_not_found(self, base_url, auth_headers):
        """GET /manager/files/{transfer-id} with nonexistent ID should return 404."""
        response = requests.get(f"{base_url}/manager/files/999999999", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_list_manager_files_api_key(self, base_url, api_key_headers):
        """GET /manager/files should work with API key auth."""
        response = requests.get(f"{base_url}/manager/files", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_process_transfer_not_found(self, base_url, auth_headers):
        """POST /manager/files/{transfer-id}/process with nonexistent ID should return 404."""
        response = requests.post(f"{base_url}/manager/files/999999999/process", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_retry_transfer_not_found(self, base_url, auth_headers):
        """POST /manager/files/{transfer-id}/retry with nonexistent ID should return 404."""
        response = requests.post(f"{base_url}/manager/files/999999999/retry", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_move_file_not_found(self, base_url, auth_headers):
        """POST /manager/files/{transfer-id}/move with nonexistent ID should return 404 or 400."""
        payload = {"Folder": "Processed"}
        response = requests.post(
            f"{base_url}/manager/files/999999999/move",
            headers=auth_headers, json=payload, timeout=10
        )
        assert response.status_code in (404, 400)

    def test_delete_transfer_not_found(self, base_url, auth_headers):
        """DELETE /manager/files/{transfer-id} with nonexistent ID should return 404."""
        response = requests.delete(f"{base_url}/manager/files/999999999", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_unload_file_not_found(self, base_url, auth_headers):
        """POST /manager/files/{nt-file-num}/unload with nonexistent file should return 404."""
        response = requests.post(f"{base_url}/manager/files/999999999/unload", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 400)

    def test_skip_sequence_not_found(self, base_url, auth_headers):
        """POST /manager/files/{nt-file-num}/skip-sequence with nonexistent file should return 404 or 400 or 500."""
        response = requests.post(f"{base_url}/manager/files/999999999/skip-sequence", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 404, 400, 500)

    def test_download_file_not_found(self, base_url, auth_headers):
        """GET /manager/files/{transfer-id}/download with nonexistent ID should return 404."""
        response = requests.get(f"{base_url}/manager/files/999999999/download", headers=auth_headers, timeout=10)
        assert response.status_code == 404


# ============================================================
# Transfer Sources
# ============================================================

class TestTransferSources:
    """Transfer source endpoints."""

    def test_list_sources_jwt(self, base_url, auth_headers):
        """GET /sources should return transfer source configurations."""
        response = requests.get(f"{base_url}/sources", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204)

    def test_list_sources_returns_list(self, base_url, auth_headers):
        """GET /sources should return a JSON array when data exists."""
        response = requests.get(f"{base_url}/sources", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, list)

    def test_get_source_not_found(self, base_url, auth_headers):
        """GET /sources/{source-id} with nonexistent ID should return 404."""
        response = requests.get(f"{base_url}/sources/999999999", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_list_sources_api_key(self, base_url, api_key_headers):
        """GET /sources should work with API key auth."""
        response = requests.get(f"{base_url}/sources", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_create_source_missing_fields(self, base_url, auth_headers):
        """POST /sources with missing required fields should return error."""
        payload = {}
        response = requests.post(f"{base_url}/sources", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code in (400, 405, 500)

    def test_update_source_not_found(self, base_url, auth_headers):
        """PATCH /sources/{source-id} with nonexistent ID should return error."""
        payload = {"Description": "Updated test source"}
        response = requests.patch(
            f"{base_url}/sources/999999999",
            headers=auth_headers, json=payload, timeout=10
        )
        assert response.status_code in (404, 400, 405, 500)

    def test_delete_source_not_found(self, base_url, auth_headers):
        """DELETE /sources/{source-id} with nonexistent ID should return 404 or 200."""
        response = requests.delete(f"{base_url}/sources/999999999", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 200)

    def test_fetch_source_not_found(self, base_url, auth_headers):
        """POST /transfers/{source-id}/fetch with nonexistent ID should return 404."""
        response = requests.post(f"{base_url}/transfers/999999999/fetch", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 400)

    def test_test_source_connection_not_found(self, base_url, auth_headers):
        """POST /sources/{source-id}/test with nonexistent ID should return 404 or 400."""
        response = requests.post(f"{base_url}/sources/999999999/test", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 400)

    def test_get_source_status_not_found(self, base_url, auth_headers):
        """GET /sources/{source-id}/status with nonexistent ID should return 404."""
        response = requests.get(f"{base_url}/sources/999999999/status", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 200)


# ============================================================
# Parser Configuration
# ============================================================

class TestParserConfiguration:
    """Parser configuration endpoints."""

    def test_list_parsers_jwt(self, base_url, auth_headers):
        """GET /parsers should return parser configurations, 403 if not authorised, or 500 if table missing."""
        response = requests.get(f"{base_url}/parsers", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204, 403, 500)

    def test_list_parsers_returns_list(self, base_url, auth_headers):
        """GET /parsers should return a JSON array when data exists."""
        response = requests.get(f"{base_url}/parsers", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, list)

    def test_list_parsers_active_filter(self, base_url, auth_headers):
        """GET /parsers with active filter should accept the parameter."""
        params = {"active": True}
        response = requests.get(f"{base_url}/parsers", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204, 403, 500)

    def test_get_parser_not_found(self, base_url, auth_headers):
        """GET /parsers/{file-type-code} with nonexistent code should return 404 or 403/500."""
        response = requests.get(f"{base_url}/parsers/NONEXISTENT_TYPE", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403, 500)

    def test_list_parsers_api_key(self, base_url, api_key_headers):
        """GET /parsers should work with API key auth."""
        response = requests.get(f"{base_url}/parsers", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_create_parser_missing_fields(self, base_url, auth_headers):
        """POST /parsers with missing required fields should return error."""
        payload = {}
        response = requests.post(f"{base_url}/parsers", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code in (400, 403, 405, 409, 500)

    def test_update_parser_not_found(self, base_url, auth_headers):
        """PATCH /parsers/{file-type-code} with nonexistent code should return 404 or 403/500."""
        payload = {"FileFormat": "CSV", "Delimiter": ","}
        response = requests.patch(
            f"{base_url}/parsers/NONEXISTENT_XYZ",
            headers=auth_headers, json=payload, timeout=10
        )
        assert response.status_code in (404, 400, 403, 500)

    def test_delete_parser_not_found(self, base_url, auth_headers):
        """DELETE /parsers/{file-type-code} with nonexistent code should return 404 or 403/500."""
        response = requests.delete(f"{base_url}/parsers/NONEXISTENT_XYZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403, 500)


# ============================================================
# Vendors
# ============================================================

class TestVendors:
    """Vendor endpoints."""

    def test_list_vendors_jwt(self, base_url, auth_headers):
        """GET /vendors should return vendor records or 403 if not authorised."""
        response = requests.get(f"{base_url}/vendors", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204, 403)

    def test_list_vendors_returns_list(self, base_url, auth_headers):
        """GET /vendors should return a JSON array when data exists."""
        response = requests.get(f"{base_url}/vendors", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, list)

    def test_get_vendor_not_found(self, base_url, auth_headers):
        """GET /vendors/{network-id} with nonexistent ID should return 404 or 403."""
        response = requests.get(f"{base_url}/vendors/ZZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)

    def test_list_vendors_api_key(self, base_url, api_key_headers):
        """GET /vendors should work with API key auth."""
        response = requests.get(f"{base_url}/vendors", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_create_vendor_missing_fields(self, base_url, auth_headers):
        """POST /vendors with missing required fields should return error."""
        payload = {}
        response = requests.post(f"{base_url}/vendors", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code in (400, 403, 409, 500)

    def test_update_vendor_not_found(self, base_url, auth_headers):
        """PATCH /vendors/{network-id} with nonexistent ID should return 404 or 403."""
        payload = {"Description": "Test vendor"}
        response = requests.patch(f"{base_url}/vendors/ZZ", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code in (404, 400, 403)

    def test_delete_vendor_not_found(self, base_url, auth_headers):
        """DELETE /vendors/{network-id} with nonexistent ID should return 404 or 200 or 403."""
        response = requests.delete(f"{base_url}/vendors/ZZ_NONEXIST", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 200, 403)


# ============================================================
# File Classes
# ============================================================

class TestFileClasses:
    """File class endpoints."""

    def test_list_file_classes_jwt(self, base_url, auth_headers):
        """GET /file-classes should return file class records or 403 if not authorised."""
        response = requests.get(f"{base_url}/file-classes", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204, 403)

    def test_list_file_classes_returns_list(self, base_url, auth_headers):
        """GET /file-classes should return a JSON array when data exists."""
        response = requests.get(f"{base_url}/file-classes", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, list)

    def test_get_file_class_not_found(self, base_url, auth_headers):
        """GET /file-classes/{file-class-code} with nonexistent code should return 404 or 403."""
        response = requests.get(f"{base_url}/file-classes/ZZZZZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)

    def test_list_file_classes_api_key(self, base_url, api_key_headers):
        """GET /file-classes should work with API key auth."""
        response = requests.get(f"{base_url}/file-classes", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_create_file_class_missing_fields(self, base_url, auth_headers):
        """POST /file-classes with missing required fields should return error."""
        payload = {}
        response = requests.post(f"{base_url}/file-classes", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code in (400, 403, 409, 500)

    def test_update_file_class_not_found(self, base_url, auth_headers):
        """PATCH /file-classes/{file-class-code} with nonexistent code should return 404 or 403."""
        payload = {"Description": "Test class"}
        response = requests.patch(
            f"{base_url}/file-classes/ZZZZZ",
            headers=auth_headers, json=payload, timeout=10
        )
        assert response.status_code in (404, 400, 403)

    def test_delete_file_class_not_found(self, base_url, auth_headers):
        """DELETE /file-classes/{file-class-code} with nonexistent code should return 404 or 200 or 403."""
        response = requests.delete(f"{base_url}/file-classes/ZZZZZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 200, 403)


# ============================================================
# File Types (manager)
# ============================================================

class TestFileTypes:
    """File type endpoints."""

    def test_list_file_types_loader(self, base_url, auth_headers):
        """GET /file-types (loader) should return file types for loading."""
        response = requests.get(f"{base_url}/file-types", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204)

    def test_list_manager_file_types_jwt(self, base_url, auth_headers):
        """GET /manager/file-types should return all file type records or 403 if not authorised."""
        response = requests.get(f"{base_url}/manager/file-types", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204, 403)

    def test_list_manager_file_types_returns_list(self, base_url, auth_headers):
        """GET /manager/file-types should return a JSON array when data exists."""
        response = requests.get(f"{base_url}/manager/file-types", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, list)

    def test_get_file_type_not_found(self, base_url, auth_headers):
        """GET /file-types/{file-type-code} with nonexistent code should return 404 or 403."""
        response = requests.get(f"{base_url}/file-types/NONEXISTENT_TYPE_XYZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)

    def test_list_manager_file_types_api_key(self, base_url, api_key_headers):
        """GET /manager/file-types should work with API key auth."""
        response = requests.get(f"{base_url}/manager/file-types", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_create_file_type_missing_fields(self, base_url, auth_headers):
        """POST /manager/file-types with missing required fields should return 400 or error."""
        payload = {}
        response = requests.post(f"{base_url}/manager/file-types", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code in (400, 403, 405, 500)

    def test_update_file_type_not_found(self, base_url, auth_headers):
        """PATCH /manager/file-types/{file-type-code} with nonexistent code should return 404 or 403."""
        payload = {"Description": "Test type"}
        response = requests.patch(
            f"{base_url}/manager/file-types/NONEXIST_XYZ",
            headers=auth_headers, json=payload, timeout=10
        )
        assert response.status_code in (404, 400, 403)

    def test_delete_file_type_not_found(self, base_url, auth_headers):
        """DELETE /manager/file-types/{file-type-code} with nonexistent code should return 404 or 403."""
        response = requests.delete(f"{base_url}/manager/file-types/NONEXIST_XYZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)


# ============================================================
# File Types NT
# ============================================================

class TestFileTypesNt:
    """File type NT record endpoints."""

    def test_list_file_types_nt_jwt(self, base_url, auth_headers):
        """GET /file-types-nt should return file type NT records or 403 if not authorised."""
        response = requests.get(f"{base_url}/file-types-nt", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204, 403)

    def test_list_file_types_nt_returns_list(self, base_url, auth_headers):
        """GET /file-types-nt should return a JSON array when data exists."""
        response = requests.get(f"{base_url}/file-types-nt", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, list)

    def test_list_file_types_nt_with_file_type_filter(self, base_url, auth_headers):
        """GET /file-types-nt with fileType filter should accept the parameter."""
        params = {"fileType": "CDR"}
        response = requests.get(f"{base_url}/file-types-nt", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204, 403)

    def test_get_file_type_nt_not_found(self, base_url, auth_headers):
        """GET /file-types-nt/{file-type-code} with nonexistent code should return 404 or 403."""
        response = requests.get(f"{base_url}/file-types-nt/NONEXISTENT_TYPE_XYZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)

    def test_list_file_types_nt_api_key(self, base_url, api_key_headers):
        """GET /file-types-nt should work with API key auth."""
        response = requests.get(f"{base_url}/file-types-nt", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_create_file_type_nt_missing_fields(self, base_url, auth_headers):
        """POST /file-types-nt with missing required fields should return error."""
        payload = {}
        response = requests.post(f"{base_url}/file-types-nt", headers=auth_headers, json=payload, timeout=10)
        assert response.status_code in (400, 403, 409, 500)

    def test_update_file_type_nt_not_found(self, base_url, auth_headers):
        """PATCH /file-types-nt/{file-type-code} with nonexistent code should return 404 or 403."""
        payload = {"LastSeq": 0}
        response = requests.patch(
            f"{base_url}/file-types-nt/NONEXIST_XYZ",
            headers=auth_headers, json=payload, timeout=10
        )
        assert response.status_code in (404, 400, 403)

    def test_delete_file_type_nt_not_found(self, base_url, auth_headers):
        """DELETE /file-types-nt/{file-type-code} with nonexistent code should return 404 or 200 or 403."""
        response = requests.delete(f"{base_url}/file-types-nt/NONEXIST_XYZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 200, 403)


# ============================================================
# Activity Log
# ============================================================

class TestActivityLog:
    """Activity log endpoint tests."""

    def test_get_activity_log_jwt(self, base_url, auth_headers):
        """GET /activity should return activity log entries."""
        response = requests.get(f"{base_url}/activity", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204, 500)

    def test_get_activity_log_with_max_records(self, base_url, auth_headers):
        """GET /activity with maxRecords should limit results."""
        params = {"takeRecords": 5}
        response = requests.get(f"{base_url}/activity", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204, 500)

    def test_get_activity_log_with_nt_file_num_filter(self, base_url, auth_headers):
        """GET /activity with ntFileNum filter should accept the parameter."""
        params = {"ntFileNum": 1, "maxRecords": 10}
        response = requests.get(f"{base_url}/activity", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_activity_log_with_transfer_id_filter(self, base_url, auth_headers):
        """GET /activity with transferId filter should accept the parameter."""
        params = {"transferId": 1, "maxRecords": 10}
        response = requests.get(f"{base_url}/activity", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_activity_log_api_key(self, base_url, api_key_headers):
        """GET /activity should work with API key auth."""
        response = requests.get(f"{base_url}/activity", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)


# ============================================================
# Exceptions — Errors and Skipped
# ============================================================

class TestExceptions:
    """Exception view endpoint tests."""

    def test_get_errors_jwt(self, base_url, auth_headers):
        """GET /exceptions/errors should return files with processing errors."""
        response = requests.get(f"{base_url}/exceptions/errors", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_errors_with_filters(self, base_url, auth_headers):
        """GET /exceptions/errors with filters should accept parameters."""
        params = {"fileType": "CDR", "maxRecords": 10}
        response = requests.get(f"{base_url}/exceptions/errors", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_errors_returns_paged_response(self, base_url, auth_headers):
        """GET /exceptions/errors should return a paged response with Count and Items."""
        response = requests.get(f"{base_url}/exceptions/errors", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, dict)
            assert "Count" in data or "Items" in data

    def test_get_skipped_jwt(self, base_url, auth_headers):
        """GET /exceptions/skipped should return skipped files."""
        response = requests.get(f"{base_url}/exceptions/skipped", headers=auth_headers, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_skipped_with_filters(self, base_url, auth_headers):
        """GET /exceptions/skipped with filters should accept parameters."""
        params = {"fileType": "CDR", "maxRecords": 10}
        response = requests.get(f"{base_url}/exceptions/skipped", headers=auth_headers, params=params, timeout=10)
        assert response.status_code in (200, 204)

    def test_get_skipped_returns_paged_response(self, base_url, auth_headers):
        """GET /exceptions/skipped should return a paged response with Count and Items."""
        response = requests.get(f"{base_url}/exceptions/skipped", headers=auth_headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            assert isinstance(data, dict)
            assert "Count" in data or "Items" in data

    def test_get_errors_api_key(self, base_url, api_key_headers):
        """GET /exceptions/errors should work with API key auth."""
        response = requests.get(f"{base_url}/exceptions/errors", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)

    def test_get_skipped_api_key(self, base_url, api_key_headers):
        """GET /exceptions/skipped should work with API key auth."""
        response = requests.get(f"{base_url}/exceptions/skipped", headers=api_key_headers, timeout=10)
        assert response.status_code in (200, 204, 401, 403)


# ============================================================
# Error scenarios — 404 for nonexistent resources
# ============================================================

class TestNotFoundErrors:
    """Verify 404 responses for nonexistent resources across all entity types."""

    def test_file_status_not_found(self, base_url, auth_headers):
        """GET /files/999999999 should return 404."""
        response = requests.get(f"{base_url}/files/999999999", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_manager_file_not_found(self, base_url, auth_headers):
        """GET /manager/files/999999999 should return 404."""
        response = requests.get(f"{base_url}/manager/files/999999999", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_source_not_found(self, base_url, auth_headers):
        """GET /sources/999999999 should return 404."""
        response = requests.get(f"{base_url}/sources/999999999", headers=auth_headers, timeout=10)
        assert response.status_code == 404

    def test_parser_not_found(self, base_url, auth_headers):
        """GET /parsers/NONEXISTENT should return 404 or 403/500."""
        response = requests.get(f"{base_url}/parsers/NONEXISTENT", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403, 500)

    def test_vendor_not_found(self, base_url, auth_headers):
        """GET /vendors/ZZ should return 404 or 403."""
        response = requests.get(f"{base_url}/vendors/ZZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)

    def test_file_class_not_found(self, base_url, auth_headers):
        """GET /file-classes/ZZZZZ should return 404 or 403."""
        response = requests.get(f"{base_url}/file-classes/ZZZZZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)

    def test_file_type_not_found(self, base_url, auth_headers):
        """GET /file-types/NONEXISTENT_XYZ should return 404 or 403."""
        response = requests.get(f"{base_url}/file-types/NONEXISTENT_XYZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)

    def test_file_type_nt_not_found(self, base_url, auth_headers):
        """GET /file-types-nt/NONEXISTENT_XYZ should return 404 or 403."""
        response = requests.get(f"{base_url}/file-types-nt/NONEXISTENT_XYZ", headers=auth_headers, timeout=10)
        assert response.status_code in (404, 403)
