-- A DateTimeOffset column is text on SQLite, compared byte by byte. Trax writes it as fixed-width UTC
-- text (yyyy-MM-dd HH:mm:ss.fffffff+00:00, 33 characters), but a row written before that holds EF's
-- default text, whose fraction is only as long as it needs to be and whose offset is whatever the value
-- carried: 2026-09-29 12:00:00.12+00:00. Against a different instant that still sorts in time order
-- when the offset is +00:00, but against the same instant in the fixed-width form it compares as less
-- ('+' sorts before '0'), so a lease or a draft's age read one tick early at the exact boundary. A
-- non-UTC offset sorted wrong outright.
--
-- Every such value is rewritten into the fixed-width form. The whole seconds go through datetime(),
-- which applies the offset and gives UTC; the fraction is copied as written and padded to seven
-- digits, because datetime() keeps only milliseconds. An offset is whole minutes, so it never touches
-- the fraction. Rows already in the fixed-width form are left alone, so running this again changes
-- nothing, and a value with no offset suffix (which Trax never wrote) is not touched.

UPDATE effect_claim
SET lease_expires_at = datetime(substr(lease_expires_at, 1, 19) || substr(lease_expires_at, -6))
        || '.'
        || substr(
            CASE WHEN substr(lease_expires_at, 20, 1) = '.' THEN substr(lease_expires_at, 21, length(lease_expires_at) - 26) ELSE '' END
                || '0000000',
            1, 7)
        || '+00:00'
WHERE (length(lease_expires_at) <> 33 OR substr(lease_expires_at, -6) <> '+00:00')
  AND substr(lease_expires_at, -6, 1) IN ('+', '-') AND substr(lease_expires_at, -3, 1) = ':';

UPDATE effect_claim
SET created_at = datetime(substr(created_at, 1, 19) || substr(created_at, -6))
        || '.'
        || substr(
            CASE WHEN substr(created_at, 20, 1) = '.' THEN substr(created_at, 21, length(created_at) - 26) ELSE '' END
                || '0000000',
            1, 7)
        || '+00:00'
WHERE (length(created_at) <> 33 OR substr(created_at, -6) <> '+00:00')
  AND substr(created_at, -6, 1) IN ('+', '-') AND substr(created_at, -3, 1) = ':';

UPDATE snapshot_draft
SET updated_at = datetime(substr(updated_at, 1, 19) || substr(updated_at, -6))
        || '.'
        || substr(
            CASE WHEN substr(updated_at, 20, 1) = '.' THEN substr(updated_at, 21, length(updated_at) - 26) ELSE '' END
                || '0000000',
            1, 7)
        || '+00:00'
WHERE (length(updated_at) <> 33 OR substr(updated_at, -6) <> '+00:00')
  AND substr(updated_at, -6, 1) IN ('+', '-') AND substr(updated_at, -3, 1) = ':';
