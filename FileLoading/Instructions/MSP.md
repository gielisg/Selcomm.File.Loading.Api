This is an MSP (Managed Service Provider) supplier charge file. These files represent the COST SIDE — what the MSP/reseller pays their upstream suppliers/distributors. The data is used for margin analysis and cost reconciliation.

## Business Context
- MSPs resell IT services from multiple suppliers (Microsoft/Crayon, Intermedia, Webroot, Datto, etc.) to their MSP partners or end customers
- Each supplier provides monthly billing files in their own format
- The key challenge is matching supplier records to internal customer records (often by name only)
- Files may contain buy price (cost) AND sell price (RRP) — both are important for margin visibility

## Customer Identification (CRITICAL)
- Identify the column that names the END CUSTOMER (not the reseller/distributor) — map to AccountCode
- Look for customer tenant/domain identifiers (e.g., `ramfreighters.onmicrosoft.com`) — map to ServiceId
- Look for subscription IDs or GUIDs that uniquely identify a service instance — map to ExternalRef
- If multiple customer identifier columns exist, note which is the primary match key
- Flag if customer identification relies on name strings (fragile matching) vs unique codes
- Note any reseller/distributor name columns — these identify WHO is buying, not the end customer

## Charge Classification
- Product or offer name column (e.g., "Exchange Online (Plan 1)") — map to ChargeType or Description
- Whether the file contains multiple product categories (subscriptions, usage/consumption, reserved instances)
- Line item description or usage description
- Billing cycle indicator (Monthly, Annual, etc.)
- Term duration (1 month, 1 year, 3 years)

## Dates
- Charge period start date (FromDate) and end date (ToDate)
- Date formats vary widely: ISO datetime with timezone (`2025-09-01T00:00:00.0000000+00:00`), `dd/MM/yyyy`, `yyyy-MM-dd`, `MM/dd/yyyy`
- Some suppliers use separate date columns, others combine into a period string
- Note if dates represent billing period vs service activation date

## Pricing — Buy Side and Sell Side
- **Buy/Cost price** (what the MSP pays the supplier):
  - Unit price — map to CostAmount
  - Total/subtotal — check if this equals unit price × quantity × proration
- **Sell/RRP price** (recommended retail or what the MSP charges downstream):
  - Unit RRP — map to unit_price_rrp
  - Total RRP — map to sub_total_rrp
- **Proration/BillableRatio**: Factor applied for mid-period changes (e.g., 0.5 for half month, 0.666667 for 20/30 days) — map to billable_ratio
- Identify whether amounts are per-unit or pre-multiplied totals
- Check: does SubTotal = UnitPrice × Quantity × BillableRatio?

## Quantities
- License seat count, user count, or subscription quantity — map to Quantity
- Unit of measure if present (EACH, USER, GB, etc.) — map to UOM
- Note if quantity represents concurrent licenses vs allocated vs consumed

## Currency and Pricing
- Identify the currency (AUD, USD, NZD) — flag if non-AUD
- Some suppliers bill in USD (Avanan, Dropsuite) requiring FX conversion
- Flag any currency indicator columns
- Note if tax is included or excluded from amounts

## External References and Identifiers
- Agreement/contract numbers — map to ExternalRef
- Purchase order numbers
- Subscription IDs (GUIDs are common for Microsoft/Crayon)
- Supplier-specific customer codes (e.g., Webroot `MP-xxxxxxx`, Datto TeamID)
- Invoice numbers or billing reference numbers

## Multi-File and Cross-File Patterns
- Does this supplier provide multiple file variants (e.g., Crayon provides Subscriptions, Azure, Reserved Instance as separate files)?
- Are the column structures consistent across variants, or do they differ?
- Note any header inconsistencies between months (e.g., extra columns appearing)

## Data Quality Checks
- **$0 rows**: Zero-value charges (e.g., Azure subscriptions with no usage) — flag but don't reject
- **Negative amounts**: Credits, adjustments, or refunds — note how they appear
- **NFR/NFP accounts**: Not-for-resale or not-for-profit accounts with 100% discount — may appear as $0
- **IUL accounts**: Internal Use License — typically zero-cost but tracked
- **Duplicate detection**: Same customer + product + period appearing multiple times
- **Column count consistency**: Verify all rows have the same number of columns (watch for mid-file header shifts)
- **Customer name inconsistencies**: Same customer appearing with different name spellings across rows or files
- **Empty required fields**: SubscriptionId, CustomerName, or amounts that should not be blank
- **Decimal precision**: Long decimal fractions in proration factors (0.666666667)

## Ingestion Readiness Assessment
Rate as HIGH, MEDIUM, or LOW based on:
- **HIGH**: Clean CSV/XLSX, consistent columns, unique identifiers, clear pricing, standard dates
- **MEDIUM**: Usable but needs transformation (date parsing, FX conversion, multi-sheet handling, missing IDs)
- **LOW**: Significant issues (inconsistent structure, no customer identifiers, formula-dependent values, PDF)
