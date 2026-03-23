This is a CHARGE file. Pay special attention to identifying:

## Customer Identification
- Which column identifies the customer (account code, customer name, or tenant/domain)
- Which column identifies the service this charge is for (service ID, subscription ID, phone number)
- If no service identifier exists, fall back to the customer account

## Charge Classification
- The charge type or product name column
- Whether charges are grouped by category (subscriptions, usage, reserved instances, etc.)
- Any line item description or offer name

## Dates
- Charge dates: period start (FromDate) and period end (ToDate)
- Not all charges have a ToDate — some are one-off charges
- Date formats vary: ISO datetime, dd/MM/yyyy, yyyy-MM-dd, etc.

## Pricing
- Cost/buy price amount (what the reseller pays) — map to CostAmount
- RRP/sell price amount (what the end customer pays) — map using the snake_cased header name (e.g. unit_price_rrp, sub_total_rrp)
- Unit price vs total price — identify which is which
- BillableRatio or proration factors — map using the snake_cased header name (e.g. billable_ratio)
- Tax amount if present — map to TaxAmount

## Quantities
- Unit of measure (UOM) and quantity
- Whether quantity represents count, duration, data volume, or license seats

## Currency
- Note if non-AUD pricing is present
- Flag any currency indicator columns

## External References
- Invoice numbers, agreement numbers, purchase order numbers — map to ExternalRef
- Subscription IDs, GUIDs, or other unique identifiers

## Data Quality
- Check for $0 rows (zero-value charges that may need filtering)
- Check for negative amounts (credits/adjustments)
- Check for duplicate rows or near-duplicates
- Verify column count consistency across all rows
- Flag any rows with missing required fields
