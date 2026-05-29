<?php
/**
 * channel_join.php
 * ============================================================================
 * Endpoint: POST /channel_join.php
 *
 * Verifies that a channel exists and the submitted credentials are correct.
 * Intentionally gives the same error message whether the channel doesn't
 * exist or the password is wrong — this prevents channel enumeration.
 *
 * Request body (JSON):
 *   {
 *     "channel_id": "<64-char hex>",
 *     "auth_token":  "<64-char hex>"
 *   }
 *
 * Responses:
 *   200  { "status": "joined" }
 *   401  { "error": "Invalid credentials" }   (wrong password OR no such channel)
 *   400  { "error": "..." }   missing / malformed fields
 *   405  wrong HTTP method
 * ============================================================================
 */

declare(strict_types=1);
require_once __DIR__ . '/_common.php';

requireMethod('POST');

try {
    $body      = readJsonBody();
    $channelId = requireHex64($body, 'channel_id');
    $authToken = requireHex64($body, 'auth_token');

    // Structural check first (no DB hit needed for malformed tokens)
    if (!verifyAuthToken($channelId, $authToken)) {
        jsonResponse(['error' => 'Invalid credentials'], 401);
    }

    $db = getDB();

    if (!channelCredentialsValid($db, $channelId, $authToken)) {
        jsonResponse(['error' => 'Invalid credentials'], 401);
    }

    jsonResponse(['status' => 'joined'], 200);

} catch (PDOException $e) {
    error_log('[SecureChannel] channel_join.php DB error: ' . $e->getMessage());
    jsonResponse(['error' => 'Internal server error'], 500);
} catch (Throwable $e) {
    error_log('[SecureChannel] channel_join.php error: ' . $e->getMessage());
    jsonResponse(['error' => 'Internal server error'], 500);
}
