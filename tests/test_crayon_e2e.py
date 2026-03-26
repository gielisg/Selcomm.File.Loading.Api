"""
End-to-end test: Crayon subscription CSV through generic parser config + custom table workflow.

Exercises the full lifecycle:
  1. Create file type + file type NT
  2. Create parser config with 19 column mappings (including ProrateRatio)
  3. Propose and create custom staging table
  4. Test-load the real Crayon CSV (~4,276 rows)
  5. Verify record counts
  6. Clean up all created resources

Tests are ordered alphabetically (test_01_ through test_13_) and share state
via class attributes.
"""
import pytest
import requests
from pathlib import Path


FILE_TYPE_CODE = "MCR"
CSV_PATH = Path(__file__).parent / "test_crayon_20k.csv"

PARSER_CONFIG = {
    "FileTypeCode": FILE_TYPE_CODE,
    "FileFormat": "CSV",
    "Delimiter": ",",
    "HasHeaderRow": True,
    "SkipRowsTop": 0,
    "SkipRowsBottom": 0,
    "RowIdMode": "POSITION",
    "Active": True,
    "ColumnMappings": [
        {"ColumnIndex": 0,  "SourceColumnName": "ResellerName",        "TargetField": "reseller_name",         "DataType": "String"},
        {"ColumnIndex": 1,  "SourceColumnName": "PrimaryDomainName",   "TargetField": "ServiceId",             "DataType": "String"},
        {"ColumnIndex": 2,  "SourceColumnName": "SubscriptionId",      "TargetField": "ExternalRef",           "DataType": "String"},
        {"ColumnIndex": 3,  "SourceColumnName": "OfferName",           "TargetField": "Description",           "DataType": "String"},
        {"ColumnIndex": 4,  "SourceColumnName": "ChargeStartDate",     "TargetField": "FromDate",              "DataType": "DateTime"},
        {"ColumnIndex": 5,  "SourceColumnName": "ChargeEndDate",       "TargetField": "ToDate",                "DataType": "DateTime"},
        {"ColumnIndex": 6,  "SourceColumnName": "LineDescription",     "TargetField": "line_description",      "DataType": "String"},
        {"ColumnIndex": 7,  "SourceColumnName": "UnitPrice",           "TargetField": "unit_price",            "DataType": "String"},
        {"ColumnIndex": 8,  "SourceColumnName": "UnitPriceRrp",        "TargetField": "unit_price_rrp",        "DataType": "String"},
        {"ColumnIndex": 9,  "SourceColumnName": "Quantity",            "TargetField": "Quantity",              "DataType": "Decimal"},
        {"ColumnIndex": 10, "SourceColumnName": "BillableRatio",       "TargetField": "ProrateRatio",          "DataType": "Decimal"},
        {"ColumnIndex": 11, "SourceColumnName": "SubTotal",            "TargetField": "CostAmount",            "DataType": "Decimal"},
        {"ColumnIndex": 12, "SourceColumnName": "SubTotalRrp",         "TargetField": "sub_total_rrp",         "DataType": "String"},
        {"ColumnIndex": 13, "SourceColumnName": "AgreementNumber",     "TargetField": "agreement_number",      "DataType": "String"},
        {"ColumnIndex": 14, "SourceColumnName": "PurchaseOrderNumber",  "TargetField": "purchase_order_number", "DataType": "String"},
        {"ColumnIndex": 15, "SourceColumnName": "CustomerName",        "TargetField": "AccountCode",           "DataType": "String"},
        {"ColumnIndex": 16, "SourceColumnName": "BillingCycle",        "TargetField": "billing_cycle",         "DataType": "String"},
        {"ColumnIndex": 17, "SourceColumnName": "TermDurationNumber",  "TargetField": "term_duration_number",  "DataType": "String"},
        {"ColumnIndex": 18, "SourceColumnName": "TermDurationUnit",    "TargetField": "term_duration_unit",    "DataType": "String"},
    ],
}


