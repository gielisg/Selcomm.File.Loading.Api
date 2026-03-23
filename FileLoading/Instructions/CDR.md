This is a USAGE/CDR (Call Detail Record) file. Pay special attention to identifying:

## Service Identification
- Service identifier (phone number, circuit ID, username, SIP URI)
- A-number (calling party) and B-number (called party)
- Direction indicator (inbound/outbound/internal)

## Event Details
- Call/event date and time — map to FromDate
- Duration in seconds or HH:MM:SS — map to Quantity
- Data volume (bytes, KB, MB, GB) if applicable
- Call type or event type (voice, SMS, MMS, data, roaming)

## Rating
- Rated/charged amount — map to CostAmount
- Rate per unit if available
- Whether amounts are inclusive or exclusive of tax

## Location
- Origin and destination locations or zones
- Roaming indicators
- Network or carrier identifiers

## Data Quality
- Check for zero-duration calls (may indicate failed calls)
- Check for extremely long durations (>24 hours may be errors)
- Verify date/time format consistency
- Flag any rows with missing service identifiers
- Check for duplicate CDRs (same A-number, B-number, timestamp)
