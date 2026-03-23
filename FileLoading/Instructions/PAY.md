This is a PAYMENT file. Pay special attention to identifying:

## Customer Identification
- Customer or account identifier — map to AccountCode
- Customer name if separate from account code

## Payment Details
- Payment date — map to FromDate
- Payment amount — map to CostAmount
- Payment method (credit card, direct debit, EFT, cheque, etc.)
- Reference or transaction number — map to ExternalRef

## Status
- Payment status (completed, pending, failed, reversed)
- Settlement date if different from payment date

## Banking
- Bank account or card details (last 4 digits, BSB, etc.)
- Gateway or processor reference numbers

## Data Quality
- Check for negative amounts (refunds vs errors)
- Check for duplicate payments (same amount, date, customer)
- Verify all payments have a customer identifier
- Flag any unusually large amounts