class TestCrayonE2EWorkflow:
    """Ordered end-to-end test: file type → parser config → custom table → test load → cleanup."""

    # Shared state across ordered tests
    _created_file_type = False
    _created_file_type_nt = False
    _parser_created = False
    _custom_table_version = None
    _test_load_nt_file_num = None
    _records_loaded = None

    # ------------------------------------------------------------------
    # Setup
    # ------------------------------------------------------------------

    def test_01_ensure_file_type_exists(self, base_url, auth_headers):
        """Verify MCR file type exists (created externally, not deleted by tests)."""
        response = requests.get(
            f"{base_url}/file-types/MCR", headers=auth_headers, timeout=15
        )
        if response.status_code == 404:
            # Create it
            payload = {
                "FileTypeCode": FILE_TYPE_CODE,
                "FileType": "Miscellaneous Charge Record",
                "FileClassCode": "CHG",
                "NetworkId": "BB",
            }
            r = requests.post(
                f"{base_url}/file-types", headers=auth_headers, json=payload, timeout=15
            )
            assert r.status_code in (200, 201), (
                f"Failed to create file type: {r.status_code} {r.text}"
            )
        else:
            assert response.status_code == 200

    def test_02_ensure_file_type_nt_exists(self, base_url, auth_headers):
        """Verify MCR file type NT record exists."""
        response = requests.get(
            f"{base_url}/file-types-nt/MCR", headers=auth_headers, timeout=15
        )
        if response.status_code == 404:
            payload = {
                "FileTypeCode": FILE_TYPE_CODE,
                "NtCustNum": "DEMO3",
                "LastSeq": 0,
            }
            r = requests.post(
                f"{base_url}/file-types-nt", headers=auth_headers, json=payload, timeout=15
            )
            assert r.status_code in (200, 201), (
                f"Failed to create file type NT: {r.status_code} {r.text}"
            )
        else:
            assert response.status_code == 200

    def test_03_cleanup_existing(self, base_url, auth_headers):
        """Remove any stale parser config, custom table, and orphan files from a prior run."""
        # Delete any orphan MCR files from previous failed test loads
        files_resp = requests.get(
            f"{base_url}/files",
            headers=auth_headers,
            params={"fileTypeCode": FILE_TYPE_CODE, "takeRecords": 50},
            timeout=15,
        )
        if files_resp.status_code == 200:
            items = files_resp.json().get("Items") or files_resp.json().get("items") or []
            for item in items:
                nfn = item.get("NtFileNum") or item.get("ntFileNum")
                if nfn:
                    requests.delete(
                        f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/test-load/{nfn}",
                        headers=auth_headers, timeout=15,
                    )

        # Drop any existing custom table
        ct_resp = requests.get(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table",
            headers=auth_headers, timeout=15,
        )
        if ct_resp.status_code == 200:
            data = ct_resp.json()
            active = data.get("ActiveVersion") or data.get("activeVersion")
            if active:
                version = active.get("Version") or active.get("version")
                if version:
                    requests.delete(
                        f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/{version}",
                        headers=auth_headers, timeout=15,
                    )

        # Delete parser config (best-effort)
        response = requests.delete(
            f"{base_url}/parsers/{FILE_TYPE_CODE}", headers=auth_headers, timeout=15
        )
        assert response.status_code in (200, 404, 500), (
            f"Unexpected status {response.status_code}: {response.text}"
        )

    # ------------------------------------------------------------------
    # Parser Configuration
    # ------------------------------------------------------------------

    def test_04_create_parser_config(self, base_url, auth_headers):
        """POST /parsers — create parser config with 19 column mappings."""
        response = requests.post(
            f"{base_url}/parsers", headers=auth_headers, json=PARSER_CONFIG, timeout=15
        )
        assert response.status_code in (200, 201), (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()
        ftc = data.get("FileTypeCode") or data.get("fileTypeCode")
        assert ftc == FILE_TYPE_CODE
        mappings = data.get("ColumnMappings") or data.get("columnMappings") or []
        assert len(mappings) == 19, f"Expected 19 mappings, got {len(mappings)}"
        TestCrayonE2EWorkflow._parser_created = True

    def test_05_get_parser_config(self, base_url, auth_headers):
        """GET /parsers/MCR — verify stored config and ProrateRatio mapping."""
        response = requests.get(
            f"{base_url}/parsers/{FILE_TYPE_CODE}", headers=auth_headers, timeout=15
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()
        mappings = data.get("ColumnMappings") or data.get("columnMappings") or []
        assert len(mappings) == 19

        # Verify ProrateRatio mapping
        prorate_mapping = next(
            (m for m in mappings
             if (m.get("TargetField") or m.get("targetField")) == "ProrateRatio"),
            None,
        )
        assert prorate_mapping is not None, "ProrateRatio mapping not found"
        idx = prorate_mapping.get("ColumnIndex") or prorate_mapping.get("columnIndex")
        assert idx == 10
        dt = prorate_mapping.get("DataType") or prorate_mapping.get("dataType")
        assert dt == "Decimal"

    # ------------------------------------------------------------------
    # Custom Table
    # ------------------------------------------------------------------

    def test_06_propose_custom_table(self, base_url, auth_headers):
        """POST /parsers/MCR/custom-table/propose — preview DDL."""
        response = requests.post(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/propose",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()

        # Verify DDL
        ddl = data.get("Ddl") or data.get("ddl") or ""
        assert "CREATE TABLE" in ddl.upper(), f"DDL missing CREATE TABLE: {ddl[:200]}"
        assert "prorate_ratio" in ddl.lower(), f"DDL missing prorate_ratio column: {ddl[:500]}"

        # Verify table name convention
        table_name = data.get("TableName") or data.get("tableName") or ""
        assert table_name.startswith("ntfl_"), f"Unexpected table name: {table_name}"

        # Verify columns
        columns = data.get("Columns") or data.get("columns") or []
        assert len(columns) > 0, "No columns in proposal"

    def test_07_create_custom_table(self, base_url, auth_headers):
        """POST /parsers/MCR/custom-table — create the physical table."""
        response = requests.post(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table",
            headers=auth_headers, timeout=30,
        )
        assert response.status_code == 201, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()

        status = data.get("Status") or data.get("status")
        assert status == "ACTIVE", f"Expected ACTIVE, got {status}"

        version = data.get("Version") or data.get("version")
        assert version is not None and version >= 1
        TestCrayonE2EWorkflow._custom_table_version = version

    def test_08_get_custom_table_info(self, base_url, auth_headers):
        """GET /parsers/MCR/custom-table — verify active version."""
        response = requests.get(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()
        active = data.get("ActiveVersion") or data.get("activeVersion")
        assert active is not None, "No active version returned"

        status = active.get("Status") or active.get("status")
        assert status == "ACTIVE"

    # ------------------------------------------------------------------
    # Test Load
    # ------------------------------------------------------------------

    def test_09_test_load_csv(self, base_url, auth_headers):
        """POST /parsers/MCR/custom-table/test-load — upload and load the Crayon CSV."""
        assert CSV_PATH.exists(), f"CSV file not found: {CSV_PATH}"

        with open(CSV_PATH, "rb") as f:
            files = {"file": (CSV_PATH.name, f, "text/csv")}
            response = requests.post(
                f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/test-load",
                headers=auth_headers, files=files, timeout=120,
            )

        assert response.status_code == 201, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()

        records_loaded = data.get("RecordsLoaded") or data.get("recordsLoaded") or 0
        records_failed = data.get("RecordsFailed") or data.get("recordsFailed") or 0
        nt_file_num = data.get("NtFileNum") or data.get("ntFileNum")
        errors = data.get("Errors") or data.get("errors") or []

        assert records_loaded > 0, f"No records loaded. Errors: {errors}"
        assert records_failed == 0, (
            f"{records_failed} records failed. Errors: {errors[:5]}"
        )
        assert nt_file_num is not None and nt_file_num > 0

        TestCrayonE2EWorkflow._test_load_nt_file_num = nt_file_num
        TestCrayonE2EWorkflow._records_loaded = records_loaded

        print(f"\n  Loaded {records_loaded} records, ntFileNum={nt_file_num}")

    def test_10_verify_record_count(self, base_url, auth_headers):
        """GET /parsers/MCR/custom-table/{version}/count — verify record count matches."""
        version = TestCrayonE2EWorkflow._custom_table_version
        if version is None:
            pytest.skip("No custom table version (test_07 failed)")

        response = requests.get(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/{version}/count",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )

        count = response.json()
        expected = TestCrayonE2EWorkflow._records_loaded
        if expected is not None:
            assert count == expected, f"Count mismatch: table has {count}, loaded {expected}"
        else:
            assert count > 0, "Table has zero records"

    # ------------------------------------------------------------------
    # Cleanup
    # ------------------------------------------------------------------

    def test_11_cleanup_test_load(self, base_url, auth_headers):
        """Delete the test-loaded file record and all its detail records."""
        nt_file_num = TestCrayonE2EWorkflow._test_load_nt_file_num
        if nt_file_num is None:
            pytest.skip("No test load to clean up (test_09 failed)")

        response = requests.delete(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/test-load/{nt_file_num}",
            headers=auth_headers, timeout=30,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )

    def test_12_cleanup_custom_table(self, base_url, auth_headers):
        """DROP the custom table version."""
        version = TestCrayonE2EWorkflow._custom_table_version
        if version is None:
            pytest.skip("No custom table to drop (test_07 failed)")

        response = requests.delete(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/{version}",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )

    def test_13_cleanup_parser(self, base_url, auth_headers):
        """DELETE the parser configuration."""
        if not TestCrayonE2EWorkflow._parser_created:
            pytest.skip("No parser config to delete (test_04 failed)")

        response = requests.delete(
            f"{base_url}/parsers/{FILE_TYPE_CODE}", headers=auth_headers, timeout=15
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )


class TestCrayonFolderWorkflow:
    """
    Test the full folder workflow: Upload → Transfer → Process → Processed.

    Requires parser config and custom table to exist (run TestCrayonE2EWorkflow
    tests 01-08 first, or this class sets them up itself).
    """

    _transfer_id = None
    _nt_file_num = None
    _custom_table_version = None
    _parser_created = False

    def test_01_setup_parser_and_table(self, base_url, auth_headers):
        """Ensure parser config and custom table exist for MCR."""
        # Check if parser exists
        r = requests.get(
            f"{base_url}/parsers/{FILE_TYPE_CODE}", headers=auth_headers, timeout=15
        )
        if r.status_code == 404:
            # Create parser
            r2 = requests.post(
                f"{base_url}/parsers", headers=auth_headers, json=PARSER_CONFIG, timeout=15
            )
            assert r2.status_code in (200, 201), (
                f"Failed to create parser: {r2.status_code} {r2.text}"
            )
            TestCrayonFolderWorkflow._parser_created = True

        # Check if custom table exists
        r3 = requests.get(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table",
            headers=auth_headers, timeout=15,
        )
        active = None
        if r3.status_code == 200:
            data = r3.json()
            active = data.get("ActiveVersion") or data.get("activeVersion")

        if active is None or active.get("Status") != "ACTIVE":
            # Create custom table
            r4 = requests.post(
                f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table",
                headers=auth_headers, timeout=30,
            )
            assert r4.status_code == 201, (
                f"Failed to create custom table: {r4.status_code} {r4.text}"
            )
            version = r4.json().get("Version") or r4.json().get("version")
            TestCrayonFolderWorkflow._custom_table_version = version
        else:
            TestCrayonFolderWorkflow._custom_table_version = (
                active.get("Version") or active.get("version")
            )

    def test_02_upload_to_transfer(self, base_url, auth_headers):
        """Upload CSV to Transfer folder via manager upload endpoint."""
        assert CSV_PATH.exists(), f"CSV file not found: {CSV_PATH}"

        with open(CSV_PATH, "rb") as f:
            files = {"file": (CSV_PATH.name, f, "text/csv")}
            response = requests.post(
                f"{base_url}/manager/files/upload",
                headers=auth_headers,
                files=files,
                params={"fileTypeCode": FILE_TYPE_CODE},
                timeout=30,
            )

        assert response.status_code == 201, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()

        transfer_id = data.get("TransferId") or data.get("transferId")
        assert transfer_id is not None and transfer_id > 0
        TestCrayonFolderWorkflow._transfer_id = transfer_id

        folder = data.get("CurrentFolder") or data.get("currentFolder")
        assert folder == "Transfer", f"Expected Transfer folder, got {folder}"

        print(f"\n  Transfer ID: {transfer_id}, Folder: {folder}")

    def test_03_verify_in_transfer_folder(self, base_url, auth_headers):
        """Verify the file appears in the Transfer folder listing."""
        transfer_id = TestCrayonFolderWorkflow._transfer_id
        if transfer_id is None:
            pytest.skip("No transfer ID (test_02 failed)")

        response = requests.get(
            f"{base_url}/manager/files/{transfer_id}",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()
        folder = data.get("CurrentFolder") or data.get("currentFolder")
        assert folder == "Transfer", f"Expected Transfer, got {folder}"

    def test_04_process_file(self, base_url, auth_headers):
        """Process the file — should move Transfer → Processing → Processed."""
        transfer_id = TestCrayonFolderWorkflow._transfer_id
        if transfer_id is None:
            pytest.skip("No transfer ID (test_02 failed)")

        response = requests.post(
            f"{base_url}/manager/files/{transfer_id}/process",
            headers=auth_headers,
            params={"fileTypeCode": FILE_TYPE_CODE},
            timeout=120,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()

        records_loaded = data.get("RecordsLoaded") or data.get("recordsLoaded") or 0
        nt_file_num = data.get("NtFileNum") or data.get("ntFileNum")

        assert records_loaded > 0, f"No records loaded: {data}"
        assert nt_file_num is not None and nt_file_num > 0
        TestCrayonFolderWorkflow._nt_file_num = nt_file_num

        print(f"\n  Loaded {records_loaded} records, NtFileNum: {nt_file_num}")

    def test_05_verify_in_processed_folder(self, base_url, auth_headers):
        """Verify the file moved to the Processed folder after loading."""
        transfer_id = TestCrayonFolderWorkflow._transfer_id
        if transfer_id is None:
            pytest.skip("No transfer ID (test_02 failed)")

        response = requests.get(
            f"{base_url}/manager/files/{transfer_id}",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()
        folder = data.get("CurrentFolder") or data.get("currentFolder")
        assert folder == "Processed", f"Expected Processed, got {folder}"

        status = data.get("Status") or data.get("status") or ""
        print(f"\n  Folder: {folder}, Status: {status}")

    def test_06_verify_file_status(self, base_url, auth_headers):
        """Verify the nt_file record has the correct loaded status."""
        nt_file_num = TestCrayonFolderWorkflow._nt_file_num
        if nt_file_num is None:
            pytest.skip("No nt_file_num (test_04 failed)")

        response = requests.get(
            f"{base_url}/files/{nt_file_num}",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )

    def test_07_cleanup(self, base_url, auth_headers):
        """Clean up: unload file, delete transfer, drop custom table, delete parser."""
        # Delete test load (nt_file + detail records)
        nt_file_num = TestCrayonFolderWorkflow._nt_file_num
        if nt_file_num:
            requests.delete(
                f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/test-load/{nt_file_num}",
                headers=auth_headers, timeout=30,
            )

        # Delete transfer record
        transfer_id = TestCrayonFolderWorkflow._transfer_id
        if transfer_id:
            requests.delete(
                f"{base_url}/manager/files/{transfer_id}",
                headers=auth_headers, timeout=15,
            )

        # Drop custom table
        version = TestCrayonFolderWorkflow._custom_table_version
        if version:
            requests.delete(
                f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/{version}",
                headers=auth_headers, timeout=15,
            )

        # Delete parser if we created it
        if TestCrayonFolderWorkflow._parser_created:
            requests.delete(
                f"{base_url}/parsers/{FILE_TYPE_CODE}",
                headers=auth_headers, timeout=15,
            )


BAD_CSV_PATH = Path(__file__).parent / "test_crayon_bad_row.csv"


class TestCrayonBadRowRejection:
    """
    Test all-or-nothing loading: a file with one bad row should be completely
    rejected — zero records loaded, file moved to Errors folder, appropriate status.

    The bad CSV has 11 data rows with row 7 containing 'NOT_A_NUMBER' in the
    Quantity (Decimal) field, which triggers a parse validation error.
    """

    _transfer_id = None
    _nt_file_num = None
    _custom_table_version = None
    _parser_created = False

    def test_01_setup_and_cleanup(self, base_url, auth_headers):
        """Clean orphans, ensure parser config and custom table exist for MCR."""
        # Clean orphan MCR files from previous runs
        files_resp = requests.get(
            f"{base_url}/files",
            headers=auth_headers,
            params={"fileTypeCode": FILE_TYPE_CODE, "takeRecords": 50},
            timeout=15,
        )
        if files_resp.status_code == 200:
            for item in files_resp.json().get("Items") or []:
                nfn = item.get("NtFileNum") or item.get("ntFileNum")
                if nfn:
                    requests.delete(
                        f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/test-load/{nfn}",
                        headers=auth_headers, timeout=15,
                    )

        # Drop existing custom table (may have leftover records)
        ct_resp = requests.get(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table",
            headers=auth_headers, timeout=15,
        )
        if ct_resp.status_code == 200:
            active = (ct_resp.json().get("ActiveVersion") or
                      ct_resp.json().get("activeVersion"))
            if active:
                v = active.get("Version") or active.get("version")
                if v:
                    requests.delete(
                        f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/{v}",
                        headers=auth_headers, timeout=15,
                    )

        # Delete existing parser
        requests.delete(
            f"{base_url}/parsers/{FILE_TYPE_CODE}", headers=auth_headers, timeout=15
        )

        # Create fresh parser
        r2 = requests.post(
            f"{base_url}/parsers", headers=auth_headers, json=PARSER_CONFIG, timeout=15
        )
        assert r2.status_code in (200, 201), (
            f"Failed to create parser: {r2.status_code} {r2.text}"
        )
        TestCrayonBadRowRejection._parser_created = True

        # Create fresh custom table
        r4 = requests.post(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table",
            headers=auth_headers, timeout=30,
        )
        assert r4.status_code == 201, (
            f"Failed to create custom table: {r4.status_code} {r4.text}"
        )
        version = r4.json().get("Version") or r4.json().get("version")
        TestCrayonBadRowRejection._custom_table_version = version

    def test_02_upload_bad_file_to_transfer(self, base_url, auth_headers):
        """Upload the bad CSV to the Transfer folder."""
        assert BAD_CSV_PATH.exists(), f"Bad CSV not found: {BAD_CSV_PATH}"

        with open(BAD_CSV_PATH, "rb") as f:
            files = {"file": (BAD_CSV_PATH.name, f, "text/csv")}
            response = requests.post(
                f"{base_url}/manager/files/upload",
                headers=auth_headers,
                files=files,
                params={"fileTypeCode": FILE_TYPE_CODE},
                timeout=30,
            )

        assert response.status_code == 201, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()
        transfer_id = data.get("TransferId") or data.get("transferId")
        assert transfer_id is not None
        TestCrayonBadRowRejection._transfer_id = transfer_id

        folder = data.get("CurrentFolder") or data.get("currentFolder")
        assert folder == "Transfer"
        print(f"\n  Transfer ID: {transfer_id}")

    def test_03_process_bad_file(self, base_url, auth_headers):
        """Process the file — should fail due to bad row. File should NOT load successfully."""
        transfer_id = TestCrayonBadRowRejection._transfer_id
        if transfer_id is None:
            pytest.skip("No transfer ID (test_02 failed)")

        response = requests.post(
            f"{base_url}/manager/files/{transfer_id}/process",
            headers=auth_headers,
            params={"fileTypeCode": FILE_TYPE_CODE},
            timeout=60,
        )

        data = response.json()
        records_loaded = data.get("RecordsLoaded") or data.get("recordsLoaded") or 0
        records_failed = data.get("RecordsFailed") or data.get("recordsFailed") or 0
        nt_file_num = data.get("NtFileNum") or data.get("ntFileNum")
        status = data.get("Status") or data.get("status") or ""
        error = data.get("Error") or data.get("error") or data.get("ErrorMessage") or ""

        if nt_file_num:
            TestCrayonBadRowRejection._nt_file_num = nt_file_num

        print(f"\n  HTTP: {response.status_code}, Status: {status}")
        print(f"  Loaded: {records_loaded}, Failed: {records_failed}")
        print(f"  NtFileNum: {nt_file_num}, Error: {error}")

        # The process should NOT return a success status
        assert response.status_code != 200 or records_loaded == 0, (
            f"Expected failure or zero records loaded, got HTTP {response.status_code} with {records_loaded} loaded"
        )

    def test_04_verify_in_errors_folder(self, base_url, auth_headers):
        """Verify the file was moved to the Errors folder after rejection."""
        transfer_id = TestCrayonBadRowRejection._transfer_id
        if transfer_id is None:
            pytest.skip("No transfer ID (test_02 failed)")

        response = requests.get(
            f"{base_url}/manager/files/{transfer_id}",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()
        folder = data.get("CurrentFolder") or data.get("currentFolder")
        assert folder == "Errors", f"Expected Errors folder, got {folder}"

        status = data.get("Status") or data.get("status") or ""
        print(f"\n  Folder: {folder}, Status: {status}")

    def test_05_verify_file_status_is_error(self, base_url, auth_headers):
        """Verify the nt_file record has an error/validation status."""
        nt_file_num = TestCrayonBadRowRejection._nt_file_num
        if nt_file_num is None:
            pytest.skip("No nt_file_num (test_03 didn't produce one)")

        response = requests.get(
            f"{base_url}/files/{nt_file_num}",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200, (
            f"Unexpected status {response.status_code}: {response.text}"
        )
        data = response.json()
        status = data.get("Status") or data.get("status") or ""
        status_id = data.get("StatusId") or data.get("statusId")

        # Status should indicate error (ValidationError or LoadError)
        assert "error" in status.lower() or status_id in (6, 7, 8, 9), (
            f"Expected error status, got '{status}' (id={status_id})"
        )
        print(f"\n  File status: {status} (id={status_id})")

    def test_06_verify_zero_records_in_custom_table(self, base_url, auth_headers):
        """Verify no records were inserted into the custom table for this file."""
        version = TestCrayonBadRowRejection._custom_table_version
        if version is None:
            pytest.skip("No custom table version")

        response = requests.get(
            f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/{version}/count",
            headers=auth_headers, timeout=15,
        )
        assert response.status_code == 200
        count = response.json()
        assert count == 0, f"Expected 0 records in custom table, got {count}"

    def test_07_cleanup(self, base_url, auth_headers):
        """Clean up all test resources."""
        # Delete nt_file record if it exists
        nt_file_num = TestCrayonBadRowRejection._nt_file_num
        if nt_file_num:
            requests.delete(
                f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/test-load/{nt_file_num}",
                headers=auth_headers, timeout=30,
            )

        # Delete transfer record
        transfer_id = TestCrayonBadRowRejection._transfer_id
        if transfer_id:
            requests.delete(
                f"{base_url}/manager/files/{transfer_id}",
                headers=auth_headers, timeout=15,
            )

        # Drop custom table
        version = TestCrayonBadRowRejection._custom_table_version
        if version:
            requests.delete(
                f"{base_url}/parsers/{FILE_TYPE_CODE}/custom-table/{version}",
                headers=auth_headers, timeout=15,
            )

        # Delete parser if we created it
        if TestCrayonBadRowRejection._parser_created:
            requests.delete(
                f"{base_url}/parsers/{FILE_TYPE_CODE}",
                headers=auth_headers, timeout=15,
            )
